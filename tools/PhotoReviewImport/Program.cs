using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorApp.Shared.PhotoReview;
using BlazorApp.Shared.PhotoShoot;
using LuckyLuke.PhotoReviewStorage;
using SkiaSharp;

namespace PhotoReviewImport;

/// <summary>
/// Photo review importer. See docs/photoshoot/ARTIFACT_MANIFEST.md.
///   build-selection --collection DIR --planner FILE --out FILE
///   import --manifest FILE --root DIR [--live | --local-dir DIR] [--planner FILE]
/// Default for import is a dry run that writes nothing.
/// </summary>
public static class Program
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            return options.Command switch
            {
                "build-selection" => BuildSelection(options),
                "import" => await ImportAsync(options),
                _ => Usage()
            };
        }
        catch (ToolException ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("""
            Photo review importer (docs/photoshoot/ARTIFACT_MANIFEST.md)

              build-selection --collection DIR --planner SHOOT.json --out MANIFEST.json
              import --manifest MANIFEST.json --root DIR            dry run (default, writes nothing)
              import ... --local-dir DIR                            write to an isolated local store
              import ... --live                                     write to production storage
                    (needs AZURE_STORAGE_CONNECTION_STRING; local and production share storage)
              Options: --planner SHOOT.json (override planner source), --skip-thumbs, --parallel N
            """);
        return 1;
    }

    // ---------- build-selection ----------

    private static int BuildSelection(Options options)
    {
        var collection = options.Require("collection");
        var plannerPath = options.Require("planner");
        var output = options.Require("out");
        var planner = LoadPlannerFile(plannerPath);
        var manifest = SelectionManifestBuilder.Build(collection, planner, out var notes);
        foreach (var note in notes)
        {
            Console.WriteLine(note);
        }

        File.WriteAllText(output, JsonSerializer.Serialize(manifest, Json));
        Console.WriteLine($"Wrote {output}: {manifest.Shots.Count} shots, {manifest.Photos.Count} photos.");
        return 0;
    }

    internal static PhotoShootDocument LoadPlannerFile(string path) =>
        PhotoShootEditor.Normalize(JsonSerializer.Deserialize<PhotoShootDocument>(File.ReadAllText(path), Json) ?? throw new ToolException("Planner file is empty."));

    // ---------- import ----------

    private static async Task<int> ImportAsync(Options options)
    {
        var manifestPath = Path.GetFullPath(options.Require("manifest"));
        var root = Path.GetFullPath(options.Get("root") ?? Path.GetDirectoryName(manifestPath)!);
        var live = options.Has("live");
        var localDir = options.Get("local-dir");
        if (live && localDir != null) throw new ToolException("Use either --live or --local-dir, not both.");
        var manifestBytes = File.ReadAllBytes(manifestPath);
        var manifest = JsonSerializer.Deserialize<ReviewManifest>(manifestBytes, Json) ?? throw new ToolException("Manifest is empty.");
        if (!PhotoShootIds.IsValid(manifest.ShootId)) throw new ToolException("Manifest shootId is not a valid id.");

        IReviewBlobStore? store = null;
        if (live)
        {
            var connection = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(connection)) throw new ToolException("--live needs AZURE_STORAGE_CONNECTION_STRING.");
            store = new BlobReviewStore(connection);
        }
        else if (localDir != null)
        {
            store = new LocalReviewStore(localDir);
        }

        Console.WriteLine($"Mode: {(live ? "LIVE (production storage)" : localDir != null ? "local store " + Path.GetFullPath(localDir) : "dry run")}");

        // Planner: from --planner, else from the target store (read-only; the importer never writes it).
        PhotoShootDocument planner;
        var plannerOption = options.Get("planner");
        if (plannerOption != null)
        {
            planner = LoadPlannerFile(plannerOption);
        }
        else if (store != null)
        {
            var raw = await store.ReadAsync($"shoots/{manifest.ShootId}.json") ?? throw new ToolException($"Planner shoots/{manifest.ShootId}.json not found in the target store.");
            planner = PhotoShootEditor.Normalize(JsonSerializer.Deserialize<PhotoShootDocument>(raw.Bytes, Json)!);
        }
        else
        {
            throw new ToolException("A dry run needs --planner SHOOT.json to validate shot ids.");
        }

        if (!string.IsNullOrEmpty(planner.Id) && planner.Id != manifest.ShootId) throw new ToolException($"Planner is shoot {planner.Id}, manifest is for {manifest.ShootId}.");
        var planned = PhotoReviewProjection.PlannedRefs(planner);

        var (catalog, catalogETag) = store == null ? (new ReviewCatalog { ShootId = manifest.ShootId }, "") : await LoadCatalogAsync(store, manifest.ShootId);
        var preview = PhotoReviewIngest.Validate(manifest, planned, catalog);
        var files = VerifyFiles(manifest, root, preview);
        Report(preview, "Validation");
        if (!preview.Ok) return 3;

        if (store == null)
        {
            var dry = Clone(catalog);
            var report = PhotoReviewIngest.Apply(dry, manifest, planned, new Dictionary<string, ThumbInfo>(), file => ReadNotes(root, file), DateTime.UtcNow);
            Report(report, "Dry run (nothing written)");
            Console.WriteLine($"Files: {files.Count}, {files.Values.Sum(f => f.Bytes) / 1048576.0:F1} MB.");
            return report.Ok ? 0 : 3;
        }

        // Back up the current catalog before changing anything.
        if (catalogETag.Length > 0)
        {
            var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LukeHammerPhotoshoot", "backups",
                DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") + "-review-import");
            Directory.CreateDirectory(backupDir);
            File.WriteAllText(Path.Combine(backupDir, "catalog.json"), JsonSerializer.Serialize(catalog, Json));
            File.Copy(manifestPath, Path.Combine(backupDir, "manifest.json"));
            Console.WriteLine("Backed up catalog to " + backupDir);
        }

        var thumbs = await UploadAsync(store, files, options);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var report = PhotoReviewIngest.Apply(catalog, manifest, planned, thumbs, file => ReadNotes(root, file), DateTime.UtcNow);
            Report(report, "Import");
            if (!report.Ok) return 3;
            if (!report.Changed)
            {
                Console.WriteLine("Catalog already up to date. Nothing written.");
                return 0;
            }

            catalog.Imports.Last().ManifestSha256 = Sha256(manifestBytes);
            catalog.Revision++;
            catalog.UpdatedUtc = DateTime.UtcNow;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, Json);
            var saved = await store.TryWriteAsync(ReviewBlobNames.Catalog(manifest.ShootId), bytes, "application/json",
                catalogETag.Length == 0 ? null : catalogETag, createOnly: catalogETag.Length == 0);
            if (saved != null)
            {
                Console.WriteLine($"Catalog saved (revision {catalog.Revision}, {catalog.Photos.Count} photos).");
                return 0;
            }

            Console.WriteLine("Catalog changed while importing; retrying on the latest version.");
            (catalog, catalogETag) = await LoadCatalogAsync(store, manifest.ShootId);
        }

        throw new ToolException("Could not save the catalog after several attempts.");
    }

    private static async Task<(ReviewCatalog, string)> LoadCatalogAsync(IReviewBlobStore store, string shootId)
    {
        var stored = await store.ReadAsync(ReviewBlobNames.Catalog(shootId));
        if (stored == null) return (new ReviewCatalog { ShootId = shootId }, "");
        var catalog = JsonSerializer.Deserialize<ReviewCatalog>(stored.Bytes, Json) ?? new ReviewCatalog();
        catalog.ShootId = shootId;
        return (PhotoReviewIngest.Normalize(catalog), stored.ETag);
    }

    private static ReviewCatalog Clone(ReviewCatalog catalog) =>
        JsonSerializer.Deserialize<ReviewCatalog>(JsonSerializer.SerializeToUtf8Bytes(catalog, Json), Json)!;

    internal sealed class VerifiedFile
    {
        public string Path { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public long Bytes { get; init; }
        public string ContentType { get; init; } = "";
        public int Width { get; init; }
    }

    /// <summary>Checks every listed file: inside the root, exists, hash, size, type, dimensions. Adds errors to the report.</summary>
    internal static Dictionary<string, VerifiedFile> VerifyFiles(ReviewManifest manifest, string root, IngestReport report)
    {
        var files = new Dictionary<string, VerifiedFile>();
        var rootFull = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        foreach (var photo in manifest.Photos)
        {
            foreach (var version in photo.Versions)
            {
                var where = $"photo \"{photo.PhotoKey}\" {version.Kind}";
                var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, version.File ?? ""));
                if (!path.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    report.Errors.Add($"{where}: file is outside --root.");
                    continue;
                }

                if (!File.Exists(path))
                {
                    report.Errors.Add($"{where}: file not found: {version.File}");
                    continue;
                }

                var bytes = File.ReadAllBytes(path);
                var sha = Sha256(bytes);
                if (sha != version.Sha256) report.Errors.Add($"{where}: SHA-256 mismatch (file {sha.Substring(0, 12)}…, manifest {(version.Sha256 ?? "").PadRight(12).Substring(0, 12)}…).");
                if (bytes.LongLength != version.Bytes) report.Errors.Add($"{where}: size mismatch (file {bytes.LongLength}, manifest {version.Bytes}).");
                var detected = Detect(bytes);
                var declared = string.IsNullOrEmpty(version.ContentType) ? PhotoReviewIngest.ContentTypeForFile(version.File ?? "") : version.ContentType;
                if (detected == null || detected != declared) report.Errors.Add($"{where}: file content is {detected ?? "not a supported image"}, expected {declared}.");
                using var codec = SKCodec.Create(new SKMemoryStream(bytes));
                if (codec == null)
                {
                    report.Errors.Add($"{where}: image could not be decoded.");
                    continue;
                }

                var rawW = codec.Info.Width;
                var rawH = codec.Info.Height;
                var rotated = Rotates(codec.EncodedOrigin);
                var (w, h) = rotated ? (rawH, rawW) : (rawW, rawH);
                var matches = (w == version.Width && h == version.Height) || (rawW == version.Width && rawH == version.Height);
                if (!matches) report.Errors.Add($"{where}: dimensions are {w}×{h}, manifest says {version.Width}×{version.Height}.");
                files[sha] = new VerifiedFile { Path = path, Sha256 = sha, Bytes = bytes.LongLength, ContentType = detected ?? "", Width = w };
            }
        }

        return files;
    }

    private static async Task<Dictionary<string, ThumbInfo>> UploadAsync(IReviewBlobStore store, Dictionary<string, VerifiedFile> files, Options options)
    {
        var thumbs = new System.Collections.Concurrent.ConcurrentDictionary<string, ThumbInfo>();
        var skipThumbs = options.Has("skip-thumbs");
        var parallel = int.TryParse(options.Get("parallel"), out var p) ? Math.Clamp(p, 1, 8) : 4;
        var done = 0;
        long uploaded = 0;
        await Parallel.ForEachAsync(files.Values, new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (file, _) =>
        {
            var name = ReviewBlobNames.Asset(file.Sha256);
            var existing = await store.SizeAsync(name);
            if (existing == null)
            {
                var bytes = await File.ReadAllBytesAsync(file.Path);
                if (Sha256(bytes) != file.Sha256) throw new ToolException($"{file.Path} changed during import.");
                // Create-only: an existing blob with this content hash is never overwritten.
                await store.TryWriteAsync(name, bytes, file.ContentType, null, createOnly: true);
                Interlocked.Add(ref uploaded, bytes.LongLength);
            }
            else if (existing.Value != file.Bytes)
            {
                throw new ToolException($"Stored {name} has {existing} bytes but the file has {file.Bytes}. Refusing to continue.");
            }

            var widths = new List<int>();
            foreach (var width in PhotoReviewIngest.ThumbWidths)
            {
                var target = Math.Min(width, file.Width);
                if (widths.Contains(target)) continue;
                var thumbName = ReviewBlobNames.Thumb(file.Sha256, target);
                if (!skipThumbs && !await store.ExistsAsync(thumbName))
                {
                    await store.TryWriteAsync(thumbName, MakeThumb(file.Path, target), "image/webp", null, createOnly: true);
                }

                if (await store.ExistsAsync(thumbName)) widths.Add(target);
            }

            thumbs[file.Sha256] = new ThumbInfo { Widths = widths, ContentType = "image/webp" };
            var count = Interlocked.Increment(ref done);
            if (count % 10 == 0 || count == files.Count) Console.WriteLine($"  {count}/{files.Count} files ready ({Interlocked.Read(ref uploaded) / 1048576.0:F0} MB uploaded)");
        });
        return new Dictionary<string, ThumbInfo>(thumbs);
    }

    /// <summary>Browsing preview only (EXIF-oriented, resized, WebP keeps transparency). The artifact itself is untouched.</summary>
    internal static byte[] MakeThumb(string path, int width)
    {
        using var codec = SKCodec.Create(path) ?? throw new ToolException($"{path} could not be decoded.");
        var origin = codec.EncodedOrigin;
        var rotated = Rotates(origin);
        var rawW = codec.Info.Width;
        var rawH = codec.Info.Height;
        var orientedW = rotated ? rawH : rawW;

        // JPEG can decode at 1/2, 1/4, 1/8 cheaply; stay at or above the target size for quality.
        var scaled = codec.GetScaledDimensions((float)width / orientedW);
        if ((rotated ? scaled.Height : scaled.Width) < width) scaled = new SKSizeI(rawW, rawH);
        var info = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) throw new ToolException($"{path} could not be decoded ({result}).");

        var (dw, dh) = (decoded.Width, decoded.Height);
        var (ow, oh) = rotated ? (dh, dw) : (dw, dh);
        var targetW = Math.Min(width, ow);
        var targetH = Math.Max(1, (int)Math.Round(oh * (double)targetW / ow));
        using var surface = SKSurface.Create(new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale((float)targetW / ow, (float)targetH / oh);
        canvas.Concat(OrientationMatrix(origin, dw, dh));
        using var source = SKImage.FromBitmap(decoded);
        canvas.DrawImage(source, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var snapshot = surface.Snapshot();
        // Metadata is not copied into the thumbnail; the original keeps all of its own.
        using var data = snapshot.Encode(SKEncodedImageFormat.Webp, width <= 480 ? 72 : 80);
        return data.ToArray();
    }

    internal static bool Rotates(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>Maps stored pixel coordinates (w × h) to display coordinates for an EXIF orientation.</summary>
    internal static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity
    };

    internal static string? Detect(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return "image/webp";
        return null;
    }

    internal static string? ReadNotes(string root, string relative)
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        return path.StartsWith(System.IO.Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? File.ReadAllText(path) : null;
    }

    internal static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void Report(IngestReport report, string title)
    {
        Console.WriteLine($"{title}: {report}");
        foreach (var error in report.Errors.Take(60)) Console.WriteLine("  error: " + error);
        if (report.Errors.Count > 60) Console.WriteLine($"  … {report.Errors.Count - 60} more errors");
        foreach (var warning in report.Warnings.Take(30)) Console.WriteLine("  warning: " + warning);
    }
}

public sealed class ToolException : Exception
{
    public ToolException(string message) : base(message) { }
}

internal sealed class Options
{
    public string Command { get; private set; } = "";
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public static Options Parse(string[] args)
    {
        var options = new Options { Command = args.Length > 0 ? args[0] : "" };
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) throw new ToolException($"Unexpected argument \"{args[i]}\".");
            var key = args[i].Substring(2);
            string? value = null;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) value = args[++i];
            options._values[key] = value;
        }

        return options;
    }

    public bool Has(string key) => _values.ContainsKey(key);
    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;
    public string Require(string key) => Get(key) ?? throw new ToolException($"--{key} is required.");
}
