# Ben.Web.WebApp — Project Summary

**Type:** Blazor Server App (`Microsoft.NET.Sdk.Web`)  
**Target Framework:** net10.0  
**Base URL (dev):** `http://localhost:5078`  
**Packages:** `Telerik.UI.for.Blazor 14.0.0`, `Microsoft.AspNetCore.Authentication.OpenIdConnect`

## Purpose

The Blazor Server application — the primary user interface.  
All data access goes through `Ben.Data.WebApi` via HTTP; the WebApp has no direct database dependency.

## Dependencies

| Direction | Project |
|---|---|
| Depends on | Ben.Data.Common, Ben.Service.Models, Ben.Web.Library |
| Referenced by | *(none — entry point)* |

## Contents

| File | Description |
|---|---|
| [Services-Interfaces.md](Services-Interfaces.md) | `IWebApiTokenStore`, `IWebApiAuthService`, `IWebApiClient`, `IWebApiIdentityClient` |
| [Services-Implementations.md](Services-Implementations.md) | `WebApiTokenStore`, `WebApiAuthService`, `WebApiClient`, `WebApiIdentityClient`, `WebApiBearerTokenHandler`, `BenAdminClientAdapter`, `JwtClaimsParser` |
| [Services-Contracts.md](Services-Contracts.md) | Request/response DTOs for org security, upload files, and auth |
| [Components-Pages.md](Components-Pages.md) | All Blazor pages: Login, OrganizationSecurity, UploadFiles, CompleteProfile |
| [Components-Layout.md](Components-Layout.md) | `MainLayout`, `ThemeChanger` |

## Authentication Flow

1. **Password login:** `POST /login` → opaque bearer token → `GET /api/me` → sets `IsSuperAdmin`/`UserId`.
2. **Entra login:** OIDC challenge → `EntraTokenHolder` captures token + OID → `GET /api/me` OID lookup → links to local account if found, else → `/entra/complete-profile`.
3. **Persistence:** `ProtectedLocalStorage["ben-auth-state"]` survives page reload.
4. **All API calls:** `WebApiBearerTokenHandler` auto-injects the current bearer token.

## Key DI Registrations

```csharp
builder.Services.AddScoped<IWebApiTokenStore, WebApiTokenStore>();
builder.Services.AddScoped<IBenUserState>(sp => 
    (IBenUserState)sp.GetRequiredService<IWebApiTokenStore>());
builder.Services.AddTransient<WebApiBearerTokenHandler>();
builder.Services.AddHttpClient<IWebApiClient, WebApiClient>(...).AddHttpMessageHandler<WebApiBearerTokenHandler>();
builder.Services.AddHttpClient<IWebApiIdentityClient, WebApiIdentityClient>(...);
builder.Services.AddScoped<IWebApiAuthService, WebApiAuthService>();
builder.Services.AddScoped<IBenAdminClient, BenAdminClientAdapter>();
builder.Services.AddScoped<EntraTokenHolder>();
```

Note: `WebApiTokenStore` implements **both** `IWebApiTokenStore` **and** `IBenUserState` — registered separately so library components receive the `IBenUserState` projection.
