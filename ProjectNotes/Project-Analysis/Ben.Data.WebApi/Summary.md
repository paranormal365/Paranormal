# Ben.Data.WebApi — Project Summary

**Type:** Web API (`Microsoft.NET.Sdk.Web`)  
**Target Framework:** net10.0  
**Base URL (dev):** `http://localhost:5252`  
**Swagger UI:** `/swagger/index.html`

## Purpose

The ASP.NET Core Web API that provides all data access and business logic over HTTP.  
All client applications (currently only `Ben.Web.WebApp`) communicate exclusively through this API.

## Authentication

- **Local Identity:** `POST /login` → opaque data-protected bearer token (NOT a JWT). Role resolved server-side via `GET /api/me`.
- **Microsoft Entra OIDC:** JWT bearer token validated against `login.microsoftonline.com/common/v2.0`.
- Both schemes are accepted by the default authorization policy. `[Authorize(Roles = "SuperAdmin")]` still requires the local Identity role.

## Dependencies

| Direction | Project |
|---|---|
| Depends on | Ben.Data.Common, Ben.Data.Source, Ben.Service.Mappings, Ben.Service.Models, Ben.Service.RepositoryService, Ben.Service.Security |
| Referenced by | *(none — entry point)* |

## Contents

| File | Description |
|---|---|
| [Controllers-Base.md](Controllers-Base.md) | `AdminEntityControllerBase`, `EntityReadControllerBase` |
| [Controllers-Security.md](Controllers-Security.md) | `MeController`, `OrganizationSecurityController`, `OrganizationMembershipController`, `EntraAuthController` |
| [Controllers-Admin.md](Controllers-Admin.md) | All `Admin/*` SuperAdmin entity controllers |
| [Controllers-Entities.md](Controllers-Entities.md) | All `Entities/*` standard entity controllers |
| [SeedData.md](SeedData.md) | `SuperAdminSeeder`, `OrganizationSeeder` |

## Key DI Registrations

```csharp
// EF Core factory
builder.Services.AddDbContextFactory<BenDataContext>(options => ...);

// Repository + Security services
builder.Services.AddScoped<IRepositoryManager, RepositoryManager>();
builder.Services.AddScoped<Ben.Service.Security.Services.IOrganizationSecurityService,
    Ben.Service.RepositoryService.Services.OrganizationSecurityService>();
builder.Services.AddScoped<Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService,
    Ben.Service.RepositoryService.Services.OrganizationSecurityService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

// AutoMapper — scans Ben.Service.Mappings assembly
builder.Services.AddAutoMapper(_ => { }, typeof(AppUserProfile).Assembly);
```
