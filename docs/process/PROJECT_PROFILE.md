# Project profile: LukeHammerWebsite (lukehammer.com)

This is the project-specific half of the `agent-team` plugin's dispatcher playbook. The shared rules come from the plugin; this file holds only this project's facts. The facts come from the repo as of 2026-11-13 (`origin/main` at `6360812`). Items marked **OPEN** need Luke's answer (they're listed at the end). Link to docs instead of copying them. No secrets, emails or keys go in this file. **This repository is public on GitHub**, so the same goes for every file in it.

## 1. Product and people
- **What it is:** Luke's personal website, lukehammer.com. Besides the public pages (home, Lucky Luke, Services, Resume, Programmer, PDX Magic Jam), it holds several family and work tools:
  - **Family Events** (`/FamilyEvents`): potlucks with theme voting, RSVPs, sign-up lists and Q&A. See [README.md](../../README.md#family-events-potlucks).
  - **Sports** (`/Sports`): the kids' game schedule, editable by family. See [README.md](../../README.md#sports-schedule-crud).
  - **Photo Shoot planner, director and photo review** (`/photoshoot/...`) and the public **Photo Library** (`/photos`). See [docs/photoshoot/README.md](../photoshoot/README.md).
- **Owner:** Luke, a magician and performer, not a software expert. Use plain words.
- **Users or personas:**
  - Visitors (public pages, the photo library, AI tools reading the library data).
  - Family members (Family Events and Sports; no sign-in, they type their name).
  - Luke as organizer (Family Events and Sports, by organizer code).
  - Photo shoot owner and photographer (signed-in roles), plus photo-review visitors.

## 2. Repo and stack
- **Repo:** `lukehammer/LukeHammerWebsite`, **public**. Default branch: `main`.
- **Layout:**
  - `Client/`: Blazor WebAssembly site (pages in `Client/Pages`, photo shoot UI in `Client/PhotoShoot` and `Client/Pages/PhotoShoot`, static files in `Client/wwwroot`, SWA routes in `Client/staticwebapp.config.json`).
  - `Api/`: Azure Functions API (.NET isolated), with seed data in `Api/data`.
  - `Shared/`: models and rules shared by both.
  - `UnitTest/`: xUnit tests.
  - `tools/`, `scripts/`: one-off tools (seed formatting, photo import, copy-to-blob).
  - `docs/photoshoot/`: photo shoot docs, status and decisions.
- **Solution or entry points:** `BlazorStaticWebApps.sln`.

## 3. Commands
- **Build:** `dotnet build BlazorStaticWebApps.sln`
- **Unit tests:** `dotnet test UnitTest/UnitTest.csproj` (photo shoot only: add `--filter "FullyQualifiedName~PhotoShoot"`). One sports test depends on today's date and has failed on `main` before; treat it as known-flaky and say so when it fails.
- **E2E tests:** none yet. Browser checks so far were done by hand (see [STATUS.md](../photoshoot/STATUS.md)).
- **Run locally:** API with `scripts/start-api.ps1` (port 7071, creates `Api/local.settings.json` from the example). Client with `Client/restart-dev.ps1` (port 5000). Optional `swa start http://localhost:5000 --api-location http://localhost:7071` gives one address on port 4280.
- **Dev sign-in bypass:** photo shoot only, `PHOTOSHOOT_DEV_AUTH=true` in `Api/local.settings.json`. It works only when `WEBSITE_SITE_NAME` is not set, so never in Azure.

## 4. Worktrees and pushing
- **New worktree:** no script; the Claude Desktop worktree default (one worktree and one `claude/...` branch per child).
- **Finish and push:** no script. Build and run the unit tests in the worktree, push the branch, open a pull request into `main`, and merge after Luke's OK. Every past change went in by pull request.
- **Remove a worktree:** `git worktree remove <path>` after checking `git cherry origin/main <branch>` shows nothing unpushed. Delete the branch only for work that is merged or that Luke said to discard.
- **Shared state between worktrees:** the local API stores (`%TEMP%\lukehammer-sports-schedules.json`, `%TEMP%\lukehammer-photoshoot`) are shared by every worktree on this PC. Only one session may run the local API against them at a time.
- **Never edit the main checkout:** `C:\Users\LukeR\source\repos\LukeHammerWebsite`. On 2026-11-13 it sits on an old branch (`cursor/owner-full-access`) with uncommitted changes that are not the dispatcher's; leave them alone.

## 5. Hosting and deploy
- **Hosting:** Azure Static Web App `LukeHammerWebsite` (resource group `Luke_Hammer`), site lukehammer.com.
- **Deploy:** GitHub Actions ([workflow](../../.github/workflows/azure-static-web-apps-agreeable-wave-05d270b1e.yml)). A push to `main` deploys production. Each pull request gets its own preview (staging) site.
- **Preview limit:** the app allows only **three** preview sites. When all three are in use, the PR build fails ("maximum number of staging environments"). Remove previews of merged or closed PRs first with `az staticwebapp environment delete`; never delete `default` (production). Luke wants this done by hand, not automated.
- **Storage and data:** Azure Blob storage account `lukehammerweb`, reached through the app setting `LUKE_HAMMER_WEBSITE`. Family Events and Sports data live in container `camping-potluck`; the photo shoot and photo review in private container `photoshoot`. Sports edits are also backed up to `Api/data/sports-schedules.json` on GitHub.
- **Cost rule:** no paid services without Luke's approval. Heavy image work runs in the browser, and no AI model is used on photos (see [POC-DECISIONS.md](../photoshoot/POC-DECISIONS.md)).

## 6. Rules never to weaken
- **Saved data is permanent** (from `.cursor/rules/data-safety.mdc`). Real families save data on this site. Never delete, reset, overwrite or re-seed it, in code or by command. Model changes are additive and backward compatible. Seeds only add missing records. Keep the ETag concurrency check on every save. Never run code or tests against production storage. If a change would need migrating or removing data, stop and ask Luke.
- **Photo shoot access:** `photoshoot-owner` and `photoshoot-photographer` roles are checked on the server. The dev bypass must never work in Azure.
- **Photo review admin:** one bound admin account; a binding is create-only and never widened. See [claude-website-status.md](../photoshoot/claude-website-status.md).
- **Locked events:** a Family Event locks 3 days after its last day; after that the API refuses every change, including Luke's.
- **Public repo:** no keys, connection strings, tokens, passwords or personal contact details in any committed file. `Api/local.settings.json` stays uncommitted.
- **Naming** (from `.cursor/rules/naming.mdc`): spell out names, no abbreviations except lambda parameters, `ex` and loop counters.

## 7. Where things live
| What | Path |
| --- | --- |
| Decision log | `docs/process/DECISIONS.md` (newest first). Photo shoot decisions also in [POC-DECISIONS.md](../photoshoot/POC-DECISIONS.md) |
| Status | [docs/photoshoot/STATUS.md](../photoshoot/STATUS.md) and [claude-website-status.md](../photoshoot/claude-website-status.md) (photo shoot only; no site-wide status yet) |
| Open questions | "Open for Luke" at the end of this file |
| Permission or security audit | none yet |
| Navigation and personas | none yet (section 1 above, and the page table in [docs/photoshoot/README.md](../photoshoot/README.md)) |
| QA ledger | `docs/reviews/qa-ledger.md` |
| Design reviews | `docs/reviews/YYYY-MM-DD-design-review.md` |

## 8. Milestone
- **Current:** **OPEN**
- **Next deadline:** **OPEN**
- **Merge policy:** pull request into `main`, merged after Luke's OK.

## 9. Owner's data
- **Owner's workspace:** the production blob containers above, plus the local working folder `%LOCALAPPDATA%\LukeHammerPhotoshoot` (backups and proof screenshots, kept out of the repo because they hold real photos).
- **What must never be overwritten:** every family entry (events, RSVPs, sign-ups, votes, questions, schedules) and every photo, photo version and Luke's photo statuses.
- **Backups:** before any approved data write, back up to `%LOCALAPPDATA%\LukeHammerPhotoshoot\backups\<timestamp>-<what>\` and write down the restore command. Luke approves every write to production data in the session doing it.

## 10. Model routing overrides
- None. The playbook defaults apply: Haiku for mechanical work, Sonnet for builders, Opus for planning and review.

## 11. Standing sessions
- **Dispatcher session id:** `local_229a9ed0-d89c-4ee1-a965-edb24fef539b` ("LukeHammerWebsite Dispatcher", started 2026-11-13).
- **QA reviewer:** none yet. **OPEN**
- **Design reviewer:** on demand.
- **Others to keep:** none. Other tools (Cursor, Codex) have also worked in this repo; their notes are in `docs/handoff/` in the main checkout (not committed).

## 12. Review settings (read by the qa-review and design-review skills)
- **Personas and how to become each one locally:**
  | Persona | How |
  | --- | --- |
  | Visitor | Open the site signed out. |
  | Family member | Open `/FamilyEvents` or `/Sports` and type any name other than Luke's. |
  | Organizer (Luke) | Enter the organizer code on `/FamilyEvents` or `/Sports`. Local value: **OPEN** (never commit it). |
  | Photo shoot owner | `PHOTOSHOOT_DEV_AUTH=true` locally, owner role (see [ARCHITECTURE.md](../photoshoot/ARCHITECTURE.md)). |
  | Photographer | Same bypass with the photographer role. |
- **Run the app locally for review:** `scripts/start-api.ps1` (7071) and `Client/restart-dev.ps1` (5000), with **no** storage connection string in `Api/local.settings.json`, so the API uses the local temp store. Dev sign-in off unless testing the photo shoot.
- **E2E runner and support files:** none yet.
- **Production smoke pages (read-only, signed out):** `https://lukehammer.com/`, `/FamilyEvents`, `/Sports`, `/photos`, `/photoshoot` (expect the sign-in screen), `/api/photoshoot/me` (expect 401).
- **First-user journey:**
  1. A family member opens the Family Events link Luke sent.
  2. They see the next upcoming event, type their name, and RSVP.
  3. They vote on a theme and say what they're bringing.
  4. They ask or answer a question.
- **Product principles beyond the shared ones:** mobile first (checked at 390 px wide); family pages need no account; Luke is notified of every change someone else makes.

## Open for Luke
1. Should a standing QA reviewer start now? (Recommendation in the setup report.)
2. What is the current milestone, and is there a deadline?
3. Can the photo shoot docs' status file serve as the project status doc, or should there be a site-wide `docs/process/STATUS.md`?
4. How should reviewers sign in as organizer locally (the local organizer code)? Give it in chat to the session that needs it; it never goes in the repo.
5. The main checkout has uncommitted work on an old branch (`cursor/owner-full-access`: photo shoot catalog, tests, `.gitignore`, a transparency tool, handoff notes). Whose is it, and should it be saved to a branch?
6. Cursor rules live in `.cursor/rules/`. Keep them (for Cursor) alongside this profile, or point them here?
