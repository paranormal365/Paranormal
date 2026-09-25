# Deploying IsHaunted.com

Five applications share one IIS site and one certificate:

| Path | What | Kind | Application pool |
|---|---|---|---|
| `/` | the website | Blazor Server — a .NET process | `IsHaunted.com` |
| `/webapi` | the WebApi | a .NET process | `IsHaunted.com-webapi` |
| `/editors/video` | the video editor | static files, runs in the browser | `IsHaunted.com-static` |
| `/editors/canvas` | the canvas editor | static files, runs in the browser | `IsHaunted.com-static` |
| `/files` | sidecar downloads | static files, kept outside the site | `IsHaunted.com-static` |

They are **separate IIS Applications** under a single site. That is what lets five things share
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

Elevated. Creates the three application pools, the five applications, the folders, the file
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

### EmailTemplates — one new table, and what happens while it is empty

`EmailTemplates` (2026-09-20) creates one table, keyed uniquely by the letter's `Kind`. Nothing
else is touched and nothing is dropped in `Up`.

**An empty table is the working state, not an unfinished one.** No row for a kind means the site
sends the letter its code writes, which is every letter until somebody writes one. So this
migration changes nothing anybody receives — it only gives the editor somewhere to save.

Applied to `IsHauntedDb_player` on 2026-09-20 and verified by asking the API for the letter list
(35 returned). **Production has not had it**, and until it does the API will not start against that
database at all: `/api/admin/email-templates` is the least of it — EF asks for the table on the
first request that touches it.

Run it with the other two below, in one pass.

### EventStaffRoomCursors — two nullable columns, and nothing to plan around

`EventStaffRoomCursors` (2026-09-20) adds `StaffRoomCoversUpToUtc` and `StaffRoomLastPostUtc` to
`HostedEvents`, both nullable `datetime2`. SQL Server adds a nullable column as a metadata change,
so it does not rewrite the table and does not hold a long lock the way the index migration above
does. Nothing is dropped and no data changes.

**It has NOT been applied anywhere yet** — not to `IsHauntedDb_player` either. Until it runs, the
API will not start against that database: EF asks for columns the table does not have, and the
failure is an immediate `Invalid column name 'StaffRoomCoversUpToUtc'` rather than anything subtle.

Both columns are cursors for the event staff room (item 238C). Null means "nothing posted yet", and
the first post covers only the recent past rather than the event's whole history, so applying this
late does not flood a venue's thread with months of old bookings.

### DashboardDateIndexes — additive, but it writes while it runs

`DashboardDateIndexes` (2026-09-19) adds one index to `AppUsers.DateCreated` and one to
`Cases.DateCreated`. Nothing is dropped and no data changes, so its `Down` simply removes them and
the rollback is complete.

The only thing worth knowing is that creating an index on a large table takes a write lock on it
for the duration. On a database this size it is seconds, but it is not the migration to run while
somebody is signing up. Apply it with the rest, before the deploy, rather than to a running site
mid-afternoon.

They exist because the dashboard's "new this week" figures filtered on `DateCreated` with no index
behind it — a full scan wearing a date filter, on the two tables that grow the most.

### One migration that destroys rows

`RetireResearchPages` (2026-09-16) **drops `CaseResearchEntries` and `CaseResearchAttachments`**.
Research is written on canvas boards now, and the block-editor pages that stood beside them are
gone. Its `Down` rebuilds the two tables **empty** — the schema comes back, the writing does not.

Before applying it to a database anybody has written research in:

```powershell
# Keep a copy. Any form will do; this one is readable and needs nothing installed.
sqlcmd -S <server> -d <database> -Q "SELECT * FROM CaseResearchEntries" -o research-entries.txt -W -s"|"
sqlcmd -S <server> -d <database> -Q "SELECT * FROM CaseResearchAttachments" -o research-attachments.txt -W -s"|"
```

Files those pages referenced are **not** touched: they are `UploadFiles` rows reached through the
case's Files tab, and they stay exactly where they are.

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

## Releasing a new sidecar

The sidecar is not deployed. It is a desktop app people install on their own machines, and the
deploy only STAGES the installers it finds in the drop folder — it never builds them. There is also
no auto-updater and no update feed, so the only thing that will ever tell somebody their sidecar is
old is the editor's own notice, and that notice believes whatever the site advertises.

**The artifacts go in the drop folder BEFORE the site that advertises them goes out.** The editor
compares the installed version against `SidecarRelease.Version` and sends people to the downloads
page. Deploy a site that advertises 1.1.0 while the drop folder still holds the 1.0.0 `.dmg`, and
every user is told to update and handed back exactly what they already have — which teaches them to
ignore the notice for the release where it matters.

### 1. Bump the version, in both places

`Ben.Video.Core/SidecarContracts/SidecarRelease.cs` and `<Version>` in
`Ben.Video.Sidecar/Ben.Video.Sidecar.csproj` must hold the same number. One is what the sidecar
reports from `/v1/health`, the other is what the site advertises; `SidecarReleaseVersionTests`
fails the build if they drift, because bumping one and forgetting the other nags everybody for ever
or nobody at all, and neither failure shows up anywhere except in a user's face.

Bump it when you PUBLISH, not when you change the code.

### 2. Build the installers — on two different machines

Neither can be built on the server alone, and that is not a limitation anyone can work around: the
Mac formats need Mac tooling and the Windows one needs Inno Setup.

| Artifact | Build it on | Why there |
|---|---|---|
| `BenVideoSidecar-osx-arm64.dmg` | macOS | `hdiutil` makes the disk image, `pkgbuild` the package, and the SDK ad-hoc-signs the apphost |
| `BenVideoSidecar-osx-x64.dmg` | macOS | same |
| `BenVideoSidecar-win-x64.exe` | Windows | `build-installer.ps1` needs Inno Setup's `ISCC.exe` |
| `BenVideoSidecar-win-x64.msix` | Windows | `build-msix.ps1` needs the Windows SDK's `makeappx.exe`; Store only, see §5 |

The .NET payload itself cross-publishes, so `build.sh` runs anywhere. It is only the packaging that
is tied to a platform.

**Fetch ffmpeg first, every time, on every machine.** `ffmpeg/` is gitignored, so a clone has none,
and — less obviously — a machine that built a release months ago holds the binaries of whatever pin
was current *then*. `FfmpegLocator.VerifyIntegrity` re-hashes them at startup against the manifest,
so a stale copy produces a package that installs, passes its health check, and refuses every job
with a 503: a failure that reads as a broken sidecar rather than a broken build. The Windows pin
moved on 2026-09-20 (the old dated autobuild tag 404'd inside five weeks), and the first build after
that re-pin would have shipped exactly that package if the fetch had been skipped.

```bash
Ben.Video.Sidecar/scripts/fetch-ffmpeg.sh  win-x64      # or .ps1 on Windows
Ben.Video.Sidecar/scripts/fetch-ffmpeg.sh  osx-arm64
Ben.Video.Sidecar/scripts/fetch-ffmpeg.sh  osx-x64
```

The script verifies the downloaded archive AND each extracted binary against the manifest, so a
mismatch stops there rather than at a user's machine.

```bash
# macOS, once per architecture
Ben.Video.Sidecar/installer/macos/build.sh      osx-arm64
Ben.Video.Sidecar/installer/macos/build-dmg.sh  osx-arm64
Ben.Video.Sidecar/installer/macos/build.sh      osx-x64
Ben.Video.Sidecar/installer/macos/build-dmg.sh  osx-x64
```

```powershell
# Windows
Ben.Video.Sidecar\installer\windows\build.sh              # the payload
Ben.Video.Sidecar\installer\windows\build-installer.ps1   # wraps it with Inno Setup
```

### 3. Put them in the drop folder, then deploy

Copy every `.dmg` and `.exe` into the drop folder the deploy reads (`-SidecarDrop`, defaulting to
`Ben.Video.Sidecar/installer/dist`). `deploy-ishaunted.ps1` copies each one to
`/files/sidecar-video/<rid>/` and writes the SHA-256 `checksums.txt` beside it — the only integrity
story an unsigned build has.

A missing file is not silent: the deploy warns `No installer for <rid> ... the downloads page will
404 that link` and names both build steps. Read those warnings — a 404 on the downloads page is
what the update notice sends people to.

### 4. Check it

- `https://ishaunted.com/files/sidecar-video/osx-arm64/BenVideoSidecar-osx-arm64.dmg` downloads
- its `checksums.txt` matches `shasum -a 256` of the file you built
- the editor's Native acceleration panel, against an OLD sidecar, offers the update

### 5. The Microsoft Store package (a separate route, not a fourth artifact)

The `.msix` is **not** staged by the deploy and never appears on the downloads page. It goes to
Partner Center, and Microsoft serves it. Everything in §3 and §4 is about the files we host
ourselves; this section is the other path.

It is a different payload from the `.exe` beside it, in two ways that both matter:

**ffmpeg is the LGPL build**, pinned separately in `ffmpeg-manifest.store.json`, because a Store
submission is a redistribution we would rather keep permissively licensed. That file travels into
the package *as* `ffmpeg-manifest.json`, so the startup integrity check tests the binaries the
package actually carries. You do not fetch it yourself: `build-msix.ps1` calls `fetch-ffmpeg.ps1`
with that manifest and its own output folder, kept separate from `ffmpeg/` so the GPL and LGPL
builds cannot be confused for one another, and downloaded once because it is ~150 MB.

**The LGPL build has no libx264 or libx265.** It carries libopenh264, h264_mf and libkvazaar —
all three tested on 2026-09-20, all three do produce video. `VideoEncoders` chooses from what
ffmpeg reports it has, so the app needs no build-time switch. What does need care is *quality*:
`ArgvFactory` passes x264's `-preset` and `-crf`, which none of the three replacements accept.
Check an export from the Store build before submitting, not after.

**Identity comes from Partner Center** — Product management → Product identity. Without it the
script stamps a placeholder and says so; that package is good for testing and cannot be uploaded.

```powershell
Ben.Video.Sidecar\installer\windows\build-msix.ps1 `
    -IdentityName <from Partner Center> `
    -Publisher "CN=<from Partner Center>" `
    -PublisherDisplayName "<from Partner Center>"
```

The package version is the app version with a fourth part forced to `0` — the Store reserves the
revision field for itself.

**Nothing here is signed, and that is the point of the route.** Microsoft re-signs Store packages,
so a Store install shows no SmartScreen warning, unlike the `.exe` we host. To install the `.msix`
on your own machine for testing it must be signed with a certificate that machine trusts; trusting
one needs an administrator, so `-SelfSign` writes the package and prints the two elevated commands
rather than running them.

**One gap to know about while testing.** The plain zip payload's `install.ps1` does not register the
`benvideo-sidecar:` scheme — only the Inno installer and the MSIX manifest do. The editor's sidecar
switch has an asymmetry behind it: *off* is a request to a running program, but *on* can only be an
OS-handled link, because a web page cannot start a process. So a tester installed from the zip gets
a switch that turns off and never turns back on. `ProtocolSchemeContractTests` holds the three
declarations in agreement; it cannot make the zip a fourth one.

### History

**1.1.2** — the on/off switch, and an ffmpeg that is chosen rather than assumed.

Windows gains a switch in the editor's Native acceleration panel. **Off** is `POST /v1/shutdown`,
token-gated like every other endpoint, refused with 409 while a job is running so nobody's export is
thrown away silently. **On** is a registered `benvideo-sidecar:` link the editor opens, because a web
page cannot start a program; the installer now writes that handler under `HKCU\Software\Classes`, and
the Store package declares it in its manifest. macOS is unchanged — launchd already starts it on
demand and stops it when idle, so a switch there would offer to do what already happens.

**The button is gated on a capability, not on the version.** A sidecar advertises `shutdown` from
`/v1/capabilities`, and the editor shows "Turn off" only when the connected one does. That is what
lets a site advertising 1.1.2 sit safely in front of installed 1.1.1s: they simply do not get the
button, rather than getting one that 404s.

The exporter also no longer assumes libx264. It asks ffmpeg what encoders it has and picks the best
one present, giving each its own rate-control arguments — which is what allows the permissively
licensed build the Microsoft Store package needs. On such a build H.264 runs through `h264_mf` and
VP9 through `libvpx-vp9`; H.265 is refused with a message naming what to pick instead, because the
only remaining candidate rejects frame sizes that are not a multiple of 8. A VP9 bug went with it:
`-crf` without `-b:v 0` is a ceiling on libvpx's default 256k bitrate rather than a quality target,
so VP9 exports came out far worse than asked for.

**Both platforms need the 1.1.2 build before the site advertises it** — the same rule as every
release below. The Windows installer can go up on its own beforehand; the notice only nags somebody
whose installed version is *older* than what the site publishes.

**1.1.1** — Windows only, in effect; macOS behaves exactly as 1.1.0 did. 1.1.0 armed its fifteen
minute idle shutdown on every platform, but only macOS has anything that starts the sidecar again:
launchd holds the socket and relaunches it on the next connection. The Windows installer starts it
once, at login, from a Run key. So on Windows the sidecar stopped fifteen quiet minutes after login
and stayed stopped until the next login, and the editor quietly fell back to doing the work in the
browser. 1.1.1 arms the timeout only where something will bring the process back
(`IdleShutdownPolicy.EffectiveTimeout`); on Windows it runs from login again, as 1.0.0 did.
**Both platforms need the 1.1.1 build before the site advertises it**, or every Mac user is told to
update and handed 1.1.0 back.

**1.1.0** — the first release worth telling anybody about, and the reason the notice exists at all.
1.0.0 resolved its content root to `/` under launchd and put a recursive file watch over the entire
filesystem: a pinned CPU core for as long as it ran, 2.9 GB resident, and `/v1/health` answering in
340ms. It also ran from login to shutdown whether or not anyone opened the editor. 1.1.0 starts on
demand when the editor looks for it and stops fifteen minutes after the editor closes.

## What a release needs besides the deploy

Newest first. Each entry is what the database or the site settings need for that release, in the order to do it. Remove
nothing: a server that skipped a release needs the older entries too.

### Storefront — not released yet (date this entry the day it ships)

The store ships dark: the code can be deployed and the catalogue entered on the live site long
before anybody can buy. The whole sequence — the Stripe dashboard, the keys, the migrations, the
dark period and opening — is in [stripe-go-live.md](stripe-go-live.md), under "The store". What the
database needs, in order, **before** the code is deployed:

1. `StoreCatalog`, `StoreCartsAndOrders`, `StoreFavouritesAndReviews`, `StoreProductSeller`,
   `StoreSubcategories` — `dotnet ef database update` with an explicit `--connection` (it ignores
   the environment variable). Additive: 21 new tables, two new nullable columns; nothing existing is
   changed or dropped.

   With the sellers work (branch `store-sellers`), then: `StoreProductSaleCounters` (one nullable
   column, backfilled from paid orders), `StoreProductHistory` (one new table, a "Created it."
   line backfilled per product) and `StoreSellerSaleRequests` (one new table, two nullable columns,
   `FirstOnSaleUtc` backfilled) `StoreProductParts` (one table, two columns) and `StoreOrderParcels` (one table, two nullable
   columns; one package backfilled per existing order). Additive — except `StoreOrderTrackingToParcels`,
   which moves any order-level tracking onto package 1 and then drops StoreOrders' Carrier, TrackingNumber
   and TrackingUrl. Deploy the code with it: the old code reads those columns. Then `StoreRefundShipping`
   (one new table).
2. Deploy `webapi` and `website`. `features.store` stays off until the checklist on
   `/admin/store/settings` reads **Ready to sell**.

### 2026-09-17 — research cards, address lookup, the client's words, and a Research file type

1. **Deploy the canvas application, or none of this appears**:
   `.\scripts\deploy-ishaunted.ps1 -Apps webapi,canvas,website`. Almost everything in this release
   lives in `Ben.Wasm.Canvas`, which is its own IIS application at `/editors/canvas/` — the website
   and the API alone would deploy cleanly and change nothing anybody can see. Check it afterwards:
   `/editors/canvas/` answers 200, and a board's card menu offers **Make a map** on a card with an
   address in it.
2. **No migration for the research board** (there is one for place posts — see 6). The one new
   database row here is a file type, and `UploadFileTypeSeeder` adds it on
   startup as it does the others; running it again changes nothing. After deploying, Site
   Administration → File Types should list **Research** beside Case Evidence and Board Snapshot.
3. **The API server needs to reach Apple's Maps API**, which it already does for every map on the
   site: the map box's new Find button, a pasted address and **Make a map** all go through
   `api/geocode/search`. That endpoint answers anonymously and is rate limited, because its calls
   spend the same daily allowance the maps do.
4. **Boards now ask for link previews to be kept** (`POST api/link-previews`), so the outbound HTTPS
   and the file store's `link-previews` folder from the 2026-09-14 entry are what a board's link
   cards need too. Without them a card falls back to the site and address, which is what it did
   before — nothing breaks, the pictures are just missing.
5. Nothing to turn on. Everything here is part of the research board, which is already on.
6. **One migration, and it must be applied before the website is deployed**: `PlacePosts` adds a
   nullable `PlaceId` to `OrgMessages` with an index and a SetNull foreign key to `Places`. One
   `AddColumn`, so SQL Server applies it in place with no table rebuild and no downtime.
   `dotnet ef database update` applies it along with anything older. Deploy the API and the website
   together afterwards: the website asks a place's page for its posts, and an older API simply
   answers without them, which shows as a place with no posts rather than an error. Check it
   afterwards: a public place's page offers a box under **Posts about this place** to anybody signed
   in, and a private residence's page offers none. Nothing to turn on — place posts follow the
   existing public-feed switch, so they appear only where the feed already does.
7. **The API and the website both matter for a case's evidence, and for different reasons.** The API
   now answers a byte range for video, audio and images, which is what lets a recording play in the
   page at all — Safari refuses a `<video>` that cannot be seeked, and Ben's upload showed a black
   rectangle with dead controls until this. The website carries the new upload path: the browser
   posts a case file to `/uploads/case-file/{org}/{case}` on the site's own origin, which redeems a
   short-lived ticket and streams the body to the API. Deploying one without the other leaves either
   a file that will not play or an upload button that posts to a URL nothing answers. No migration,
   no setting. Check it afterwards: a case's **Files** tab names a chosen file with a bar that fills,
   and a video already on the case plays where it sits.

8. **The audit fixes are code-only, and one of them is why this release should not wait.** No
   migration and no setting: `.\scripts\deploy-ishaunted.ps1 -Apps webapi,website`.

   The reason to deploy promptly is that a place's page serves a **private residence's street
   address, postcode and exact map pin to anybody holding the URL**, and the same projection with
   no scoping to any signed-in account. Deploying the API alone fixes it — the withholding is
   entirely server-side — so if the website deploy has to wait for any reason, deploy the API
   anyway.

   **Nobody's home is exposed today, and this was checked rather than assumed** (added 2026-09-17,
   correcting an earlier "that is live on production now" in this entry). All six places on the
   live database are `PublicLocation`; there is not one `PrivateResidence` row, so the leaking
   projection has nothing to leak. Deploy before that changes — the first person to add their own
   house is the one exposed, and they will have no way of knowing. That is a reason to go now, and
   a better one than a breach that is not happening:

   ```sql
   SELECT Kind, COUNT(*) FROM Places GROUP BY Kind;   -- Kind 1 = PrivateResidence, 2 = PublicLocation
   ```

   Deploy both together for everything else, because several fixes are a new page or a new button
   against an endpoint that already exists:

   - `/moderation/archive` (Moderation → Place Archive) is new and is the only way to release a
     field session or a photograph somebody flagged. **Worth working through once after
     deploying**: anything flagged before today has been held with no way back, so there may be a
     backlog nobody could see. It opens showing what is held.
   - `/admin/mail` grows the outbox list and its retry buttons.
   - The bookings board grows **Invite by email** and **Book somebody in**.
   - Equipment and experience taxonomy grow **Rename**, and the merge offer that goes with it.
   - `/my-field-sessions` shows storage used.

   An older website against the new API is safe everywhere: each of these is an addition, and the
   pages that read them are the new ones. An older API against the new website is the pairing to
   avoid — the new pages would call endpoints that answer 404.

   Check it afterwards: open a private residence's place page **signed out** and confirm it shows
   the city and state with no street address and no exact pin; then Moderation → Place Archive
   answers 200 and lists whatever is held.

9. **The billing fixes are code-only too — API only, no migration and no setting**:
   `.\scripts\deploy-ishaunted.ps1 -Apps webapi`. Nothing on the website changes for them.

   **Nothing here has harmed a live customer, and that was checked rather than assumed.** This
   entry first said two of these were "actively costing money right now". That was wrong: it was
   inferred from the code without looking at the data. A read-only pass over the live database on
   2026-09-17 found **no subscriptions, no seats, no ledger rows and no coupon redemptions at
   all** — so there is nothing to repair and no back-billing to decide about. Every fix below is
   preventive, and the first group to subscribe is the one it protects.

   Re-run that check before believing this paragraph on a later date, because it stops being true
   the moment somebody subscribes. The query under the first item is the one to start from.

   **Two of them would bite hardest once there IS a paying customer, and both are on the API alone:**

   - A SuperAdmin editing a subscription's period set its provider to "Manual", and the renewal job
     only charges subscriptions marked "Stripe". Any group whose period is hand-adjusted after
     subscribing **silently stops being billed**, with every provider reference still in place so
     nothing looks wrong on any screen. No group is in that state today — see above — but it is a
     single period edit away, and the edit is a routine thing to do. Worth checking: Site Administration
     → Subscriptions, look for an Active group on a paid band whose provider reads Manual and that
     you did not set up manually. The fix stops it recurring; it cannot repair a row already
     flipped, so those need setting back to Stripe by hand.

     The rows to look at, read-only — a group with a saved card that the renewal job is skipping,
     which is the exact signature of the bug (a genuinely manual group has no `ProviderCustomerRef`):

     ```sql
     SELECT o.Name, s.ProviderName, s.Status, s.CurrentPeriodEnd, s.DateUpdated
     FROM   OrganizationSubscriptions s
     JOIN   Organizations o ON o.Id = s.OrganizationId
     WHERE  s.Status = 1                      -- Active
       AND  s.ProviderName <> 'Stripe'
       AND  s.ProviderCustomerRef IS NOT NULL
       AND  s.ProviderPaymentMethodRef IS NOT NULL
     ORDER  BY s.CurrentPeriodEnd;
     ```

     Anything it returns was set up to be charged automatically and is not being. Setting
     `ProviderName` back to `'Stripe'` resumes it at the next period end; decide per group whether
     to also collect the periods that were missed, because the fix deliberately does not
     back-charge anybody.
   - An overflow seat was never lapsed, so a seat whose card declined was retried on every pass
     indefinitely, and back-charged every missed month at once when the card finally worked. After
     deploying, the lapse job ends any seat already past its period end on its next run and writes
     to the holder once.

   Two more change what a person is charged, in their favour, from the moment it lands: a member
   stops paying for their own seat once the group's plan grows to cover them, and a tour added
   mid-period is priced at the rate the group signed up at rather than a price that has risen since.

   Event-credit receipts now record the tax that was actually charged rather than re-deriving it, so
   a receipt and the card statement cannot disagree. Rows already written are not rewritten — the
   ledger is append-only — so any existing mismatch stays as it is and is corrected, if it matters,
   with an Adjustment row the way the ledger's own rules say.

   An older website against this API is safe: nothing here changes a contract the website reads.

   Check it afterwards: a group's billing page still shows its plan, price and receipts, and a
   receipt still downloads.

### 2026-09-18 — live configuration changed by hand (no deploy needed for these)

Two **data** changes were applied directly to the live database. Neither needs a deploy; both are
already in effect. Recorded here because a reseed, a restore or a migration has to know.

1. **The price ladder gained a Free band.** Until now no band was priced at nothing, so
   `TierAreaResolution.FreeTierAsync` returned null for any group with no subscription and every
   capability check **failed open** — the whole paid lane included for free. The ladder is now:

   | band | members | monthly |
   |---|---|---|
   | Free | 1–1 | $0 — excludes `PrivateResidenceCases` |
   | Small Group | **2**–3 | $20 |
   | Standard Group | 4–10 | $40 |
   | Large Group | 11–25 | $60 |
   | Enterprise | 26+ | $100 |

   **Small Group moved from 1 to 2 in the same transaction and must stay there.** `Validate`
   refuses an overlapping ladder and `Resolve` *throws* on one, which takes checkout, the permission
   area gate and the renewal job down together. If you ever edit these bands, keep them contiguous
   from 1 with an unbounded top.

2. **A comp campaign exists**: 100% off, every period forever, one generated single-use code,
   campaign capped at one redemption. It is how Apple-Beta — the iOS release test group — is given
   Small Group free. The code is NOT recorded in this repository on purpose: this repo is public and
   a free-forever code in it is a free plan for anybody who reads it. It is in the `CouponCodes`
   table. To comp another group, raise the campaign cap on the coupon screen and generate a second
   code; never reuse one, and never make it a shared code.

**One deploy is still owed, and this is the reason to do it promptly.** Adding the Free band walled
new groups in: a one-member group resolves to Free, Free lists at nothing, checkout refuses a zero
list price, and `PaidPlan.WhyCannotAddMemberAsync` refuses the second member without a plan. Cannot
buy because too small, cannot grow because has not bought — and **every group starts with one
member**. The fix (`BillableUnits` prices a purchase at the cheapest band actually sold) is code and
does nothing until the API ships:

```bash
.\scripts\deploy-ishaunted.ps1 -Apps webapi
```

Apple-Beta is not affected — it has two members and prices into Small Group — but any group created
on the live site before that deploy is stuck. Check afterwards: a brand-new group's billing page
offers a plan at $20 rather than refusing with "There is nothing to subscribe to at that size."

### 2026-09-16 — the case canvas becomes the Research tab

1. Apply the migrations, in order, before deploying — `dotnet ef database update` applies all of them:
   - `AddCanvasEditor` — the boards table and the link-unfurl cache.
   - `CanvasPublishedDocument` — the published copy of a board and the revision it came from.
   - `CanvasPieceOwners` — who put each piece on a board.
   - `RetireResearchPages` — **destroys rows**; read the section above before running it.
   Everything but the last is additive.
2. **Deploy the canvas application too**: `.\scripts\deploy-ishaunted.ps1 -Apps webapi,canvas,website`. It publishes
   `Ben.Wasm.Canvas` to `/editors/canvas/`, which `setup-iis-ishaunted.ps1` creates as its own IIS application on the
   static pool. A site that skips it has a Research tab whose boards open a 404. Check it after deploying:
   `/editors/canvas/` must answer 200.
3. **There is no switch, and looking for one wastes an evening.** This entry said until 2026-09-17 that the canvas
   waited behind Site Settings → Features → *Canvas editor*. That flag was **removed** on the day it shipped, while
   this entry still described the plan it had been written against. Research is boards now — the block-editor research
   pages it would have fallen back to are the rows `RetireResearchPages` drops — so a site with the switch off would
   have had no research at all, and a flag whose off position breaks the product is a trap rather than a choice.
   `CanvasDeployScriptGuardTests.No_canvas_feature_flag_is_declared_anywhere` keeps it from coming back.
4. **What changes for people**, from the moment the deploy lands: research is written in the canvas on their own
   machine; a board is theirs alone until they publish it; publishing shows it to the group and files a picture on the
   case. A member who may edit the case can add to somebody else's board but not rework it — that is for the author, a
   group administrator, or a site administrator.

### 2026-09-14 — beta feedback (research pages, formatted notes and messages, plans off sale)

1. Apply the two migrations, in order, before deploying — `dotnet ef database update` applies both:
   - `CaseMessageBodyHtml` — one nullable column on case messages for the formatted copy.
   - `ResearchPages` — research-page columns on research entries, and the research attachments and link previews
     tables. Additive only; no existing row changes.
2. Nothing to run for case notes: the API converts plain-text notes to HTML once, in the background, on its first
   start. The log says how many it converted.
3. **Sell plans and seats** (`billing.purchases-enabled`, under Site Settings → Selling plans) is on when unset, so the
   site keeps selling after the deploy. Turn it off there to take plans off sale.
4. Link previews read other sites from the API server: it needs outbound HTTPS (443) and HTTP (80) to the internet, and
   writes small pictures under the file store's `link-previews` folder. Verified on macOS only; after deploying, paste a
   public web address into a research page and check the card shows its picture.

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
| `AppleTeamId`, `AppleMapsKeyId`, `AppleMapsKeyPath` | API and website `appsettings.json` (`Maps:*`) | every map says it could not be loaded; address lookup answers nothing |
| `AppleSignInKeyId`, `AppleSignInKeyPath` | API `appsettings.json` (`Apple:*`) | Apple tokens are never revoked when an account is deleted |
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

`IEmailService.IsConfigured` is then false and nothing leaves the machine, but every letter is
still queued in the outbox. A local sign-up or seat pick is completed from its link at `/admin/mail`,
signed in as the SuperAdmin — which is also where the browser tests read it
(`BenTestBase.LinkFromTheOutboxAsync`). Measured effect of nulling the values: the sign-up
end-to-end test went from failing at 21 s to passing at 1 s.

The link is **never** written to the log. It used to be, "so a local sign-up can still be
completed"; but a confirmation link finishes somebody's account and a pick link holds their seats,
and a log is not a place for a credential. `NoCredentialsInLogsTests` refuses any log line in the API
that names a `{Token}` or `{Link}`.

**Production is not affected and was never wrong** — `appsettings.json` pairs 465 with
`SslOnConnect`, which is correct. If you ever want real mail locally, 587 goes with `StartTls`.

## A second DEVELOPMENT-settings note: the API base URL

`Ben.Web.Website/appsettings.Development.json` is gitignored too, so this is recorded here for the
same reason as the SMTP trap above.

Set `Services:BaseUrl` (both occurrences) to **`http://127.0.0.1:5252`**, not
`http://localhost:5252`.

Local hosts bind IPv4 only — see backlog item 187: `localhost` makes Kestrel open an IPv6 listener
as well, and .NET on macOS has a bug in the IPv6 accept path that kills the process outright, which
made every long test run flaky. With the API listening on `127.0.0.1` alone, a `localhost` base URL
still works, but every server-to-server connection first attempts `::1`, is refused, and falls back.
Naming the address skips the doomed attempt.

**Production is unaffected and must not copy this.** There the value is overridden per-deployment,
and Windows/IIS does not have the bug.

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

## How long the error log is kept

`LogRetentionJob` deletes rows from the `Logs` table older than a window. **Default 30 days**, set
by `Logging:Retention:Days`.

| Value | Effect |
|---|---|
| unset | 30 days |
| `0` or negative | **Off.** Nothing is ever deleted. |
| 1–6 | **Clamped up to 7** and a warning is logged. A mistyped `1` must not empty the table. |
| 7 or more | Honoured exactly. |

`Logging:Retention:TableName` exists for completeness and defaults to `Logs`. It is validated as a
plain SQL identifier and the job refuses to run rather than guess if it is anything else.

**It touches nothing but that one table.** `AuditLogs` is deliberately excluded — the audit trail
is archived, never deleted, because *who did what, when* is part of what the platform sells.
`SignInEvents` is excluded too; it feeds the sign-in insights dashboard.

**On first deployment it will delete nothing**, because no row is older than the window yet. That
is expected, not a misconfiguration. It sweeps at most once every six hours, and says nothing at
all when there was nothing to do.

**Watch out when querying these tables by hand.** `AuditLogs.OccurredAt` is **UTC**;
`Logs.TimeStamp` is **LOCAL** time — Serilog's sink stores the logging process's clock. Measured
2026-08-31, the same instant read 19:31 in one table and 14:30 in the other. Nothing in the column
names warns you, and a cutoff built from the wrong clock shifts the window by the whole UTC offset.

## What the site sends, and what it records

Every account email — confirmation, both password resets, and the "somebody tried to sign up with
your address" notice — goes out through one branded layout with the site logo. The logo is an
**absolute** URL to `/icon-192.png` on the public site, because a mail client resolves nothing
relative, and it is a **PNG** because most clients strip SVG. If that path stops being served, the
emails still arrive but every one shows a broken image.

Two columns on `AppUsers` record what happened, and both are visible in Administration → Users:

- `DateConfirmationSent` — stamped only when a send actually **succeeds**. "Handed to the mail
  server" is all it can honestly claim; acceptance is not delivery. But it separates *we never
  tried, or we failed* from *it left here*, which is the distinction that was missing when a real
  sign-up produced no email and nothing anywhere could say why.
- `DateEmailConfirmed` — Identity keeps confirmation as a bare true/false with no time on it.

A failure is logged at **Error** with no link in it, so it survives in the `Logs` table where the
sink keeps only Error. There is no second line carrying the link any more: the letter itself waits in
the outbox, and `/admin/mail` shows it — audited, SuperAdmin only — to anybody who needs to finish a
flow by hand.

Members can ask for a new link themselves: the "Confirm your email address first" message on the
sign-in page carries a **Send the email again** button, throttled to one a minute.

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
7. **Open `/admin/mail`** (Administration → System → Outgoing Mail) as a SuperAdmin, and press
   **Send a test message**. This is now the SMTP check, and it is a far better one than registering
   a test account: it runs inside the web application, so it sees the app pool's own
   `Smtp__Password` — the value a shell on the same server cannot read — and it prints the SMTP
   server's error verbatim instead of failing silently.

   **If it says "A host is set but no password is present", that is the fault**, and no amount of
   retrying will fix it: add `Smtp__Password` to the API's app pool environment and recycle the
   pool.

8. **Then register a test account** and confirm the branded email arrives with the logo showing.
   That proves the whole path end to end, including that the logo URL is reachable from a mail
   client — the layout points at `https://ishaunted.com/icon-192.png`, so a site that is up but
   serving that path wrongly gives everyone a broken image.

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
- [deploy-canvas.md](deploy-canvas.md) — the case canvas at `/editors/canvas`: its project path,
  the migration it needs first, its feature switch and its rollout.
