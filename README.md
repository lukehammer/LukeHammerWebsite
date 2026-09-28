# Blazor Starter Application

This template contains an example .NET 7 [Blazor WebAssembly](https://docs.microsoft.com/aspnet/core/blazor/?view=aspnetcore-6.0#blazor-webassembly) client application, a .NET 7 C# [Azure Functions](https://docs.microsoft.com/azure/azure-functions/functions-overview), and a C# class library with shared code.

> Note: Azure Functions only supports .NET 7 in the isolated process execution model

## Getting Started

1. Create a repository from the [GitHub template](https://docs.github.com/en/enterprise/2.22/user/github/creating-cloning-and-archiving-repositories/creating-a-repository-from-a-template) and then clone it locally to your machine.

1. In the **ApiIsolated** folder, copy `local.settings.example.json` to `local.settings.json`

1. Continue using either Visual Studio or Visual Studio Code.

### Visual Studio 2022

Once you clone the project, open the solution in the latest release of [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) with the Azure workload installed, and follow these steps:

1. Right-click on the solution and select **Set Startup Projects...**.

1. Select **Multiple startup projects** and set the following actions for each project:
    - *Api* - **Start**
    - *Client* - **Start**
    - *Shared* - None

1. Press **F5** to launch both the client application and the Functions API app.

### Visual Studio Code with Azure Static Web Apps CLI for a better development experience (Optional)

1. Install the [Azure Static Web Apps CLI](https://www.npmjs.com/package/@azure/static-web-apps-cli) and [Azure Functions Core Tools CLI](https://www.npmjs.com/package/azure-functions-core-tools).

1. Open the folder in Visual Studio Code.

1. Delete file `Client/wwwroot/appsettings.Development.json`

1. In the VS Code terminal, run the following command to start the Static Web Apps CLI, along with the Blazor WebAssembly client application and the Functions API app:

    ```bash
    swa start http://localhost:5000 --api-location http://localhost:7071
    ```

    The Static Web Apps CLI (`swa`) starts a proxy on port 4280 that will forward static site requests to the Blazor server on port 5000 and requests to the `/api` endpoint to the Functions server. 

1. Open a browser and navigate to the Static Web Apps CLI's address at `http://localhost:4280`. You'll be able to access both the client application and the Functions API app in this single address. When you navigate to the "Fetch Data" page, you'll see the data returned by the Functions API app.

1. Enter Ctrl-C to stop the Static Web Apps CLI.

## Template Structure

- **Client**: The Blazor WebAssembly sample application
- **Api**: A C# Azure Functions API, which the Blazor application will call
- **Shared**: A C# class library with a shared data model between the Blazor and Functions application

## Deploy to Azure Static Web Apps

This application can be deployed to [Azure Static Web Apps](https://docs.microsoft.com/azure/static-web-apps), to learn how, check out [our quickstart guide](https://aka.ms/blazor-swa/quickstart).

## Sports schedule CRUD

The **Sports** page (`/Sports`) lists upcoming games and includes a **Manage schedule** section to add, edit, or delete events. Every write requires **Your name** (`submittedBy`).

### API endpoints

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/sports/schedules` | Load all events (optional `?sport=`) |
| POST | `/api/sports/schedules/events` | Add event (body: `{ submittedBy, event }`) |
| PUT | `/api/sports/schedules/events/{id}` | Update event |
| DELETE | `/api/sports/schedules/events/{id}?submittedBy=` | Delete event |

Events use a stable `id` (GUID). Data is stored in shared blob storage (`camping-potluck/sports-schedules.json`, same account as potluck) via `SportsSchedulesStorage`. Family edits on the live site read/write that blob (optimistic concurrency, like potluck).

**Deploy / merge:** When you ship a new `Api/data/sports-schedules.json`, the first API load in Azure compares a hash of the deployed seed file to `sports-schedules.seed-hash` in storage. If the seed changed, the blob is **merged** with the repo seed (not overwritten): events added on the site are kept; events only in the seed are added; when the same `id` exists in both, the copy with the newer `lastModifiedAt` wins (live edits beat an older seed row; seed updates rows that were never modified on the site). After sync, runtime edits stay in the blob until the next seed change is deployed.

Local dev uses `%TEMP%\lukehammer-sports-schedules.json` with the same merge when the deployed seed hash changes.

### Blazor client dev (`Loading…` / `_framework` 404 / SRI errors)

If the browser console shows **404** on `Client.*.wasm` or `*.pdb` and **integrity** / **Failed to fetch** for `blazor.boot.json` assets, the dev server is out of sync with the last build (common after rebuilding while `dotnet run` is still running).

1. **Stop** the Client process (`Ctrl+C` on port **5000**).
2. From `Client/`, run **`./restart-dev.ps1`** (builds, then `dotnet run --no-build`), **or** manually:
   ```bash
   dotnet build Client/Client.csproj
   dotnet run --project Client/Client.csproj --no-build
   ```
3. **Hard refresh** the browser (**Ctrl+F5**) on `/Sports`.

Do **not** run `dotnet build` while `dotnet run` is still serving the site—the browser may load a new `blazor.boot.json` while the server still returns **404** for the matching `.wasm`/`.pdb` files (SRI / “Failed to fetch” errors). Always stop the dev server, rebuild, start again, then hard refresh.

### Notifications (family site — no auth)

When the actor is **not** Luke, the API sends email and SMS after a successful change. Luke is detected case-insensitively if `submittedBy` matches any entry in `LUKE_NAME_ALIASES` (default: `Luke`, `Luke Hammer`, `luke hammer`).

Configure in `Api/local.settings.json` (see `local.settings.example.json`):

- **Email:** `SENDGRID_API_KEY` and/or `SMTP_*`, plus `NOTIFY_EMAIL_TO` (default `luke@lukehammermagic.com`)
- **SMS:** `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`, `TWILIO_FROM_NUMBER`, `NOTIFY_SMS_TO` (default `+15035053005`)

If providers are missing in dev, notifications are **logged to the Functions console**; CRUD still succeeds.

### GitHub backup

After each successful mutation, the API attempts to update `Api/data/sports-schedules.json` on GitHub via the Contents API (`GITHUB_TOKEN` with **repo** scope, `GITHUB_REPO` as `owner/name`, optional `GITHUB_BRANCH` and `GITHUB_SCHEDULE_FILE_PATH`). If the token is missing, storage is still updated and a warning is logged.
