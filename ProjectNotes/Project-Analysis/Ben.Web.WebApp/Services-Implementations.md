# Ben.Web.WebApp — Service Implementations

---

## `WebApiTokenStore`

**File:** [`Ben.Web.WebApp/Services/WebApi/WebApiTokenStore.cs`](../../../Ben.Web.WebApp/Services/WebApi/WebApiTokenStore.cs)  
**Implements:** [`IWebApiTokenStore`](Services-Interfaces.md#iwebapitokenstore), [`IBenUserState`](../Ben.Web.Library/Services.md#ibenaccountstate)  
**Lifetime:** Scoped (one instance per Blazor Server circuit)

Concrete implementation of the token store.  
Fires `StateChanged` via `NotifyStateChanged()` after any mutation so `MainLayout` can persist state and re-render the app bar.

---

## `WebApiAuthService`

**File:** [`Ben.Web.WebApp/Services/WebApi/WebApiAuthService.cs`](../../../Ben.Web.WebApp/Services/WebApi/WebApiAuthService.cs)  
**Implements:** [`IWebApiAuthService`](Services-Interfaces.md#iwebapiuauthservice)

### `LoginAsync` flow
1. `IWebApiIdentityClient.LoginAsync(email, password)` → `WebApiTokenResponse`.
2. Store `AccessToken`, `RefreshToken`, `ExpiresAt` in `IWebApiTokenStore`.
3. `IWebApiClient.GetAsync<MeResponse>("/api/me")` → set `UserId`, `IsSuperAdmin`, `IsEntraSession = false`.
4. Call `NotifyStateChanged()`.

### `ImpersonateAsync` flow
1. Save original tokens to `OriginalAccessToken`, `OriginalRefreshToken`, `OriginalUserId`, `OriginalUserEmail`.
2. `IWebApiClient.ImpersonateAsync(targetUserId)` → new `WebApiTokenResponse`.
3. Apply new token, set `IsImpersonating = true`.
4. Call `NotifyStateChanged()`.

### `StopImpersonating` flow
1. Restore `OriginalAccessToken` etc. to active token fields.
2. `IsImpersonating = false`.
3. Re-parse `IsSuperAdmin` from the restored token or saved original state.
4. Call `NotifyStateChanged()`.

---

## `WebApiBearerTokenHandler`

**File:** [`Ben.Web.WebApp/Services/WebApi/WebApiBearerTokenHandler.cs`](../../../Ben.Web.WebApp/Services/WebApi/WebApiBearerTokenHandler.cs)  
**Inherits:** `DelegatingHandler`  
**Status:** ⚠️ **Retired — no longer in the HttpClient pipeline**

> **DI scope bug (2026-07-15):** `IHttpClientFactory` resolves `DelegatingHandler` instances from the **root DI scope**, not the Blazor circuit scope. This meant the `IWebApiTokenStore` injected into `WebApiBearerTokenHandler` was always a fresh, empty instance unrelated to the circuit's token store. The bearer token was never sent. The handler was removed from the pipeline and auth header injection was moved into `WebApiClient` directly.

The file is retained for reference but `AddHttpMessageHandler<WebApiBearerTokenHandler>()` is no longer called in `Program.cs`.

---

## `WebApiClient`

**File:** [`Ben.Web.WebApp/Services/WebApi/WebApiClient.cs`](../../../Ben.Web.WebApp/Services/WebApi/WebApiClient.cs)  
**Implements:** [`IWebApiClient`](Services-Interfaces.md#iwebapiclient)

Typed HTTP client wrapping `HttpClient`. Resolved as a transient typed client from the Blazor circuit scope, so constructor-injected `IWebApiTokenStore` is always the correct circuit-scoped instance.

**Constructor:** `WebApiClient(HttpClient httpClient, IWebApiTokenStore tokenStore)`

**`Auth(method, url)` private helper:** Creates an `HttpRequestMessage` and attaches `Authorization: Bearer <token>` if `IWebApiTokenStore.AccessToken` is non-null. Called at the start of every `GetAsync`, `PostAsync`, `PutAsync`, `DeleteAsync`, `UploadFileAsync`, `DownloadFileAsync`, and `EntraLinkAsync` — the token is read at **request time**, so `LoginAsync` can set the token and immediately call `/api/me` with the correct bearer.

All generic methods return `null`/`false` on non-2xx responses without throwing.

---

## `WebApiIdentityClient`

**File:** [`Ben.Web.WebApp/Services/WebApi/WebApiIdentityClient.cs`](../../../Ben.Web.WebApp/Services/WebApi/WebApiIdentityClient.cs)  
**Implements:** [`IWebApiIdentityClient`](Services-Interfaces.md#iwebapiidentityclient)

Anonymous HTTP client registered separately (no `WebApiBearerTokenHandler`).  
Calls `/login` and `/refresh` only.

---

## `BenAdminClientAdapter`

**File:** [`Ben.Web.WebApp/Services/WebApi/BenAdminClientAdapter.cs`](../../../Ben.Web.WebApp/Services/WebApi/BenAdminClientAdapter.cs)  
**Implements:** [`IBenAdminClient`](../Ben.Web.Library/Services.md#ibenadminclient)

Bridges `IBenAdminClient` (defined in the library) to `IWebApiClient` and `IWebApiAuthService`.  
Each method delegates to the appropriate `IWebApiClient` typed call.

---

## `JwtClaimsParser`

**File:** [`Ben.Web.WebApp/Services/WebApi/JwtClaimsParser.cs`](../../../Ben.Web.WebApp/Services/WebApi/JwtClaimsParser.cs)  
**Type:** Static class

### Summary
Decodes a JWT payload without signature validation.  
Used for **synthetic JWTs in tests** — the live Identity API issues opaque tokens so role resolution uses `GET /api/me` instead.

### `ParseClaims(string token)`
Returns `(Guid? UserId, bool IsSuperAdmin)`.  
Decodes the base64url payload, extracts `sub` claim as `UserId`, and checks `role` claim for `RoleNames.SuperAdmin`.

---

## `EntraTokenHolder`

**File:** [`Ben.Web.WebApp/Services/EntraTokenHolder.cs`](../../../Ben.Web.WebApp/Services/EntraTokenHolder.cs)  
**Lifetime:** Scoped

### Summary
Captures Entra token data from the HTTP request context so Blazor components can access it throughout the circuit lifetime.

Populated by middleware in `Program.cs` on every incoming request where `user.Identity.IsAuthenticated == true` (which in the WebApp is only possible via the OIDC cookie).

### Blazor Scope Bridge
In .NET 8+, the static SSR pre-render and the Interactive Server circuit run in **separate DI scopes**. The HTTP-scope `EntraTokenHolder` (populated by middleware) is invisible to the circuit-scope instance. The bridge:

1. **`EntraTokenPersister.razor`** (SSR, `App.razor`) — calls `PersistentComponentState.RegisterOnPersisting` to serialize the token into the HTML as `SerializedEntraToken`.
2. **`MainLayout.OnInitialized`** (circuit) — calls `PersistentComponentState.TryTakeFromJson` to restore the data into the circuit-scope `EntraTokenHolder` before `OnAfterRenderAsync` fires.

### Properties

| Property | Type | Description |
|---|---|---|
| `AccessToken` | `string?` | The Entra access token from the OIDC cookie. |
| `Email` | `string?` | `preferred_username` or `email` claim. |
| `EntraOid` | `string?` | `oid` claim — used for `FindByLoginAsync("Microsoft", oid)` lookup. |
| `IsEntraAuthenticated` | `bool` | `true` when `AccessToken` is non-null. |

### `SerializedEntraToken`

A companion `record` in the same file. Used exclusively by `PersistentComponentState` for the HTTP→circuit bridge. Contains the same four fields as `EntraTokenHolder`.

---

## `WebApiOptions`

**File:** [`Ben.Web.WebApp/Services/WebApi/WebApiOptions.cs`](../../../Ben.Web.WebApp/Services/WebApi/WebApiOptions.cs)

Simple options class bound to the `"WebApi"` configuration section.

| Property | Description |
|---|---|
| `BaseUrl` | Base URL of the WebApi (e.g. `http://localhost:5252`). |
