# AverageBen

Full-stack .NET solution — ASP.NET Core Web API + Blazor Server + EF Core + SQL Server (Docker), with Microsoft Entra OIDC, Telerik UI for Blazor, and a rich organization/security model.

---

## 🏗️ Solution Structure

| Project | Role |
|---|---|
| `Ben.Data.Common` | Shared enums, interfaces (`IIDStd`), helpers (`FileExtensionPatternMatcher`) |
| `Ben.Data.Source` | EF Core data layer — entities, `BenDataContext`, migrations |
| `Ben.Data.WebApi` | ASP.NET Core Web API — Identity, controllers, seeders (`:5252`) |
| `Ben.Service.Mappings` | AutoMapper profiles (Entity → Record) |
| `Ben.Service.Models` | DTOs / records |
| `Ben.Service.RepositoryService` | Repository pattern over `BenDataContext` |
| `Ben.Service.Security` | Org-level tenant security service |
| `Ben.Web.Library` | Razor Class Library — shared Blazor + Telerik components |
| `Ben.Web.WebApp` | Blazor Server app — Telerik UI for Blazor (`:5078`) |
| `Ben.Service.RepositoryService.Tests` | xUnit — 123 tests |
| `Ben.Web.Tests` | xUnit — 102 tests |

**225 tests — 0 failures, 0 warnings**

---

## 🚀 Getting Started

**Prerequisites:** .NET 9 SDK, Docker Desktop, Telerik license

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

## 📋 Daily Logs

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
