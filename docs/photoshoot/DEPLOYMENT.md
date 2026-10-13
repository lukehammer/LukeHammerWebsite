# Photo Shoot Planner — Deployment

Nothing has been deployed. The feature ships with the normal site deployment (GitHub Actions → Azure Static Web Apps) once `feature/photoshoot-planner` is merged into `main`.

## What changes in production when this is merged

| Change | Effect |
| --- | --- |
| New pages under `/photoshoot` + one nav item | Visible to everyone in the nav. The pages show a sign-in / no-access screen unless the user has a photo shoot role |
| `Client/staticwebapp.config.json`: new route rule for `/api/photoshoot/*` | Only users with `photoshoot-owner` or `photoshoot-photographer` can reach the photo shoot API. Existing routes and `navigationFallback` are unchanged |
| `Client/wwwroot/service-worker.published.js` | The service worker no longer intercepts `/.auth/*`, so sign-in/out always reaches SWA. No effect on other pages |
| New Azure Functions under `/api/photoshoot/*` | Existing functions unchanged |
| Private blob container `photoshoot` | Already exists in the site's storage account (`lukehammerweb`, the one `LUKE_HAMMER_WEBSITE` points to) and already holds the shoot Luke built locally. See [Local development against production storage](#local-development-against-production-storage). The deployed API uses it as-is, so no migration is needed at deploy time |

No new Azure resources, app settings or secrets are required.

## Steps for Luke

1. **Review and merge** `feature/photoshoot-planner` into `main` (PR is your call). The existing workflow builds and deploys.

2. **Confirm the storage setting exists** (it already powers the sports schedules and camping features). In the Azure portal, open the Static Web App, then **Settings → Environment variables (Application settings)**. `LUKE_HAMMER_WEBSITE` should contain the storage connection string.
   - If it's missing, the photo shoot API returns an error rather than silently losing data. It refuses to use temp storage in Azure.
   - Do **not** set `PHOTOSHOOT_DEV_AUTH` in Azure. It's ignored there anyway.

3. **Invite yourself as owner.** In the portal, open the Static Web App, then **Settings → Role management → Invite**:
   - Authorization provider: **GitHub** or **Microsoft** (whichever account you'll sign in with).
   - Invitee details: your GitHub username or Microsoft email.
   - Domain: your site's domain (for example `lukehammer.com` or the `*.azurestaticapps.net` host).
   - Role: `photoshoot-owner`
   - Expiration: up to 168 hours. Open the generated link while signed in to accept.

4. **Invite the photographer** the same way with role `photoshoot-photographer`. Send them the link; they accept it once, then use `https://<your-site>/photoshoot/photographer` on shoot day.
   - To give someone full edit rights (for example an assistant director), use `photoshoot-owner`.
   - Roles can be changed or removed later under Role management. Removing a role takes effect on their next sign-in, or within about 8 hours as the SWA session expires.

5. **First run.** Sign in and open **Photo Shoot**. The shoot Luke planned locally (100 shots, 25 setups, 128 reference images) should already be there, because it lives in production storage. Only if the container were empty would you tap **Load starter catalog**. Set the shoot date under **Manage**.

## Post-deploy checklist

- [ ] Signed out: `/photoshoot` shows the sign-in screen, and `https://<site>/api/photoshoot/me` returns **401**.
- [ ] Signed in with an account that has no role: `/photoshoot` shows "No access yet", and the API returns **403**.
- [ ] Owner: can seed, edit a shot, open Director.
- [ ] Photographer (second phone): sees the current shot; it follows the director within a few seconds; can't see Setups/Manage.
- [ ] The rest of the site (home, Lucky Luke, Sports, PDX Magic Jam) behaves as before.
- [ ] In the storage account, container `photoshoot` exists with **Anonymous access level: Private**.

If a phone still shows an old version after deploy, close and reopen the tab. The service worker updates on the next visit.

## Local development against production storage

The local API can use the production blob container, so whatever Luke plans locally is already in place when the feature deploys. Put the storage account's connection string in **`Api/local.settings.json`** (gitignored; never commit it):

```json
"Values": {
  "LUKE_HAMMER_WEBSITE": "<lukehammerweb connection string>"
}
```

Get it without printing it, for example `$cs = az storage account show-connection-string -n lukehammerweb -g lukehammer-rg --subscription <Pay-As-You-Go id> --query connectionString -o tsv`. Remove the entry to go back to the `%TEMP%\lukehammer-photoshoot` file store.

> **Warning:** with this setting, local testing reads and writes the **real** shoot data. Every edit, session command, import, reset or delete made on `localhost` changes what the photographer sees in production. Use a duplicate shoot, or remove the setting, for throwaway experiments.

To check which store is active, call `GET /api/photoshoot/shoots/{id}`. Blob mode returns an `eTag` like `"0x8D…"` (the blob's ETag); the local file store returns a 24-character hex hash.

### How the local data was migrated (2026-10-13)

1. Backed up `%TEMP%\lukehammer-photoshoot` to `%LOCALAPPDATA%\LukeHammerPhotoshoot\backups\<timestamp>\`.
2. Confirmed the `photoshoot` container did not exist yet. The script refuses to run if the container already holds a shoot, and never overwrites existing blobs.
3. Ran [`scripts/Copy-PhotoShootLocalToBlob.ps1`](../../scripts/Copy-PhotoShootLocalToBlob.ps1) with the connection string in `$env:AZURE_STORAGE_CONNECTION_STRING`. It creates the container as **private** and maps the local store to the blob layout the API uses:

   | Local (`%TEMP%\lukehammer-photoshoot`) | Blob (container `photoshoot`) |
   | --- | --- |
   | `shoots\<shootId>.json` | `shoots/<shootId>.json` (`application/json`) |
   | `settings.json` | `settings.json` (default shoot id) |
   | `images\<imageId>` + `images\<imageId>.type` | `images/<imageId>`, with the content type taken from the `.type` file |

4. The script verified every blob against its local file (size, MD5 and content type): 1 shoot, the settings file, and 128 images (about 42 MB). Image ids are unchanged, because references store only the image id.

The local folder was left in place as a fallback.

**Export JSON is metadata only.** **Manage → Export** contains the shoot document, and its references point at image ids. It does not contain image bytes. Importing it into a different store (for example local into production) creates the shots and references, but their images will be missing. To move a shoot between stores, copy the `images/` blobs or files with the **same ids** as well, as the script above does.

## Cost

- Polling only runs while a live page is open: about 2 requests per phone every 2.5 s, mostly cheap "unchanged" checks. A full shoot day with two phones is well under 30k Functions executions and blob operations, typically pennies.
- Storage: one JSON document per shoot (about 200 KB) plus any reference images you upload (6 MB max each).

## Rollback

Revert the merge commit on `main` and let the workflow redeploy. Shoot data stays in the `photoshoot` container untouched. Delete the container manually if you want it gone. A local backup of the pre-migration data is under `%LOCALAPPDATA%\LukeHammerPhotoshoot\backups\`.

## Questions for Luke

1. **Shoot length.** The full catalog is about 8 hours (about 4h20m of shots, 2h30m of changeovers, plus breaks). Must-have-only is about 3h20m. Do you want to trim the list or merge setups before the day, or rely on **Must-have only** mode during the shoot?
2. **Sign-in providers.** The sign-in screen offers GitHub and Microsoft. Will the photographer have one of those? (Google sign-in would need a custom auth provider, which requires the SWA Standard plan.)
3. **Nav visibility.** The "Photo Shoot" nav item is visible to every visitor (the data is private). Hide it for signed-out visitors, or remove it after the shoot?
4. **Children in photos.** The two child-volunteer setups are pinned in place in the suggested order. Confirm the time slot works for the families, and that you have signed photo releases.
5. **Retention.** Keep the shoot data after the shoot (for example as a template for next year via **Duplicate**), or delete it?
