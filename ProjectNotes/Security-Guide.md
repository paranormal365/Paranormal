# Ben Security Reference Guide

**Last Updated:** 2026-07-18  
**Status:** ✅ Fully implemented and integrated

---

## Table of Contents

1. [Security Model Overview](#1-security-model-overview)
2. [Level 1 — Site-Level Authentication](#2-level-1--site-level-authentication)
3. [JWT — Structure, Claims & Parsing](#3-jwt--structure-claims--parsing)
4. [Level 2 — Organization-Level Authorization](#4-level-2--organization-level-authorization)
5. [SuperAdmin Role](#5-superadmin-role)
6. [Microsoft Entra (External) Authentication](#6-microsoft-entra-external-authentication)
7. [User Impersonation](#7-user-impersonation)
8. [Security Level Comparison — Examples](#8-security-level-comparison--examples)
9. [Organization Security Service](#9-organization-security-service)
10. [OrganizationSecurityAuthorize Attribute](#10-organizationsecurityauthorize-attribute)
11. [Example: Securing a Full CRUD Controller](#11-example-securing-a-full-crud-controller)
12. [WebApp Auth State](#12-webapp-auth-state)
13. [Database Schema](#13-database-schema)
14. [Best Practices](#14-best-practices)

---

## 1. Security Model Overview

Ben uses a **two-level security model**:

```
┌────────────────────────────────────────────────┐
│  LEVEL 1 — Site Authentication                 │
│  ASP.NET Identity + Bearer JWT                 │
│  Who are you? (email/password → token)         │
│  Roles: SuperAdmin (full access)               │
└────────────────────┬───────────────────────────┘
                     │ authenticated request
                     ▼
┌────────────────────────────────────────────────┐
│  LEVEL 2 — Organization Authorization          │
│  Tenant-based: each org is an isolated scope   │
│  Who can do what inside which organization?    │
│  Membership + Access Grants (table + actions)  │
└────────────────────────────────────────────────┘
```

**Flow:**
```
Request arrives at WebApi
  ↓
[Authorize] — is the bearer token valid? (Level 1)
  ↓ YES
[OrganizationSecurityAuthorize] — is the user allowed to do this in this org? (Level 2)
  ↓ YES
Controller action executes
```

---

## 2. Level 1 — Site-Level Authentication

### Token Flow

```
WebApp Login Page
  ↓ POST /login { email, password }
WebApi Identity Endpoint
  ↓ returns { accessToken, refreshToken, expiresIn }
WebApp stores tokens in IWebApiTokenStore (scoped, per-circuit)
  ↓
All subsequent API calls include:
  Authorization: Bearer {accessToken}
  (injected by WebApiBearerTokenHandler)
```

### JWT Claims Parsed on Login

`WebApiAuthService.ParseJwtClaims(token)` decodes the JWT payload (base64, no extra package) and extracts:

| Claim | Token Store Property | Notes |
|---|---|---|
| `sub` | `UserId` (Guid) | User's primary key |
| `role` | `IsSuperAdmin` (bool) | true if role == "SuperAdmin" |

These are set on every `ApplyTokenResponse` call (login, refresh, and impersonate).

### Token Refresh

`RefreshIfNeededAsync()` — called before sensitive operations:
- If `AccessTokenExpiresAtUtc` is in the future → skip
- Otherwise: `POST /refresh { refreshToken }` → apply new tokens

### WebApp Token Store (`IWebApiTokenStore`)

```csharp
public interface IWebApiTokenStore
{
    string? AccessToken { get; set; }
    string? RefreshToken { get; set; }
    DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
    string? UserEmail { get; set; }
    string? UserDisplayName { get; set; }
    Guid? UserId { get; set; }
    bool IsSuperAdmin { get; set; }
    bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

    // Impersonation state
    bool IsImpersonating { get; set; }
    string? OriginalAccessToken { get; set; }
    string? OriginalRefreshToken { get; set; }
    Guid? OriginalUserId { get; set; }
    string? OriginalUserEmail { get; set; }
}
```

---

## 3. JWT — Structure, Claims & Parsing

### What Is a JWT?

A **JSON Web Token** is a compact, URL-safe token made of three Base64URL-encoded parts separated by dots:

```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9    ← Header
.
eyJzdWIiOiI5ZjM4MjFkNi0uLi4iLCJlbWFpbCI6ImhhdmViZW5AbXNuLmNvbSIsInJvbGUiOiJTdXBlckFkbWluIiwiZXhwIjoxNzgzOTQ5Njk1fQ
.
Y4cLSO7ywJmvF2WgkeM3WaSZds0HpQC712D9OdZqlwQ  ← Signature
```

| Part | Content | Used for |
|---|---|---|
| **Header** | Algorithm (`HS256`) + type (`JWT`) | Token validation |
| **Payload** | Claims (user data, roles, expiry) | Identity information |
| **Signature** | HMAC of header + payload | Tamper detection |

The payload is **not encrypted** — it is Base64URL encoded and readable by anyone. Never store sensitive data (passwords, secrets) in a JWT.

### Claims in the Ben JWT

When `POST /login` succeeds, ASP.NET Identity issues a token whose payload contains:

```json
{
  "sub":   "9f3821d6-4d2a-4a1e-b345-c1234abcdef0",
  "email": "haveben@msn.com",
  "role":  "SuperAdmin",
  "nbf":   1783946095,
  "exp":   1783949695,
  "iat":   1783946095,
  "iss":   "dotnet-user-jwts",
  "aud":   "http://localhost:5252"
}
```

| Claim | Type | Description |
|---|---|---|
| `sub` | Guid (string) | User's primary key — the canonical identity |
| `email` | string | User's email address |
| `role` | string or array | Role(s) assigned to the user |
| `exp` | Unix timestamp | Token expiry (default 1 hour) |
| `nbf` | Unix timestamp | Not valid before |
| `iat` | Unix timestamp | Issued at |
| `iss` | string | Issuer (the WebApi) |
| `aud` | string | Audience (the intended consumer) |

> If a user has multiple roles, `role` becomes a JSON array: `["Editor", "SuperAdmin"]`

### How ASP.NET Identity Issues the Token

The `/login` endpoint is provided by `MapIdentityApi<AppUser>()` in `Ben.Data.WebApi/Program.cs`. It uses `BearerTokenDefaults.AuthenticationScheme` and the `BearerTokenOptions` configuration (opaque bearer — but internally structured as a JWT).

```csharp
// Ben.Data.WebApi/Program.cs
builder.Services.AddIdentityApiEndpoints<AppUser>(options => { ... })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<BenDataContext>()
    .AddDefaultTokenProviders();

// ...
app.MapIdentityApi<AppUser>();
// Registers: POST /login, POST /register, POST /refresh,
//            GET /manage/info, POST /confirmEmail, etc.
```

**Response from `POST /login`:**
```json
{
  "tokenType":    "Bearer",
  "accessToken":  "CfDJ8N5jyN1...",
  "expiresIn":    3600,
  "refreshToken": "CfDJ8N5jyN1..."
}
```

### How the WebApp Parses the Token

On login, `WebApiAuthService.ApplyTokenResponse` decodes the JWT without any extra package:

```csharp
private static (Guid? UserId, bool IsSuperAdmin) ParseJwtClaims(string token)
{
    // Split: header.payload.signature
    var parts = token.Split('.');
    if (parts.Length < 2) return (null, false);

    // Base64URL → Base64 → UTF-8 JSON
    var payload = parts[1];
    var padded  = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
    var json    = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));

    using var doc = System.Text.Json.JsonDocument.Parse(json);

    // Extract sub (user ID)
    Guid? userId = null;
    if (doc.RootElement.TryGetProperty("sub", out var sub)
        && Guid.TryParse(sub.GetString(), out var id))
        userId = id;

    // Extract role (string or array)
    bool isSuperAdmin = false;
    if (doc.RootElement.TryGetProperty("role", out var role))
    {
        isSuperAdmin = role.ValueKind == JsonValueKind.String
            ? role.GetString() == "SuperAdmin"
            : role.EnumerateArray().Any(r => r.GetString() == "SuperAdmin");
    }

    return (userId, isSuperAdmin);
}
```

### Token Expiry and Refresh

Tokens expire after 3600 seconds (1 hour). The WebApp tracks expiry in `AccessTokenExpiresAtUtc` and refreshes silently:

```csharp
public async Task<bool> RefreshIfNeededAsync(CancellationToken token = default)
{
    // Still valid — skip
    if (_tokenStore.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow)
        return true;

    // Expired — exchange refresh token for a new access token
    var response = await _identityClient.RefreshAsync(_tokenStore.RefreshToken, token);
    if (response is null) return false;

    ApplyTokenResponse(response);  // parses new JWT, updates UserId + IsSuperAdmin
    return true;
}
```

**Refresh flow:**
```
POST /refresh { "refreshToken": "CfDJ8..." }
  ↓ returns new { accessToken, refreshToken, expiresIn }
  ↓ WebApp stores new tokens; old refresh token is invalidated
```

### How the WebApi Validates Tokens

Every controller action decorated with `[Authorize]` triggers ASP.NET's bearer token middleware:

```
Incoming request
  ↓
Authorization: Bearer CfDJ8N5jyN1...
  ↓
BearerTokenOptions validates: signature, expiry, issuer, audience
  ↓ VALID
User.Identity.IsAuthenticated = true
User.Claims populated (sub, email, role, etc.)
  ↓
Controller action executes
```

Accessing claims in a controller:

```csharp
// Get user ID from token claims
var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

// Get email
var email = User.FindFirstValue(ClaimTypes.Email);

// Check role
bool isSuperAdmin = User.IsInRole("SuperAdmin");
```

### Token Storage Rules

| Location | Used | Reason |
|---|---|---|
| `IWebApiTokenStore` (Blazor scoped) | ✅ YES | Server-side, per-circuit, never exposed to browser JS |
| Browser `localStorage` / `sessionStorage` | ❌ NO | Vulnerable to XSS |
| Cookies (httpOnly) | ❌ Not currently | Would require separate cookie auth layer |
| In-memory on client | ❌ NO | Lost on page refresh; not applicable to Blazor Server |

In Blazor Server, the entire app runs on the server inside a SignalR circuit. The token never leaves the server — only the UI is sent to the browser. This makes `IWebApiTokenStore` (a simple scoped C# class) safe and sufficient.

---

## 4. Level 2 — Organization-Level Authorization

### Core Concept

Each organization is a **tenant** — an isolated data scope. A user can belong to zero, one, or many organizations. Within each organization, access is controlled by:

1. **Membership role** — Owner, Administrator, Manager, Member, Viewer
2. **Access Grants** — explicit permissions for specific table + action combinations

### Enums

#### `OrganizationSecurityAction` (Flags)

```csharp
[Flags]
public enum OrganizationSecurityAction
{
    None   = 0,
    Create = 1,
    Read   = 2,
    Update = 4,
    Delete = 8,
    All    = Create | Read | Update | Delete  // = 15
}
```

Can combine: `Read | Update` (value = 6)

#### `OrganizationSecurityTable`

25 tables representing all org-related entities:

| Domain | Tables |
|---|---|
| Organization | `Organization`, `OrganizationAddress`, `OrganizationEmail`, `OrganizationPhone`, `OrganizationLink`, `OrganizationNote`, `OrganizationPage` |
| Org Types | `OrganizationAddressType`, `OrganizationEmailType`, `OrganizationPhoneType`, `OrganizationLinkType`, `OrganizationNoteType` |
| User | `User`, `UserAddress`, `UserEmail`, `UserPhone`, `UserLink`, `UserNote`, `UserMessage` |
| User Types | `UserAddressType`, `UserEmailType`, `UserPhoneType`, `UserLinkType`, `UserNoteType`, `UserMessageType` |

#### `OrganizationMemberRole`

```csharp
public enum OrganizationMemberRole
{
    Owner         = 1,  // Full control; bypasses all permission checks
    Administrator = 2,
    Manager       = 3,
    Member        = 4,
    Viewer        = 5   // Read-only intent
}
```

### Permission Decision Logic

```
HasPermission(userId, orgId, table, action)
  ↓
Is user a SuperAdmin?  ──YES──▶ ✅ ALLOW
  ↓ NO
Is user an Owner of this org?  ──YES──▶ ✅ ALLOW
  ↓ NO
Query OrganizationAccessGrant WHERE
  OrganizationId = orgId AND UserId = userId AND TableName = table
  ↓
Grant found AND (Actions & action) != None?  ──YES──▶ ✅ ALLOW
  ↓ NO
❌ DENY (403 Forbidden)
```

---

## 5. SuperAdmin Role

**SuperAdmin** is a site-wide role with unrestricted access.

- Seeded automatically at startup via `SuperAdminSeeder` in `Ben.Data.WebApi/SeedData/`
- Only one role exists: `"SuperAdmin"` in `AspNetRoles`
- All `/api/admin/*` endpoints require this role via the **`"SuperAdmin"` policy** (not a Roles attribute — see below)
- SuperAdmin bypasses all `IOrganizationSecurityService` permission checks
- SuperAdmin is detected from the JWT `role` claim, stored in `IWebApiTokenStore.IsSuperAdmin`

### Why Policy, Not `[Authorize(Roles = "SuperAdmin")]`

With Microsoft Entra JWTs, the `role` claim is not guaranteed to be present even after `IClaimsTransformation` runs — authentication scheme caching can cause the enriched claims to be missed by `RolesAuthorizationRequirement`. A custom `IAuthorizationHandler` that queries the database directly is more reliable for both token types.

All admin controllers use:
```csharp
[Authorize(Policy = RoleNames.SuperAdmin)]
```

### `SuperAdminHandler` (DB-Backed Authorization)

**File:** `Ben.Data.WebApi/Authorization/SuperAdminRequirement.cs`

`SuperAdminHandler` extends `AuthorizationHandler<SuperAdminRequirement>` and checks the SuperAdmin role via three paths, in order:

| Path | How | Used for |
|---|---|---|
| 1 | `context.User.IsInRole("SuperAdmin")` | Local Identity bearer tokens (fast path — role claim present) |
| 2 | `app_user_id` claim → `UserManager.FindByIdAsync` → `IsInRoleAsync` | Entra JWTs enriched by `EntraClaimsTransformation` |
| 3 | `oid` claim → `UserManager.FindByLoginAsync("Microsoft", oid)` → `IsInRoleAsync` | Entra JWTs where `app_user_id` is absent |

If no user is found or the user is not in the SuperAdmin role, the requirement is not marked succeeded (returns 403).

#### Registration in Program.cs

```csharp
builder.Services.AddScoped<IAuthorizationHandler, SuperAdminHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(RoleNames.SuperAdmin, policy =>
        policy.AddAuthenticationSchemes(schemes)
              .RequireAuthenticatedUser()
              .AddRequirements(new SuperAdminRequirement()));
});
```

### Admin Controller Base

```csharp
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
public abstract class AdminEntityControllerBase<TEntity, TRecord> : ControllerBase
{
    // GetAll, GetById, Create, Update, Delete — all SuperAdmin only
}
```

### Checking SuperAdmin in WebApp

```razor
@inject IWebApiTokenStore TokenStore

@if (TokenStore.IsSuperAdmin && !TokenStore.IsImpersonating)
{
    <NavLink href="/admin/users">Admin: Users</NavLink>
}
```

---

## 6. Microsoft Entra (External) Authentication

### Overview

Ben supports Microsoft Entra ID (Azure AD) as an **external login provider**. Users can sign in with a Microsoft account and have it linked to their Ben `AppUser` record. Once linked, Entra JWT tokens are accepted at all WebApi endpoints that require `[Authorize]`.

### App Registration

| Setting | Value |
|---|---|
| App name | `AverageBen.net (2026-07-18)` |
| Client ID | `3e37e6d7-13ea-4b94-b271-618267256d8b` |
| Tenant | `common` (personal + work Microsoft accounts) |
| signInAudience | `AzureADandPersonalMicrosoftAccount` |
| Token version | `2` (must be set in manifest JSON) |
| Redirect URIs | `https://localhost:5078/signin-oidc` |
| Client secret | In `appsettings.Development.json` (gitignored) |

### Entra Login Flow

```
User clicks "Sign in with Microsoft"
  ↓
WebApp redirects to Microsoft OIDC endpoint
  ↓ Microsoft login / consent
Microsoft redirects back with authorization code
  ↓
WebApp exchanges code for ID + access tokens
  ↓
WebApp calls GET /api/entra/link-or-register
  ↓
WebApi checks AspNetUserLogins for OID:
  FOUND  → return existing AppUser token
  NOT FOUND + email match → link OID → return token
  NOT FOUND + no email match → redirect to /entra/complete-profile
  ↓
WebApp stores bearer token in IWebApiTokenStore
```

### `EntraClaimsTransformation` (WebApi)

**File:** `Ben.Data.WebApi/Services/EntraClaimsTransformation.cs`

Runs for every Entra JWT request. Enriches the `ClaimsPrincipal` with:
- `app_user_id` — the Ben `AppUser.Id` (Guid)
- `role` claims — the user's Ben roles (e.g. `SuperAdmin`)

Lookup order:
1. `oid` claim → `FindByLoginAsync("Microsoft", oid)` — exact OID match
2. `preferred_username` / `email` claim → `FindByEmailAsync` + auto-re-links OID

> ⚠️ Custom API access tokens do not always include `preferred_username`. For authorization decisions, `SuperAdminHandler` is the authoritative check; `EntraClaimsTransformation` is a best-effort enrichment.

### Account Linking

An Entra OID is stored in `AspNetUserLogins` with `LoginProvider = "Microsoft"` and `ProviderKey = oid`. Linking happens automatically when:
- User registers via Entra (new account)
- Existing account's email matches the Entra `preferred_username`
- SuperAdmin manually links an account

### Token Differences — Local vs Entra

| Aspect | Local Identity Token | Entra JWT |
|---|---|---|
| Issuer | `dotnet-user-jwts` / WebApi URL | `https://login.microsoftonline.com/{tenantId}/v2.0` |
| `sub` claim | `AppUser.Id` | Entra `sub` (not the AppUser ID) |
| `role` claim | ✅ Always present | ❌ Not present by default |
| `oid` claim | ❌ Not present | ✅ Always present |
| `app_user_id` claim | ❌ Not present | ✅ Added by `EntraClaimsTransformation` |
| SuperAdmin check | `User.IsInRole("SuperAdmin")` (fast path) | `SuperAdminHandler` path 2 or 3 (DB lookup) |

---

## 7. User Impersonation

SuperAdmin can view the application exactly as any user, then return to their own account.

### How It Works

> Note: Impersonation tokens are Local Identity tokens (not Entra). SuperAdmin must be logged in with a local account or an Entra account that has been linked to a local SuperAdmin `AppUser`.

```
SuperAdmin clicks "Impersonate" on /admin/users page
  ↓
POST /api/admin/impersonate/{targetUserId}  (SuperAdmin bearer token required)
  ↓ WebApi: SignInManager.CreateUserPrincipalAsync(targetUser)
           return SignIn(principal, BearerTokenDefaults.AuthenticationScheme)
  ↓ Returns a real bearer token for the target user
  ↓
WebApp: saves SuperAdmin tokens to OriginalAccessToken / OriginalRefreshToken
        applies target user's token
        sets IsImpersonating = true
  ↓
All subsequent API calls are made as the target user
Banner shown: "Viewing as: user@email.com  [Return to SuperAdmin]"
  ↓
SuperAdmin clicks "Return to SuperAdmin"
  ↓
WebApp: restores original tokens from OriginalAccessToken
        re-parses JWT to restore IsSuperAdmin = true
        clears IsImpersonating flag
```

### Triggering Impersonation

```csharp
// In WebApiAuthService
public async Task<bool> ImpersonateAsync(Guid targetUserId, string targetUserEmail, CancellationToken token)
{
    var response = await _apiClient.ImpersonateAsync(targetUserId, token);
    if (response is null) return false;

    // Save current SuperAdmin session
    _tokenStore.OriginalAccessToken = _tokenStore.AccessToken;
    _tokenStore.OriginalRefreshToken = _tokenStore.RefreshToken;
    _tokenStore.OriginalUserId = _tokenStore.UserId;
    _tokenStore.OriginalUserEmail = _tokenStore.UserEmail;

    // Apply target user's token
    ApplyTokenResponse(response);           // sets UserId, IsSuperAdmin from new token
    _tokenStore.UserEmail = targetUserEmail;
    _tokenStore.IsImpersonating = true;
    return true;
}

public void StopImpersonating()
{
    _tokenStore.AccessToken = _tokenStore.OriginalAccessToken;
    _tokenStore.RefreshToken = _tokenStore.OriginalRefreshToken;
    _tokenStore.UserEmail = _tokenStore.OriginalUserEmail;
    // Re-parse original JWT to restore IsSuperAdmin
    var (userId, isSuperAdmin) = ParseJwtClaims(_tokenStore.OriginalAccessToken!);
    _tokenStore.UserId = userId;
    _tokenStore.IsSuperAdmin = isSuperAdmin;
    _tokenStore.IsImpersonating = false;
    // Clear saved state
    _tokenStore.OriginalAccessToken = _tokenStore.OriginalRefreshToken =
        _tokenStore.OriginalUserEmail = null;
    _tokenStore.OriginalUserId = null;
}
```

### WebApi Impersonation Endpoint

```csharp
[ApiController]
[Route("api/admin/impersonate")]
[Authorize(Roles = "SuperAdmin")]
public sealed class ImpersonateController : ControllerBase
{
    [HttpPost("{targetUserId:guid}")]
    public async Task<IActionResult> ImpersonateUser(Guid targetUserId, ...)
    {
        var user = await _userManager.FindByIdAsync(targetUserId.ToString());
        if (user is null) return NotFound();

        var principal = await _signInManager.CreateUserPrincipalAsync(user);
        return SignIn(principal, BearerTokenDefaults.AuthenticationScheme);
        // Returns same JSON format as /login: { accessToken, refreshToken, expiresIn }
    }
}
```

---

## 8. Security Level Comparison — Examples

This section shows the same endpoint concept implemented at three different security levels, so you can see exactly what each layer adds.

### Scenario: "Get notes for a user"

---

### Example A — Site Level Only (`[Authorize]`)

**Who can call it?** Any authenticated user (valid bearer token).  
**What it does NOT check:** whether the caller owns the note, or has org permission.

```csharp
[ApiController]
[Route("api/notes")]
public class NoteController : ControllerBase
{
    private readonly BenDataContext _db;

    // ✅ Any logged-in user can read ANY note — no ownership check
    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetNote(Guid id, CancellationToken ct)
    {
        var note = await _db.UserNotes.FindAsync([id], ct);
        if (note is null) return NotFound();
        return Ok(note);
    }
}
```

**Result matrix:**

| Caller | Outcome |
|---|---|
| Not logged in | ❌ 401 Unauthorized |
| Any logged-in user | ✅ 200 — can read any note |
| Owner of the note | ✅ 200 |
| SuperAdmin | ✅ 200 |

---

### Example B — User Level (`[Authorize]` + ownership check)

**Who can call it?** Any authenticated user, but they can only see their own notes.  
**What it adds:** the caller's `UserId` from the JWT `sub` claim is compared to the note's `AppUserId`.

```csharp
[HttpGet("{id:guid}")]
[Authorize]
public async Task<IActionResult> GetMyNote(Guid id, CancellationToken ct)
{
    // Extract caller identity from token claims
    var callerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    var note = await _db.UserNotes.FindAsync([id], ct);
    if (note is null) return NotFound();

    // Ownership check — only the note's owner may read it
    // SuperAdmin bypass: always allow
    if (note.AppUserId != callerId && !User.IsInRole("SuperAdmin"))
        return Forbid();   // 403

    return Ok(note);
}
```

**Result matrix:**

| Caller | Outcome |
|---|---|
| Not logged in | ❌ 401 Unauthorized |
| Logged-in user, not the owner | ❌ 403 Forbidden |
| Owner of the note | ✅ 200 |
| SuperAdmin | ✅ 200 (bypasses ownership check) |

---

### Example C — Organization Level (`[Authorize]` + `[OrganizationSecurityAuthorize]`)

**Who can call it?** Users who are members of the organization AND have been explicitly granted Read permission on `UserNote` within that org.  
**What it adds:** org membership check + access grant check via `IOrganizationSecurityService`.

```csharp
[ApiController]
[Route("api/organizations/{organizationId}/notes")]
public class OrgNoteController : ControllerBase
{
    // ✅ Only org members with Read permission on UserNote can call this
    [HttpGet]
    [Authorize]
    [OrganizationSecurityAuthorize("organizationId",
        OrganizationSecurityTable.UserNote,
        OrganizationSecurityAction.Read)]
    public async Task<IActionResult> GetOrgNotes(Guid organizationId, CancellationToken ct)
    {
        // Attribute has already verified:
        // 1. Caller is authenticated (valid token)
        // 2. Caller is a member of organizationId
        // 3. Caller has Read on UserNote in this org (or is Owner/SuperAdmin)
        var notes = await _db.UserNotes
            .Where(n => n.CreatedByAppUserId == /* org member */ Guid.Empty) // example
            .ToListAsync(ct);
        return Ok(notes);
    }
}
```

**Result matrix:**

| Caller | Outcome |
|---|---|
| Not logged in | ❌ 401 Unauthorized |
| Logged in, not a member of the org | ❌ 403 Forbidden |
| Org member, but no UserNote Read grant | ❌ 403 Forbidden |
| Org member WITH UserNote Read grant | ✅ 200 |
| Org Owner | ✅ 200 (owner bypasses grant check) |
| SuperAdmin | ✅ 200 (SuperAdmin bypasses everything) |

---

### Example D — All Three Combined

A realistic endpoint that requires login, ownership or org membership, and a specific org permission:

```csharp
[HttpGet("{noteId:guid}")]
[Authorize]
public async Task<IActionResult> GetNote(
    Guid organizationId, Guid noteId,
    [FromServices] IOrganizationSecurityService security,
    CancellationToken ct)
{
    var callerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var note = await _db.UserNotes.FindAsync([noteId], ct);
    if (note is null) return NotFound();

    // SuperAdmin bypasses all checks
    if (User.IsInRole("SuperAdmin"))
        return Ok(note);

    // Owner always sees their own note
    if (note.AppUserId == callerId)
        return Ok(note);

    // Org member with Read permission can see it if it's shared with their org
    var hasOrgAccess = await security.HasPermissionAsync(
        callerId, organizationId,
        OrganizationSecurityTable.UserNote,
        OrganizationSecurityAction.Read, ct);

    if (!hasOrgAccess) return Forbid();

    return Ok(note);
}
```

**Decision tree:**

```
Request arrives
  ↓
[Authorize] — token valid?  ──NO──▶ 401
  ↓ YES
Is SuperAdmin?  ──YES──▶ ✅ 200
  ↓ NO
Is note owner?  ──YES──▶ ✅ 200
  ↓ NO
HasPermission(orgId, UserNote, Read)?  ──NO──▶ 403
  ↓ YES
✅ 200
```

---

### Side-by-Side Summary

| Aspect | Site Level | User Level | Org Level |
|---|---|---|---|
| **Requires login** | ✅ | ✅ | ✅ |
| **Attribute** | `[Authorize]` | `[Authorize]` | `[Authorize]` + `[OrganizationSecurityAuthorize]` |
| **Ownership check** | ❌ None | ✅ `callerId == resource.OwnerId` | ✅ via permission grant |
| **Org membership check** | ❌ None | ❌ None | ✅ Must be org member |
| **Permission grant check** | ❌ None | ❌ None | ✅ Must have grant for table+action |
| **Owner bypass** | n/a | n/a | ✅ Org owner skips grant check |
| **SuperAdmin bypass** | ✅ Authenticated | ✅ Skip ownership | ✅ Skip all checks |
| **Use when** | Public app data | Per-user private data | Multi-tenant org data |

---

## 9. Organization Security Service

Defined in `Ben.Service.Security/Services/IOrganizationSecurityService.cs`.  
Registered in WebApi DI as `AddScoped<IOrganizationSecurityService, OrganizationSecurityService>()`.

### Full Interface

```csharp
public interface IOrganizationSecurityService
{
    // --- Permission Checking ---

    Task<bool> HasPermissionAsync(
        Guid userId,
        Guid organizationId,
        OrganizationSecurityTable table,
        OrganizationSecurityAction action,
        CancellationToken cancellationToken = default);

    Task<bool> IsMemberAsync(Guid userId, Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<bool> IsOwnerAsync(Guid userId, Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<OrganizationMemberRole?> GetUserRoleAsync(Guid userId, Guid organizationId,
        CancellationToken cancellationToken = default);

    // --- User's Organizations ---

    Task<IReadOnlyList<Guid>> GetUserOrganizationsAsync(Guid userId,
        CancellationToken cancellationToken = default);

    // --- Membership Management ---

    Task AddMemberAsync(Guid organizationId, Guid userId, OrganizationMemberRole role,
        CancellationToken cancellationToken = default);

    Task RemoveMemberAsync(Guid organizationId, Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(Guid UserId, OrganizationMemberRole Role)>> GetOrganizationMembersAsync(
        Guid organizationId, CancellationToken cancellationToken = default);

    // --- Access Grants ---

    Task GrantAccessAsync(
        Guid organizationId, Guid userId,
        OrganizationSecurityTable table, OrganizationSecurityAction actions,
        Guid grantedByUserId, CancellationToken cancellationToken = default);

    Task RevokeAccessAsync(
        Guid organizationId, Guid userId,
        OrganizationSecurityTable table,
        CancellationToken cancellationToken = default);

    // --- User Search ---

    Task<IReadOnlyList<AppUser>> SearchUsersAsync(
        Guid actingUserId, string? query,
        int skip = 0, int take = 25,
        CancellationToken cancellationToken = default);
    // SuperAdmin: searches all users. Others: searches only users in shared active orgs.
}
```

### Using the Service Directly in a Controller

```csharp
[ApiController]
[Route("api/organizations/{organizationId}/members")]
[Authorize]
public class MemberController : ControllerBase
{
    private readonly IOrganizationSecurityService _security;

    [HttpGet]
    public async Task<IActionResult> GetMembers(Guid organizationId, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (!await _security.IsMemberAsync(userId, organizationId, ct))
            return Forbid();

        var members = await _security.GetOrganizationMembersAsync(organizationId, ct);
        return Ok(members);
    }
}
```

---

## 10. OrganizationSecurityAuthorize Attribute

Automatically enforces org-level permission checks on controller actions.

### Attribute Definition

Located in `Ben.Service.Security/Attributes/OrganizationSecurityAuthorizeAttribute.cs`.

**Parameters:**
1. `organizationIdParameter` — name of the route/query param containing the org Guid
2. `table` — which `OrganizationSecurityTable` is being accessed
3. `action` — which `OrganizationSecurityAction` is being performed

### What It Does

1. Extracts `UserId` from JWT claims (`sub` claim)
2. Reads `organizationId` from the named route parameter
3. Calls `HasPermissionAsync(userId, organizationId, table, action)`
4. Returns HTTP 403 if check fails; proceeds to action if it passes

### Usage Pattern

```csharp
[ApiController]
[Route("api/organizations/{organizationId}/notes")]
[Authorize]
public class OrgNoteController : ControllerBase
{
    [HttpGet]
    [OrganizationSecurityAuthorize("organizationId",
        OrganizationSecurityTable.OrganizationNote,
        OrganizationSecurityAction.Read)]
    public async Task<IActionResult> GetAll(Guid organizationId, CancellationToken ct)
    {
        // Only reached if user has Read on OrganizationNote in this org
        // (or is Owner / SuperAdmin)
    }

    [HttpPost]
    [OrganizationSecurityAuthorize("organizationId",
        OrganizationSecurityTable.OrganizationNote,
        OrganizationSecurityAction.Create)]
    public async Task<IActionResult> Create(Guid organizationId, [FromBody] CreateNoteRequest req, CancellationToken ct)
    {
        // Only reached if user has Create permission
    }

    [HttpPut("{id:guid}")]
    [OrganizationSecurityAuthorize("organizationId",
        OrganizationSecurityTable.OrganizationNote,
        OrganizationSecurityAction.Update)]
    public async Task<IActionResult> Update(Guid organizationId, Guid id, [FromBody] UpdateNoteRequest req, CancellationToken ct)
    {
        // Only reached if user has Update permission
    }

    [HttpDelete("{id:guid}")]
    [OrganizationSecurityAuthorize("organizationId",
        OrganizationSecurityTable.OrganizationNote,
        OrganizationSecurityAction.Delete)]
    public async Task<IActionResult> Delete(Guid organizationId, Guid id, CancellationToken ct)
    {
        // Only reached if user has Delete permission
    }
}
```

---

## 11. Example: Securing a Full CRUD Controller

This example shows a complete secured controller for managing users within an organization:

```csharp
[ApiController]
[Route("api/organizations/{organizationId}/users")]
[Authorize]
public class OrganizationUserController : ControllerBase
{
    private readonly IOrganizationSecurityService _security;
    private readonly IRepositoryManager _repository;

    public OrganizationUserController(
        IOrganizationSecurityService security,
        IRepositoryManager repository)
    {
        _security = security;
        _repository = repository;
    }

    // ── Read ─────────────────────────────────────────────────────────────────
    [HttpGet]
    [OrganizationSecurityAuthorize("organizationId",
        OrganizationSecurityTable.User, OrganizationSecurityAction.Read)]
    public async Task<ActionResult<IEnumerable<UserRecord>>> GetAll(
        Guid organizationId, CancellationToken ct)
    {
        // Permission auto-checked by attribute
        var members = await _security.GetOrganizationMembersAsync(organizationId, ct);
        return Ok(members);
    }

    // ── Grant Access ─────────────────────────────────────────────────────────
    [HttpPost("{targetUserId:guid}/grant")]
    public async Task<IActionResult> GrantAccess(
        Guid organizationId, Guid targetUserId,
        [FromBody] GrantAccessRequest request,
        CancellationToken ct)
    {
        var callerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Only owners and admins can grant access
        var role = await _security.GetUserRoleAsync(callerId, organizationId, ct);
        if (role is null or > OrganizationMemberRole.Administrator)
            return Forbid();

        await _security.GrantAccessAsync(
            organizationId, targetUserId,
            request.Table, request.Actions,
            grantedByUserId: callerId, ct);

        return NoContent();
    }

    // ── Add Member ───────────────────────────────────────────────────────────
    [HttpPost("{targetUserId:guid}/membership")]
    [OrganizationSecurityAuthorize("organizationId",
        OrganizationSecurityTable.User, OrganizationSecurityAction.Create)]
    public async Task<IActionResult> AddMember(
        Guid organizationId, Guid targetUserId,
        [FromBody] AddMemberRequest request, CancellationToken ct)
    {
        await _security.AddMemberAsync(organizationId, targetUserId, request.Role, ct);
        return NoContent();
    }

    // ── Remove Member ─────────────────────────────────────────────────────────
    [HttpDelete("{targetUserId:guid}/membership")]
    [OrganizationSecurityAuthorize("organizationId",
        OrganizationSecurityTable.User, OrganizationSecurityAction.Delete)]
    public async Task<IActionResult> RemoveMember(
        Guid organizationId, Guid targetUserId, CancellationToken ct)
    {
        await _security.RemoveMemberAsync(organizationId, targetUserId, ct);
        return NoContent();
    }
}
```

### Granting Permissions by Role Scenario

```csharp
// Read-only viewer
await security.GrantAccessAsync(orgId, userId,
    OrganizationSecurityTable.Organization,
    OrganizationSecurityAction.Read,
    grantedByUserId, ct);

// Editor (read + write, no delete)
await security.GrantAccessAsync(orgId, userId,
    OrganizationSecurityTable.OrganizationNote,
    OrganizationSecurityAction.Read | OrganizationSecurityAction.Create | OrganizationSecurityAction.Update,
    grantedByUserId, ct);

// Full CRUD
await security.GrantAccessAsync(orgId, userId,
    OrganizationSecurityTable.User,
    OrganizationSecurityAction.All,
    grantedByUserId, ct);
```

---

## 12. WebApp Auth State

### Checking Auth State in Blazor Components

```razor
@inject IWebApiTokenStore TokenStore
@inject IWebApiAuthService AuthService

@* Show content based on auth *@
@if (!TokenStore.IsAuthenticated)
{
    <p>Please <NavLink href="/login">log in</NavLink>.</p>
}
else if (TokenStore.IsImpersonating)
{
    <div class="alert alert-warning">
        Viewing as: @TokenStore.UserEmail
        <button @onclick="AuthService.StopImpersonating">Return to SuperAdmin</button>
    </div>
}
else if (TokenStore.IsSuperAdmin)
{
    <p>Logged in as SuperAdmin: @TokenStore.UserEmail</p>
}
else
{
    <p>Logged in as: @TokenStore.UserEmail</p>
}
```

### Guard Pages from Non-SuperAdmin

```razor
@code {
    protected override void OnInitialized()
    {
        // Redirect if not SuperAdmin, or if currently impersonating
        if (!TokenStore.IsSuperAdmin || TokenStore.IsImpersonating)
            NavManager.NavigateTo("/");
    }
}
```

### Accessing Current User Identity

```razor
@code {
    private Guid CurrentUserId => TokenStore.UserId ?? Guid.Empty;
    private bool IsSuperAdmin => TokenStore.IsSuperAdmin;
    private bool IsImpersonating => TokenStore.IsImpersonating;
}
```

---

## 13. Database Schema

### Identity Tables (ASP.NET Identity managed)

| Table | Purpose |
|---|---|
| `AppUsers` | Users — extends `IdentityUser<Guid>` with `DisplayName`, `DateCreated`, `DateUpdated` |
| `AspNetRoles` | Roles — only `"SuperAdmin"` currently seeded |
| `AspNetUserRoles` | User ↔ role mapping |
| `AspNetUserClaims` | Per-user claims |
| `AspNetUserLogins` | External login providers |
| `AspNetUserTokens` | Tokens (refresh, etc.) |
| `AspNetRoleClaims` | Per-role claims |

### Organization Security Tables

| Table | Purpose |
|---|---|
| `OrganizationUserMemberships` | User ↔ Org relationship; `Role` (OrganizationMemberRole enum: Owner/Admin/Manager/Member/Viewer), `IsActive` soft-delete |
| `OrganizationAccessGrants` | Per-user, per-org, per-table permission; `Actions` int bitmask (`OrganizationSecurityAction [Flags]`); one row per (user, org, table) |

### Key Relationships

```
AppUsers ──< OrganizationUserMemberships >── Organizations
AppUsers ──< OrganizationAccessGrants >────── Organizations
```

---

## 14. Best Practices

### ✅ DO

- Always combine `[Authorize]` + `[OrganizationSecurityAuthorize]` on org-scoped endpoints
- Use `OrganizationSecurityAction` flag combinations for precise permission grants
- Check `IsSuperAdmin` in the service layer to skip org-level checks for SuperAdmin
- Use `IWebApiTokenStore.UserId` (parsed from JWT) as the canonical user identity in the WebApp
- Soft-delete membership records (`IsActive = false`) — never hard-delete
- Refresh the access token before sensitive operations using `RefreshIfNeededAsync()`
- Guard admin Blazor pages with `OnInitialized` redirect if not SuperAdmin
- Use `[Authorize(Policy = RoleNames.SuperAdmin)]` for all admin endpoints — the `SuperAdminHandler` works for both Local Identity and Entra tokens
- Use `AddAuthenticationSchemes(...)` in the policy to accept all valid authentication schemes

### ❌ DON'T

- Never use `[Authorize(Roles = "SuperAdmin")]` — use `[Authorize(Policy = RoleNames.SuperAdmin)]` instead. The Roles attribute relies on role claims that may not be present in Entra JWTs
- Don't bypass `[OrganizationSecurityAuthorize]` for org-scoped data
- Don't hard-delete `OrganizationUserMembership` rows — use `IsActive = false`
- Don't store bearer tokens in browser localStorage (only in the Blazor circuit's `IWebApiTokenStore`)
- Don't call impersonation endpoints from the WebApp unless `IsSuperAdmin` is true
- Don't cache `IsSuperAdmin` beyond the token lifetime — re-parse on every `ApplyTokenResponse`
- Don't rely on `IClaimsTransformation` alone for SuperAdmin authorization with Entra tokens — use `SuperAdminHandler`

### Known Technical Debt

| Item | Location | Notes |
|---|---|---|
| NU1903 `Microsoft.OpenApi` advisory | `Ben.Data.WebApi` | Transitive vulnerability; awaiting upstream fix |
| `preferred_username` missing in Entra access tokens | `EntraClaimsTransformation` | Custom API access tokens may omit this claim; email fallback may miss non-standard accounts. `SuperAdminHandler` is authoritative and unaffected. |
| Entra client secret expiry | `appsettings.Development.json` / Azure Portal | Secret ID `9b9c40f2` expires 2028-07-17; rotate before expiry via Azure Portal → App registrations → `AverageBen.net (2026-07-18)` → Certificates & secrets |
