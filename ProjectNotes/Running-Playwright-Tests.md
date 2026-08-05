# Running Playwright Tests

Front-end E2E tests live in `Ben.Web.Playwright/`. They run against a live dev stack and require the app to be running and seeded.

---

## One-time setup

### 1. Build the project to generate the Playwright CLI
```bash
cd /Users/ben/Source/Ben
dotnet build Ben.Web.Playwright
```

### 2. Install Chromium (only needed once per machine)
```bash
~/.nuget/packages/microsoft.playwright/1.52.0/runtimes/unix/native/playwright.sh install chromium
```

> If you have PowerShell (`pwsh`) available you can alternatively run:
> `pwsh Ben.Web.Playwright/bin/Debug/net10.0/playwright.ps1 install chromium`

---

## Before every test run

### 1. Start the full stack
Use the VS Code task **`start-full-stack`** (Terminal → Run Task), or:
```bash
bash scripts/start-webapp-with-api.sh
```
Wait until both the WebApi (port 5252) and WebApp (port 5078) are listening.

### 2. Confirm dev seed data is enabled
In `Ben.Data.WebApi/appsettings.Development.json`, verify:
```json
"SeedData": {
  "DevData": { "Enabled": true }
}
```
The tests rely on seeded orgs (`tgh`, `benco`), users, and cases. If the DB is empty the tests will mostly fail.

---

## Running the tests

### All tests
```bash
cd /Users/ben/Source/Ben
dotnet test Ben.Web.Playwright --no-build
```

### A single category
```bash
dotnet test Ben.Web.Playwright --no-build --filter TestCategory=Smoke
```

Available categories:

| Category | What it covers |
|---|---|
| `Smoke` | Every public route renders without errors |
| `Home` | Hero, search box, CTA buttons |
| `HomeMap` | Map tiles, markers, popup, sort toggle, vote buttons |
| `Auth` | Login/logout, email in app bar, SuperAdmin button |
| `Navigation` | App bar, drawer, theme switch |
| `OrgDiscovery` | `/find` page, org public page |
| `PublicCase` | Case detail, Community Rating, voting |
| `CaseManagement` | Org case list and detail |
| `CaseMessages` | Client and org message board |
| `CaseReports` | Report builder, published report card |
| `CaseTransfer` | Transfer history, propose/cancel dialog |
| `InvestigationPanel` | Investigation list, scheduling, attendees |
| `MyCases` | Client case list and detail |
| `Voting` | Evidence vote widget |
| `ErrorHandling` | 404, invalid guids, Telerik parameter errors |

### A single test by name
```bash
dotnet test Ben.Web.Playwright --no-build --filter "FullyQualifiedName~LoginPage_HasRequiredFields"
```

---

## Headed mode (watch the browser)

Set the environment variable before running:
```bash
export HEADED=1
dotnet test Ben.Web.Playwright --no-build --filter TestCategory=Smoke
```

Unset when done:
```bash
unset HEADED
```

---

## Environment variables

All variables have safe defaults for local dev. Only override if your setup differs.

| Variable | Default | Purpose |
|---|---|---|
| `BEN_BASE_URL` | `http://localhost:5078` | WebApp root URL |
| `BEN_SUPERADMIN_EMAIL` | `haveben@msn.com` | SuperAdmin login |
| `BEN_SUPERADMIN_PASSWORD` | *(see appsettings)* | SuperAdmin password |
| `BEN_USER_EMAIL` | `sarah.mitchell@benco.dev` | Regular user login |
| `BEN_USER_PASSWORD` | *(see appsettings)* | Regular user password |

Passwords are in `Ben.Data.WebApi/appsettings.Development.json` under `SeedData` — do not commit them.

---

## Troubleshooting

**Tests time out on map or card selectors**
The Blazor circuit takes a moment to connect. Tests already have 12–15 s timeouts for async-loaded elements. If they still time out, check that the WebApp started cleanly (`tail -f .vscode/webapi.log`).

**"No test matches the given testcase filter"**
You need to rebuild before running without `--no-build`:
```bash
dotnet build Ben.Web.Playwright && dotnet test Ben.Web.Playwright --no-build
```

**Login tests fail with wrong password**
The default passwords are set by `DevelopmentDataSeeder`. If the DB was re-seeded with different values, update the `BEN_*_PASSWORD` env vars or check `appsettings.Development.json`.

**Chromium not found**
Re-run the install step. The path `~/.nuget/packages/microsoft.playwright/1.52.0/...` may differ if the NuGet version changed — check `Ben.Web.Playwright/Ben.Web.Playwright.csproj` for the exact `Microsoft.Playwright` package version.
