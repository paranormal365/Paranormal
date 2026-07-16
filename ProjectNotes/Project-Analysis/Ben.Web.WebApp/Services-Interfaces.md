# Ben.Web.WebApp — Service Interfaces

---

## `IWebApiTokenStore`

**Namespace:** `Ben.Web.WebApp.Services.WebApi`  
**File:** [`Ben.Web.WebApp/Services/WebApi/IWebApiTokenStore.cs`](../../../Ben.Web.WebApp/Services/WebApi/IWebApiTokenStore.cs)  
**Implemented by:** [`WebApiTokenStore`](Services-Implementations.md#webapitokenstore)  
**Also implements:** [`IBenUserState`](../Ben.Web.Library/Services.md#ibenaccountstate) (registered separately in DI)

### Summary
The central in-memory token and auth state store for the current Blazor Server circuit.  
All services that need the current access token, user info, or auth state read from this interface.

### Properties

| Property | Type | Description |
|---|---|---|
| `AccessToken` | `string?` | Current bearer token for API calls. |
| `RefreshToken` | `string?` | Token used to obtain a new access token without re-login. |
| `AccessTokenExpiresAtUtc` | `DateTimeOffset?` | When the access token expires. |
| `UserEmail` | `string?` | Authenticated user's email. |
| `UserDisplayName` | `string?` | Authenticated user's display name. |
| `UserId` | `Guid?` | Authenticated user's PK. |
| `IsSuperAdmin` | `bool` | Whether the user holds the SuperAdmin role. |
| `IsAuthenticated` | `bool` | Computed: `true` when `AccessToken` is non-empty. |
| `IsImpersonating` | `bool` | `true` when a SuperAdmin is acting as another user. |
| `OriginalAccessToken` | `string?` | SuperAdmin's saved token during impersonation. |
| `OriginalRefreshToken` | `string?` | SuperAdmin's saved refresh token. |
| `OriginalUserId` | `Guid?` | SuperAdmin's saved user ID. |
| `OriginalUserEmail` | `string?` | SuperAdmin's saved email. |
| `IsEntraSession` | `bool` | `true` for Entra OIDC sessions — auth state is NOT persisted to `ProtectedLocalStorage`. |

### Events & Methods

| Member | Description |
|---|---|
| `StateChanged` event | `Action?` — fires after any auth state change. `MainLayout` subscribes to persist state and re-render. |
| `NotifyStateChanged()` | Call this after setting state fields to trigger `StateChanged` subscribers. |

---

## `IWebApiAuthService`

**Namespace:** `Ben.Web.WebApp.Services.WebApi`  
**File:** [`Ben.Web.WebApp/Services/WebApi/IWebApiAuthService.cs`](../../../Ben.Web.WebApp/Services/WebApi/IWebApiAuthService.cs)

### Summary
High-level authentication operations: login, logout, token refresh, and impersonation lifecycle.

### Methods

| Method | Returns | Description |
|---|---|---|
| `LoginAsync(email, password, token)` | `bool` | POST `/login` → stores token → GET `/api/me` → sets `IsSuperAdmin`/`UserId` → fires `StateChanged`. |
| `RefreshIfNeededAsync(token)` | `bool` | POST `/refresh` if the access token is expired or close to expiry. |
| `Logout()` | `void` | Clears all token state and fires `StateChanged`. |
| `ImpersonateAsync(targetUserId, targetUserEmail, token)` | `bool` | Saves original tokens, POST `/api/admin/impersonate/{id}`, applies new token, fires `StateChanged`. |
| `StopImpersonating()` | `void` | Restores original tokens, fires `StateChanged`. |

---

## `IWebApiClient`

**Namespace:** `Ben.Web.WebApp.Services.WebApi`  
**File:** [`Ben.Web.WebApp/Services/WebApi/IWebApiClient.cs`](../../../Ben.Web.WebApp/Services/WebApi/IWebApiClient.cs)

### Summary
Typed HTTP client for all authenticated API calls.  
All requests automatically include the bearer token via `WebApiBearerTokenHandler`.

### Generic Methods

| Method | Returns | Description |
|---|---|---|
| `GetAsync<TResponse>(url, token)` | `TResponse?` | GET request, deserialises response. |
| `PostAsync<TRequest, TResponse>(url, payload, token)` | `TResponse?` | POST request with JSON body. |
| `PutAsync<TRequest, TResponse>(url, payload, token)` | `TResponse?` | PUT request with JSON body. |
| `DeleteAsync(url, token)` | `bool` | DELETE request. |

### Typed Methods (selected)

| Method | Endpoint | Description |
|---|---|---|
| `GetUsersAsync` | `GET /api/users` | All users. |
| `GetMyOrganizationsAsync` | `GET /api/security/organizations/mine` | Caller's organisations. |
| `SearchUsersAsync` | `GET /api/security/organizations/users/search` | Scoped user search. |
| `RegisterOrganizationAsync` | `POST /api/security/organizations/register` | Create org. |
| `GetUploadFileTypesAsync` | `GET /api/upload-file-types` | Available file type categories. |
| `GetUploadFilesAsync` | `GET /api/upload-files` | User's uploaded files. |
| `UploadFileAsync` | `POST /api/upload-files` (multipart) | Upload binary. |
| `DownloadFileAsync` | `GET /api/upload-files/{id}/download` | Returns `(byte[], ContentType, FileName)`. |
| `ImpersonateAsync` | `POST /api/admin/impersonate/{id}` | Get impersonation token. |
| `EntraRegisterAsync` | `POST /api/auth/entra/register` | Create local account linked to Entra OID. |
| `EntraLinkAsync` | `POST /api/auth/entra/link` | Link Entra OID to existing local account. |

---

## `IWebApiIdentityClient`

**Namespace:** `Ben.Web.WebApp.Services.WebApi`  
**File:** [`Ben.Web.WebApp/Services/WebApi/IWebApiIdentityClient.cs`](../../../Ben.Web.WebApp/Services/WebApi/IWebApiIdentityClient.cs)

### Summary
Anonymous HTTP client for endpoints that don't require an existing access token.  
Separate from `IWebApiClient` to avoid the `WebApiBearerTokenHandler` dependency cycle.

| Method | Description |
|---|---|
| `LoginAsync(email, password)` | POST `/login` → returns `WebApiTokenResponse`. |
| `RefreshAsync(refreshToken)` | POST `/refresh` → returns new `WebApiTokenResponse`. |
