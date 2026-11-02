# Photo review artifact manifest (for Codex)

How to deliver original and derivative image versions to the live photo review at `https://lukehammer.com/photoshoot/review`.

**Mechanism:** write a manifest JSON next to the files, then run the importer:

```powershell
cd C:\Users\LukeR\source\repos\LukeHammerWebsite-review   # or the main checkout once merged
# 1. Dry run (default). Reads files, checks SHA-256/size/dimensions, prints the plan. Writes nothing.
dotnet run --project tools/PhotoReviewImport -- import --manifest "<dir>\review-manifest.json" --root "<dir>"

# 2. Live import into the production shoot. Storage is shared by local and production, so this IS production.
$env:AZURE_STORAGE_CONNECTION_STRING = az storage account show-connection-string -n lukehammerweb -g lukehammer-rg --query connectionString -o tsv
dotnet run --project tools/PhotoReviewImport -- import --manifest "<dir>\review-manifest.json" --root "<dir>" --live
Remove-Item Env:AZURE_STORAGE_CONNECTION_STRING
```

What a live import does, in order:
1. Validates the manifest and every file (exists, SHA-256 and byte size match, decodable, dimensions match). Any error stops the import before anything is written.
2. Backs up the current `review/{shootId}/catalog.json` to `%LOCALAPPDATA%\LukeHammerPhotoshoot\backups\<utc>-review-import\`.
3. Uploads each file to the private blob `review/assets/{sha256}` **only if it is not already there**. Bytes are uploaded unchanged, so downloads are byte-identical.
4. Makes browsing thumbnails (480 and 1280 px wide, EXIF-oriented; WebP keeps transparency) as `review/thumbs/{sha256}-{width}.webp`. These are previews only. The artifact itself is never modified.
5. Upserts the catalog under ETag concurrency. Photos are matched by `photoKey`, and versions by `kind` + `sha256`. Re-running the same manifest changes nothing. Ratings, comments, suggestions and owner statuses live in other records and are never touched.

The importer never touches the planner document (`shoots/{id}.json`) and never deletes blobs.

To test without production storage, use `--local-dir <empty folder>` instead of `--live`. It writes the same layout to that folder, which the local API reads with `PHOTOSHOOT_LOCAL_DIR`.

## File format

```json
{
  "format": "lucky-luke-photo-review-manifest",
  "version": 1,
  "shootId": "f77cc235728d",
  "generatedUtc": "2026-11-02T21:00:00Z",
  "generator": "codex",
  "shots": [
    {
      "shotId": "e75ee391f0f7",
      "shotNumber": 1,
      "title": "Warm Smile Headshot",
      "category": "Headshots",
      "coverage": "Selected",
      "coverageNotes": "See individual selection notes…",
      "suggestedStatus": null
    }
  ],
  "photos": [
    {
      "photoKey": "lucky-luke-magic-warm-smile-headshot-01",
      "shotId": "e75ee391f0f7",
      "shotNumber": 1,
      "category": "Headshots",
      "selection": "ProposedPrimary",
      "evidence": "SlateSupported",
      "evidenceText": "Slate-supported: source photo 1 shows shot 1; …",
      "reason": "The toothy smile, lifted cheeks …",
      "warnings": [],
      "notesFile": "Headshots/lucky-luke-magic-warm-smile-headshot-01-selection-notes.md",
      "source": { "photoNumber": 3, "url": "https://www.flickr.com/photos/…", "originalFileName": "102026-luke-hammer-3_…_o.jpg", "archive": "2026….zip" },
      "versions": [
        {
          "kind": "Original",
          "file": "Headshots/lucky-luke-magic-warm-smile-headshot-01.jpg",
          "sha256": "7638ff91…",
          "bytes": 5921334,
          "width": 3648,
          "height": 5472,
          "parentKind": null,
          "provenance": "Camera",
          "processingNotes": "",
          "tool": "",
          "createdUtc": null
        }
      ]
    }
  ]
}
```

### Rules

| Field | Rule |
| --- | --- |
| `shootId` | Must be the live shoot (`f77cc235728d`). |
| `shotId` | Stable planner shot id (12 lowercase hex). **Authoritative.** `shotNumber` and `title` are cross-checks only: a mismatch with the live planner is reported as an error, and nothing is imported. |
| `category` | One of `Headshots`, `FullBody`, `PointingAndPresenting`, `ChildishPoses`, `ComedyPoses`, `MagicAndProps`, `SeatedPortraits`, `AdvertisingAndSigns`, `KidsAndVolunteers`. The display names with spaces (for example "Full Body") are also accepted. |
| `coverage` (shot) | `Selected` or `NoDedicatedMatch`. This is evidence only. The authoritative status (Needs review / Successful / Partially successful / Unsuccessful / Not captured) is set by Luke on the site. `suggestedStatus` is optional and shown as "Codex suggests". |
| `photoKey` | Stable, unique, `[a-z0-9-]{1,100}`. Never reuse a key for a different photo. Use the original's file stem. |
| `selection` | `Primary`, `ProposedPrimary` or `Alternate`. |
| `evidence` | `SlateSupported`, `Inferred`, `Approximate` or `Unmatched`. |
| `warnings` | Short strings shown prominently, for example the source 827 contradictory Magic Print Shirt slate. |
| `notesFile` / `notesMarkdown` | The selection sidecar, as a path relative to `--root` or inline text. Local absolute paths (for example `C:\Users\…\Downloads\…`) are reduced to the file name before storing. |
| `versions[].kind` | `Original`, `Cropped`, `Transparent`, `CroppedTransparent`, `WhiteBackground`, `CroppedWhiteBackground`. At most one current version per kind. |
| `versions[].parentKind` | Required for every kind except `Original`: the version it was produced from (for example `CroppedTransparent` → `Transparent` or `Cropped`). It must exist already or appear in the same manifest. |
| `versions[].provenance` | `Camera` (original only), `NonGenerative` (crop, mask, levels, cleanup without synthesis) or `Generative` (any AI fill, extension or synthesis). |
| `versions[].file` | Path relative to `--root`. JPEG, PNG or WebP. Transparent kinds should be PNG or WebP with alpha. |
| `sha256`, `bytes`, `width`, `height` | Of the file exactly as delivered. They are verified. |

### Delivering derivatives later

A derivative-only manifest is fine: list each photo by `photoKey` with only its new versions. `shots` can be omitted. Behavior on re-import:

- Same `kind` and same `sha256`: no change.
- Same `kind`, new `sha256`: the new file becomes current, and the previous version is kept as superseded history (still downloadable by the admin).
- An `Original` whose `sha256` differs from the stored original: **refused**. Originals are immutable.
- An unknown `photoKey` without an `Original`: refused.

The site shows "Not created yet" for every kind a photo does not have. It never claims a derivative exists before it is imported.

## Building the manifest for the current selection

The 155 originals can be described automatically from `selection-index.csv`, `shot-coverage.csv` and the sidecars. Shot numbers are mapped to planner ids by the planner's order; all 118 titles were verified to match:

```powershell
dotnet run --project tools/PhotoReviewImport -- build-selection --collection "C:\Users\LukeR\OneDrive\Magic\marketing\Raw Photos\2026\Sherry photo shoot" --planner "<backup>\shoots\f77cc235728d.json" --out "<collection>\review-manifest.json"
```
