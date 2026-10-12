# Photo Shoot Planner — Architecture

## Overview

```
Phone (Director)          Phone (Photographer)
Blazor WASM /photoshoot   Blazor WASM /photoshoot
        │  poll every 2.5s (ETag)  │
        └──────────┬───────────────┘
                   ▼
   Azure Static Web Apps  ── route rule: /api/photoshoot/* needs
                   │          photoshoot-owner or photoshoot-photographer
                   ▼
   Azure Functions (isolated, Api/)  ── re-checks x-ms-client-principal
                   │
                   ▼
   Blob Storage, private container "photoshoot"
     shoots/{shootId}.json   one document per shoot
     settings.json           default shoot id
     images/{imageId}        uploaded reference images
```

Everything is additive to the existing site: no existing pages, routes, functions or storage containers were changed.

## Code layout

| Area | Location | Notes |
| --- | --- | --- |
| Shared model and logic | `Shared/PhotoShoot/` | Used by both API and client, and fully unit-tested |
| API | `Api/PhotoShootFunction.cs`, `Api/PhotoShootStorage.cs`, `Api/PhotoShootAuth.cs` | HTTP endpoints, blob/local persistence, auth resolution |
| Seed catalog | `Api/data/photoshoot-catalog.json` | 25 setups / 100 shots; see [PHOTO_CATALOG.md](PHOTO_CATALOG.md) |
| Client services | `Client/PhotoShoot/PhotoShootApiClient.cs`, `PhotoShootStore.cs`, `PhotoShootBrowser.cs` | Typed API client, state + polling, JS interop |
| Client layouts/components | `Client/PhotoShoot/*.razor` | `PhotoShootLayout` (inside the site layout), `PhotoShootLiveLayout` (full screen), `PsGate`, `PsConnection`, ... |
| Pages | `Client/Pages/PhotoShoot/*.razor` | All routes under `/photoshoot` |
| Styles | `Client/wwwroot/css/photoshoot.css` | Loaded only on photoshoot pages via `<HeadContent>`; all classes are `.ps-*` |
| Browser helpers | `Client/wwwroot/js/photoshoot.js` | ES module: Wake Lock, visibility/online events, download, print |
| Tests | `UnitTest/PhotoShoot/` | xUnit + FluentAssertions |

### Shared logic (`Shared/PhotoShoot`)

- `PhotoShootModels.cs`: `PhotoShootDocument` (setups, shots, references, session, deleted seed keys), `PhotoSetup`, `PhotoShot`, `PhotoReference`, `PhotoShootSession`.
- `PhotoShootEnums.cs`: status (Not Started / In Progress / Completed / Skipped), priority (Must Have / Standard / Bonus), categories A–H, orientation, negative-space direction, access level. Enums serialize as strings.
- `PhotoShootPlanner.cs`: grouping by setup, ordering, moving shots and setups, progress, time estimates (shots + changeovers + breaks), next/previous/upcoming navigation, suggested order.
- `PhotoShootSessionEngine.cs`: applies session commands (start, complete & next, skip, set current, request next, notes, ...) and enforces which commands a photographer may send.
- `PhotoShootEditor.cs`: create/update/delete for shots, setups and references, plus `Normalize` (repairs dangling ids and sequences).
- `PhotoShootValidator.cs`: field limits, URL safety (http/https only), document and catalog validation.
- `PhotoShootSeeder.cs`: catalog → document, idempotent merge, and `PhotoShootCloner` for import/duplicate (fresh ids, remapped links, image copy list).
- `PhotoShootAuthorization.cs`: decodes the SWA principal, maps roles to access, and handles the local-only dev bypass.

## Data model notes

- **One JSON document per shoot.** About 100 shots is roughly 150–250 KB, small enough to load and save whole, and it makes import/export and duplicate trivial.
- **Order**: setups have `Sequence`. Shots have `SetupId` and `Sequence` *within* their setup, so the overall order is setup order and then shot order. `Resequence` keeps both contiguous.
- **Seed keys**: seeded items carry `SeedKey` (`setup:<key>` / `shot:<key>`). Merging the catalog only adds missing keys and never modifies existing items. Deleting a seeded item records its key in `DeletedSeedKeys`, so re-seeding does not resurrect it.
- **References** link to shots by id (`ShotIds`). Uploaded images are stored separately (`images/{imageId}`) and are only served through the authorized API.

## Concurrency and sync

- Every save uses **ETag optimistic concurrency** (blob `If-Match`, or a SHA-256 ETag for the local file store) with up to 8 retries. Mutations are expressed as functions applied to the *latest* document, so two phones tapping at once never overwrite each other.
- Session actions are **semantic commands** (`CompleteCurrent`, `RequestNext`, ...), not "set state to X", so they compose safely.
- Every mutation returns the full document and its new ETag (`LiveShootResponse`), so the acting phone updates instantly.
- Other phones **poll** `GET /shoots/{id}?since={etag}` every 2.5 s. When nothing changed, the server only reads blob properties and returns `{changed:false}`, which is cheap. On failure the client backs off exponentially up to 15 s and shows **Reconnecting** / **Offline**. A manual refresh, tab-becomes-visible, or network-returns event triggers an immediate sync and cuts any backoff short.
- SignalR was deliberately not used: it would need a new paid Azure SignalR resource, and polling at this scale (two phones, one small blob) is well within free-tier limits. Measured locally, director-to-photographer propagation took about 1–1.5 s.

## Security

Defense in depth:

1. **SWA route rule** (`Client/staticwebapp.config.json`): `/api/photoshoot/*` requires role `photoshoot-owner` or `photoshoot-photographer`. Unauthenticated users get 401, and signed-in users without a role get 403, before the function runs.
2. **Server-side check** in every endpoint (`PhotoShootAuth.Resolve`): decodes `x-ms-client-principal` (must contain a `userId`), maps roles to `Owner` / `Photographer` / `None`, then:
   - Owner: everything.
   - Photographer: read the shoot, references and images; session commands `RequestNext`, `CompleteCurrent`, `AddNote`, plus `Next`/`Previous` only when the owner enables **Photographer can advance**. Everything else returns 403.
3. **Dev bypass** (`PHOTOSHOOT_DEV_AUTH=true`) is honored only when `WEBSITE_SITE_NAME` is *not* set, so it can never activate in Azure.
4. **Storage**: the `photoshoot` container is created with `PublicAccessType.None`. Images are served by the API with `Cache-Control: private` and `X-Content-Type-Options: nosniff`. Uploads are limited to 6 MB and the type is detected from magic bytes (JPEG/PNG/GIF/WebP), so HTML disguised as an image is rejected (415).
5. **Validation**: lengths and list sizes are capped, URLs must be http/https, ids are `[a-z0-9-]{1,64}`.
6. The `/photoshoot` *pages* are part of the public SPA bundle, but they contain no shoot data. All data comes from the protected API. The gate component shows sign-in buttons (GitHub / Microsoft) or a "no access" message.

The service worker (`service-worker.published.js`) now ignores `/.auth/*` requests so SWA sign-in/sign-out reaches the server instead of the cached app shell.

## API endpoints

All routes are prefixed with `/api/photoshoot`. **R** = owner or photographer, **O** = owner only.

| Method | Route | Access | Purpose |
| --- | --- | --- | --- |
| GET | `/me` | any signed-in | Access level and user name (401 if anonymous) |
| GET | `/shoots` | R | List shoots and the default shoot id |
| POST | `/shoots` | O | Create a blank shoot |
| POST | `/shoots/seed?shootId=` | O | Create from the catalog, or merge missing catalog items into a shoot |
| GET | `/shoots/{id}?since={etag}` | R | Load a shoot, or `{changed:false}` if unchanged |
| PUT | `/shoots/{id}` | O | Update name, description, date, break settings, default |
| DELETE | `/shoots/{id}?confirm=true` | O | Delete a shoot and its images |
| POST | `/shoots/{id}/duplicate` | O | Copy with fresh ids (optionally reset progress) |
| GET | `/shoots/{id}/export` | O | Export JSON (`format: lucky-luke-photoshoot`, v1) |
| POST | `/import` | O | Import as new, or replace an existing shoot (`confirmOverwrite` required, otherwise 409) |
| POST / PUT / DELETE | `/shoots/{id}/shots[/{shotId}]` | O | Shot CRUD |
| POST | `/shoots/{id}/shots/{shotId}/status` | O | Set status |
| POST | `/shoots/{id}/shots/{shotId}/favorite` | O | Toggle favorite |
| POST | `/shoots/{id}/shots/{shotId}/move` | O | Move up/down or to another setup |
| POST | `/shoots/{id}/suggested-order` | O | Apply suggested order |
| POST / PUT / DELETE | `/shoots/{id}/setups[/{setupId}]` | O | Setup CRUD (`DELETE ?moveTo=` reassigns shots) |
| POST | `/shoots/{id}/setups/{setupId}/move` | O | Reorder setups |
| POST | `/shoots/{id}/session` | R* | Session command (*photographer limited as above) |
| POST / PUT / DELETE | `/shoots/{id}/references[/{refId}]` | O | Reference CRUD |
| POST / DELETE | `/shoots/{id}/references/{refId}/image` | O | Upload (raw bytes) / remove image |
| GET | `/shoots/{id}/references/{refId}/image` | R | Serve image privately |

## Planning intelligence

- **Estimates**: shot minutes, plus each setup's changeover minutes (the current setup's changeover is skipped once the session is running), plus breaks (`floor(work / BreakEveryMinutes) * BreakDurationMinutes`, default 10 minutes every 60).
- **Must-have only**: estimates and "next" consider only Must Have shots.
- **Suggested order**: must-haves first within each setup; setups clustered by background color family (white, gray, black, burgundy, ...), with families ordered by must-have count. Child-volunteer setups keep their position so the families' time slot is respected.
- **Next shot**: the next Not Started / In Progress shot after the current one, then wrap around, then fall back to skipped shots so nothing is forgotten.
