# Full Code Audit — develop branch (2026-08-16)

A complete audit of the Ben solution on `develop` (commit `3a3ef21`, post-Paranormal migration):
gaps, bad practices, consolidation opportunities, and staleness. **No fixes were applied** — this
is the working document to burn down. Scope is the Ben repo only; the Media Projects
(`../Github-BenVideo`) are a separate repo audited separately.

**Baseline health (verified today):** solution builds clean (0 warnings, 0 errors) and all
**2,219 tests pass** (377 RepositoryService + 1,842 Web, 0 skipped). Prior security-audit phases
A–D remain in effect; nothing from those phases has regressed, and several previously-flagged gaps
(UploadFile GetAll/Download auth, GetById visibility) are confirmed fixed with explanatory comments
in place.

**How to read severity:**
- **P1** — architectural debt or a real operational risk; schedule deliberately
- **P2** — worth fixing soon; medium effort or medium impact
- **P3** — hygiene / polish; batch into a cleanup pass

Effort: S (< half a day), M (a day-ish), L (multi-day).

---

## Section A — Dead code & dead architecture (the big consolidation wins)

### A1. The entire generic repository layer is dead code — P1, effort M
**Where:** `Ben.Service.RepositoryService/` — `RepositoryManager.cs`, `AppUserRepositoryManager.cs`,
`OrganizationRepositoryManager.cs`, `GenericInterfaces/RepositoryBase.cs` + `IRepositoryBase.cs` +
the three `I*RepositoryManager` interfaces, all 50 files in `Repositories/`, all 50 files in
`EntityInterfaces/`.

**Evidence:** `IRepositoryManager` is registered in DI (`Ben.Data.WebApi/Program.cs:115`) and then
never injected anywhere. Every repository class and entity interface is referenced *only* by its own
tests (`RepositoryManagerTests`, `UserRepositoryManagerTests`, `OrganizationRepositoryManagerTests`,
`RepositoryReadPathTests` — ~48 tests exercising code no production path reaches). Meanwhile 127 of
the WebApi's controller/helper files use `IDbContextFactory<BenDataContext>` directly.

**The call to make:** either (a) adopt the repository layer as the mandated data-access path — a
large refactor nobody has been doing organically — or (b) delete the ~103 dead files + their tests +
the DI registration, and codify "controllers use `IDbContextFactory` + shared access-helper classes"
as the official architecture. Given 21.5K lines of controllers already follow pattern (b), deletion
is the honest option. What actually earns its keep in `Ben.Service.RepositoryService` and stays:
`AuditLogService`, `OrganizationSecurityService`, `AddressGeocodingService`, `PlaceGeocoder`.

### A2. Ben.Service.Security project is dead except one unused DI registration — P1, effort M
**Where:** entire `Ben.Service.Security/` project.

**Evidence:** `OrganizationSecurityAttribute`/`OrganizationSecurityAuthorizeAttribute` — used in **0**
files. `SecurityExtensions` — 0 files. Its `IOrganizationSecurityService` — injected nowhere; the
one the controllers actually use is `Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService`
(both are registered side-by-side at `Program.cs:116-117`). A comment in its own
`Enums/OrganizationSecurityTable.cs` already admits the attribute "never fired." The Swagger API
description (`Program.cs:65`) still tells API consumers that "other routes enforce org-level
membership via `OrganizationSecurityAuthorize`" — **that claim is false.**

**Also:** this is the project dragging in ASP.NET Core **2.2-era packages** (see D1).

**Recommendation:** remove the project, its DI registration, its solution entry, and fix the Swagger
description to say what's true (per-route ownership checks via the shared access helpers). If the
two-level attribute-driven model is still the long-term dream, park the design in ProjectNotes — not
as a compiled, misleading, dependency-dragging project.

### A3. Dead Entity Developer artifacts + misleading "Generated" naming — P2, effort S
**Where:** `Ben.Data.Source/BenDataModel.efml`, `.edps`, two `.view` files; the
`BenDataModel.*.Generated.cs` naming convention in `Entities/` and `Context/`.

**Evidence:** the designer files were last touched in the Initial Commit (2026-07-16) and describe
26 entities; the codebase now has 158. `BenDataContext.Generated.cs` has been hand-edited
continuously (last: yesterday). Nothing generates anything anymore; the `.Generated.cs` suffix and
the `BenDataModel.` file prefix are fossils of a dead Devart workflow, and newer entities are
already inconsistent (some have the partial-class pair, some don't).

**Recommendation:** delete the four designer artifacts; rename `BenDataContext.Generated.cs` →
`BenDataContext.cs` (merging the empty partial); optionally flatten the `BenDataModel.X.cs` +
`BenDataModel.X.Generated.cs` pairs into single `X.cs` files during quiet moments. Zero behavior
change, large confusion-removal.

### A4. Vendored wavesurfer.js working copy lives inside wwwroot — P2, effort M
**Where:** `Ben.Web.WebApp/wwwroot/ts/wavesurfer/` — a full fork checkout: 137 tracked source
files plus (untracked but on disk) `node_modules/` at **170MB**, cypress suites, jest config,
`AGENTS.md`, etc. Total 174MB under a publicly-served static root.

**Evidence:** `WaveSurferPlayer.razor.js` documents the build (`npm run build:blazor` from that
dir) — so it *is* the live build source for the patched player, but everything under `wwwroot` is
served to the public and swept into `dotnet publish` output. Meanwhile `App.razor:39` *also* loads
stock wavesurfer from unpkg (see D4), so two copies of wavesurfer are in play.

**Recommendation:** move the fork out of `wwwroot` (e.g. `tools/wavesurfer-fork/` or a sibling
repo), keep only the built bundle under `wwwroot`/`_content`, and add the build step to the
fork's README. Confirm publish output afterward.

### A5. HomeSvg.razor is a 9,596-line Illustrator export with an embedded base64 PNG — P2, effort S
**Where:** `Ben.Web.WebApp/Components/Pages/HomeSvg.razor`.

**Evidence:** raw Adobe Illustrator SVG (with `<image ... xlink:href="data:image/png;base64,...`)
pasted as a .razor component — it compiles into the render tree, bloating the assembly and making
that component ~22% of all razor lines in the solution.

**Recommendation:** ship it as a static `.svg` (or extract the embedded PNG to a real image file)
under `wwwroot/img/` and reference it with `<img>`/`<object>`. Keep razor only if parts of it are
genuinely dynamic.

---

## Section B — Operational gaps (things that will bite in production)

### B1. File uploads buffer the entire payload in memory, with no size limit — P1, effort M
**Where:** `UploadFileController.Upload` (`Controllers/Entities/UploadFileController.cs:178+`),
`MyCaseController.cs:527`, `CaseFileController.cs:64` and `:136`, `AdminVideoAssetController.cs:180`,
`VideoProjectController.cs:153`, plus the audio-edit/clip/mix endpoints.

**Evidence:** the pattern is `using var ms = new MemoryStream(); await file.CopyToAsync(ms); var
bytes = ms.ToArray();` — that's **two full copies** of the upload in RAM — under
`[DisableRequestSizeLimit]` (deliberate, per the no-caps decision). A single 8GB video upload ≈
16GB+ of server memory; a handful of concurrent large uploads is a self-inflicted denial of
service. The storage layer already writes to disk (`IFileStorageService.WriteAsync(stream)`), so
the buffering is unnecessary.

**Recommendation:** stream `file.OpenReadStream()` directly to `_fileStorage.WriteAsync`, then run
SVG sanitization/metadata extraction from the stored file (metadata extraction is already
fire-and-forget). The audio-processing endpoints legitimately need full buffers (NAudio), but
those operate on existing stored files with known sizes — consider a sanity cap there instead.

### B2. No rate limiting on any endpoint; several anonymous endpoints are abusable — P1, effort M
**Where:** WebApi-wide — `AddRateLimiter` appears nowhere.

**Evidence / attack surface:**
- `GeocodingController.Search` — `[AllowAnonymous]`, proxies to geocod.io (**a paid, metered API**);
  anyone can burn the quota/bill from a shell loop.
- `MapIdentityApi` (`Program.cs:261`) — anonymous `/register` and `/login`; no lockout tarpit
  beyond Identity defaults, no throttle on account creation or password guessing.
- `Public/*` controllers (search, case discovery, org search, votes) — unthrottled anonymous reads.
- The support form is the *only* guarded surface (`SupportFormGuard`: honeypot + form-token +
  address/IP rate limits — good pattern, and its remarks explicitly anticipate layering).

**Recommendation:** add ASP.NET Core rate limiting (`AddRateLimiter`) with a tight policy on the
anonymous geocoding proxy and Identity endpoints, and a generous global policy as backstop.
SupportFormGuard's per-IP logic could inform the shape.

### B3. Open self-registration with no email confirmation — P2 (verify intent), effort S–M
**Where:** `Program.cs:131-142` + `MapIdentityApi<AppUser>()` at `:261`.

**Evidence:** `RequireConfirmedEmail`/`RequireConfirmedAccount` are configured nowhere, so
`POST /register` creates an immediately-usable account for any email address the caller invents —
including someone else's address. Combined with B2 (no throttle), bulk fake-account creation is
trivial. If self-signup *is* the intended public flow, it still shouldn't accept unverified
emails silently; the SMTP service already exists (`SmtpEmailService`) to send confirmations.

**Recommendation:** decide the intended flow, then either require confirmed email (Identity has
this built in) or gate `/register` off entirely if accounts are invite/Entra-only.

### B4. CORS origins are hardcoded to localhost — P2, effort S
**Where:** `Program.cs:38-46`.

**Evidence:** `WithOrigins("http://localhost:5078", "https://localhost:7078")` with
`AllowCredentials()`. First production deployment on a real origin silently fails CORS (or invites
an inline "just widen it" hack under deadline pressure).

**Recommendation:** read origins from configuration (`Cors:AllowedOrigins` array) with the
localhost pair as Development defaults. Pairs naturally with the deployment-guide work already in
ProjectNotes.

### B5. Audit pipeline: fire-and-forget + request token + swallow-everything — P2, effort M
**Where:** all 133 `_ = TryAuditAsync(...)` call sites (`BenControllerBase.TryAuditAsync`,
`AdminEntityControllerBase`, and every mutating controller).

**Evidence — three compounding weaknesses:**
1. Audit tasks are fire-and-forget **and** receive the request's `CancellationToken` — if the
   client disconnects right after the mutation commits, the audit write is cancelled and swallowed.
2. `TryAuditAsync` swallows *all* exceptions with no logging — a systemically broken audit table
   (bad migration, full disk) would be invisible. (Contrast: metadata extraction in
   `UploadFileController` logs its failures precisely because this same silence bit before.)
3. `AdminEntityControllerBase.Delete` fires the audit *before* `SaveChangesAsync` — a failed delete
   still writes a "deleted" audit row.

**Recommendation:** pass `CancellationToken.None` into audit tasks, add a `LogWarning` in the
catch, and move the Delete audit after the save. If audit rows are ever compliance-relevant,
consider making them awaited (they're one INSERT; the latency cost is small).

### B6. Auth debug logging is hardcoded on; logs write into the repo tree; one log is committed — P2, effort S
**Where:** `Ben.Data.WebApi/Program.cs:15-29`; `.vscode/webapp.log` (tracked in git);
`appsettings.json` `"Default": "Debug"`.

**Evidence:** Serilog `MinimumLevel.Override` pins `Microsoft.AspNetCore.Authentication`,
`Authorization`, and `IdentityModel` at **Debug in code** (config can't lower it), and the rolling
file sink writes `.vscode/webapi-.log` inside the working tree. `.vscode/webapp.log` is actually
committed to the repo. Debug-level auth logging can capture token/claims detail that doesn't
belong on disk long-term, and repo-relative log paths follow the code to any deployment.

**Recommendation:** move the overrides into `appsettings.Development.json`, point file sinks at a
proper log directory (or drop the file sink — Serilog also writes to SQL), `git rm --cached
.vscode/webapp.log`, and gitignore `*.log` under `.vscode/`.

### B7. No CI — 2,219 tests run only when someone remembers to run them — P2, effort S
**Where:** `.github/` contains only `agents/`; no workflows.

**Evidence:** with the move to `paranormal365/Paranormal` complete, a push/PR workflow running
`dotnet build && dotnet test` would take ~5 minutes to write. (The Media Projects references in
`Ben.slnx` mean CI needs either the second repo checked out or a solution filter — a
`Ben-CI.slnf` excluding `/Media Projects/` keeps it self-contained.)

**Recommendation:** minimal GitHub Actions workflow: restore/build/test on push to develop/master
and on PRs. Telerik NuGet feed credentials go in repo secrets.

---

## Section C — Structure & consistency

### C1. Twelve non-controller classes live in Controllers/Entities/ — P2, effort S–M
**Where:** `AudioSourceReader`, `AudioMixer`, `AudioEditor`, `SmbPitchShifter` (a full DSP
implementation), `EvpDetector` (the EVP detection engine), `CaseOrgAccess`, `FileAudienceAccess`,
`InvestigationAccess`, `InvestigationVisibilityFilter`, `InvestigationPlacement`,
`PrivatePhotoConsent`, `PlaceMatcher` — all in `Ben.Data.WebApi/Controllers/Entities/`.

**Evidence:** none of them derive from ControllerBase; they're the *real* domain/service layer,
filed under Controllers. The access-helper family (`FileAudienceAccess` in 14 files,
`CaseOrgAccess` in 7) is the de-facto authorization architecture of the app — it deserves a home
that says so.

**Recommendation:** move to `Ben.Data.WebApi/Services/` subfolders (`Services/Audio/`,
`Services/Access/`, `Services/Places/`), or into `Ben.Service.RepositoryService` if they should be
testable without the WebApi host. Namespace-only change; mechanical.

### C2. Oversized controllers and components — P3, effort L (incremental)
**Controllers >400 lines:** `MyCaseController` (1,315 lines / 28 endpoints — case timeline,
files, messages, co-clients, reports all in one), `OrgInvestigationsController` (670),
`MyContactInfoController` (656), `CaseController` (649), `InvestigationController` (581),
`UploadFileController` (563), `OrganizationMembershipRequestController` (447),
`AudioMarkerController` (420), `OrgCalendarController` (405).

**Components >800 lines:** `AudioFilePreview.razor` (2,091), `MyCaseDetail.razor` (1,346),
`OrgCmsEditor.razor` (1,099), `AdminUserDetail.razor` (1,038), `InvestigationPanel.razor` (803),
`OrgCmsPageEdit.razor` (802).

**Recommendation:** no big-bang rewrite — split opportunistically when next touching each file
(e.g. `MyCaseController` → MyCaseTimeline/MyCaseFiles/MyCaseMessages controllers sharing a route
prefix). List kept here so the debt is visible.

### C3. IBenAdminClient is a 384-method god interface — P2, effort M
**Where:** `Ben.Web.Library/Services/IBenAdminClient.cs` (2,165 lines) +
`Ben.Web.WebApp/Services/WebApi/BenAdminClientAdapter.cs` (1,778 lines).

**Evidence:** every feature area (cases, orgs, files, CMS, EVP, notifications, places, …) funnels
through one interface implemented by one adapter class. Merge conflicts concentrate here; test
doubles must stub 384 members (or use loose mocks that hide missing wiring).

**Recommendation:** split by domain into ~8–10 interfaces (`ICaseClient`, `IOrgClient`,
`IFileClient`, …) with the adapter partial-classed per domain. The DI container can register one
adapter instance against all interfaces, so call sites migrate incrementally.

### C4. All EF model config lives in one 1,795-line OnModelCreating — P3, effort M
**Where:** `Ben.Data.Source/Context/BenDataContext.Generated.cs:111+`.

**Evidence:** 158 entities configured inline; `IEntityTypeConfiguration<T>` is used zero times.
Config for delete behavior/precision/indexes is thorough (332 explicit delete behaviors, 15/15
decimals with precision) — the *content* is good; it's the monolith shape that hurts navigation.

**Recommendation:** adopt `IEntityTypeConfiguration<T>` classes (grouped per aggregate) and
`ApplyConfigurationsFromAssembly`, migrating a cluster at a time — pairs naturally with A3's
rename.

### C5. ~68% of string columns are unbounded nvarchar(max) — P3, effort M
**Evidence:** 257 string properties across entities; only 82 `HasMaxLength` configurations.
Notes/JSON/paths legitimately need max, but names, titles, emails, phone numbers, slugs, and
status-ish strings shouldn't — nvarchar(max) columns can't be indexed, inflate memory grants, and
invite silent 2GB inputs (which, combined with B1/B2, is also an abuse vector).

**Recommendation:** sweep entities in batches; add sensible `HasMaxLength` (one migration per
batch). Start with the most-queried tables (AppUsers, Organizations, Cases, UploadFiles).

### C6. Silent-failure catch blocks in the web layer (Phase-D class, new instances) — P3, effort S
**Where:** `CmsSectionEditor.razor:143`, `CaseList.razor:137` (pending-count badge),
`OrgCmsPageEdit.razor:645` + `:664`, `OrgCmsEditor.razor:802`, `OrgMessages.razor:186`.
(`WaveSurferPlayer.razor:539`'s catch around JS `destroy` at teardown is the accepted idiom — not
a finding.)

**Evidence:** bare `catch { }` around data loads/saves — same class Phase D hunted (9 fixed then).
These six either hide a real load failure behind a blank UI region or silently drop an error the
user should see.

**Recommendation:** minimum bar per Phase D: surface to the user or log with context. The
`CaseList` badge case may legitimately want "fail quietly" — if so, say it in a comment.

### C7. In-app help coverage has fallen behind — P2 (per your own rule), effort M
**Evidence:** 18 files reference `HelpLink` against 62 `@page` components. The standing rule
(memory: *"a user-visible feature isn't done until the in-app help covers it"*) implies recent
areas (Places/maps, notifications, EVP review, support tickets, self-service contact info) need a
help-docs sweep to catch up.

**Recommendation:** one dedicated docs pass; inventory which of the 62 pages are user-facing (some
are redirects/admin shells that don't need help), then close the gap and keep the rule enforced
per-branch going forward.

---

## Section D — Dependencies & configuration staleness

### D1. Ben.Service.Security references ASP.NET Core 2.2 packages — P1 (but resolved by A2), effort S
**Where:** `Ben.Service.Security.csproj`: `Microsoft.AspNetCore.Mvc.Core 2.2.5`,
`Mvc.Abstractions 2.2.0`, `Http.Abstractions 2.2.0`.

**Evidence:** these are 2018-era packages, out of support, superseded by the shared framework
since .NET Core 3.0. The correct form is `<FrameworkReference Include="Microsoft.AspNetCore.App"/>`
(as `Ben.Web.Library` and `Ben.Web.Tests` already do). If A2 deletes the project, this goes with
it; if the project survives, fix the reference.

### D2. Outdated packages worth a deliberate pass — P3, effort S–M
From `dotnet list package --outdated` (today):
- **Swashbuckle.AspNetCore 7.2.0 → 10.2.3** — three majors behind; check .NET 10's built-in
  OpenAPI (`Microsoft.AspNetCore.OpenApi`) as the modern replacement while at it.
- **NAudio 2.2.1 → 3.0.0** — major; the MP3/ACM macOS fix in EVP work means upgrade needs a
  careful audio regression pass. Do deliberately, not casually.
- **xunit.runner.visualstudio 3.1.5 → 4.0.0**, **Microsoft.NET.Test.Sdk 18.7.0 → 18.9.0** — also
  note `Ben.Web.Playwright` pins Test.Sdk **17.14.0** while the other test projects are on 18.7.0
  (drift within the solution).
- **EF Core / Extensions 10.0.9 → 10.0.11**, **Telerik.Documents.Fixed 2026.2.519 → 2026.3.810** —
  routine patch/minor bumps.
- **Telerik.UI.for.Blazor 14.1.0 → 15.0.0** — major; coordinate with the Ben.Video repo (same
  package pinned there) so both repos move together.

### D3. Committed appsettings.json points at the retired Entra app registration — P2, effort S
**Where:** `Ben.Data.WebApi/appsettings.json` `AzureAd:ClientId = e75f71ef-...`.

**Evidence:** that ClientId is the *retired* registration (superseded 2026-07-18 by `3e37e6d7-...`
per SECRETS.md; its secret was the one leaked and scrubbed during the Paranormal migration). Anyone
running without the Development override gets auth wired to a dead app. Also `Logging:Default =
Debug` as the committed default.

**Recommendation:** put the current ClientId (it's an identifier, not a secret) or a clearly-fake
placeholder in the committed file; drop default logging to Information.

### D4. wavesurfer.js loaded from unpkg CDN with a floating major tag — P2, effort S
**Where:** `Ben.Web.WebApp/Components/App.razor:39` —
`<script src="https://unpkg.com/wavesurfer.js@7/dist/wavesurfer.min.js" defer>`.

**Evidence:** a core feature (audio players) depends on a third-party CDN at runtime — breaks
offline/dev-without-internet, adds a supply-chain surface, and `@7` floats to whatever unpkg
serves next. The repo *also* maintains a patched fork (A4) — two sources of truth for the same
library.

**Recommendation:** self-host a pinned build under `wwwroot` (or serve the fork's own built
bundle everywhere) and delete the CDN tag.

### D5. Root-level clutter: stale branch docs and working files — P3, effort S
**Where:** repo root — `PHASE.md` (describes the `feature/self-service-contact-info` branch —
which is still unmerged, yet its PHASE doc is committed on develop), `README-discovery-and-taxonomy.md`,
`README-help-documentation.md`, `README-places-and-investigation-maps.md`,
`README-support-tickets.md` (all describe feature branches that already merged),
`Ben.sln.DotSettings.user` (user-specific IDE settings, tracked), `scripts/ensure-docker-running.sh`
(Docker is gone — dedicated SQL Server since 2026-08), `.vscode/webapp.log` (see B6).

**Recommendation:** move the four merged-feature READMEs into `ProjectNotes/` (they're good
history), resolve PHASE.md with its branch, untrack the `.DotSettings.user`, delete the Docker
script.

### D6. Swagger schema filter hides every property ending in "s" — P3, effort S
**Where:** `Program.cs` `CircularReferenceSchemaFilter` (`:277-299`).

**Evidence:** `p.Key.EndsWith("s")` strips *any* plural-looking property from API docs — including
scalars like `Status`, `Address`, `Notes`, `Radius`. The API itself is unaffected; the docs lie.

**Recommendation:** filter on actual navigation/collection types (inspect `context.Type`'s
properties for `IEnumerable<T>`/entity types) instead of name endswith. Or mark entities with
`[JsonIgnore]` on navigations and delete the filter.

---

## Section E — Testing gaps

### E1. ~48 repository-layer tests exercise dead code — resolves with A1
`RepositoryManagerTests`, `UserRepositoryManagerTests`, `OrganizationRepositoryManagerTests`,
`RepositoryReadPathTests` — delete alongside the layer (or keep if the layer is adopted).

### E2. Everything runs on EF InMemory — P3, awareness item
67 of the web-test files use the InMemory provider. It ignores relational behavior: unique
indexes (the Phase-C race-fix migrations!), cascade rules, transactions, string truncation,
decimal precision. The fixture-based accuracy gate for EVP and the discriminating-test rule are
good counters, but at least the unique-constraint and cascade paths deserve a handful of
SQL-backed tests (Testcontainers or the dev SQL Server with a throwaway DB).

### E3. Playwright suite is healthy but manual/local-only — fold into B7
30 files, well-documented quickstart, categories (Smoke/Auth/Home/HomeMap). Once CI exists (B7),
a nightly Playwright job against a seeded stack is the natural next step.

---

## Suggested working order

| Phase | Contents | Why this order |
|---|---|---|
| 1 | A2 + D1 (delete dead security project), A1 + E1 (delete dead repo layer), A3 (designer fossils) | Pure deletions — shrinks everything after it |
| 2 | B1 (streaming uploads), B2 (rate limiting), B3 (registration decision) | The real operational risks |
| 3 | B4–B7 (CORS config, audit hardening, logging, CI) | Production-readiness batch |
| 4 | C1 (move helper classes), C3 (split god client), C6 (silent catches), D3–D6 (config staleness) | Structure & staleness |
| 5 | C2, C4, C5, C7, D2, E2 (incremental/ongoing) | Background-pass items |

---
*Generated by full-solution audit, 2026-08-16. Build/test baseline verified same day. No code was
modified during this audit (the only repo change this session was the pre-audit secret redaction
in ProjectNotes/Notes.md during the Paranormal migration).*
