# Claude website status (photo review)

_Updated 2026-11-02 by Claude Code. No credentials in this file._

## For Codex: artifact delivery

- Schema and exact upload mechanism: **[ARTIFACT_MANIFEST.md](ARTIFACT_MANIFEST.md)**.
- The importer `tools/PhotoReviewImport` **is usable now** from the worktree `C:\Users\LukeR\source\repos\LukeHammerWebsite-review` (branch `claude/photo-review`). Run a dry run first; `--live` writes to production storage.
- **The 155 originals are already imported** (photoKey = original file stem, e.g. `lucky-luke-magic-warm-smile-headshot-01`). For derivatives, send a derivative-only manifest that lists those photoKeys with the new versions and `parentKind`. Don't re-send originals; if you do, identical hashes are a no-op and a changed hash is refused.
- The website never claims a derivative exists until it has been imported. Every missing kind shows "Not created yet".

## Current activity

Milestone 2/3: review pages and participation UI (client), then local end-to-end test, then deploy.

## Done

- Environment: git works normally (`core.bare=false`). The main checkout has someone else's uncommitted catalog edits, so the work is in a separate worktree (above), from `main` @ `f0ba777`.
- Baseline tests before changes: 285 passed, 1 failed. The failure is a pre-existing, date-dependent sports test (`WoodlandFootball_Jv_schedule_is_upcoming_only_and_ordered`) and is unrelated.
- Production planner JSON backed up (no images) to `%LOCALAPPDATA%\LukeHammerPhotoshoot\backups\20261102T201752Z-pre-photo-review\`. The planner document has **not** been modified.
- Shared review model, ingestion, participation, moderation, projection and admin rules, with 61 tests (`dotnet test UnitTest/UnitTest.csproj --filter FullyQualifiedName~PhotoReview`). All passing.
- API `/api/photoreview/*` written (not deployed yet).
- **Live import done, 2026-11-02:** 155 originals (865.1 MB) and 310 thumbnails (480 and 1280 px WebP, 18.9 MB) are in the private `photoshoot` container under `review/`, plus `review/f77cc235728d/catalog.json` (rev 1: 155 photos, 118 shot rows, 26 "no dedicated match"). **155/155 verified byte-identical** (service MD5, size and type match the local files). Import log: `%LOCALAPPDATA%\LukeHammerPhotoshoot\manifests\live-import-1.log`.
- Evidence classification from the selection files: 125 slate-supported, 22 inferred, 8 approximate. Warnings: 2 photos that rely on the unreliable source-827 slate, and 1 "Subject Right" hero banner captured facing the other way. Shot 118's coverage note keeps the 827 contradiction.

## Security note

The repository is public, and the planner's hard-coded passwords (`Luke` = owner, `LukeAdmin` = admin with hard delete) are visible in it. On this branch: `LukeAdmin` is removed; admin requires Microsoft sign-in as luke@lukehammermagic.com bound to its provider subject; `Luke` stays owner only until that binding exists, then drops to photographer. This takes effect when deployed.

## Blockers

- None. Luke must sign in with Microsoft once, interactively, to bind and verify admin. That has not happened.

## Next

Client pages → local end-to-end with synthetic data (isolated folder) → PR and merge → workflow → production smoke tests with labeled temporary data, then cleanup.
