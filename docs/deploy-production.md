# Deploying IsHaunted.com

Four applications share one IIS site and one certificate:

| Path | What | Kind | Application pool |
|---|---|---|---|
| `/` | the website | Blazor Server — a .NET process | `IsHaunted.com` |
| `/webapi` | the WebApi | a .NET process | `IsHaunted.com-webapi` |
| `/editors/video` | the video editor | static files, runs in the browser | `IsHaunted.com-static` |
| `/files` | sidecar downloads | static files, kept outside the site | `IsHaunted.com-static` |

They are **separate IIS Applications** under a single site. That is what lets four things share
`https://ishaunted.com` without a second certificate, and it is not optional — see
[Why each one is an Application](#why-each-one-is-an-application).

The API sits at `/webapi` rather than `/api` because its own routes already start with `/api`;
mounting it at `/api` would give you `/api/api/cases`. ASP.NET Core learns its path base from IIS,
so nothing in the code changes: a controller at `/api/cases` answers on
`https://ishaunted.com/webapi/api/cases`.

The editor sits at `/editors/video` and not `/video-editor` because **the website already routes
`/video-editor`** to its own in-app editor page. An IIS Application at that path would shadow it
permanently — the page would be unreachable, and nothing would say why. `/editors/…` also leaves
room for the next editor.

## Prerequisites

- **.NET 10 Hosting Bundle** on the server. Supplies both the ASP.NET Core runtime and the
  `AspNetCoreModuleV2` handler that each `web.config` names. Missing it gives HTTP 500.19 or 502.5,
  and neither error mentions it.
- **WebSockets** enabled. Blazor Server holds a SignalR circuit per visitor. Without WebSockets it
  falls back to long polling — it works, and it is worse. On Windows Server this is Server Manager
  → Web Server → Application Development → WebSocket Protocol; on Windows 11 it is
  `Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -All`.
- A **SQL Server** the web server can reach, with the schema created. Nothing applies migrations at
  startup, so an empty or absent database is a running site where every request fails.
- **Telerik** packages, either from the global NuGet cache or the Telerik feed, and the licence key
  file at `%APPDATA%\Telerik\telerik-license.txt`. Without the key the build succeeds and the UI
  renders a trial watermark, which you find out about from a screenshot.

## Set the server up once

```powershell
.\scripts\setup-iis-ishaunted.ps1
```

Elevated. Creates the three application pools, the four applications, the folders, the file
permissions and the SQL logins, and copies `scripts\secrets.template.json` to
`C:\ishaunted-deploy\secrets.json`. It is idempotent — run it again after any change and it
reconciles.

It needs SQL sysadmin to create the pool logins, which the deploying account normally has. If it
does not, `-SkipSql` skips that section and the grants have to be made by hand.

Then fill in `C:\ishaunted-deploy\secrets.json` (see [Secrets](#secrets)) and check the database is
current:

```powershell
dotnet ef migrations list --project Ben.Data.Source --startup-project Ben.Data.WebApi
```

Nothing applies migrations at startup, so anything marked `(Pending)` has to be applied with
`dotnet ef database update` before deploying. `scripts\create-database.sql` is older than the
migrations and should not be used for this.

If you forget, the API says so. It checks on startup and logs
`DATABASE IS BEHIND: N migration(s) have not been applied — <names>`, naming them and the command
to run. It is a warning, not a refusal: most of the site works fine while one new table is
missing, and refusing to start would turn a partly-degraded site into an outage. **Worth grepping
the log for after any deploy that shipped a schema change** — otherwise the first symptom is an
"Invalid object name" error from whichever feature touches the new table first, which reads as a
broken feature rather than an unapplied migration.

## Deploy

```powershell
.\scripts\deploy-ishaunted.ps1
```

Elevated. Publishes all three projects, writes the machine's facts into each package, stages the
sidecar zips, copies everything into place and smoke-tests the result. Useful variants:

```powershell
.\scripts\deploy-ishaunted.ps1 -StageOnly          # publish and patch into artifacts\, touch nothing
.\scripts\deploy-ishaunted.ps1 -Apps webapi        # redeploy one application
.\scripts\deploy-ishaunted.ps1 -Apps webapi -StdoutLog   # ...with startup logging, to chase a 500.30
```

`-StageOnly` needs no elevation and is the way to see exactly what a deploy would ship.

The applications are always deployed API first, static next, website last, so the visible cut-over
happens once everything behind it is already in place. On the very first run the Coming Soon page
is moved to `C:\ishaunted-coming-soon-backup` rather than deleted — its logo and videos exist
nowhere else.

The bash scripts (`scripts/publish-*.sh`) still work and still do the same job; they are the macOS
path. The PowerShell script differs from them in three deliberate ways, each commented where it
happens: the website's settings go into `appsettings.json` rather than `appsettings.Production.json`
(same reason as the API, below), Serilog's own copy of the connection string is patched in *both*
applications, and the sidecar zips are staged under `/files` instead of inside the editor.

## Why each one is an Application

The root `web.config` registers the ASP.NET Core handler at `path="*"`. **Every** request under the
site — including `/webapi/...` and `/editors/video/...` — is handed to the website process unless
that folder is its own Application. The website looks in its own `wwwroot`, finds nothing, and
returns its 404. The files are all present and correct; nothing serves them.

The generated `web.config` files use `inheritInChildApplications="false"`, so the root's handler
does not leak down into the children — but that only takes effect at an application boundary,
which is exactly what a plain folder is not.

## Why three application pools

The website and the API are both hosted **in-process** (`hostingModel="inprocess"` in their
generated `web.config`). Two in-process ASP.NET Core applications cannot share an application pool:
IIS refuses the second with **HTTP 500.35, "ANCM Multiple In-Process Applications in same
Process"**. So the API gets its own pool, and the two static applications share a third.

Every pool is set to **"No Managed Code"**. Counter-intuitive and correct: .NET brings its own
runtime, so the pool must not load the old CLR. The static applications need no runtime at all.

Both .NET pools also get **Load User Profile** and **no idle timeout**. The first gives Data
Protection a durable home for its key ring — Identity's bearer tokens are encrypted with it, so
without it every recycle silently signs everyone out. The second matters because a Blazor Server
circuit is in-memory state, and the signed-in API session lives inside it.

## Why the settings are in appsettings.json, not appsettings.Production.json

An environment-specific settings file loads only when `ASPNETCORE_ENVIRONMENT` matches its name,
and a copy-deployed package has no say in what that variable says on the far end. The API's upload
root was in `appsettings.Production.json` and the server started with an environment that did not
load it — so it fell back to the empty string in the base file and refused to start with
"FileStorage:RootPath is not configured", for a setting that was sitting right there in the
package, correctly spelled, and simply never read.

The deploy script merges into `appsettings.json`, which loads whatever the environment says, ships
no environment-specific file at all, and additionally pins `ASPNETCORE_ENVIRONMENT=Production` in
each `web.config` so there is nothing left to infer.

## Secrets

`C:\ishaunted-deploy\secrets.json` holds them, readable only by Administrators and SYSTEM. It is
never in source control; `scripts\secrets.template.json` is the shape, with no values.
`scripts\deploy-ishaunted.ps1` reads it on every deploy.

**There is no SQL password.** SQL Server runs on the web server, so the application pools
authenticate as themselves — `setup-iis-ishaunted.ps1` creates a login for each pool's virtual
account (`IIS APPPOOL\IsHaunted.com-webapi` and `IIS APPPOOL\IsHaunted.com`) and grants it rights on
the database. Nothing to store, nothing on disk, nothing to rotate. The API gets `db_owner`, because
it seeds reference data at startup, migrates legacy file blobs and lets Serilog create its own
table; the website gets only `db_datareader` and `db_datawriter`, since it holds no DbContext and
touches the database purely through Serilog's error sink.

| Secret | Where it ends up | What breaks without it |
|---|---|---|
| `SmtpPassword` | environment variable `Smtp__Password` on the API's app pool — never a file | registration: accounts need a confirmed address, so people sign up and can never sign in |
| `GeocodioApiKey` | API `appsettings.json` | address lookup silently returns nothing |
| `SqlConnectionString` | both packages' `appsettings.json` — **normally left null** | nothing; the Integrated Security default applies. Set it only to reach a different server, and note that a password put here does land on disk |
| `AzureAd` | API `appsettings.json` | nothing — Entra sign-in stays off until `ClientId` is a real GUID |
| `SeedSuperAdmin` | API `appsettings.json` | nothing, if the database already has its administrator |

The SMTP password is deliberately absent from every appsettings file in this repository and must
stay that way. `Smtp__Password` — double underscore — is how .NET maps an environment variable onto
the `Smtp:Password` configuration key.

## The server talking to itself

The website calls the API server-side at `https://ishaunted.com/webapi` on every user operation, and
that name resolves to the site's **public** address. Reaching it means leaving through the router and
coming back in, which worked when measured here but not reliably — repeated connections to the
public address intermittently timed out while the same request over the LAN never did.

So the server gets hosts-file entries pointing its own names at itself:

```
127.0.0.1 ishaunted.com
127.0.0.1 www.ishaunted.com
```

The certificate still validates: the request carries the right SNI host name, IIS answers with the
real certificate, and the router is simply not involved. Without this the site works — until it
doesn't, for a few seconds, for no reason visible in any log.

## A trap in the DEVELOPMENT settings, recorded here because that file is gitignored

`appsettings.Development.json` is not in source control, so this cannot be fixed once for
everybody — if it is ever recreated, it will be recreated wrong.

Configuration **layers**. An overlay that simply omits `Smtp:Host` does not disable mail: it
inherits the real host from `appsettings.json`, and every local sign-up then opens a connection
to the live mail server. Ben's overlay also set `Port` to 587 while leaving
`Security: SslOnConnect` from the base — the one pairing MailKit refuses — so each attempt failed
slowly, and the sign-up button sat on "Creating your account…" long enough to look broken.

To genuinely disable mail locally, set the values to **null**, not absent:

```json
"Smtp": { "Host": null, "User": null, "Password": null }
```

`IEmailService.IsConfigured` is then false, every caller skips it, and `IdentityEmailSender` logs
the confirmation link so a local sign-up can still be completed. Measured effect: the sign-up
end-to-end test went from failing at 21 s to passing at 1 s.

**Production is not affected and was never wrong** — `appsettings.json` pairs 465 with
`SslOnConnect`, which is correct. If you ever want real mail locally, 587 goes with `StartTls`.

## Moving evidence storage to a bigger drive

Ben's plan (2026-08-31): move everything onto a 4 TB drive when the site launches, and add the
second drive when it fills. The database stores **relative** paths (`orgs/{guid}/{file}`), so
this is a config change and not a migration — every existing row keeps resolving.

**There is one trap, and it does not look like a storage problem.**
`DataProtectionSetup.ResolveKeyRingPath` falls back to
`FileStorage:RootPath/data-protection-keys` when `DataProtection:KeyRingPath` is unset. So
repointing the upload path **moves the Data Protection key ring with it** — and if the keys do
not travel, every signed-in person is silently signed out and every outstanding media ticket
stops resolving. Nobody connects that to a storage change, which is why it is written here rather
than remembered.

Do it in this order:

1. **Pin the key ring somewhere stable and OFF the external drive**, in `appsettings.json`:
   `"DataProtection": { "KeyRingPath": "C:\\ishaunted-deploy\\keys" }`
2. **Copy the existing key files** from `<current FileStorage:RootPath>\data-protection-keys`
   into that folder.
3. **Recycle the application pools** and confirm you are still signed in. If you were signed out,
   stop — the keys did not travel, and going further will hide the cause.
4. **Now** change `FileStorage:RootPath` to the new drive and move the files across.
5. Check a thumbnail and a media download, not just a page load: those go through the ticket
   path, which is what the key ring protects.

**Before it holds client evidence**, an external drive wants a backup story. A private residence's
photographs living on one USB disk with no second copy is a promise the site is implicitly making
and cannot keep.

## Check it, in this order

The deploy script does the first four automatically and fails if any of them do:

1. `https://ishaunted.com/webapi/api/public/cases?page=1&pageSize=1` — anonymous, and it reads the
   database. If this is wrong nothing else can work, so prove it first.
2. `https://ishaunted.com/` — the home page.
3. `https://ishaunted.com/editors/video/` — the editor. Checked for its `<base href>`, not just a
   200: if the application was never created, the website's own 404 page is also a 200-shaped
   HTML response.
4. `https://ishaunted.com/files/sidecar-video/win-x64/checksums.txt` — the downloads.

The rest is not provable from a status code:

5. **Sign in.** This is the only real test of `WebApi:BaseUrl`: the page renders whether or not the
   setting is right, and only an actual API call tells you.
6. **Sign in inside the editor**, and open the Server tab — that is what proves the editor's own
   API URL.
7. **Register a test account.** The confirmation email is the SMTP check.

The smoke checks run *from the server* on purpose. The website calls the API server-side at the
same public URL, so if the router cannot route a request back to itself — no NAT hairpin — they
fail here in exactly the way the website will fail later, while every page still renders. If that
is what you find, add `127.0.0.1 ishaunted.com` to the server's hosts file.

If the site returns 502.5 the app failed to start. Redeploy that application with `-StdoutLog`,
reproduce, read `logs\stdout`, then redeploy without it — the log grows without bound. The usual
cause is the database: startup seeding runs before the host begins listening, so an unreachable
SQL Server is a process that dies rather than a site that starts broken.

## Related

- [deploy-editor.md](deploy-editor.md) — what the editor's publish sets, and why each part fails
  silently if wrong.
