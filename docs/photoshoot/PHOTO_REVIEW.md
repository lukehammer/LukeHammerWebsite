# Photo Review

Public review of the Lucky Luke Magic studio shoot: every planned shot next to what was captured, with ratings, comments, pose suggestions, a second-shoot plan and a mentor walkthrough. Plan: [PHOTO_REVIEW_PLAN.md](PHOTO_REVIEW_PLAN.md). Codex delivery format: [ARTIFACT_MANIFEST.md](ARTIFACT_MANIFEST.md). Live status: [claude-website-status.md](claude-website-status.md).

## Pages

| URL | Who | What |
| --- | --- | --- |
| `/photoshoot/review` | Everyone | All 118 planned shots in nine categories. Status tiles, search, status/evidence/coverage/warnings/must-have filters, Shots or Photos view |
| `/photoshoot/review/shot/{shotId}` | Everyone | Planned brief, expression and pose, crop, references next to captured photos. Previous/next. Ratings, discussion, status and pose suggestions. Luke sets the status here |
| `/photoshoot/review/photo/{photoId}` | Everyone | Large preview, every version (original and derivatives) with lineage, provenance, preview and download, or "Not created yet". Selection notes, comments. Luke can hide a photo |
| `/photoshoot/suggestions` | Everyone | New pose ideas, all suggestions, Luke approves or declines |
| `/photoshoot/second-shoot` | Everyone | Plan of shots to redo plus approved ideas, by priority. Print, text and JSON export. Luke edits priority, notes, leave out |
| `/photoshoot/walkthrough` | Everyone | Mentor tour of how the shoot was planned, shot, matched and reviewed |
| `/photoshoot/people` | Luke | Participants, activity, contributions; remove a person with everything they wrote, restore, audit log |

## Statuses

- **Luke's status** (Needs review, Successful, Partially successful, Unsuccessful, Not captured) is authoritative and only he can set it.
- **Provisional status** is Codex's capture assessment (`capture-assessments.json`, imported as suggestions: 62 Successful, 30 Partially successful, 26 Not captured). It is drawn dashed and labeled "provisional, Codex". Filters, tiles and the second-shoot plan use Luke's status where he has set one and the provisional status otherwise. Luke's decision always wins, and re-imports never overwrite it.
- **Match evidence** per photo: Slate-supported, Inferred or Approximate. Warnings flag the unreliable source-827 slate and a mirrored "Subject Right" title.

## Who can do what

| Caller | Identity | Can |
| --- | --- | --- |
| Anyone | none | Read everything public, view and download photos |
| Visitor | Display name → server-issued opaque id + 256-bit secret in an HttpOnly cookie (only its hash is stored) | One current 1–10 rating per photo, comments and replies, suggestions; edit or withdraw **own** items only |
| Luke | Microsoft sign-in as lukeroberthammer@outlook.com (the only admin account; the business address luke@lukehammermagic.com is not admin) **and** the provider subject bound on first sign-in (or app setting `PHOTOREVIEW_ADMIN_USER_ID`) | Statuses, hide photos, queue edits, approve suggestions, remove/restore people. Planner editing and hard delete |
| Planner password (public in the repo) | `X-PhotoShoot-Password` / cookie | **Photographer only**: read the plan, upload finished photos, limited session commands. Never edit, delete, export or moderate |

Writes need header `X-Photo-Review: 1` and are refused when `Sec-Fetch-Site: cross-site`. The visitor cookie is `HttpOnly; Secure; SameSite=Lax; Path=/api/photoreview`. Display names are labels, never identities. Duplicate names show with a short tag (`#3f2a`).

App settings (all optional):

| Setting | Effect |
| --- | --- |
| `PHOTOREVIEW_ADMIN_USER_ID` | Pins the admin to this SWA user id (overrides the stored binding) |
| `PHOTOREVIEW_ADMIN_AUTOBIND=false` | Disables first-sign-in binding |
| `PHOTOSHOOT_LEGACY_PASSWORD` | `off` disables the planner password; `owner` temporarily restores owner level (not recommended). Default: photographer |

## Moderation

**Remove person** (typed-name confirmation) soft-removes all their ratings, comments and suggestions in one atomic save, recalculates rating totals, and blocks further submissions from that browser identity. Other people's replies stay under a "This comment was removed" placeholder. **Restore** brings back exactly what that removal hid (not what the person deleted themselves). Both are audited. Nothing is hard-deleted. A new browser can start a new identity; that is accepted, and there is no fingerprinting.

## Storage and assets

Private container `photoshoot` (same account as the planner):

```
review/{shootId}/catalog.json        photos, versions, shot rows (suggestions, Luke's status), queue overrides
review/{shootId}/participation.json  visitors (hashed secrets), votes + totals, comments, suggestions, audit
review/admin-binding.json            bound Microsoft subject (create-only)
review/assets/{sha256}               artifact bytes as delivered
review/thumbs/{sha256}-{w}.webp      480 / 1280 px browsing thumbnails
```

Thumbnails stream from the API with immutable caching. Full files redirect to a 15-minute read-only SAS for that one blob, with an SEO download name (`lucky-luke-magic-…`). Hidden photos and superseded versions are visible to Luke only. Planner reference images are served only when linked to a planned shot.

## Local development and tests

Local and production share one storage account. **Never put the connection string in `Api/local.settings.json` for review testing.** Use an isolated folder:

```json
"LUKE_HAMMER_WEBSITE": "", "AzureWebJobsStorage": "", "PHOTOSHOOT_LOCAL_DIR": "C:/Users/<you>/AppData/Local/Temp/photoreview-e2e/store"
```

Seed it with a copy of a planner JSON and synthetic images via the importer's `--local-dir`. Unit tests (`dotnet test UnitTest/UnitTest.csproj --filter "FullyQualifiedName~PhotoReview|FullyQualifiedName~PhotoShoot"`) use temp folders and synthetic data only.

## Production smoke tests

Use visitor names starting with `[SMOKE TEST] ` and remove them afterwards. The command only touches visitors whose name starts with the prefix (minimum 8 characters), backs up `participation.json` first, and is a dry run without `--confirm`:

```powershell
$env:AZURE_STORAGE_CONNECTION_STRING = az storage account show-connection-string -n lukehammerweb -g lukehammer-rg --query connectionString -o tsv
dotnet run --project tools/PhotoReviewImport -c Release -- purge-test-data --prefix "[SMOKE TEST] Claude" --live            # dry run
dotnet run --project tools/PhotoReviewImport -c Release -- purge-test-data --prefix "[SMOKE TEST] Claude" --live --confirm
Remove-Item Env:AZURE_STORAGE_CONNECTION_STRING
```
