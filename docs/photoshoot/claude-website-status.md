# Claude website status (photo review)

_Updated 2026-11-02 22:33 UTC (14:33 PST) by Claude Code. No credentials in this file._

## Deployed

- **Production:** https://lukehammer.com/photoshoot/review (also `/photoshoot/walkthrough`, `/photoshoot/second-shoot`, `/photoshoot/suggestions`, `/photoshoot/people`).
- PR [#6](https://github.com/lukehammer/LukeHammerWebsite/pull/6) merged into `main` as `4d1607d`. Workflow run 41532402494 succeeded ("Deployment Complete", 21:56 UTC). The PR preview build also passed and was checked first (read-only).
- Follow-up PR [#7](https://github.com/lukehammer/LukeHammerWebsite/pull/7) (smoke-test cleanup command) merged as `d5e59df`; workflow 41533772731 succeeded.
- PR [#9](https://github.com/lukehammer/LukeHammerWebsite/pull/9) merged as `5ad0601`; workflow 41536857353 succeeded 22:26:30 UTC.
  - Every planner page (all sign-ins, including the legacy photographer password) has a banner to the photo review with live counts (155 photos, 92/118 poses, provisional 62/30/26, Luke reviewed 0).
  - Dashboard counters, progress bars and the session pill are titled **Original on-set records**. The recorded 0 finished / 1 captured / 117 remaining / session left open are kept; no planner data or capture statuses were changed.
  - The walkthrough describes what the director, photographer and slate pages provide instead of asserting which devices were used. The slate evidence (125 slate-supported) is kept.

- Photo viewer usability PR, from Codex's live check. The photo page now has a version selector above the image (accessible toggle buttons, one per available version). The caption announces the shown version's size and parent and links to download or full size. The version list is compact rows (name, dimensions and size, parent and provenance, Preview, Download), with full SEO filename, delivered name, processing notes, tool, import time and SHA-256 under a per-version **Details** element, collapsed by default. **Preview** in the list switches the image and moves focus to it, so it scrolls into view. Also fixed: the transparency checkerboard rendered as one large diamond. No data, hashes, classifications, comments or votes changed. Checked before merge with this client build against production data via a GET-only local proxy: 1265×900 and 390×844, every tab, download links match the shown version, Preview from the bottom of the list brings the viewer fully into view. Final production check: see the report to Codex.

- Phone layout fix (Luke's request, 2026-11-02 ~18:22 PST): the two-column card grid is kept. Status pills ("Successful · provisional, Codex" etc.) were clipped at the card edge; they now wrap inside the card (label and attribution can take separate lines), with nothing hidden or truncated. Evidence tags, badges and hints can also wrap. Checked before merge against production data (GET-only proxy) at 320/360/390/412 px: 2 columns, 0 px horizontal overflow, 0 clipped chips on the review grid (446 chips), Photos view, a partial-status shot page, the second-shoot plan and pose ideas.

## Admin sign-in

- **Admin account changed (Luke's explicit request, 2026-11-03):** the sole admin is now the Microsoft account **lukeroberthammer@outlook.com** (`PhotoReviewAdmin.AdminEmail`). It replaces the business address rather than adding to it. The business account luke@lukehammermagic.com is now NotAdmin. Still required: provider `aad` plus the bound provider subject.
- **The existing production binding is stale and is not reused.** `review/admin-binding.json` still records the business account (bound 02:21:29 UTC). With the new code, a binding recorded for a different email never authorizes anyone. The Outlook sign-in gets "not the bound admin account", and the create-only binding can't be overwritten automatically. So nobody is admin until **Codex backs up and resets that blob**; the next matching Outlook sign-in then binds. `PHOTOREVIEW_ADMIN_USER_ID` is **not** configured (the only app settings are the storage connection and `NTFY_TOPIC`).
- Production admin remains **unverified** until Luke signs in with the Outlook account after the reset.

Earlier record (fact only):

- `review/admin-binding.json` **now exists**, created 2026-11-03 02:21:29 UTC (18:21:29 PST) by the create-only first-sign-in path: provider `aad`, account email `luke@lukehammermagic.com`, 32-character subject starting `072da9…`. That path only runs for a Microsoft principal with exactly that email. No `PHOTOREVIEW_ADMIN_USER_ID` app setting is set. The allowlist was not broadened and nothing was bound by Claude.
- **Not yet confirmed by Luke** that this was his sign-in, so production admin is still **unverified**. Luke's reported `/.auth/me` → `clientPrincipal: null` is unexplained. One possibility to check, not a conclusion: SWA sign-in cookies are per host, so signing in on `lukehammer.com` and checking `www.lukehammer.com/.auth/me` (or the reverse) shows null.
- If Luke confirms, the People page at `/photoshoot/people` should load for him. If the binding is **not** his, removing that one blob resets it (the next matching sign-in rebinds). Claude has not done that and won't without Luke's word.

## For Codex

- Schema and mechanism: [ARTIFACT_MANIFEST.md](ARTIFACT_MANIFEST.md); importer `tools/PhotoReviewImport`.
- **Derivatives are live (imported by Codex, finished 22:16 UTC):** all 155 photos have all six versions (930 assets, 1,860 thumbnails, catalog import #3). Claude verified read-only: 155 originals unchanged (same SHA-256 as the selection); all 775 derivatives NonGenerative, with correct parents (Cropped, Transparent and WhiteBackground ← Original; CroppedTransparent ← Transparent; CroppedWhiteBackground ← WhiteBackground) and thumbnails; no superseded versions; Luke statuses untouched; random sample of 33 derivatives downloaded through the website matched catalog hashes 33/33.
- Capture assessments are live as **provisional** suggestions (62 / 30 / 26), labeled "provisional, Codex". Luke's statuses always win.

## Production verification (2026-11-02, real Azure SWA)

| Check | Result |
| --- | --- |
| Summary | 118 shots, 155 photos, 92 with photos / 26 without, provisional 62/30/26, Luke-confirmed 0, all 6 version kinds × 155 |
| **All 155 originals downloaded through the website** (API → SAS redirect, hashed in memory, nothing written to disk) | **155/155 SHA-256 identical** to the selected originals |
| Forged `x-ms-client-principal` header | stripped by SWA → 401, not admin |
| Public password `Luke` | `Photographer`; plan edit → 403 |
| Old `LukeAdmin` | 401 |
| Writes without `X-Photo-Review` | 403 |
| Anonymous blob listing/read on the storage account | refused (409) |
| Visitor cookie | `secure; samesite=lax; httponly; path=/api/photoreview` |
| Smoke test with `[SMOKE TEST] Claude A/B/C` | rating 0 → 400, 7→9 replaces, two-person average correct; other's edit/delete → 403; own edit/delete OK; author-deleted comment → placeholder, other's reply kept; suggestions pending, plan unchanged; visitor status-set/moderation → 401 |
| Cleanup | `purge-test-data` removed exactly those 3 visitors; participation back to 0 people/ratings/comments/suggestions. Backup `%LOCALAPPDATA%\LukeHammerPhotoshoot\backups\20261102T220037Z-participation-before-purge\` |
| Rendering | production review home (390 px) and a shot page (1366 px) render with real photos |

## Tests

- `dotnet test UnitTest/UnitTest.csproj -c Release`: **354 passed, 1 failed**. The failure is the pre-existing, date-dependent sports test, which also fails on `main`.
- The earlier full-run-only failure was a Windows file-lock race in the **local** test store. Fixed with retries on transient `IOException`, and the store-swapping test classes are serialized. Stable across repeated runs.
- Local HTTP end-to-end against an isolated temp store with synthetic images covers the same matrix plus admin binding, a same-email/different-subject spoof (403), and remove/restore with totals and audit.

## Production proof screenshots

Folder `C:\Users\LukeR\AppData\Local\LukeHammerPhotoshoot\proof\20261102T2226Z-production\`, captured 22:26:49 to 22:31:07 UTC against `5ad0601`. See `README.txt` there. Main files:
- `review-dashboard-1366x900.png` (review dashboard)
- `photo-versions-1366x900.png` (photo page versions table) and `photo-versions-full-1366.png` (all 6 versions, full page)
- `photo-versions-390.png` (mobile)
- `planner-dashboard-photographer-1366x900.png` and `planner-dashboard-photographer-390.png` (legacy photographer session: banner and on-set record labels)
- `walkthrough-1366.png`

They contain real photos, so they are kept out of the repository. At 22:32 UTC the planner blob was still last modified 04:56 UTC (untouched).

## Remaining manual step

- **Luke must sign in once with Microsoft** (https://lukehammer.com/photoshoot/review → "Luke: sign in", account **lukeroberthammer@outlook.com**) **after Codex resets the stale business-account binding**. The first matching sign-in binds the admin subject. Until then **nobody** is admin. The site behaves correctly without it: everything read-only and visitor features work. Admin flows (status setting, People, remove/restore) are verified only locally with a simulated principal, not yet in production. Optional afterwards: pin the bound id as app setting `PHOTOREVIEW_ADMIN_USER_ID`. **As of 22:32 UTC no admin binding exists**, so Luke has not signed in yet, and production admin is **not** verified.

## Next

- Luke's one-time Microsoft sign-in (above), then optionally pin `PHOTOREVIEW_ADMIN_USER_ID`.
- Later Codex deliveries: derivative-only manifests via the importer; identical hashes are no-ops, new hashes supersede and keep history.
