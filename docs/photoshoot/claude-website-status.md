# Claude website status (photo review)

_Updated 2026-11-02 ~15:05 Pacific by Claude Code. No credentials in this file._

## Deployed

- **Production:** https://lukehammer.com/photoshoot/review (also `/photoshoot/walkthrough`, `/photoshoot/second-shoot`, `/photoshoot/suggestions`, `/photoshoot/people`).
- PR [#6](https://github.com/lukehammer/LukeHammerWebsite/pull/6) merged into `main` as `4d1607d`. Workflow run 41532402494 succeeded ("Deployment Complete", 21:56 UTC). The PR preview build also passed and was checked first (read-only).
- Follow-up PR: smoke-test cleanup command (see below).

## For Codex

- Schema and mechanism: [ARTIFACT_MANIFEST.md](ARTIFACT_MANIFEST.md); importer `tools/PhotoReviewImport`.
- **Observed:** your live import of `derivative-review-manifest.json` (155 photos × 5 kinds = 775 versions, NonGenerative, parents set) has been running since 13:40 Pacific (PID 470416, Debug importer build). At ~15:00 it had uploaded 558 of the ~930 asset blobs. The catalog has **not** been written yet (it is the final step). Claude has not run or duplicated it and has not rebuilt the Debug binaries it uses. Production currently shows 0 derivatives, correctly; they appear automatically when the catalog save completes.
- Capture assessments are live as **provisional** suggestions (62 / 30 / 26), labeled "provisional, Codex". Luke's statuses always win.

## Production verification (2026-11-02, real Azure SWA)

| Check | Result |
| --- | --- |
| Summary | 118 shots, 155 photos, 92 with photos / 26 without, provisional 62/30/26, Luke-confirmed 0, derivatives 0 |
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

## Remaining manual step

- **Luke must sign in once with Microsoft** (https://lukehammer.com/photoshoot/review → "Luke: sign in", account luke@lukehammermagic.com). The first matching sign-in binds the admin subject. Until then **nobody** is admin. The site behaves correctly without it: everything read-only and visitor features work. Admin flows (status setting, People, remove/restore) are verified only locally with a simulated principal, not yet in production. Optional afterwards: pin the bound id as app setting `PHOTOREVIEW_ADMIN_USER_ID`.

## Next

- After Codex's import finishes: verify derivative counts and lineage read-only, and download-check a sample of derivatives.
