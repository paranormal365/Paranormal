# Ben.Web.Tests — Service Tests

All tests are in the `Ben.Web.Tests.Services` namespace.  
Uses Moq to mock HTTP client interfaces; no real HTTP calls or database access.

---

## `JwtClaimsParserTests`

**File:** [`Ben.Web.Tests/Services/JwtClaimsParserTests.cs`](../../../Ben.Web.Tests/Services/JwtClaimsParserTests.cs)  
**Test count:** ~15 tests  
**Subject:** `Ben.Web.WebApp.Services.WebApi.JwtClaimsParser.ParseClaims`

### Coverage

| Test Group | Scenarios |
|---|---|
| `UserId` extraction | Parses `sub` claim as `Guid`; returns `null` for missing or malformed `sub` |
| `IsSuperAdmin` extraction | `"SuperAdmin"` string role → `true`; array `["SuperAdmin", "Member"]` → `true`; other roles → `false`; missing role → `false` |
| Base64URL decoding | Handles `-` and `_` characters in Base64URL payload (URL-safe chars converted before decoding) |
| Edge cases | Null/empty token, token with only 2 segments (missing signature), malformed JSON payload |

> **Note:** `JwtClaimsParser` is used for diagnostic purposes only. The live Identity API issues opaque data-protected tokens — `IsSuperAdmin` is always resolved via `GET /api/me` after login, not from the token payload.

---

## `WebApiTokenStoreTests`

**File:** [`Ben.Web.Tests/Services/WebApiTokenStoreTests.cs`](../../../Ben.Web.Tests/Services/WebApiTokenStoreTests.cs)  
**Test count:** ~10 tests  
**Subject:** `Ben.Web.WebApp.Services.WebApi.WebApiTokenStore`

### Coverage

| Test | Scenario |
|---|---|
| `IsAuthenticated_WhenAccessTokenSet_ReturnsTrue` | Non-empty `AccessToken` → `IsAuthenticated = true` |
| `IsAuthenticated_WhenAccessTokenNull_ReturnsFalse` | `null` token → `false` |
| `IsAuthenticated_WhenAccessTokenEmpty_ReturnsFalse` | `""` token → `false` |
| `NewStore_AllAuthFieldsAreNull` | Fresh store defaults: `AccessToken`, `RefreshToken`, `UserEmail`, `UserId`, `AccessTokenExpiresAtUtc` all null |
| `NewStore_IsSuperAdmin_DefaultsFalse` | `IsSuperAdmin` defaults `false` |
| `NewStore_IsImpersonating_DefaultsFalse` | `IsImpersonating` defaults `false` |
| `NewStore_OriginalImpersonationFields_AllNull` | `OriginalAccessToken`, `OriginalRefreshToken`, `OriginalUserId` all null by default |

---

## `WebApiAuthServiceTests`

**File:** [`Ben.Web.Tests/Services/WebApiAuthServiceTests.cs`](../../../Ben.Web.Tests/Services/WebApiAuthServiceTests.cs)  
**Test count:** ~25 tests  
**Subject:** `Ben.Web.WebApp.Services.WebApi.WebApiAuthService`

### Coverage

| Area | Scenarios |
|---|---|
| `LoginAsync` | Sets `AccessToken`, `RefreshToken`; calls `GET /api/me` to set `UserId` and `IsSuperAdmin`; fires `StateChanged`; failed login returns `false`, store unchanged |
| `Logout` | Clears all token fields; `IsAuthenticated` → `false`; fires `StateChanged` |
| `ImpersonateAsync` | Saves original tokens; applies new token; sets `IsImpersonating = true`; fires `StateChanged` |
| `StopImpersonating` | Restores original tokens; `IsImpersonating = false`; fires `StateChanged` |
| `RefreshIfNeededAsync` | No-op if token not expired; calls `/refresh` if `AccessTokenExpiresAtUtc` is in the past; applies new token on success |
| Failure paths | Refresh failure doesn't clear state; impersonation `GET /api/me` failure returns false |

---

## `EntraTests`

**File:** [`Ben.Web.Tests/Services/EntraTests.cs`](../../../Ben.Web.Tests/Services/EntraTests.cs)  
**Test count:** ~10 tests  
**Subject:** Entra OIDC integration (session flag, `WebApiAuthService` `/api/me` override)

### Coverage

| Test | Scenario |
|---|---|
| `LoginAsync_SetsIsEntraSession_False_ForLocalLogin` | Standard local login does not set `IsEntraSession` |
| `IsEntraSession_CanBeSetDirectly` | `WebApiTokenStore.IsEntraSession = true` persists |
| `LoginAsync_MeApiOverridesJwtParsedUserId` | `/api/me` response `UserId` overrides what was in the JWT `sub` claim |
| `LoginAsync_MeApiSetsIsSuperAdmin` | `IsSuperAdmin` comes from `/api/me`, not from JWT `role` claim |
| `StateChanged_FiresOnLogin` | `StateChanged` event fires exactly once after a successful login |
| `StateChanged_FiresOnLogout` | `StateChanged` event fires on `Logout()` |

---

## `WebApiClientTests`

**File:** [`Ben.Web.Tests/Services/WebApiClientTests.cs`](../../../Ben.Web.Tests/Services/WebApiClientTests.cs)  
**Test count:** 14 tests  
**Subject:** `Ben.Web.WebApp.Services.WebApi.WebApiClient`

**Infrastructure:** Uses a custom `CapturingHandler : HttpMessageHandler` that records the last `HttpRequestMessage` and returns a configurable `HttpResponseMessage`. No real HTTP calls.

### Coverage

| Test | Scenario |
|---|---|
| `GetAsync_WhenTokenSet_SendsBearerAuthorizationHeader` | Token in store → `Authorization: Bearer <token>` header on GET |
| `GetAsync_WhenNoToken_SendsNoAuthorizationHeader` | Null token → no auth header |
| `GetAsync_TokenSetAfterConstruction_SendsNewToken` | Token set after `WebApiClient` is constructed → request uses the new token (proves request-time read, not construction-time) |
| `GetAsync_TokenClearedAfterConstruction_SendsNoHeader` | Token cleared (logout) → no header on subsequent request |
| `PostAsync_WhenTokenSet_SendsBearerAuthorizationHeader` | Bearer token on POST |
| `PutAsync_WhenTokenSet_SendsBearerAuthorizationHeader` | Bearer token on PUT |
| `DeleteAsync_WhenTokenSet_SendsBearerAuthorizationHeader` | Bearer token on DELETE |
| `GetAsync_UsesGetHttpMethod` | Verifies `HttpMethod.Get` |
| `PostAsync_UsesPostHttpMethod` | Verifies `HttpMethod.Post` |
| `PutAsync_UsesPutHttpMethod` | Verifies `HttpMethod.Put` |
| `DeleteAsync_UsesDeleteHttpMethod` | Verifies `HttpMethod.Delete` |
| `GetAsync_On401_ReturnsDefault` | 401 → returns `null` |
| `PostAsync_On400_ReturnsDefault` | 400 → returns `null` |
| `DeleteAsync_On404_ReturnsFalse` | 404 → returns `false` |

> **Background:** `WebApiBearerTokenHandler` was removed from the `IWebApiClient` pipeline because `IHttpClientFactory` resolves `DelegatingHandler` instances from the root DI scope, giving each handler an empty, unrelated `IWebApiTokenStore`. Auth header injection was moved into `WebApiClient` directly — which IS resolved from the circuit scope as a typed transient — so `_tokenStore` is always the correct instance.

---


## `CmsFileLibraryTests` *(added 2026-07-18)*

**File:** [`Ben.Web.Tests/Services/CmsFileLibraryTests.cs`](../../../Ben.Web.Tests/Services/CmsFileLibraryTests.cs)  
**Test count:** 7 tests  
**Subject:** `Ben.Web.WebApp.Services.WebApi.BenAdminClientAdapter` — CMS file library methods  
**Infrastructure:** `IWebApiClient` mocked with Moq; no HTTP calls.

### Coverage

| Test | Scenario |
|---|---|
| `GetOrgSharedFilesAsync_DelegatesToApiAndReturnsFiles` | Happy path — delegates to `IWebApiClient.GetOrgSharedFilesAsync` and returns the file list |
| `GetOrgSharedFilesAsync_WhenApiReturnsNull_ReturnsEmpty` | Null from API → empty list (never throws) |
| `GetFileDataAsync_ReturnsBytesAndContentType` | `DownloadFileAsync` succeeds → bytes + ContentType returned |
| `GetFileDataAsync_WhenApiReturnsNull_ReturnsNull` | `DownloadFileAsync` returns null → null propagated |
| `GetPublicFileTypesAsync_DelegatesToApi` | Delegates to `GetUploadFileTypesAsync` |
| `UploadImageAsync_PassesCorrectFormFieldsToApi` | Verifies multipart form fields: `uploadFileTypeId`, `appUserId`, `isPublic=true`, correct `file` part with file name |
| `UploadImageAsync_WhenApiReturnsNull_ReturnsNull` | API failure → null propagated |
