# Photo Shoot Planner — Status

Branch: `feature/photoshoot-planner` (from `main` @ `65a7350`). Not merged and not deployed.

## Summary

| Area | State |
| --- | --- |
| Data model, storage, concurrency | Done |
| API (all endpoints) + server-side authorization | Done, verified locally |
| SWA route protection | Done (takes effect on the next deploy of `main`) |
| 100-shot / 25-setup catalog + idempotent seed | Done, validated by tests |
| Planner UI: dashboard, shots, shot detail/edit, setups | Done, verified at 390 px |
| Live Director + Photographer views with sync | Done, two-tab sync verified |
| Reconnect indicator, manual refresh, resync on wake/online | Done, verified with simulated offline |
| Reference library (links + private uploads) | Done; URL references verified in the UI, image upload/serve verified via API |
| Planning intelligence: grouping, suggested order, estimates, must-have-only | Done |
| Import / export / duplicate / print | Done; API verified, print page renders |
| Single "Photo Shoot" nav item | Done |
| Docs | Done (this folder) |

## Tests

Command: `dotnet test UnitTest/UnitTest.csproj --filter "FullyQualifiedName~PhotoShoot"`

Latest result: **100 passed, 0 failed** (photo shoot tests). Full suite (`dotnet test UnitTest/UnitTest.csproj`): **233 passed, 0 failed**. `dotnet build BlazorStaticWebApps.sln`: 0 errors, and no warnings from photo shoot code (31 existing warnings elsewhere).

| File | Covers |
| --- | --- |
| `PhotoShootPlannerTests.cs` | Grouping, ordering, move shot/setup, progress, estimates (changeovers, breaks, must-have-only), next/previous/upcoming, suggested order |
| `PhotoShootSessionTests.cs` | Session commands, auto-advance, status transitions and timestamps, photographer permissions, notes |
| `PhotoShootSeedImportTests.cs` | Seed creation, merge idempotency, no overwrite of edits, no resurrection of deleted items, clone/import/duplicate id remapping, validation, editor CRUD |
| `PhotoShootAuthorizationTests.cs` | Principal decoding, role mapping, dev bypass only outside Azure, access checks |
| `PhotoShootCatalogTests.cs` | The real `photoshoot-catalog.json`: validates, 25 setups / 100 shots / 32 must-haves, all 8 categories, unique keys, seeds a valid document, re-seed is a no-op, suggested order keeps every shot |

## Manual verification (local, 2026-10-12)

Environment: Functions host with `PHOTOSHOOT_DEV_AUTH=true` and the local file store (no Azure resources touched), Blazor dev server, Cursor browser at 390×844 mobile emulation.

- Seeded the catalog via the API: 25 setups / 100 shots. A second seed added 0 (125 already present).
- `?since=` poll with an unchanged ETag returned `changed:false`.
- **Photographer role** (dev header): read OK. Edit shoot, export, `Next` (advance off) and image upload returned 403. `RequestNext`, `AddNote` and `CompleteCurrent` returned OK.
- **Real auth path** (second host without dev bypass): anonymous requests returned 401, including with a fake dev-role header, a principal without a userId, or a garbage principal. Signed-in without a role returned 403. Photographer read returned 200 and delete returned 403. Owner returned 200.
- **Two-phone sync** (two tabs, owner + photographer):
  - Director "Complete & next" appeared on the photographer screen in about 1.4 s.
  - Photographer "Ready for next shot" banner appeared on the director in about 0.9 s.
  - Offline photographer showed "Reconnecting…" plus a flash message, caught up about 2 s after the network returned, and showed "Back online".
- All pages rendered at 390 px with no horizontal overflow and no Blazor error UI: dashboard, shots (100), must-have filter (32), setups (25), references, manage, print (100 rows), new shot, director, photographer.
- Created a URL reference linked to a shot through the UI. Uploaded and served a PNG via the API. HTML disguised as PNG was rejected (415), and a `javascript:` URL was rejected (400).
- Export, import as new, replace without confirmation (409), replace with confirmation (OK), bad format (400), and duplicate (fresh ids, progress reset, image copied and served) all behaved as expected.
- Existing pages (`/`, `/LuckyLuke`, `/Programmer`, `/Chess`, `/Sports`, `/pdxMagicjam`) still render. The photo shoot stylesheet is not loaded on them. The nav shows one new "Photo Shoot" item.
- `dotnet publish -c Release` of the client includes `css/photoshoot.css`, `js/photoshoot.js` and the updated `staticwebapp.config.json`.

Bug found and fixed during verification: after the network returned, a sync could be dropped if a background poll was in flight, leaving the phone waiting out its backoff (about 8.6 s observed). It now cuts the backoff short and queues behind the in-flight poll (about 2 s).

## Not verified / known limitations

- **Not tested against real Azure Static Web Apps.** SWA role enforcement and the real `x-ms-client-principal` header were simulated locally. Do the post-deploy checklist in [DEPLOYMENT.md](DEPLOYMENT.md).
- The image upload *UI* (file picker) was not exercised in the browser, because file pickers can't be automated here. The upload endpoint was verified directly.
- Screen Wake Lock can't be verified in desktop emulation. It degrades silently where unsupported (for example older iOS).
- Print output was checked as a rendered page, not on paper.
- Reordering uses up/down buttons and "move to setup", not drag and drop.
- Edits made while offline are not queued. The action fails with a message and must be retried once back online. The live views recover automatically.
- API endpoint code (as opposed to the shared logic it calls) has no automated tests; it was verified with the manual scripts above.
- The CI workflow only runs on `main`, so these tests won't run in CI until the branch is merged.
- The full catalog is about 8 hours (7h53m) including changeovers and breaks; must-have-only is about 3h20m. See the question in DEPLOYMENT.md.
