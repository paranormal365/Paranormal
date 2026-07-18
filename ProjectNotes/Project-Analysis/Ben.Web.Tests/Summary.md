# Ben.Web.Tests — Summary

**Type:** xUnit Test Project  
**Test count:** 100 tests (all passing)

## Purpose

Unit tests for the `Ben.Web.WebApp` service layer and `Ben.Data.WebApi` controllers.  
Uses Moq for mocking HTTP client dependencies and `Microsoft.EntityFrameworkCore.InMemory` for controller tests; no real HTTP calls or database access.

## Test Files

| File | Tests | Coverage |
|---|---|---|
| [`Services/JwtClaimsParserTests.cs`](Services.md#jwtclaimsparsertests) | ~15 | `JwtClaimsParser.ParseClaims` — sub/role parsing, base64url decoding (fixes for `-`/`_` chars), array role claim, edge cases |
| [`Services/WebApiTokenStoreTests.cs`](Services.md#webapitokenstoretests) | ~10 | `WebApiTokenStore` — `IsAuthenticated`, defaults, impersonation field storage, `IsEntraSession` |
| [`Services/WebApiAuthServiceTests.cs`](Services.md#webapiAuthservicetests) | ~25 | `WebApiAuthService.LoginAsync`, `Logout`, `ImpersonateAsync`, `StopImpersonating`, `RefreshIfNeededAsync` — happy paths and failure cases |
| [`Services/EntraTests.cs`](Services.md#entratests) | ~10 | Entra session flag behaviour, `LoginAsync` resetting `IsEntraSession`, token holder properties |
| [`Services/WebApiClientTests.cs`](Services.md#webapiclienttests) | 14 | `WebApiClient` auth header injection — bearer token sent/not sent, request-time token read, HTTP verb correctness, non-2xx handling |
| [`Controllers/MeControllerTests.cs`](Controllers.md#mecontrollertests) | ~12 | `MeController.Get` — OID-first lookup, local user fallback, unlinked Entra user path, FormatException catch for MSA sub claim |
| [`Controllers/EntraAuthControllerTests.cs`](Controllers.md#entraauthcontrollertests) | ~15 | `EntraAuthController.Register` and `Link` — happy paths, duplicate OID, conflict, rollback, idempotent re-link |
| [`Controllers/UploadFileControllerTests.cs`](Controllers.md#uploadfilecontrollertests) | 4 | `UploadFileController.Upload` — extension pattern validation (AllowAll, match, no-match, type-not-found) |

## Testing Notes

- **Synthetic JWTs:** `JwtClaimsParser` tests create hand-crafted JWT strings. In production, the Identity API issues opaque data-protected tokens — role is resolved via `GET /api/me`.
- **`MoqSetup` pattern:** Most service tests mock `IWebApiIdentityClient` to return test `WebApiTokenResponse` objects and `IWebApiClient` for `MeResponse`.

---

## `SuperAdminHandlerTests.cs` *(added 2026-07-18)*

**Namespace:** `Ben.Web.Tests.Controllers`  
**Class:** `SuperAdminHandlerTests`  
**Tests:** 8

| Test | Verifies |
|---|---|
| `LocalToken_WithSuperAdminRoleClaim_Succeeds_WithoutDbLookup` | Local Identity JWT with `ClaimTypes.Role = "SuperAdmin"` → `context.HasSucceeded`, **no** DB lookup |
| `LocalToken_WithoutSuperAdminRoleClaim_DoesNotSucceedViaClaim` | Local JWT without role claim + no DB match → Fails |
| `EntraToken_AppUserIdClaim_SuperAdminUser_Succeeds` | `app_user_id` claim → `FindByIdAsync` → SuperAdmin → Succeeds |
| `EntraToken_AppUserIdClaim_NonSuperAdminUser_Fails` | Same, but user not in SuperAdmin role → Fails |
| `EntraToken_OidClaim_SuperAdminUser_Succeeds` | OID-only token → `FindByLoginAsync` → SuperAdmin → Succeeds |
| `EntraToken_OidNotLinked_Fails` | OID not in `AspNetUserLogins` → Fails |
| `EntraToken_OidFound_ButNotSuperAdmin_Fails` | OID found, not SuperAdmin → Fails |
| `AnonymousPrincipal_Fails` | Empty `ClaimsIdentity` → Fails |

## Total Tests (as at 2026-07-18 EOD)

| Project | Count |
|---|---|
| `Ben.Service.RepositoryService.Tests` | 163 |
| `Ben.Web.Tests` | 214 |
| **Total** | **377** |
