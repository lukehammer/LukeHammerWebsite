# Claude website status (photo review)

_Updated 2026-11-02 ~14:00 Pacific by Claude Code. No credentials in this file._

## For Codex

- Schema and upload mechanism: [ARTIFACT_MANIFEST.md](ARTIFACT_MANIFEST.md). Importer: `tools/PhotoReviewImport` in worktree `C:\Users\LukeR\source\repos\LukeHammerWebsite-review` (branch `claude/photo-review`).
- **Observed at ~13:50:** a live import of `derivative-review-manifest.json` (155 photos × 5 kinds = 775 non-generative versions) is running from the Debug importer build (PID 470416, started 13:40). Claude is **not** running or duplicating it. Once it exits, Claude verifies the catalog read-only. Claude is not rebuilding the Debug importer binaries while it runs.
- Capture assessments: `capture-assessments.json` is imported as **suggestions** (62 Successful, 30 Partially successful, 26 Not captured; `ownerApproved` ignored). Live catalog rev 2 at 21:32 UTC.

## Current activity

Opening the PR, preview deployment, then merge and production verification.

## Done

- Worktree on `main` @ `f0ba777`; the main checkout's unrelated uncommitted changes are untouched.
- Planner JSON backed up before any change (`%LOCALAPPDATA%\LukeHammerPhotoshoot\backups\20261102T201752Z-pre-photo-review\`). The planner document has **not** been modified.
- **155 originals live** (865.1 MB, 310 thumbnails), **155/155 byte-identical** (MD5/size/type). Catalog backups before each import under `…\backups\*-review-import\`.
- Provisional status integration: tiles, filters and the second-shoot plan use Luke's status, else the labeled Codex suggestion. Dashed "provisional, Codex" chips; Luke's verdicts are solid "· Luke". Nothing says "confirmed by Luke" unless he set it.
- **Legacy password = photographer only, by default, before and after admin binding.** `LukeAdmin` removed. All owner/admin mutations need the bound Microsoft account (luke@lukehammermagic.com; no app-imposed MFA).
- UI: review home (118 shots, nine categories, search and filters, shots/photos views), shot page (planned vs captured, references, suggestions, thread, prev/next), photo page (versions/lineage/downloads, "Not created yet"), pose ideas, second-shoot plan (priorities, notes, print, text/JSON export), mentor walkthrough, People (remove/restore + audit), planner gate links.
- Tests: **353 passed, 1 failed** (the pre-existing date-dependent sports test). The 179 photo-review/planner tests are stable across 6 repeated runs. Earlier full-run-only failure: a Windows file-lock race in the *local* test store (concurrent atomic replace vs read). Fixed with retry on transient `IOException` in `LocalReviewStore`, and the store-swapping test classes are serialized.
- Local end-to-end (isolated temp store, synthetic images, same-origin proxy that strips client principals like SWA). HTTP results: CSRF missing/cross-site → 403; rating 0/11 → 400; update replaces; forged cookie → 401; other's comment edit/delete → 403; password → Photographer, plan edit/delete/export → 403; `LukeAdmin` → 401; forged principal header → stripped/401; admin binds on first `/me`; same email with another subject → 403; remove person → totals 6.5/2 → 4/1, placeholder keeps the other's reply, removed user blocked; restore → 6.5/2; audit logged; original download byte-identical with SEO filename; hidden photo → public 404. Rendered pages inspected at 1366 px and 390 px (no horizontal overflow).

## Blockers / manual steps

- **Luke must sign in with Microsoft once** at https://lukehammer.com/photoshoot/review ("Luke: sign in") to bind the admin. That has not happened, so admin is unverified in production.

## Next

PR → preview check → merge → workflow → production smoke tests (labeled temp data, cleaned up) → verify all 155 original downloads via the website.
