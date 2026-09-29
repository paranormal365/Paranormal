# Production on MonsterASP: the API at api.ishaunted.com (09/29/2026)

Branch `feature/api-subdomain`. iOS **1.1.1 (9)**; service change log 2.8.1.

## What happened

On 09/29 ishaunted.com moved from the self-hosted IIS box to **MonsterASP** (shared IIS). There the
website and the API are separate sites: the website at `ishaunted.com`, the API at the **root** of
`api.ishaunted.com` (so `https://api.ishaunted.com/api/...`), the editors at `video.` and `canvas.`.
`ishaunted.com/webapi/*` now 307-redirects to `api.ishaunted.com/*`.

## What it broke, and the fixes

- **The iPhone app reached the API signed out.** Every build up to 1.1.0 (8) calls
  `https://ishaunted.com/webapi`. URLSession follows the redirect but **drops `Authorization` on a
  cross-host redirect** (checked with a local redirect + echo server, GET and POST). Fix: 1.1.1
  calls `https://api.ishaunted.com` directly (`APIEnvironment.production`). `websiteURL` drops an
  `api.` label, so the join QR code and Safari pages stay on `ishaunted.com` (the universal-link
  host). A saved production choice at the old address loads as today's production. Older builds
  keep the feed and Field Kit; anything signed-in fails until people update.
- **The API would not start: "The system cannot find the file specified" importing the Maps key.**
  `ImportFromPem` sends PKCS#8 through CNG's key storage provider, which needs a loaded user
  profile; shared IIS runs without one. Immediate fix: MonsterASP → site → ASP.NET → Advanced →
  **Load user profile: Enabled** (both sites). Code fix: `Es256Jwt.ImportP256` reads the DER itself
  and builds the key from `ECParameters`.
- **Settings on shared hosting.** `scripts/deploy-ishaunted.ps1` sets secrets as application-pool
  environment variables on the box it runs on; that never reaches MonsterASP. There they are the
  site's environment variables (`__` for `:`), e.g. `ConnectionStrings__BenDbConnectionString`
  (the secrets.json name `SqlConnectionString` means nothing to the app), key paths
  `App_Data\Keys\AuthKey_<id>.p8`, `FileStorage__RootPath`, `WebApi__BaseUrl` on the website.
  The script also copies `SqlConnectionString` into both `appsettings.json` files (database and
  Serilog), so a bad value there beats a correct environment variable.

## Also fixed here

The 1.1.0 service change-log entry had landed inside the file's preamble, so `/changes` never showed
it; it is back under 09/28 (one version per day, which `ChangelogServiceTests` enforces).
