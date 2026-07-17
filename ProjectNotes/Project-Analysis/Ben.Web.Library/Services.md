# Ben.Web.Library — Services

## `IBenUserState`

**Namespace:** `Ben.Web.Library.Services`  
**File:** [`Ben.Web.Library/Services/IBenUserState.cs`](../../../Ben.Web.Library/Services/IBenUserState.cs)  
**Implemented by:** `WebApiTokenStore` in `Ben.Web.WebApp`

### Summary
Exposes the minimum authentication state needed by shared Blazor library components.  
Library components depend on this interface rather than the full `IWebApiTokenStore` so that `Ben.Web.Library` does not need a project reference to `Ben.Web.WebApp`.

### Properties

| Property | Type | Description |
|---|---|---|
| `IsAuthenticated` | `bool` | `true` when the user has a valid access token. |
| `IsSuperAdmin` | `bool` | `true` when the user holds the [`RoleNames.SuperAdmin`](../Ben.Data.Common/Constants.md#rolenames) role. |
| `IsImpersonating` | `bool` | `true` when the current session is a SuperAdmin impersonation session. |
| `UserEmail` | `string?` | Authenticated user's email, or `null`. |
| `UserId` | `Guid?` | Authenticated user's primary key, or `null`. |

---

## `IBenAdminClient`

**Namespace:** `Ben.Web.Library.Services`  
**File:** [`Ben.Web.Library/Services/IBenAdminClient.cs`](../../../Ben.Web.Library/Services/IBenAdminClient.cs)  
**Implemented by:** `BenAdminClientAdapter` in `Ben.Web.WebApp`

### Summary
Defines all SuperAdmin HTTP operations available to library Blazor components.  
`BenAdminClientAdapter` delegates each call to the typed `IWebApiClient` HTTP client in the host project.

### Organization Methods

| Method | Returns | Description |
|---|---|---|
| `GetOrganizationsAsync(token)` | `IReadOnlyList<OrganizationListItemResponse>` | Orgs visible to current user with per-org `CanEdit`/`CanDelete` flags. |
| `GetOrganizationAsync(id, token)` | `OrganizationAdminRecord?` | Single org for edit form pre-fill. |
| `CreateOrganizationAsync(request, token)` | `OrganizationAdminRecord?` | Creates org (SuperAdmin only). |
| `UpdateOrganizationAsync(id, request, token)` | `OrganizationAdminRecord?` | Updates Name and UrlName. |
| `DeleteOrganizationAsync(id, token)` | `bool` | Deletes org. |

### Role Methods

| Method | Returns | Description |
|---|---|---|
| `GetRolesAsync(token)` | `IReadOnlyList<AdminRoleWithCountResponse>` | All site roles with user counts. |
| `CreateRoleAsync(roleName, token)` | `AppRoleAdminRecord?` | Creates a new role. |
| `DeleteRoleAsync(roleId, token)` | `bool` | Deletes role (server refuses if users assigned). |

### User Methods

| Method | Returns | Description |
|---|---|---|
| `GetAllUsersAsync(token)` | `IReadOnlyList<AppUserRecord>` | Lightweight list of all users. |
| `GetUserDetailAsync(userId, token)` | `AppUserDetailAdminRecord?` | Full aggregate: profile + 8 related lists. |
| `UpdateUserProfileAsync(userId, request, token)` | `AppUserAdminRecord?` | Updates editable profile fields. |

### Impersonation Methods

| Method | Returns | Description |
|---|---|---|
| `ImpersonateUserAsync(targetUserId, targetUserEmail, token)` | `bool` | Starts impersonation session; saves current token, applies target user token. |
| `StopImpersonating()` | `void` | Synchronous in-memory operation — restores original SuperAdmin token. |

### File Type Methods

| Method | Returns | Description |
|---|---|---|
| `GetFileTypesWithExtensionsAsync(token)` | `IReadOnlyList<AdminFileTypeWithExtensionsResponse>` | All file types + their extension patterns. |
| `CreateFileTypeAsync(request, token)` | `UploadFileTypeRecord?` | Creates a new file type. |
| `UpdateFileTypeAsync(id, request, token)` | `UploadFileTypeRecord?` | Updates existing file type. |
| `DeleteFileTypeAsync(id, token)` | `bool` | Deletes file type (cascades extensions). |

### File Type Extension Methods

| Method | Returns | Description |
|---|---|---|
| `CreateFileTypeExtensionAsync(request, token)` | `UploadFileTypeExtensionRecord?` | Adds an extension pattern. |
| `UpdateFileTypeExtensionAsync(id, pattern, token)` | `UploadFileTypeExtensionRecord?` | Replaces the pattern string. |
| `DeleteFileTypeExtensionAsync(id, token)` | `bool` | Removes a pattern. |

### Request/Response Records

| Type | Description |
|---|---|
| `AdminCreateOrganizationRequest(Name, UrlName)` | Create org payload. |
| `AdminUpdateOrganizationRequest(Name, UrlName)` | Update org payload. |
| `OrganizationListItemResponse(Id, Name, UrlName, DateCreated, CanEdit, CanDelete)` | Org list row with permission flags. |
| `AdminRoleWithCountResponse(Role, UserCount)` | Role + user count. |
| `AdminCreateUserRequest` | New user payload: Email, Password, DisplayName, UserName, IsEmailConfirmed, IsSuperAdmin. |
| `AdminCreateRoleRequest` | New role payload. |
| `AdminFileTypeWithExtensionsResponse(FileType, Extensions)` | Combined file type + patterns response. |
| `AdminCreateFileTypeRequest` | Create payload including display metadata and `AllowAllExtensions` flag. |
| `AdminUpdateFileTypeRequest` | Update payload. |
| `AdminCreateFileTypeExtensionRequest(UploadFileTypeId, Pattern, CreatedByAppUserId)` | Extension create payload. Pattern format: `.txt` or `.doc*`. |
| `AdminUpdateUserProfileRequest` | All editable profile fields including audit timestamps (SuperAdmin editable). |
