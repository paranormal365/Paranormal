# Running Playwright Tests

Front-end E2E tests live in `Ben.Web.Playwright/`. They run against a live dev stack and require the app to be running and seeded.

---

## One-time setup

### 1. Build the project to generate the Playwright CLI
```bash
cd /Users/ben/Source/Ben
dotnet build Ben.Web.Playwright -p:IsTestProject=true
```

> `-p:IsTestProject=true` is required on every build and test command here. The csproj sets
> `IsTestProject=false` so that `dotnet test Ben.slnx` doesn't try to run browser tests in the
> unit-test sweep; without the override `dotnet test Ben.Web.Playwright` finds **zero tests**
> and still exits 0, which reads exactly like a passing run.

### 2. Install Chromium (only needed once per machine)
The browser must match the `Microsoft.Playwright.NUnit` version in the csproj, so drive the
install from the CLI the build just dropped next to the assembly rather than a pinned NuGet path:
```bash
cd Ben.Web.Playwright/bin/Debug/net10.0 && ./.playwright/node/*/node ./.playwright/package/cli.js install chromium
```

> If you have PowerShell (`pwsh`) available you can alternatively run:
> `pwsh Ben.Web.Playwright/bin/Debug/net10.0/playwright.ps1 install chromium`

---

## Before every test run

### 1. Start the full stack
Use the VS Code task **`start-full-stack`** (Terminal → Run Task), or:
```bash
bash scripts/start-website-with-api.sh
```
Wait until both the WebApi (port 5252) and the Website (port 5078) are listening.

> The suite targets `Ben.Web.Website` on **:5078** — the only front end now. The original
> `Ben.Web.WebApp` on :5079 was removed once the template port finished; :5078 is also the
> redirect URI registered with Entra, so Microsoft sign-in works there without touching the
> app registration.

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
dotnet test Ben.Web.Playwright -p:IsTestProject=true --no-build -e BEN_BASE_URL=http://localhost:5078
```

### A single category
```bash
dotnet test Ben.Web.Playwright -p:IsTestProject=true --no-build --filter TestCategory=Smoke
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
| `Capture` | **Writes files.** Re-captures the help screenshots and recordings — see below |

### A single test by name
```bash
dotnet test Ben.Web.Playwright -p:IsTestProject=true --no-build --filter "FullyQualifiedName~LoginPage_HasRequiredFields"
```

---

## Re-capturing the help screenshots

The `Capture` category is not a test of the app — it drives the site to produce the screenshots and
GIFs the help documents embed, and it **writes into the working tree**. It is skipped unless
`BEN_CAPTURE=1` is set, so an ordinary run cannot leave files behind:

```bash
BEN_CAPTURE=1 dotnet test Ben.Web.Playwright -p:IsTestProject=true --no-build --filter TestCategory=Capture
```

Notes on what it does, because each one was a bug first:

- **Dark mode** is seeded into localStorage by an init script, so the first paint is already dark.
- **The operator's own profile photo is masked** in every shot and recording. Whoever captures
  these is signed in as a real administrator.
- **Recordings never film the sign-in page.** Sign-in happens on a non-recorded context, and the
  recorded one resumes that session — the Development login form arrives pre-filled.
- **A shot of an empty screen fails the capture.** Each one names text it must contain.

Afterwards, rebuild the PDF so the manual matches (see `docs/README.md`).

---

## Headed mode (watch the browser)

Set the environment variable before running:
```bash
export HEADED=1
dotnet test Ben.Web.Playwright -p:IsTestProject=true --no-build --filter TestCategory=Smoke
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
| `BEN_BASE_URL` | `http://localhost:5078` | Front-end root URL (`Ben.Web.Website`) |
| `BEN_API_URL` | `http://localhost:5252` | WebApi root URL |
| `BEN_SUPERADMIN_EMAIL` | `haveben@msn.com` | SuperAdmin login |
| `BEN_SUPERADMIN_PASSWORD` | *(see appsettings)* | SuperAdmin password |
| `BEN_USER_EMAIL` | `sarah.mitchell@benco.dev` | Regular user login |
| `BEN_USER_PASSWORD` | *(see appsettings)* | Regular user password |

Passwords are in `Ben.Data.WebApi/appsettings.Development.json` under `SeedData` — do not commit them.

---

## Troubleshooting

**Tests time out on map or card selectors**
The Blazor circuit takes a moment to connect. Tests already have 12–15 s timeouts for async-loaded elements. If they still time out, check that the site started cleanly, and that the API behind it did too
(`tail -f .vscode/webapi.log`). `NetworkIdle` is not a settled Blazor page — the circuit loads its
data after the network goes quiet, so wait on a known element rather than on idle.

**"No test matches the given testcase filter", or a run that finds zero tests**
Almost always the missing `-p:IsTestProject=true` (see the setup note above). If the override is
already there, rebuild — a stale assembly won't have a newly added test:
```bash
dotnet build Ben.Web.Playwright -p:IsTestProject=true && dotnet test Ben.Web.Playwright -p:IsTestProject=true --no-build
```

**Login tests fail with wrong password**
The default passwords are set by `DevelopmentDataSeeder`. If the DB was re-seeded with different values, update the `BEN_*_PASSWORD` env vars or check `appsettings.Development.json`.

**Chromium not found**
Re-run the install step. The browser cache (`~/Library/Caches/ms-playwright`) may hold builds for
older Playwright versions only; the bundled node CLI in the install step above always fetches the
build that matches the referenced package.
