// Syncs the public photo library into a local folder, ready for MagicianCRM uploads.
// Usage: node tools/photo-library/sync.mjs --out <folder> [--manifest <url or file>]
//
// Each run downloads only files it has not seen before (tracked in <folder>/synced.json),
// writes <folder>/last-sync.json, and prints what to upload and what to remove from MagicianCRM.
// Older files are kept, so nothing is lost when a newer photo takes over a pose.
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { dirname, extname, join } from "node:path";

const DEFAULT_MANIFEST = "https://lukehammer.com/api/photoreview/library/manifest";

const args = parseArgs(process.argv.slice(2));
if (!args.out) fail("Pass --out <folder> for the local photo library copy.");

const ledgerPath = join(args.out, "synced.json");
const ledger = existsSync(ledgerPath) ? JSON.parse(readFileSync(ledgerPath, "utf8")) : { files: {} };
const today = new Date().toISOString().slice(0, 10);

const items = await loadManifest(args.manifest ?? DEFAULT_MANIFEST);
const liveUrls = new Set(items.flatMap((item) => item.files.map((file) => file.url)));

const newFiles = [];
const failed = [];
for (const item of items) {
  for (const file of item.files) {
    const entry = ledger.files[file.url];
    if (entry) {
      // Back on the public page after a retirement: clear the flag.
      delete entry.retiredOn;
      continue;
    }

    const path = join(args.out, "files", file.kind, `${item.photoKey}-${versionId(file.url)}${extname(file.fileName) || ".jpg"}`);
    try {
      await download(file.downloadUrl, path);
      const record = {
        photoKey: item.photoKey,
        poseKey: item.poseKey,
        title: item.title,
        category: item.category,
        kind: file.kind,
        fileName: file.fileName,
        path,
        syncedOn: today
      };
      ledger.files[file.url] = record;
      newFiles.push(record);
    } catch (error) {
      failed.push(`${file.kind} ${item.photoKey}: ${error.message}`);
    }
  }
}

// Files that were synced before but are no longer public (hidden photo, or a version replaced).
const retired = [];
for (const [url, entry] of Object.entries(ledger.files)) {
  if (liveUrls.has(url)) continue;
  if (!entry.retiredOn) {
    entry.retiredOn = today;
    retired.push(entry);
  }
}

mkdirSync(args.out, { recursive: true });
writeJson(ledgerPath, ledger);

const current = items.filter((item) => item.isCurrent).length;
const summary = {
  syncedOn: today,
  library: { photos: items.length, current, earlier: items.length - current },
  newFiles: newFiles.map(({ path, kind, fileName, title }) => ({ path, kind, fileName, title })),
  retired: retired.map(({ path, kind, title }) => ({ path, kind, title })),
  failed
};
writeJson(join(args.out, "last-sync.json"), summary);

console.log(`Library: ${items.length} photos (${current} current, ${items.length - current} earlier).`);
console.log(`Downloaded ${newFiles.length} new file(s) into ${join(args.out, "files")}.`);
for (const file of summary.newFiles) console.log(`  + ${file.kind}: ${file.path}`);
console.log(`Public no longer: ${retired.length} file(s). Remove these from MagicianCRM.`);
for (const file of summary.retired) console.log(`  - ${file.kind}: ${file.title} (${file.path})`);
for (const problem of failed) console.log(`FAILED ${problem}`);
if (failed.length > 0) process.exitCode = 1;

async function loadManifest(source) {
  if (/^https?:\/\//.test(source)) {
    const response = await fetch(source);
    if (!response.ok) fail(`Manifest request failed: HTTP ${response.status} from ${source}`);
    return response.json();
  }

  return JSON.parse(readFileSync(source, "utf8"));
}

async function download(url, path) {
  const response = await fetch(url, { redirect: "follow" });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  mkdirSync(dirname(path), { recursive: true });
  const partial = `${path}.part`;
  writeFileSync(partial, Buffer.from(await response.arrayBuffer()));
  renameSync(partial, path);
}

function versionId(url) {
  return url.match(/versions\/([0-9a-f]+)\/file/)?.[1] ?? "version";
}

function writeJson(path, value) {
  mkdirSync(dirname(path), { recursive: true });
  const partial = `${path}.part`;
  writeFileSync(partial, JSON.stringify(value, null, 2));
  renameSync(partial, path);
}

function parseArgs(argv) {
  const parsed = {};
  for (let i = 0; i < argv.length; i += 2) {
    const key = argv[i];
    if (!key.startsWith("--")) fail(`Unexpected argument ${key}.`);
    parsed[key.slice(2)] = argv[i + 1];
  }

  return parsed;
}

function fail(message) {
  console.error(message);
  process.exit(1);
}
