# Photo Review — Implementation Plan

Branch `claude/photo-review` (worktree `LukeHammerWebsite-review`, from `main` @ `f0ba777`). The main checkout's uncommitted catalog edits are left alone.

## Findings that shaped the plan

- The live planner document (`shoots/f77cc235728d.json`, rev 2627) has **118 shots** with stable 12-hex ids, 26 setups, 291 references, 0 finished photos. The review layer joins to these ids and never rewrites the planner document.
- The repository is **public**, and planner access is granted by two hard-coded passwords in `PhotoShootAuthorization.cs` (`Luke` → owner, `LukeAdmin` → admin with hard delete). SWA has no role assignments. So today anyone who reads the source has owner/admin. This is fixed as part of milestone 2 (see "Admin identity").
- SWA is the Free plan: only the built-in `aad` (Microsoft) and GitHub providers. Managed Functions time out at about 45 s, so large originals are not proxied through Functions.

## Architecture

```
/photoshoot/review…  (public Blazor pages, layout PhotoReviewLayout)
        │  fetch, same origin, cookie llm_visitor (HttpOnly, Secure, SameSite=Lax, Path=/api/photoreview)
        ▼
/api/photoreview/*   (new PhotoReviewFunction; anonymous trigger, every handler authorizes itself)
        ▼
private container "photoshoot"
  shoots/{id}.json                    planner document (read-only from the review API)
  review/{shootId}/catalog.json       photos, artifact versions, shot review state, second-shoot queue (admin/importer)
  review/{shootId}/participation.json visitors, votes + aggregates, comments, suggestions, audit (visitors/admin)
  review/admin-binding.json           Microsoft account subject bound to the admin
  review/assets/{sha256}              artifact bytes, content-addressed (original bytes unchanged)
  review/thumbs/{sha256}-{w}.{ext}    browsing thumbnails (480 / 1280 px) made by the importer
```

- **Two documents, ETag optimistic concurrency with retries** (same pattern as the planner). Mutations are pure functions in `Shared/PhotoReview` and unit tested.
- **Assets:** thumbnails are streamed by the API with long immutable caching (URL carries the hash). Full-size previews and downloads redirect (302) to a **15-minute, read-only, single-blob SAS** with a `Content-Disposition` filename. No container listing, no account key leaves the server, and the original bytes are served exactly as uploaded.
- **Ingestion:** `tools/PhotoReviewImport` (console) reads a Codex manifest, checks every file's SHA-256 and size, backs up the current catalog, uploads blobs if absent, makes thumbnails, then upserts the catalog through the shared `PhotoReviewIngest` logic. Photo identity = manifest `photoKey`; the server photo id is derived from it, so re-imports never duplicate photos and never touch ratings or comments. An original whose hash changes is refused.

## Identity and authorization

| Caller | How | Can |
| --- | --- | --- |
| Anyone | no credentials | read the review, walkthrough and second-shoot plan; view thumbnails, previews and downloads |
| Visitor | `POST /visitor` with a display name → server mints an opaque id and a 256-bit secret; cookie stores `id.secret`, server stores SHA-256(secret) | rate 1–10 (one current vote per photo), comment, reply, suggest status or pose changes, edit or withdraw **own** items |
| Admin | SWA `aad` principal, `userDetails` = luke@lukehammermagic.com, **and** `userId` equal to the bound subject (app setting `PHOTOREVIEW_ADMIN_USER_ID`, else first matching sign-in is recorded in `review/admin-binding.json`) | everything, including authoritative shot status, people removal and restore, queue, suggestion approval |

- Display names are labels only. They are never used for authorization.
- **CSRF:** every mutating endpoint requires header `X-Photo-Review: 1` and rejects `Sec-Fetch-Site: cross-site`. The cookie is SameSite=Lax. Custom headers force a CORS preflight, which the API does not answer.
- **Planner migration:** `LukeAdmin` is removed (hard delete now needs the Microsoft admin). `Luke` keeps owner access only until the Microsoft admin is bound; after that it drops to photographer level (read, uploads, limited session commands). Override with `PHOTOSHOOT_LEGACY_PASSWORD` = `off` | `photographer` | `owner`.

## Milestones

1. **Review core.** Shared models (nine categories, statuses, evidence, artifact kinds); catalog projection that joins all 118 planner shots; public review UI (category groups, search and filters, previous/next, shot detail intended vs captured, photo detail with versions); asset serving; importer and manifest schema; tests.
2. **Participation and moderation.** Visitor identity, ratings with aggregates, threaded comments, suggestions, ownership rules, admin Microsoft sign-in and binding, People view with remove/restore and audit, planner password migration; tests.
3. **Second shoot and mentor.** Queue (uncaptured, unsuccessful, partial, approved suggestions) with priority and notes, printable and exportable plan, walkthrough page.
4. **Verification and deploy.** Build and tests, local end-to-end with synthetic fixtures, PR, merge to `main`, workflow check, production smoke tests with labeled temporary data that is cleaned up afterwards.

Out of scope for this run: transferring or reading any real photo. Codex runs the importer with the real manifest later.
