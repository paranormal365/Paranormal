# Project Notes

## Overview

Use this file for durable notes about the Ben solution.

**See also:** [WebApp ↔ WebApi Integration Guide](./WebApp-WebApi-Integration-Guide.md) — Complete guide to DI, CRUD operations, mapping, and error handling.

---

## WebApp ↔ WebApi Integration

### Configuration

**WebApi** (runs on `http://localhost:5252`)
- Port: 5252
- CORS Policy: `WebAppPolicy` — allows requests from `http://localhost:5078` and `https://localhost:7078`
- Authentication: Bearer token (Identity API endpoints)
- Schema: Swagger at `/swagger/v1/swagger.json`

**WebApp** (runs on `http://localhost:5078`)
- WebApi BaseUrl: `http://localhost:5252` (configured in `appsettings.json`)
- Authentication: Cookie-based for Blazor Server
- Calls WebApi: Via `IWebApiClient`, `IWebApiIdentityClient`
- Token Management: `WebApiTokenStore` (circuit-scoped) — bearer token injected per-request inside `WebApiClient.Auth()`

### How It Works

1. **WebApp Login Flow**:
   - User enters email/password in WebApp
   - `IWebApiAuthService.LoginAsync()` calls WebApi `/login`
   - WebApi returns `AccessToken` + `RefreshToken` (opaque data-protected tokens, **not JWTs**)
   - Tokens stored in `WebApiTokenStore`
   - `LoginAsync` then calls `GET /api/me` to fetch `UserId` and `IsSuperAdmin` (role claims are not inside the opaque token)
   - `TokenStore.NotifyStateChanged()` fires → `MainLayout` re-renders and persists state to `ProtectedLocalStorage`

2. **WebApp → WebApi API Calls**:
   - `IWebApiClient` makes HTTP requests to entity endpoints
   - `WebApiClient.Auth()` creates each `HttpRequestMessage` with `Authorization: Bearer {token}` from the circuit-scoped `IWebApiTokenStore` (read at request time)
   - WebApi validates token, processes request, response returned to WebApp

3. **Token Refresh**:
   - `WebApiAuthService.RefreshIfNeededAsync()` checks token expiry
   - If expired, calls WebApi `/refresh` with refresh token
   - New access token obtained and stored; `NotifyStateChanged()` fires to persist new token

4. **Page Reload Persistence**:
   - On reload, Blazor Server creates a new circuit (new `WebApiTokenStore` instance)
   - `MainLayout.OnAfterRenderAsync(firstRender)` restores auth state from `ProtectedLocalStorage`
   - Protected storage is encrypted by ASP.NET Core Data Protection

### Client Services

All in `Ben.Web.WebApp/Services/WebApi/`:

- **`IWebApiAuthService`**: Handle login, logout, token refresh
- **`IWebApiIdentityClient`**: Calls `/login`, `/register`, `/refresh` endpoints
- **`IWebApiClient`** / **`WebApiClient`**: Typed HTTP client for all entity CRUD. Constructor-injects `IWebApiTokenStore` (circuit-scoped) and attaches bearer token per-request via `Auth()` private helper.
- **`WebApiTokenStore`**: Stores access/refresh tokens; persisted to `ProtectedLocalStorage` by `MainLayout` across page reloads. Fires `StateChanged` event when auth state changes so UI re-renders immediately.
- **`WebApiOptions`**: Configuration POCO for BaseUrl

> **Note:** `WebApiBearerTokenHandler` (DelegatingHandler) was retired. `IHttpClientFactory` resolves handlers from the root DI scope, so injecting circuit-scoped `IWebApiTokenStore` into it always gave an empty, unrelated instance. Auth header injection was moved into `WebApiClient` directly.

### CORS Setup (WebApi)

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebAppPolicy", policy =>
        policy
            .WithOrigins("http://localhost:5078", "https://localhost:7078")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

app.UseCors("WebAppPolicy");
```

### Port Reference

| Application | Port | Typical URL |
|---|---|---|
| WebApi | 5252 | `http://localhost:5252/swagger/v1/swagger.json` |
| WebApp | 5078 | `http://localhost:5078/` |

Both must be running for the WebApp to function. Use `bash scripts/start-webapp-with-api.sh` to start both automatically.

---



The local development database runs in a Docker container named `bendb-sql` using SQL Server 2022.

### Connection Details

| Setting | Value |
|---|---|
| Host / Server | `localhost` |
| Port | `1433` |
| Database | `BenDb` |
| Username | `sa` |
| Password | `YourStrong@Password1` |
| Encrypt | `True` |
| Trust Server Certificate | `True` |

### Connecting with DBVisualizer

1. Open DBVisualizer.
2. Create a new connection → choose **Microsoft SQL Server**.
3. Set **Server** to `localhost`, **Port** to `1433`.
4. Set **Database** to `BenDb`.
5. Set **User ID** to `sa` and **Password** to `YourStrong@Password1`.
6. Under **Properties / Driver Properties**, ensure `encrypt=true` and `trustServerCertificate=true` are set (or add them as extra URL parameters: `;encrypt=true;trustServerCertificate=true`).
7. Click **Connect**.

> **Note:** The container uses a `linux/amd64` image. On Apple Silicon Macs it runs under Rosetta emulation — this is normal and fully functional.

### Starting and Stopping the Container

**Start the container** (after it has been created):
```bash
docker start bendb-sql
```

**Stop the container** (database is preserved; container is not deleted):
```bash
docker stop bendb-sql
```

**Check whether the container is running:**
```bash
docker ps --filter name=bendb-sql
```

**Check all containers (including stopped ones):**
```bash
docker ps -a --filter name=bendb-sql
```

**View SQL Server logs** (useful for diagnosing startup issues):
```bash
docker logs bendb-sql
```

> The container was created with `--restart unless-stopped`, so it will automatically restart when Docker Desktop is launched, unless you explicitly stop it with `docker stop`.

### Automated Docker Check Before Running WebApi

To ensure Docker Desktop and the `bendb-sql` container are running before launching the WebApi, use the provided helper script:

```bash
bash scripts/ensure-docker-running.sh
```

**What it does:**

1. Checks if Docker Desktop is running; starts it if not (waits up to 30 seconds).
2. Checks if the `bendb-sql` container exists and is running.
3. If the container is stopped, starts it and waits for SQL Server to be ready.
4. If the container doesn't exist, provides instructions to create it.

**Use before launching `Ben.Data.WebApi`:**

```bash
# From solution root:
bash scripts/ensure-docker-running.sh
# Then launch the WebApi from your IDE or CLI
```

---

## Startup Orchestration (WebApi First, Then WebApp)

Use the startup script to ensure `Ben.Data.WebApi` is running before launching `Ben.Web.WebApp`.

### Script

Path:

```bash
scripts/start-webapp-with-api.sh
```

What it does:

- Checks whether WebApi is reachable (default `http://localhost:5252`).
- Starts WebApi if it is not running.
- Waits until WebApi is up.
- Launches WebApp (default `http://localhost:5078`).

### Run from Terminal

From solution root:

```bash
bash scripts/start-webapp-with-api.sh
```

Optional URL overrides:

```bash
BEN_WEBAPI_URL="http://localhost:5252" BEN_WEBAPP_URL="http://localhost:5078" bash scripts/start-webapp-with-api.sh
```

### Run from VS Code Task

- Use task label: `Run WebApp (Ensure WebApi)`.
- This task executes the same startup script.

### Logs and PID

- WebApi log file: `.vscode/webapi.log`
- WebApi pid file: `.vscode/.webapi.pid`

### Stopping Services

- Stop WebApp: `Ctrl+C` in the terminal running the script.
- Stop WebApi (if it was started by the script):

```bash
kill "$(cat .vscode/.webapi.pid)" 2>/dev/null || true
rm -f .vscode/.webapi.pid
```

---

## Microsoft Entra ID (OIDC Login)

### App Registration — AverageBen.net

| Field | Value |
|---|---|
| Display name | AverageBen.net |
| Application (client) ID | `e75f71ef-cc2e-43ad-ba0f-9e24c6f805f1` |
| Object ID | `e56679df-042c-4546-8c54-dde080799690` |
| Directory (tenant) ID | `b9f905ce-f3ef-4cdf-85cb-22fe9622ff5b` |
| Supported account types | All Microsoft accounts (personal + work/school) |
| Application ID URI | `api://e75f71ef-cc2e-43ad-ba0f-9e24c6f805f1` |

**Single-app design:** one registration serves as both the OIDC client (WebApp) and the JWT resource (WebApi).

### Client Secret

| Field | Value |
|---|---|
| Name | Ben-Dev |
| Secret ID | `2b52e4a6-6361-4a25-adbb-421c6960dd47` |
| Expires | 7/13/2028 |
| Value | Stored in `Ben.Web.WebApp/appsettings.Development.json` (gitignored) |

### Required Azure Portal Steps

- [x] **Redirect URI**: Added **Web** → `http://localhost:5078/signin-oidc`
- [x] **Front-channel logout URL**: `http://localhost:5078/signout-oidc`
- [x] **Expose an API**: scope `access_as_user` added (Application ID URI set)
- [x] **API permissions**: `access_as_user` scope → Admin consent granted

### Account linking flows

| Flow | How |
|---|---|
| Known Entra user (OID linked) | `GET /api/me` → `FindByLoginAsync("Microsoft", oid)` → local user → normal session |
| New Entra user | `/entra/complete-profile` → Create new → `POST /api/auth/entra/register` → `forceLoad:true` → OIDC restarts → linked |
| Second Entra identity (e.g., `ben.clark@vanderbilt.edu`) | `/entra/complete-profile` → Link existing → local login → `POST /api/auth/entra/link` → OID attached to haveben account |

Once `ben.clark@vanderbilt.edu` is linked to the `haveben@msn.com` local account, both Entra identities return the same `UserId`, `Email`, and `IsSuperAdmin`.

### `IsEntraSession` flag

When `true` on `WebApiTokenStore`, `MainLayout.PersistAuthStateAsync` skips saving to `ProtectedLocalStorage`. Entra sessions are maintained by the OIDC cookie; local sessions use localStorage. `AuthService.LoginAsync` resets this flag to `false`.

### Configuration

**Why `TenantId: "common"`:** The app supports "All Microsoft accounts" (personal MSA + any tenant). The OIDC authority must be `https://login.microsoftonline.com/common/v2.0`. Individual tokens carry tenant-specific issuer URLs, so `ValidateIssuer = false` is set in both the WebApp OIDC handler and the WebApi JWT bearer handler.

Config location: `Ben.Web.WebApp/appsettings.Development.json` → `AzureAd` section.

```json
"AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "common",
    "ClientId": "e75f71ef-cc2e-43ad-ba0f-9e24c6f805f1",
    "ClientSecret": "(in gitignored file)",
    "CallbackPath": "/signin-oidc",
    "SignedOutCallbackPath": "/signout-oidc"
},
"DownstreamApis": {
    "BenWebApi": {
        "BaseUrl": "http://localhost:5252",
        "Scope": "api://e75f71ef-cc2e-43ad-ba0f-9e24c6f805f1/access_as_user"
    }
}
```

### How Entra Login Works

1. User clicks **Sign in with Microsoft** on the login page
2. `NavManager.NavigateTo("/auth/entra-signin", forceLoad: true)` leaves the SignalR circuit and triggers an HTTP redirect
3. Server-side OIDC challenge → browser redirected to `https://login.microsoftonline.com/common/v2.0/authorize`
4. User authenticates (+ MFA if enabled) → Entra redirects back to `http://localhost:5078/signin-oidc`
5. OIDC middleware validates the ID token, sets the auth cookie, and caches the access token (`SaveTokens = true`)
6. Entra token capture middleware runs on the new request → populates `EntraTokenHolder` (scoped, one per Blazor circuit)
7. Blazor circuit starts; `MainLayout.OnAfterRenderAsync(firstRender)` reads `EntraTokenHolder.AccessToken`
8. Access token stored in `WebApiTokenStore.AccessToken` → `GET /api/me` called to get `UserId` + `IsSuperAdmin`
9. `TokenStore.NotifyStateChanged()` fires → UI re-renders, state persisted to `ProtectedLocalStorage`

### Key Files

| File | Change |
|---|---|
| `Ben.Web.WebApp/Services/EntraTokenHolder.cs` | Scoped service; captures Entra token before SignalR circuit starts |
| `Ben.Web.WebApp/Program.cs` | OIDC config, `EntraTokenHolder` registration, middleware, `/auth/entra-signin` + `/auth/entra-signout` endpoints |
| `Ben.Web.WebApp/Components/Pages/Login.razor` | "Sign in with Microsoft" button (only shown when `AzureAd:ClientId` is configured) |
| `Ben.Web.WebApp/Components/Layout/MainLayout.razor` | `TryBridgeEntraAuthAsync()` bridges `EntraTokenHolder` → `WebApiTokenStore` (skips if `UserId` already set; clears partial state on failure) |
| `Ben.Data.WebApi/Controllers/MeController.cs` | OID-first lookup; `GetUserAsync` wrapped in `try-catch(FormatException)` — MSA `sub` claim is not a GUID |
| `Ben.Data.WebApi/Program.cs` | `AddJwtBearer("Entra", ...)` with `ValidAudiences = ["api://clientId", "clientId"]` — MSA tokens use plain GUID audience, not `api://` prefix |

### MFA

Enable via Azure Portal → **Entra ID** → **Security**:
- **Security Defaults** (free): forces MFA for all users — simplest option for dev tenant
- **Conditional Access** (P1/P2 license): policy-based, per-app or per-user targeting
- **Per-user**: Entra ID → Users → select user → Authentication methods

---

## Microsoft Identity

### Overview

`AppUser` is the application's identity user. It extends `IdentityUser<Guid>` (primary key type: `Guid`) and also implements `IIDStd`.

`BenDataContext` extends `IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>`, so ASP.NET Identity tables are managed alongside application tables.

### Identity Tables in `BenDb`

| Table | Purpose |
|---|---|
| `AppUsers` | Application users |
| `AspNetRoles` | Roles |
| `AspNetUserRoles` | User ↔ Role assignments |
| `AspNetUserClaims` | Per-user claims |
| `AspNetRoleClaims` | Per-role claims |
| `AspNetUserLogins` | External login providers |
| `AspNetUserTokens` | User tokens |

### Current Host Roles

- `Ben.Data.WebApi` is the Identity authority.
- `Ben.Web.WebApp` calls WebApi identity endpoints and uses bearer tokens for API calls.
- WebApp no longer registers DB-backed Identity stores directly.

### DI Registration (WebApi `Program.cs`)

```csharp
builder.Services.AddIdentityApiEndpoints<AppUser>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<BenDataContext>()
.AddDefaultTokenProviders();
```

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.MapIdentityApi<AppUser>();
```

### WebApp -> WebApi Calls with Microsoft Identity

#### Recommended Flow

1. Configure WebApi base URL in `appsettings*.json`:

```json
"WebApi": {
  "BaseUrl": "http://localhost:5252"
}
```

2. Register WebApi clients + token services in WebApp `Program.cs`:

```csharp
builder.Services.Configure<WebApiOptions>(builder.Configuration.GetSection("WebApi"));

builder.Services.AddScoped<IWebApiTokenStore, WebApiTokenStore>();
builder.Services.AddScoped<IWebApiAuthService, WebApiAuthService>();

builder.Services.AddHttpClient<IWebApiIdentityClient, WebApiIdentityClient>((sp, client) =>
{
  var options = sp.GetRequiredService<IOptions<WebApiOptions>>().Value;
  client.BaseAddress = new Uri(options.BaseUrl);
});

// IWebApiTokenStore is injected into WebApiClient directly (circuit-scoped transient).
// Do NOT use AddHttpMessageHandler here — IHttpClientFactory resolves handlers from
// the root DI scope, so circuit-scoped IWebApiTokenStore would always be a fresh
// empty instance unrelated to the current user session.
builder.Services.AddHttpClient<IWebApiClient, WebApiClient>((sp, client) =>
{
  var options = sp.GetRequiredService<IOptions<WebApiOptions>>().Value;
  client.BaseAddress = new Uri(options.BaseUrl);
});
```

3. Token contracts:

```csharp
public sealed class WebApiOptions
{
  public string BaseUrl { get; set; } = "http://localhost:5252";
}

public sealed record WebApiLoginRequest(string Email, string Password);
public sealed record WebApiRefreshRequest(string RefreshToken);

public sealed class WebApiTokenResponse
{
  public string TokenType { get; set; } = string.Empty;
  public string AccessToken { get; set; } = string.Empty;
  public int ExpiresIn { get; set; }
  public string? RefreshToken { get; set; }
}
```

4. Auth service uses WebApi identity endpoints (`/login`, `/refresh`):

```csharp
public interface IWebApiIdentityClient
{
  Task<WebApiTokenResponse?> LoginAsync(string email, string password, CancellationToken ct = default);
  Task<WebApiTokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default);
}

public interface IWebApiAuthService
{
  Task<bool> LoginAsync(string email, string password, CancellationToken ct = default);
  Task<bool> RefreshIfNeededAsync(CancellationToken ct = default);
  void Logout();
}
```

5. Bearer token injected inside `WebApiClient`:

```csharp
// WebApiClient constructor injects the circuit-scoped IWebApiTokenStore directly.
// IHttpClientFactory resolves DelegatingHandlers from the ROOT scope — injecting
// IWebApiTokenStore there always gives an empty, unrelated instance.
// Moving auth header injection into WebApiClient itself (circuit-scoped transient)
// ensures the correct token is used at request time.
public sealed class WebApiClient : IWebApiClient
{
  private readonly HttpClient _httpClient;
  private readonly IWebApiTokenStore _tokenStore;

  public WebApiClient(HttpClient httpClient, IWebApiTokenStore tokenStore)
  {
    _httpClient = httpClient;
    _tokenStore = tokenStore;
  }

  private HttpRequestMessage Auth(HttpMethod method, string url)
  {
    var req = new HttpRequestMessage(method, url);
    if (!string.IsNullOrWhiteSpace(_tokenStore.AccessToken))
      req.Headers.Authorization =
        new AuthenticationHeaderValue("Bearer", _tokenStore.AccessToken);
    return req;
  }

  public async Task<TResponse?> GetAsync<TResponse>(string url, CancellationToken ct = default)
  {
    using var req = Auth(HttpMethod.Get, url);
    using var response = await _httpClient.SendAsync(req, ct);
    if (!response.IsSuccessStatusCode) return default;
    return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
  }
  // PostAsync, PutAsync, DeleteAsync follow the same Auth() pattern
}
```

6. Use typed client with `Ben.Service.Models` contracts:

```csharp
var users = await webApiClient.GetUsersAsync(ct); // returns AppUserRecord contracts
```

#### Current State Checklist

- WebApi Identity endpoints are enabled.
- WebApi auth middleware is enabled.
- WebApi EF store is wired to `BenDataContext`.
- WebApp has typed WebApi clients and token middleware registered.
- WebApp no longer directly registers DB-backed Identity.

#### Example Code: Roles, Role Claims, User Claims

Use `UserManager<AppUser>` and `RoleManager<IdentityRole<Guid>>` in WebApi for managing user/role authorization data.

1. Seed roles and add users to roles:

```csharp
var roles = new[] { "Admin", "Manager", "Reader" };
foreach (var roleName in roles)
{
  if (!await roleManager.RoleExistsAsync(roleName))
    await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
}

var user = await userManager.FindByEmailAsync("admin@ben.local");
if (user is not null && !await userManager.IsInRoleAsync(user, "Admin"))
{
  await userManager.AddToRoleAsync(user, "Admin");
}
```

2. Add role claims (apply to all users in that role):

```csharp
var adminRole = await roleManager.FindByNameAsync("Admin");
if (adminRole is not null)
{
  await roleManager.AddClaimAsync(adminRole, new Claim("permission", "users.read"));
  await roleManager.AddClaimAsync(adminRole, new Claim("permission", "users.write"));
}
```

3. Add user-specific claims:

```csharp
var user = await userManager.FindByEmailAsync("someone@ben.local");
if (user is not null)
{
  await userManager.AddClaimAsync(user, new Claim("department", "Operations"));
  await userManager.AddClaimAsync(user, new Claim("permission", "reports.read"));
}
```

4. Read roles and claims for a user:

```csharp
var user = await userManager.FindByIdAsync(userId.ToString());
var roles = user is null ? new List<string>() : await userManager.GetRolesAsync(user);
var claims = user is null ? new List<Claim>() : await userManager.GetClaimsAsync(user);

foreach (var roleName in roles)
{
  var role = await roleManager.FindByNameAsync(roleName);
  if (role is not null)
  {
    var roleClaims = await roleManager.GetClaimsAsync(role);
    // Merge roleClaims into effective permissions if needed.
  }
}
```

5. Access current caller claims in WebApi controller:

```csharp
[Authorize]
[ApiController]
[Route("api/me")]
public class MeController : ControllerBase
{
  [HttpGet("claims")]
  public IActionResult GetClaims()
  {
    var claims = User.Claims.Select(c => new { c.Type, c.Value });
    return Ok(claims);
  }

  [HttpGet("roles")]
  public IActionResult GetRoles()
  {
    var roles = User.Claims
      .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
      .Select(c => c.Value)
      .Distinct();

    return Ok(roles);
  }
}
```

6. Enforce role- and claim-based authorization:

```csharp
builder.Services.AddAuthorization(options =>
{
  options.AddPolicy("CanReadUsers", p => p.RequireClaim("permission", "users.read"));
  options.AddPolicy("CanWriteUsers", p => p.RequireClaim("permission", "users.write"));
});
```

```csharp
[Authorize(Roles = "Admin")]
[HttpGet("api/admin/users")]
public IActionResult GetUsersForAdmin() => Ok();

[Authorize(Policy = "CanReadUsers")]
[HttpGet("api/users")]
public IActionResult GetUsers() => Ok();
```

#### Notes and Caveats

- Role membership and claims management should generally be done in admin-only endpoints.
- Keep claim types stable (for example, always use `permission` for permission-style claims).
- If both user and role claims can grant permissions, define a single effective-permissions resolver in one place.
- Store refresh tokens securely; avoid browser local storage for server-side Blazor.

#### External Login Providers (Microsoft, Facebook, X/Twitter)

Use external providers to let users sign in with existing accounts, then map them to local `AppUser` identities.

##### 1) Register OAuth apps in provider consoles

- Microsoft Entra ID (or Microsoft Account app):
  - Create app registration.
  - Add redirect URI for your WebApi callback endpoint.
- Facebook Developers:
  - Create app and Facebook Login product.
  - Add Valid OAuth Redirect URI.
- X/Twitter Developer Portal:
  - Create app with OAuth 2.0 (or 1.0a depending on setup).
  - Add callback URL and enable email scope if needed.

Typical local callback paths:
- `/signin-microsoft`
- `/signin-facebook`
- `/signin-twitter`

##### 2) Store provider secrets in config or user-secrets

Example (`appsettings.Development.json`):

```json
"Authentication": {
  "Microsoft": {
    "ClientId": "...",
    "ClientSecret": "..."
  },
  "Facebook": {
    "AppId": "...",
    "AppSecret": "..."
  },
  "Twitter": {
    "ConsumerKey": "...",
    "ConsumerSecret": "..."
  }
}
```

NOTE: 
Name: Ben-Dev
Value: [REDACTED — this value was exposed in git history; rotate the secret in Azure Portal → App Registrations → AverageBen.net → Certificates & Secrets]
Secret ID: 2b52e4a6-6361-4a25-adbb-421c6960dd47
Expires: 7/13/2028

##### 3) Register providers in WebApi `Program.cs`

For API-based identity, register external providers in the WebApi host:

```csharp
builder.Services
  .AddAuthentication()
  .AddMicrosoftAccount(options =>
  {
    options.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"]!;
    options.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"]!;
  })
  .AddFacebook(options =>
  {
    options.AppId = builder.Configuration["Authentication:Facebook:AppId"]!;
    options.AppSecret = builder.Configuration["Authentication:Facebook:AppSecret"]!;
  })
  .AddTwitter(options =>
  {
    options.ConsumerKey = builder.Configuration["Authentication:Twitter:ConsumerKey"]!;
    options.ConsumerSecret = builder.Configuration["Authentication:Twitter:ConsumerSecret"]!;
    options.RetrieveUserDetails = true;
  });
```

If `AddTwitter` is unavailable, add the provider package first:

```bash
dotnet add Ben.Data.WebApi package Microsoft.AspNetCore.Authentication.Twitter
```

##### 4) Start external login challenge and process callback

In WebApi controller/endpoints:

```csharp
[HttpGet("external-login/{provider}")]
public IActionResult ExternalLogin([FromRoute] string provider, [FromQuery] string? returnUrl = "/")
{
    var redirectUrl = Url.Action(nameof(ExternalLoginCallback), new { returnUrl });
    var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl!);
    return Challenge(properties, provider);
}

[HttpGet("external-login-callback")]
public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = "/", string? remoteError = null)
{
    if (!string.IsNullOrEmpty(remoteError))
        return Redirect($"/login?error={Uri.EscapeDataString(remoteError)}");

    var info = await signInManager.GetExternalLoginInfoAsync();
    if (info is null)
        return Redirect("/login?error=ExternalLoginInfoMissing");

    var signInResult = await signInManager.ExternalLoginSignInAsync(
        info.LoginProvider,
        info.ProviderKey,
        isPersistent: false,
        bypassTwoFactor: true);

    if (signInResult.Succeeded)
        return LocalRedirect(returnUrl ?? "/");

    // First-time external login: create local user and link login
    var email = info.Principal.FindFirstValue(ClaimTypes.Email)
             ?? info.Principal.FindFirstValue("email");

    if (string.IsNullOrWhiteSpace(email))
        return Redirect("/login?error=EmailClaimMissing");

    var user = await userManager.FindByEmailAsync(email);
    if (user is null)
    {
        user = new AppUser
        {
            UserName = email,
            Email = email,
            DisplayName = info.Principal.Identity?.Name ?? email,
            DateCreated = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
            return Redirect("/login?error=CreateUserFailed");
    }

    var linkResult = await userManager.AddLoginAsync(user, info);
    if (!linkResult.Succeeded)
        return Redirect("/login?error=LinkExternalLoginFailed");

    await signInManager.SignInAsync(user, isPersistent: false);
    return LocalRedirect(returnUrl ?? "/");
}
```

##### 5) Add provider claims to local user claims (optional but useful)

Map incoming external claims to local claims after successful link/sign-in:

```csharp
var provider = info.LoginProvider;
var providerUserId = info.ProviderKey;
await userManager.AddClaimAsync(user, new Claim("idp", provider));
await userManager.AddClaimAsync(user, new Claim("idp_sub", providerUserId));

var emailVerified = info.Principal.FindFirstValue("email_verified");
if (!string.IsNullOrWhiteSpace(emailVerified))
    await userManager.AddClaimAsync(user, new Claim("email_verified", emailVerified));
```

##### 6) Use roles and permissions after external login

- External provider authenticates identity.
- Your app still controls authorization with local roles/claims.
- Typical flow after first login:
  - Assign default role (`Reader`).
  - Add app permission claims (`permission:reports.read`).
  - Elevate roles in admin workflows only.

Example role assignment after first external sign-in:

```csharp
if (!await userManager.IsInRoleAsync(user, "Reader"))
    await userManager.AddToRoleAsync(user, "Reader");
```

##### 7) Verify effective identity/authorization

In secured endpoints, inspect:
- `User.Identity?.IsAuthenticated`
- provider claim (`idp`)
- role claims (`ClaimTypes.Role` / `role`)
- permission claims (`permission`)

This confirms both external authentication and local authorization wiring are working.

---

---

## OpenAPI / Swagger Documentation

### Status: ✅ Fixed with Swashbuckle

**Swashbuckle replaced Scalar** to properly handle Entity Framework circular references.

### What Changed

- Removed: `Scalar.AspNetCore` + `Microsoft.AspNetCore.OpenApi`
- Added: `Swashbuckle.AspNetCore` v7.2.0
- Added: `CircularReferenceSchemaFilter` to exclude navigation properties from schema

### How to Access the Schema

The OpenAPI schema is available at:
```
http://localhost:5275/swagger/v1/swagger.json
```

**View with:**
- **Postman**: File → Import → Paste schema URL
- **Swagger Editor**: https://editor.swagger.io/ (paste JSON)
- **VS Code**: REST Client extension + schema URL
- **curl**: `curl http://localhost:5275/swagger/v1/swagger.json | jq .`

### Schema Details

- **113 total endpoints** documented
- **All DTOs properly included** (clean, flat records without navigation properties)
- **No circular references** — schema generates instantly
- **All entity CRUD endpoints** included (Admin, Read, Entities)
- **Identity endpoints** (`/login`, `/register`, `/refresh`) fully documented

### Current Limitation

The Swagger UI HTML page (`/swagger/`, `/swagger/index.html`) returns 404. This is a known issue with Swashbuckle/Kestrel on .NET 10. The schema JSON itself works perfectly; only the UI HTML isn't being served.

**Workaround:** Use one of the access methods above. The schema is valid and complete.

### CircularReferenceSchemaFilter

Located in `Program.cs`, this filter removes properties that typically cause circular references:
- Collection properties (plural names: `UserAddresses`, `Organizations`)
- Navigation back to parent (`CreatedByAppUser`, `UpdatedByAppUser`)
- Audit reference properties

---



When building `Ben.Data.WebApi`, you may see a prompt from your IDE to authorize or configure a `libraryimports.g.cs` file. This is safe and expected.

**What it is:**
- A source-generated file created at build time by a NuGet package dependency (most likely `Serilog.Sinks.MSSqlServer`).
- Contains P/Invoke bindings (native interop code) for accessing platform-specific libraries.
- Generated using the modern .NET `LibraryImport` source generator (introduced in .NET 7+).

**Why it appears:**
- Modern .NET uses source generators to create P/Invoke stubs at compile time instead of the older `DllImport` attribute.
- Your IDE may ask you to trust or authorize source-generated code as a security feature.

**What to do:**
- Click **Authorize** / **Trust** / **Allow** when prompted.
- The generated code is from a trusted NuGet package and is safe.
- The file is auto-generated and should not be edited manually.

**Note:** The file is generated in `obj/` or `.generated/` folders and is not tracked in source control.

---

## Conventions

- Entity PK type: `Guid`, generated on add.
- All entities implement `IIDStd` (requires `Guid Id { get; set; }`).
- Audit columns: `DateCreated` (required), `DateUpdated?`, `CreatedByAppUserId` (required), `UpdatedByAppUserId?`.
- Generated partial files are named `*.Generated.cs` alongside user stub files.
- Cascade deletes are enabled only on ownership relationships. Audit FK relationships use `DeleteBehavior.NoAction`.

## Telerik UI for Blazor

### License Key File

Telerik requires a `telerik-license.txt` file at build time. The file is separate from NuGet credentials.

**Current location (solution root):**
```
/Users/ben/Source/Ben/telerik-license.txt
```
This path is automatically searched by Telerik's build tooling.

**Alternative — user-level (applies to all projects on this machine):**
```
~/.telerik/telerik-license.txt
```

**To regenerate or download the license key:**
1. Log in at [https://www.telerik.com/account/your-licenses](https://www.telerik.com/account/your-licenses)
2. Find **Telerik UI for Blazor** and click **Download License Key**.
3. Save the file to one of the paths above.

> **Important:** Do not commit `telerik-license.txt` to source control. Add it to `.gitignore`.

### NuGet Source & Credentials

**Option 1: Using `.nuget/NuGet.Config` (Recommended for local dev)**

A `.nuget/NuGet.Config` file at the solution root stores Telerik credentials so NuGet doesn't prompt during restore/build:

```
.nuget/NuGet.Config
```

**To set up:**
1. Open `.nuget/NuGet.Config`
2. Replace `YOUR_TELERIK_EMAIL` and `YOUR_TELERIK_PASSWORD_OR_TOKEN` with your Telerik account credentials
3. Save and rebuild

The file is already added to `.gitignore` to prevent committing credentials.

**Option 2: Using CLI (one-time setup)**

Alternatively, configure the NuGet source globally:
```bash
dotnet nuget update source Telerik \
  --username YOUR_EMAIL \
  --password YOUR_PASSWORD \
  --store-password-in-clear-text
```
The `--store-password-in-clear-text` flag is required on macOS/Linux.

**Verify the source:**
```bash
dotnet nuget list source
```

### Custom Theme — `ben-light` (Meridian)

A custom Telerik theme based on the **Meridian** base theme was created and placed at:

```
Ben.Web.WebApp/wwwroot/theme/ben-light/
  dist/
    css/
      ben-light.css        ← compiled CSS to reference in the app
    scss/
      index.scss
      _tokens.scss         ← design token overrides
      _overrides.scss      ← component-level overrides
      _placeholders.scss
      _kendo.scss
  package.json
```

To use the theme in the Blazor app, reference the compiled CSS in `App.razor` (or `_Host.cshtml`) instead of the default Telerik CDN stylesheet:

```html
<link rel="stylesheet" href="theme/ben-light/dist/css/ben-light.css" />
```

To rebuild the SCSS after making token/override changes, run from the `ben-light` folder:
```bash
npm install
npm run build
```

---

## Open Questions

- Add unresolved questions here.
Telerik Nuget Key: [REDACTED — was exposed in git history; retrieve current key from telerik.com → Your Account → Downloads → License Keys]

## Microsoft URLs


Authority URL (Accounts in this organizational directory only)
https://login.microsoftonline.com/common

Authority URL (Accounts in any organizational directory)
https://login.microsoftonline.com/organizations

Authority URL (Accounts in any organizational directory and personal Microsoft accounts)
https://login.microsoftonline.com/common

Authority URL (Personal Microsoft accounts only)
https://login.microsoftonline.com/consumers

OAuth 2.0 authorization endpoint (v2)
https://login.microsoftonline.com/common/oauth2/v2.0/authorize

OAuth 2.0 token endpoint (v2)
https://login.microsoftonline.com/common/oauth2/v2.0/token

OAuth 2.0 authorization endpoint (v1)
https://login.microsoftonline.com/common/oauth2/authorize

OAuth 2.0 token endpoint (v1)
https://login.microsoftonline.com/common/oauth2/token

SAML-P sign-on endpoint
https://login.microsoftonline.com/b9f905ce-f3ef-4cdf-85cb-22fe9622ff5b/saml2

SAML-P sign-out endpoint
https://login.microsoftonline.com/b9f905ce-f3ef-4cdf-85cb-22fe9622ff5b/saml2

WS-Federation sign-on endpoint
https://login.microsoftonline.com/b9f905ce-f3ef-4cdf-85cb-22fe9622ff5b/wsfed

Federation metadata document
https://login.microsoftonline.com/b9f905ce-f3ef-4cdf-85cb-22fe9622ff5b/federationmetadata/2007-06/federationmetadata.xml

OpenID Connect metadata document
https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration

Microsoft Graph API endpoint
https://graph.microsoft.com
