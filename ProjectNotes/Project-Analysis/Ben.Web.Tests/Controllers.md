# Ben.Web.Tests — Controller Tests

All tests are in the `Ben.Web.Tests.Controllers` namespace.  
Controllers are unit-tested by directly instantiating them with mocked or in-memory dependencies — no HTTP host or integration test framework is used.

---

## `MeControllerTests`

**File:** [`Ben.Web.Tests/Controllers/MeControllerTests.cs`](../../../Ben.Web.Tests/Controllers/MeControllerTests.cs)  
**Test count:** ~12 tests  
**Subject:** `Ben.Data.WebApi.Controllers.MeController.Get()`

### Coverage

| Test | Scenario |
|---|---|
| `Get_LocalUser_ReturnsLocalUserIdAndEmail` | `GetUserAsync` returns an `AppUser` → UserId, email, `IsSuperAdmin` from role check |
| `Get_LocalSuperAdminUser_ReturnsSuperAdminTrue` | `IsInRoleAsync` → `true` |
| `Get_LocalNonAdminUser_ReturnsSuperAdminFalse` | `IsInRoleAsync` → `false` |
| `Get_EntraUser_WithLinkedAccount_ReturnsLocalUserData` | `FindByLoginAsync("Microsoft", oid)` returns a user → local user's data returned (not Entra email) |
| `Get_EntraUser_NoLocalAccount_ReturnsGuidEmpty` | No OID link + `GetUserAsync` = null → `Guid.Empty` |
| `Get_EntraUser_PreferredUsername_UsedAsEmail` | `preferred_username` claim used as email in `Guid.Empty` response |
| `Get_EntraUser_FallsBackToEmailClaim_WhenNoPreferredUsername` | Falls back to `ClaimTypes.Email` |
| `Get_EntraUser_IsSuperAdminAlwaysFalse` | Unlinked Entra user always gets `IsSuperAdmin = false` |
| `Get_EntraUser_NoOidClaim_UsesGuidEmpty` | No `oid` claim → skips FindByLoginAsync → `Guid.Empty` |
| `Get_EntraUser_NoEmailClaims_ReturnsEmptyString` | No email claims → empty string |
| `Get_EntraUser_GetUserAsyncThrowsFormatException_ReturnsGuidEmpty` | MSA `sub` claim is not a GUID → `FormatException` caught → falls through to `Guid.Empty` (root cause of the original crash) |
| `Get_EntraUser_LinkedAccountAfterFormatExceptionWouldNotBeReached` | `FindByLoginAsync` succeeds before `GetUserAsync` is called → linked account returned even for MSA principals |

---

## `EntraAuthControllerTests`

**File:** [`Ben.Web.Tests/Controllers/EntraAuthControllerTests.cs`](../../../Ben.Web.Tests/Controllers/EntraAuthControllerTests.cs)  
**Test count:** ~15 tests  
**Subject:** `Ben.Data.WebApi.Controllers.EntraAuthController`

### Coverage

**`POST /api/auth/entra/register`** (`Register` action):

| Test | Scenario |
|---|---|
| `Register_NewUser_CreatesAndLinksEntraLogin` | Happy path — `CreateAsync` + `AddLoginAsync` succeed → 200 with `UserId` |
| `Register_DuplicateOid_ReturnsConflict` | `FindByLoginAsync` already returns a user for the OID → 409 |
| `Register_CreateFails_Returns400` | `CreateAsync` fails (e.g. duplicate email) → 400 with identity errors |
| `Register_AddLoginFails_DeletesUser_Returns500` | `AddLoginAsync` fails → rollback via `DeleteAsync` |

**`POST /api/auth/entra/link`** (`Link` action):

| Test | Scenario |
|---|---|
| `Link_AuthenticatedUser_LinksEntraOid` | Authenticated user links OID → `AddLoginAsync` called with `("Microsoft", oid)` |
| `Link_AlreadyLinked_IsIdempotent` | `FindByLoginAsync` already returns the same user → no error, 200 |
| `Link_AlreadyLinkedToDifferentUser_ReturnsConflict` | OID linked to a different user → 409 |
| `Link_Unauthenticated_ReturnsUnauthorized` | No NameIdentifier claim → 401 |
| `Link_AddLoginFails_Returns400` | `AddLoginAsync` fails → 400 |

---

## `UploadFileControllerTests`

**File:** [`Ben.Web.Tests/Controllers/UploadFileControllerTests.cs`](../../../Ben.Web.Tests/Controllers/UploadFileControllerTests.cs)  
**Test count:** 4 tests  
**Subject:** `Ben.Data.WebApi.Controllers.Entities.UploadFileController.Upload`  
**Infrastructure:** Uses `Microsoft.EntityFrameworkCore.InMemory` to seed `UploadFileType` + `UploadFileTypeExtension` rows; `IMapper` is mocked.

### Coverage

| Test | Scenario |
|---|---|
| `Upload_WhenFileTypeNotFound_ReturnsBadRequest` | Unknown `uploadFileTypeId` → 400 containing "not found" |
| `Upload_WhenAllowAllExtensions_AcceptsAnyExtension` | `AllowAllExtensions = true` → any file extension accepted → 201 |
| `Upload_WhenExtensionMatchesPattern_ReturnsCreated` | `.docx` matches `.doc*` wildcard pattern → 201 |
| `Upload_WhenExtensionNotAllowed_ReturnsBadRequest` | `.png` not in `[".txt", ".pdf"]` → 400 containing the rejected extension |

---
