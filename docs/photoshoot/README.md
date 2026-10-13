# Lucky Luke Magic — Photo Shoot Planner & Director

A private, mobile-first tool inside LukeHammer.com for planning and running the Lucky Luke Magic studio photo shoot. It lives entirely under `/photoshoot` and is reachable from a single **Photo Shoot** item in the site's side navigation.

- Plan about 100 shots across about 25 setups (lighting, backdrop, outfit and prop changes).
- Run the shoot from two phones: the **Director** (Luke or a helper) drives, the **Photographer** sees the same current shot and directions, synced through the server.
- Track progress, must-haves, skipped shots, favorites and notes; it estimates remaining time including changeovers and breaks.
- Keep a private reference library (links and uploaded images) attached to shots.
- Import, export, duplicate and print.

Related docs: [ARCHITECTURE.md](ARCHITECTURE.md) · [DEPLOYMENT.md](DEPLOYMENT.md) · [STATUS.md](STATUS.md) · [PHOTO_CATALOG.md](PHOTO_CATALOG.md)

## Pages

| URL | Who | What |
| --- | --- | --- |
| `/photoshoot` | Owner, Photographer | Dashboard: progress, must-have progress, current shot, setup summary, links to the live views |
| `/photoshoot/shots` | Owner, Photographer | Shot list with search, priority/status/category filters, group-by-setup, favorites, quick complete/skip, reorder (owner) |
| `/photoshoot/shots/{id}` | Owner, Photographer | Full shot detail: performer and photographer directions, references, notes, big-text mode |
| `/photoshoot/shots/new`, `/photoshoot/shots/{id}/edit` | Owner | Create / edit / delete a shot |
| `/photoshoot/setups` | Owner | Setups (backdrop, lighting, camera, outfit, props, changeover minutes), reorder, delete, apply suggested order |
| `/photoshoot/director` | Owner | Full-screen live view: start/resume session, complete & next, skip, previous/next, jump to any shot, next setup, photographer requests, notes, options |
| `/photoshoot/photographer` | Owner, Photographer | Full-screen live view for the camera operator: current shot, framing/orientation/crop, reference image, "Got it — mark complete", "Ready for next shot", notes |
| `/photoshoot/references` | Owner | Reference library: links, credits, what to imitate / change, private image upload, inspiration ideas from the catalog |
| `/photoshoot/manage` | Owner | Shoots list, shoot details and date, seed/merge catalog, export JSON, import (preview, create new or replace with confirmation), duplicate, delete |
| `/photoshoot/print` | Owner, Photographer | Printable checklist grouped by setup |

Photographers only see Dashboard, Shots and Photographer in the sub-navigation; the server enforces the same limits.

## Running a shoot (day-of)

1. Luke opens **Photo Shoot → Director** on his phone and taps **Start session**.
2. The photographer signs in on their phone and opens **Photographer** (their invite gives them the `photoshoot-photographer` role).
3. The director calls each shot and taps **Complete & next** (or **Skip**). The photographer's screen follows within about 2–3 seconds.
4. The photographer can tap **Ready for next shot** (shows a banner on the director phone), **Got it — mark complete**, and add notes. Turning on **Photographer can advance** in the director's Options tab also gives them Previous/Next.
   On both live views these action buttons sit in a bar pinned to the bottom of the screen, so they never need scrolling to reach.
5. If time gets short, turn on **Must-have only** in Options: "next" then skips non-must-have shots, and the remaining-time estimate updates.
6. **Keep screen awake** (Options) uses the browser Wake Lock API where supported.

The connection pill in the top bar shows **Live / Syncing / Reconnecting / Offline**; tap the refresh icon to force a sync. Phones resync immediately when the tab becomes visible again or the network returns.

## Local development

Prerequisites: .NET 9 SDK, Azure Functions Core Tools v4.

```powershell
# API (port 7071). Dev auth bypass ONLY works locally (ignored when WEBSITE_SITE_NAME is set, i.e. in Azure).
cd Api
$env:PHOTOSHOOT_DEV_AUTH = 'true'      # or set it in your local.settings.json
func start --port 7071

# Client (another terminal)
cd Client
dotnet run --urls http://localhost:5000
```

Open `http://localhost:5000/photoshoot`. With no shoot yet, the owner view offers **Load starter catalog**, which seeds 25 setups and 100 shots from `Api/data/photoshoot-catalog.json`.

- To test the photographer experience, open a second browser tab with `?devRole=photographer` (for example `/photoshoot/photographer?devRole=photographer`). The role sticks for that tab.
- Without a storage connection string, data is stored as JSON files in `%TEMP%\lukehammer-photoshoot` (override with `PHOTOSHOOT_LOCAL_DIR`).
- See `Api/local.settings.example.json` for `PHOTOSHOOT_DEV_AUTH` / `PHOTOSHOOT_DEV_ROLE`.

## Tests

```powershell
dotnet test UnitTest/UnitTest.csproj --filter "FullyQualifiedName~PhotoShoot"
```

Covers planning/grouping/ordering/estimates, session commands and status transitions, permissions, seeding idempotency, import/duplicate, validation, authorization header parsing, and validation of the real 100-shot catalog. See [STATUS.md](STATUS.md) for the latest counts and the manual verification that was done.
