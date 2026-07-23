# AverageBen

Full-stack .NET solution — ASP.NET Core Web API + Blazor Server + EF Core + SQL Server (Docker), with Microsoft Entra OIDC, Telerik UI for Blazor, and a rich organization/security model.

---

## 🏗️ Solution Structure

| Project | Role |
|---|---|
| `Ben.Data.Common` | Shared enums, interfaces (`IIDStd`), helpers (`FileExtensionPatternMatcher`, `MapGeoJsonHelper`) |
| `Ben.Data.Source` | EF Core data layer — entities, `BenDataContext`, 12 migrations |
| `Ben.Data.WebApi` | ASP.NET Core Web API — Identity, controllers, seeders (`:5252`) |
| `Ben.Service.Mappings` | AutoMapper profiles (Entity → Record) |
| `Ben.Service.Models` | DTOs / records |
| `Ben.Service.RepositoryService` | Repository pattern over `BenDataContext` |
| `Ben.Service.Security` | Org-level tenant security service |
| `Ben.Web.Library` | Razor Class Library — Blazor + Telerik components: `WaveSurferPlayer`, `AudioFilePreview`, `AddressMapPlayer`, `IconClassPicker`, `IconPickerDialog`, org/CMS/user management pages |
| `Ben.Web.WebApp` | Blazor Server app — Telerik UI for Blazor (`:5078`) |
| `Ben.Service.RepositoryService.Tests` | xUnit — 279 tests |
| `Ben.Web.Tests` | xUnit — 446 tests |

**737 tests — 0 failures, 0 warnings**

---

## 🚀 Getting Started

**Prerequisites:** .NET 10 SDK, Docker Desktop, Telerik license

```bash
# Start Docker DB + WebApi + WebApp in one step
# Via VS Code: Cmd+Shift+P → Run Task → start-full-stack

# Or manually:
docker start bendb-sql
dotnet run --project Ben.Data.WebApi/Ben.Data.WebApi.csproj --urls http://localhost:5252
dotnet run --project Ben.Web.WebApp/Ben.Web.WebApp.csproj --urls http://localhost:5078
```

Add your secrets to `appsettings.Development.json` (see [WebApp-WebApi Integration Guide](./ProjectNotes/WebApp-WebApi-Integration-Guide.md) for configuration details).

---

## �️ Database Setup

### Development (Docker — default)

```bash
docker start bendb-sql   # start existing container
# or create fresh:
docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=YourStrong@Password1 \
           -p 1433:1433 --name bendb-sql \
           -d mcr.microsoft.com/mssql/server:2022-latest
```

Then apply migrations:

```bash
dotnet ef database update \
  --project Ben.Data.Source \
  --startup-project Ben.Data.WebApi
```

### New / Production environment

**Option A — EF Core migrations (recommended)**

```bash
# Set the production connection string, then:
dotnet ef database update \
  --project Ben.Data.Source \
  --startup-project Ben.Data.WebApi
```

The WebApi seeders run automatically at first startup and create the SuperAdmin role/user, seed organization, and "Logo" upload file type.

**Option B — SQL script**

A pre-generated idempotent SQL script covers all 10 migrations:

```bash
sqlcmd -S <host>,<port> -U <user> -P <password> \
       -d BenDb -i scripts/create-database.sql
```

### Regenerating the SQL script (after adding a migration)

```bash
dotnet ef migrations script \
  --project Ben.Data.Source \
  --startup-project Ben.Data.WebApi \
  --output scripts/create-database.sql \
  --idempotent
```

See [today's log](./ProjectNotes/DailyLogs/2026-07-18.md#database-script-scriptscreate-databasesql) for the full deployment guide including connection string format and post-deploy checklist.

For a full end-to-end production setup walkthrough see the [Production Deployment Guide](./ProjectNotes/Production-Deployment-Guide.md).

---

## �📋 Daily Logs

### [2026-07-18](./ProjectNotes/DailyLogs/2026-07-18.md) — Organization CMS, Tests, Telerik API Fixes
**Summary:** Designed and built the full Organization CMS feature — data model (5 new entities + migration), API layer (5 controllers), Blazor UI (`OrgCmsEditor`, `OrgCmsPageEdit`, `CmsSectionEditor`, `CmsFileThumbnail`), logo thumbnail gallery with file upload, and 66 new tests. Fixed Telerik 14.x `TelerikTabStrip`/`TelerikWindow` API changes. 348 tests, 0 errors.

**Key Accomplishments:**
- CMS data model: ✅ `OrganizationLogo`, `CmsSection`, `OrgMemberGroup`, `OrgMemberGroupMembership`, `CmsPagePermission` + migration `20260718122428_AddCmsEntities`
- CMS API: ✅ `OrgCmsPageController`, `CmsSectionController`, `OrganizationLogoController`, `OrgMemberGroupController`, `CmsPagePermissionController` (all under `/api/organizations/{orgId}/...`)
- CMS UI: ✅ `OrgCmsEditor.razor` (tabbed hub — pages gallery + logo management), `OrgCmsPageEdit.razor` (page metadata + ordered sections + inline preview), `CmsSectionEditor.razor` (type-switching editor with `TelerikEditor` for rich text), `CmsFileThumbnail.razor` (base64 lazy-load thumbnails)
- Logo picker: ✅ Replaced GUID-input with thumbnail gallery from org shared files + `InputFile` upload
- Telerik fixes: ✅ `TelerikTabStrip` — removed `ActiveTabIndex` (use `ActiveTabId`); `TelerikWindow` — `Title` → `<WindowTitle>` child tag
- Tests: ✅ 348/348 — 59 CMS controller tests + 7 CMS file library adapter tests

---

### [2026-07-17](./ProjectNotes/DailyLogs/2026-07-17.md) — Telerik Upgrade, SuperAdmin Pages, Org CRUD, Entra Fix
**Summary:** Upgraded Telerik to 14.1.0, added Create User and Site Roles admin pages, extracted `MainNavigationDrawer`, built org CRUD with permission-aware API, fixed Entra claims transformation so `User.IsInRole()` works for Entra JWT tokens. 225 tests.

**Key Accomplishments:**
- Telerik 14.0.0 → 14.1.0: ✅ `e.Items.First()` API fix in 3 components
- `AdminUserCreate.razor` (`/admin/users/create`): ✅ SuperAdmin create-user form
- `AdminRoles.razor` + `AdminRoleController`: ✅ Site-level role CRUD
- `MainNavigationDrawer.razor`: ✅ Extracted drawer into standalone component
- `OrganizationList.razor` + `OrganizationCreateEdit.razor`: ✅ Org CRUD with `CanEdit`/`CanDelete` flags
- `OrganizationController`: ✅ Permission-aware GET/POST/PUT/DELETE at `/api/organizations`
- `EntraClaimsTransformation`: ✅ Injects `app_user_id` + DB role claims after Entra JWT auth

---

### [2026-07-16](./ProjectNotes/DailyLogs/2026-07-16.md) — GitHub Repository Setup, Ben Agent
**Summary:** Initialized git repository, configured `.gitignore` (moved `TelerikKey` to Development config, added standard .NET patterns), resolved merge conflict with GitHub template, pushed to `VandyBen/AverageBen`. Created `.github/agents/ben.agent.md` — a VS Code Copilot agent with full solution context.

**Key Accomplishments:**
- Git init + remote set to `https://github.com/VandyBen/AverageBen` ✅
- `TelerikKey` removed from `appsettings.json`, moved to `appsettings.Development.json` ✅
- `.gitignore` expanded: `bin/`, `obj/`, `.vs/`, `.idea/`, `.DS_Store`, `.env`, secrets ✅
- Ben Copilot agent created at `.github/agents/ben.agent.md` ✅

---

### [2026-07-15](./ProjectNotes/DailyLogs/2026-07-15.md) — Entra Login End-to-End, DI Scope Fix, Auth Hardening
**Summary:** Root-caused and fixed the "Administration button missing after Entra login" bug. Found 5 layered issues: `WebApiBearerTokenHandler` DI scope bug (circuit-scoped token never reached the HTTP pipeline), MSA sub-claim `FormatException`, JWT audience mismatch, partial auth state leakage, and `Login.razor` false redirect. All fixed. 225 tests passing (+16 new).

**Key Accomplishments:**
- DI scope fix: ✅ Auth header injection moved into `WebApiClient` directly (circuit-scoped)
- `MeController` fix: ✅ `FormatException` caught for MSA sub claims
- JWT audience: ✅ `ValidAudiences` accepts both `api://clientId` and plain GUID
- `TryBridgeEntraAuthAsync`: ✅ Guards added for race conditions + partial state cleanup
- `Login.razor`: ✅ Redirect condition changed to `IsAuthenticated && UserId.HasValue`
- Entra end-to-end working: ✅ Link flow → Administration button shows immediately
- Serilog sinks: ✅ Console + rolling file added for auth debug logging
- Tests: ✅ 225/225 — +14 `WebApiClientTests` + 2 `MeControllerTests`

---

### [2026-07-14](./ProjectNotes/DailyLogs/2026-07-14.md) — Bug Fixes, File Type Extensions, SuperAdmin UI, Auth Persistence
**Summary:** Fixed WebApp circular DI crash and startup script health-check URL. Built `UploadFileTypeExtension` entity with wildcard pattern support, full admin API, and SuperAdmin UI. Added SuperAdmin right-side slide-in administration panel. Fixed opaque token / `IsSuperAdmin` bug. 170 tests passing.

**Key Accomplishments:**
- Circular DI fix: ✅ `WebApiBearerTokenHandler` no longer depends on `IWebApiAuthService`
- File type extensions: ✅ `UploadFileTypeExtension` entity, `AllowAllExtensions` flag, wildcard patterns, migration, admin API + UI, `FileExtensionPatternMatcher`
- SuperAdmin side panel: ✅ Right-side slide-in drawer (`AdminSidePanel.razor`), "Administration" app bar button
- Auth fixes: ✅ `GET /api/me` for role resolution; `StateChanged` event; `ProtectedLocalStorage` persistence
- Entra login: ✅ OIDC config, `EntraTokenHolder`, account linking, `/entra/complete-profile`
- Tests: ✅ 170/170 passing — 38 new `UploadFileTypeExtension` tests, 24 new Entra/auth/controller tests

---

### [2026-07-13](./ProjectNotes/DailyLogs/2026-07-13.md) — Full Day: Auth, File Upload, Impersonation, Library Admin UI, Tests & Org Seed
**Summary:** Comprehensive day covering login, task automation, theme switcher, file upload system, role fixes, JWT parsing, impersonation, `Ben.Web.Library` SuperAdmin pages, 84 automated tests, 3 end-to-end bug fixes, and BenCo organization seed data.

**Key Accomplishments:**
- Auth + login: ✅ JWT parsing, UserId/IsSuperAdmin from token, login page, task automation, theme switcher
- File upload: ✅ 4 DB tables, org-sharing (3-tier), permission requests (Use/Share/Display)
- Impersonation: ✅ `POST /api/admin/impersonate/{id}`, token save/restore, amber banner
- Ben.Web.Library: ✅ Telerik added; `IBenAdminClient`/`IBenUserState`; AdminUsers + AdminUserDetail pages
- Tests: ✅ 84/84 — JWT parsing, token store, auth service, org security service, upload file entities
- Org seed: ✅ BenCo org — 5 users (owner + 1 admin + 2 members + 1 non-member)

---

### [2026-07-12](./ProjectNotes/DailyLogs/2026-07-12.md) — User Search Pagination & Security Integration
**Summary:** Implemented pagination for scoped user search, integrated Ben.Service.Security into WebApi with namespace conflict resolution.

**Key Accomplishments:**
- User search pagination (skip/take parameters) implemented
- Scope enforcement: SuperAdmin searches all users; others search shared organization members only
- Ben.Service.Security project fully integrated into WebApi
- Resolved enum, entity, and namespace conflicts through aliasing

---

### [2026-07-11](./ProjectNotes/DailyLogs/2026-07-11.md) — Health Check & Vulnerability Audit
**Summary:** Full solution health check, identified OpenAPI vulnerability (NU1903), tested remediation paths, suppressed generated-code warnings.

**Key Accomplishments:**
- Solution build: ✅ Succeeds with warnings only
- Unit tests: ✅ 29/29 passing
- Vulnerability audit: ❌ NU1903 (OpenAPI) remains; documented as known issue awaiting upstream fix
- Generated-code warnings: ✅ Suppressed CS8669 in Ben.Data.Source

---

### [2026-07-10](./ProjectNotes/DailyLogs/2026-07-10.md) — WebApi Creation & OpenAPI Fix
**Summary:** Created Ben.Data.WebApi with Identity endpoints, Serilog, and Swashbuckle. Fixed circular reference errors in OpenAPI schema generation. Created Docker startup helper script.

**Key Accomplishments:**
- Ben.Data.WebApi project scaffolded and configured
- 113 fully documented OpenAPI endpoints
- Identity API endpoints wired (login, register, refresh)
- Circular reference issue resolved (Scalar → Swashbuckle + schema filter)
- Docker helper script created

---

### [2026-07-09](./ProjectNotes/DailyLogs/2026-07-09.md) — Initial Setup: Mac Migration & Schema Extraction
**Summary:** Moved Ben solution from Windows to macOS, diagnosed Entity Developer generated code gap, extracted full schema from BenDataModel.efml.

**Key Accomplishments:**
- Docker SQL Server setup planned
- 26 entities and 71 associations extracted from XML model
- Entity Developer generated partial files recreated as `*.Generated.cs`
- EF Core migrations scaffolded and applied
- Database schema created successfully

---

## 📚 Documentation

| File | Description |
|---|---|
| [Notes.md](./ProjectNotes/Notes.md) | Ongoing cross-day notes: WebApp↔WebApi integration, client patterns, Docker scripts |
| [Security-Guide.md](./ProjectNotes/Security-Guide.md) | Ben.Service.Security — enums, models, service interface, usage patterns, auth attribute |
| [WebApp-WebApi-Integration-Guide.md](./ProjectNotes/WebApp-WebApi-Integration-Guide.md) | Complete integration reference: DI, models, HTTP methods, CRUD, AutoMapper, error handling |
| [SESSION-SUMMARY-2026-07-10.md](./ProjectNotes/SESSION-SUMMARY-2026-07-10.md) | Day 3 milestone summary: OpenAPI fix, port config, CORS, auth flow |
| [Project-Analysis/](./ProjectNotes/Project-Analysis/) | Per-project analysis — classes, interfaces, enums, services |

---

## 📁 Repository Structure

```
AverageBen/
├── README.md                              ← you are here
├── Ben.slnx                               ← solution file
├── Ben.Data.Common/
├── Ben.Data.Source/
├── Ben.Data.WebApi/
├── Ben.Service.Mappings/
├── Ben.Service.Models/
├── Ben.Service.RepositoryService/
├── Ben.Service.RepositoryService.Tests/
├── Ben.Service.Security/
├── Ben.Web.Library/
├── Ben.Web.Tests/
├── Ben.Web.WebApp/
├── scripts/                               ← Docker + startup scripts
├── .github/
│   └── agents/
│       └── ben.agent.md                   ← Copilot agent with full solution context
└── ProjectNotes/
    ├── DailyLogs/
    │   ├── 2026-07-09.md
    │   ├── 2026-07-10.md
    │   ├── 2026-07-11.md
    │   ├── 2026-07-12.md
    │   ├── 2026-07-13.md
    │   ├── 2026-07-14.md
    │   └── 2026-07-15.md
    ├── Project-Analysis/                  ← per-project class/interface/enum detail
    ├── Notes.md
    ├── Security-Guide.md
    ├── WebApp-WebApi-Integration-Guide.md
    └── SESSION-SUMMARY-2026-07-10.md
```

---

## 🎯 Quick Navigation

**Starting the project?**
→ Start with [2026-07-09.md](./ProjectNotes/DailyLogs/2026-07-09.md) and work forward

**Understanding org security?**
→ Read [Security-Guide.md](./ProjectNotes/Security-Guide.md)

**Integrating WebApp ↔ WebApi?**
→ See [WebApp-WebApi-Integration-Guide.md](./ProjectNotes/WebApp-WebApi-Integration-Guide.md)

**Latest daily log?**
→ [2026-07-15.md](./ProjectNotes/DailyLogs/2026-07-15.md)

**Cross-day topics and notes?**
→ See [Notes.md](./ProjectNotes/Notes.md)

---

## 📝 Daily Log Format

```md
# Daily Log - YYYY-MM-DD

## Summary
Brief overview of the day's work

## Work Completed Today
Detailed accomplishments organized by topic

## Decisions
Key architectural or process decisions made

## Blockers
Any blockers encountered and how they were resolved

## Next Steps (Candidate)
Suggested follow-up work
```

To add a new log: create `ProjectNotes/DailyLogs/YYYY-MM-DD.md` using the format above, then add a summary entry to this README under **Daily Logs**.
