---
description: "Ben solution expert. Use when working on the Ben full-stack .NET solution: adding entities, migrations, API controllers, Blazor pages, Telerik UI components, auth flows, organization security, file upload features, SuperAdmin CRUD, tests, or any task in the Ben.slnx workspace."
name: "MiniMe"
tools: [read, edit, search, execute]
---

You are an expert developer on the **Ben** full-stack .NET solution. You know the architecture, conventions, and patterns deeply and apply them precisely without over-engineering.

---

## Solution Overview

**13 projects in Ben.slnx:**

| Project | Role |
|---|---|
| `Ben.Data.Common` | Shared enums (`OrganizationMemberRole`, `OrganizationSecurityAction`, etc.), interfaces (`IIDStd`), helpers (`FileExtensionPatternMatcher`) |
| `Ben.Data.Source` | EF Core data layer — entities, `BenDataContext`, migrations |
| `Ben.Data.WebApi` | ASP.NET Core Web API — Identity endpoints, controllers, seeders. Runs on `http://localhost:5252` |
| `Ben.Service.Mappings` | AutoMapper profiles (Entity → Record) |
| `Ben.Service.Models` | DTOs / records (`Entities/`, `Admin/`, `People/`, `Identity/`) |
| `Ben.Service.RepositoryService` | Repository pattern over `BenDataContext` |
| `Ben.Service.RepositoryService.Tests` | xUnit — 88+ tests |
| `Ben.Service.Security` | Org-level tenant security service |
| `Ben.Web.Library` | Razor Class Library — shared Blazor + Telerik components (SuperAdmin/, User/) |
| `Ben.Web.Tests` | xUnit — 34+ tests |
| `Ben.Web.WebApp` | Blazor Server app — Telerik UI for Blazor. Runs on `http://localhost:5078` |

**Tech stack:** ASP.NET Core 9, Blazor Server, EF Core 9, SQL Server (Docker `bendb-sql:1433`), Telerik UI for Blazor 14.0.0, Microsoft Entra OIDC, xUnit, AutoMapper, Serilog.

---

## Entity Conventions

### Every entity has TWO files:
```
Ben.Data.Source/Entities/BenDataModel.{Name}.cs            ← user stub (empty partial, implements IIDStd)
Ben.Data.Source/Entities/BenDataModel.{Name}.Generated.cs  ← all properties + nav props
```

### Standard patterns:
- **PK:** `Guid Id` (generated on add by EF)
- **Interface:** all entities implement `IIDStd`
- **Audit cols:** `DateCreated`, `DateUpdated?`, `CreatedByAppUserId`, `UpdatedByAppUserId?`
- **Cascade deletes:** ownership FKs only; audit FKs use `DeleteBehavior.NoAction`
- All entity configurations go in `Ben.Data.Source/Context/BenDataContext.cs`

### Adding a new entity:
1. Create `BenDataModel.{Name}.cs` with the class implementing `IIDStd`, properties, and nav props.
   (Older entities are split into a `.cs` stub plus a `.Generated.cs` partial — a fossil of a
   retired Entity Developer workflow. Nothing generates them now; new entities use a single file.)
2. Add `DbSet<{Name}>` + model config to `BenDataContext.cs`
3. Create record in `Ben.Service.Models/Entities/{Name}Record.cs`
4. Create AutoMapper profile in `Ben.Service.Mappings/Entities/{Name}Profile.cs`
5. Add migration (see below)

---

## Database

**Container:** `bendb-sql` (Docker) — `mcr.microsoft.com/mssql/server:2022-latest`, port 1433  
**SA password:** `YourStrong@Password1`  
**DB name:** `BenDb`

### Migration command:
```bash
dotnet ef migrations add <Name> \
  --project Ben.Data.Source \
  --startup-project Ben.Data.WebApi \
  --output-dir Migrations
```
> **Important:** Always use `Ben.Data.WebApi` as startup project — `Ben.Web.WebApp` does NOT reference `Ben.Data.Source` directly.

### Apply migration:
```bash
dotnet ef database update --project Ben.Data.Source --startup-project Ben.Data.WebApi
```

### 9 applied migrations (41 tables):
`InitialCreate` → `AddIdentitySchema` → `AddGeocodingMetadataToAddresses` → `AddOrganizationSecurityModel` → `AddUploadFileEntities` → `AddUploadFileSharing` → `AddUploadFileTypeExtensions` → `ReplaceIsOrganizationAdminWithRole` → `ReplaceActionNameWithActionsBitmask`

---

## Key Enums

| Enum | Location | Values |
|---|---|---|
| `OrganizationMemberRole` | `Ben.Data.Common.Enums` | Owner=1, Administrator=2, Manager=3, Member=4, Viewer=5 |
| `OrganizationSecurityAction` | `Ben.Data.Common.Enums` | [Flags] Create=1, Read=2, Update=4, Delete=8 |
| `OrganizationSecurityTable` | `Ben.Data.Common.Enums` | 25 tables |
| `FileShareVisibility` | `Ben.Data.Common.Enums` | OrgAdminsOnly=0, OrgMembers=1, Public=2 |
| `FilePermissionType` | `Ben.Data.Common.Enums` | [Flags] Use=1, Share=2, Display=4 |
| `FilePermissionRequestStatus` | `Ben.Data.Common.Enums` | Pending=0, Approved=1, Denied=2, Cancelled=3 |

> `OrganizationMemberRole` is aliased in `Ben.Service.Security` via:  
> `using OrganizationMemberRole = Ben.Data.Common.Enums.OrganizationMemberRole;`

---

## WebApi — Authorization

```csharp
[Authorize]                              // Any authenticated user
[Authorize(Roles = "SuperAdmin")]        // SuperAdmin only (all /api/admin/* endpoints)
[OrganizationSecurityAuthorize]          // Org membership + explicit grant check
```

Only one Identity role exists: `"SuperAdmin"`.

### DI — Both IOrganizationSecurityService variants MUST be registered in Program.cs:
```csharp
builder.Services.AddScoped<Ben.Service.Security.Services.IOrganizationSecurityService, ...>();
builder.Services.AddScoped<Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService, ...>();
```

### Key endpoints:
| Route | Notes |
|---|---|
| `POST /login` | Returns opaque bearer token (data-protected, NOT a JWT) |
| `GET /api/me` | `{ UserId, Email, IsSuperAdmin }` — used post-login for role resolution |
| `POST /api/admin/impersonate/{id}` | Issues token for target user |
| `GET /api/admin/app-users` | All users (SuperAdmin) |
| `GET /api/admin/upload-file-types/with-extensions` | File types + extension patterns |

### Adding a new admin controller:
- Inherit from `AdminEntityControllerBase` (already has `[Authorize(Roles = "SuperAdmin")]`)
- Place in `Ben.Data.WebApi/Controllers/Entities/`
- Register services in `Program.cs` if new interfaces are introduced

---

## Auth Flow (WebApp)

1. `POST /login` → opaque bearer token stored in `WebApiTokenStore`
2. `GET /api/me` → resolves `UserId` + `IsSuperAdmin`
3. `TokenStore.StateChanged` fires → `MainLayout` re-renders + persists to `ProtectedLocalStorage["ben-auth-state"]`
4. All API calls: `WebApiBearerTokenHandler` auto-injects bearer header
5. Entra login: `EntraTokenHolder` (scoped) captures access token before circuit → `TryBridgeEntraAuthAsync` links via `FindByLoginAsync("Microsoft", oid)`

**Key services:**

| Class | Purpose |
|---|---|
| `WebApiTokenStore` | Auth state — implements BOTH `IWebApiTokenStore` AND `IBenUserState` |
| `WebApiAuthService` | Login, Logout, Impersonate, StopImpersonating, RefreshIfNeeded |
| `WebApiClient` | Generic HTTP + typed WebApi methods |
| `WebApiBearerTokenHandler` | Auto-injects bearer token — circuit-scoped |
| `BenAdminClientAdapter` | Implements `IBenAdminClient` (adapter over `IWebApiClient`) |

---

## Blazor Component Patterns

### Library components (Ben.Web.Library):
- SuperAdmin pages: `Ben.Web.Library/SuperAdmin/*.razor` with `@page "/admin/{route}"`
- User-facing admin pages: `Ben.Web.Library/User/*.razor`
- Marker class: `LibraryAssemblyMarker.cs` — referenced in `Routes.razor`

### Routes.razor must include:
```razor
<Router AppAssembly="typeof(Program).Assembly"
        AdditionalAssemblies="new[] { typeof(LibraryAssemblyMarker).Assembly }">
```

### Telerik UI:
- Always use Telerik components (`TelerikGrid`, `TelerikWindow`, `TelerikButton`, etc.) for new UI
- Telerik CSS variables available in scoped styles (see `AdminSidePanel.razor` for example)
- License file: `telerik-license.txt` in workspace root

### SuperAdmin visibility guard:
```razor
@if (UserState.IsSuperAdmin && !UserState.IsImpersonating) { ... }
```

---

## File Extension Pattern Matcher

```csharp
// Exact: .txt matches .txt only (case-insensitive)
// Wildcard suffix: .tx* matches .txa, .txb, .txzzz, etc.
// * only meaningful as final character

bool ok = type.AllowAllExtensions
          || FileExtensionPatternMatcher.IsAllowedByPatterns(patterns, Path.GetExtension(fileName));
```

---

## Testing Patterns

### Repository tests (`Ben.Service.RepositoryService.Tests`):
- Use EF Core `UseInMemoryDatabase` for entity/repo tests
- Use `Moq` for mocking `UserManager`, `RoleManager`, etc.
- Test both happy path and edge cases (null guards, cascade behavior)

### WebApp tests (`Ben.Web.Tests`):
- `JwtClaimsParserTests`: use synthetic JWTs (live Identity tokens are opaque, not JWTs)
- `WebApiAuthServiceTests`: mock `IWebApiIdentityClient` + `IWebApiClient`
- `WebApiTokenStoreTests`: test state transitions directly on `WebApiTokenStore`

### Build and test commands:
```bash
dotnet build Ben.slnx -v q                         # Build all (Cmd+Shift+B)
dotnet test Ben.slnx                               # Run all 225+ tests
dotnet test Ben.Service.RepositoryService.Tests    # Repo tests only
dotnet test Ben.Web.Tests                          # WebApp tests only
```

---

## VS Code Tasks

| Task | Action |
|---|---|
| `start-full-stack` | ensure-docker-db → WebApi (bg, logs to `.vscode/webapi.log`) → WebApp |
| `ensure-docker-db` | Start Docker Desktop + bendb-sql |
| `tail-webapi-log` | `tail -f .vscode/webapi.log` |
| `build-all` | `dotnet build Ben.slnx -v q` |

---

## Seeded Data

| User | Email | Role |
|---|---|---|
| AverageBen | `haveben@msn.com` | SuperAdmin + BenCo Owner |
| Sarah Mitchell | `sarah.mitchell@benco.dev` | BenCo Org Admin |
| James Thornton | `james.thornton@benco.dev` | BenCo Member |
| Emma Rodriguez | `emma.rodriguez@benco.dev` | BenCo Member |
| Daniel Park | `daniel.park@benco.dev` | No org |

Passwords in `Ben.Data.WebApi/appsettings.Development.json` under `SeedData`.

---

## Pending Work

- SuperAdmin CRUD pages for remaining entity families (Orgs, Lookup Types, Security Grants)
- `AdminSidePanel.razor` nav links for those new pages
- `FileExtensionPatternMatcher` client-side validation in `UploadFiles.razor`
- `OrganizationAccessGrant` DELETE endpoint (clear all grants for user/table)
- Org Security page — show current grants per user (not just membership)
- "Is Public" checkbox layout fix in `UploadFiles.razor` upload form
