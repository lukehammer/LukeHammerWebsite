# Photo shoot POC: decisions

This records the decisions made while building the proof of concept inside lukehammer.com. Luke plans to split the photo shoot application into a standalone service. Use this file as the starting list for that service. Each entry says what was decided, why, and what the standalone service must change.

## Status

- The app runs inside lukehammer.com as one deployment (Blazor WebAssembly client, Azure Functions API, Azure Blob storage).
- It is a POC. Some choices are shortcuts that only make sense for one studio and one owner. They are marked **POC shortcut**.

## Decisions

### 1. Shot code on the marker (QR)
- **Decided:** the marker screen shows a QR code for the current shot, payload `lhp:shoot/{shootId}/shot/{shotId}`.
- **Why:** a QR code lets a photo name its shot without OCR or AI. It is reliable, costs nothing, and works offline.
- **Scope:** shoots taken after this change only. Older shoots have no codes.
- **Standalone:** keep the payload format stable, or version it (`lhp:v2:`). Shot ids must stay unique across shoots.

### 2. Sorting by marker runs
- **Decided:** photos are ordered by capture time. A shot code starts a run that covers following photos until the next code. Photos before the first code, or with a code from another shoot, stay unsorted.
- **Why:** the photographer already photographs the marker before each shot, so the run boundaries are the shot boundaries.
- **Limit:** a missed code extends the previous run. Out-of-order camera clocks break the order. Luke reviews unsorted photos by hand.
- **Standalone:** the sort rule lives in `PhotoShootUploadSort.Sort`. Keep it pure and tested.

### 3. No AI for now
- **Decided:** no vision or language model is used for matching or ranking.
- **Why:** cost, and photos should not leave the app. Luke's call.
- **Standalone:** a paid model can be added as another step after `Sort`, behind a feature flag. Record the provider, cost per photo and privacy terms when it is added.

### 4. Heavy analysis runs in the browser
- **Decided:** sharpness, exposure, clipping, QR reading and EXIF reading run on the uploader's machine (`Client/wwwroot/js/photoshoot-upload.js`). The server does no image analysis.
- **Why:** keeps server cost near zero. The browser does the work.
- **Libraries (loaded from CDN on first use):** jsQR 1.4.0 (jsdelivr), exifr 7.1.3 lite (jsdelivr), qrcodejs 1.0.0 (cdnjs). They are pinned, and each is at least two weeks old.
- **Standalone:** move analysis to a worker or a desktop tool if uploads get large. The numbers it produces feed the same `Sort`.

### 5. Two-stage best photo
- **Decided:** stage 1 is pass or fail: blur below `MinSharpness`, or shadows crushed above `MaxClippedShadows`. Highlights are **not** a fail, because the seamless backdrop is lit to pure white on purpose (a test frame on white measured 65% clipped). Stage 2 ranks the passes by a score: 60% sharpness, 25% exposure balance, 15% clipping, so bright highlights still lower the score.
- **Why:** Luke asked for a simple pass or fail first, then quality among the passes.
- **Limit:** the score does not judge expression, pose, or framing. Framing is not measured yet.
- **Standalone:** thresholds and weights are constants in `PhotoShootUploadSort`. Calibrate them on real shoots before trusting them. The sharpness scale (variance / 1000) is a guess.

### 6. Recommendation only
- **Decided:** the recommended photo is shown in the upload table. It does not become the final automatically. Luke sets the final with **Make final**.
- **Why:** the owner makes the final decision.
- **POC shortcut:** recommendations are not stored. They last only until the page is reloaded.
- **Standalone:** store the recommendation and its reason with the photo, so it survives a reload.

### 7. Who can upload
- **Decided:** Luke and photographers (`photoshoot-photographer`), the existing roles.
- **Why:** photographers already upload finished photos.
- **Standalone:** keep roles in one place, and decide whether clients ever get access.

### 8. Upload path
- **Decided:** each photo goes through the existing finished-photo endpoint (`POST .../finished` or `.../shots/{shotId}/finished`), up to 100 MB each, with a limit of 1000 files per selection.
- **POC shortcut:** photos pass through the Functions host, so a very large shoot takes a while. Direct-to-blob upload with short-lived SAS URLs is the next step and is not built yet.
- **Standalone:** use direct-to-blob uploads from the start, with resumable uploads for large files.

### 9. Data kept
- Originals are stored as uploaded. EXIF is not stripped, so camera data stays with the photo.
- Recommendations and sort results are not stored (see decision 6).
- **Standalone:** decide on EXIF stripping before any public use.

### 10. Hosting and cost
- Everything runs on Azure Static Web Apps, Azure Functions and Blob storage, under one owner account.
- **Standalone:** separate tenancy, billing, and deployment, and a plan for multiple studios.

## Open items

- Calibrate the thresholds and sharpness scale on a real shoot.
- Store recommendations (decision 6).
- Direct-to-blob upload with SAS (decision 8).
- Decide on EXIF stripping (decision 9).
- Decide whether a paid AI step is ever needed (decision 3).
