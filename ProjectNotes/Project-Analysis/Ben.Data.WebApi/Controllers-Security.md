# Ben.Data.WebApi — Security & Identity Controllers

---

## `MeController`

**Route:** `GET /api/me`  
**File:** [`Ben.Data.WebApi/Controllers/MeController.cs`](../../../Ben.Data.WebApi/Controllers/MeController.cs)  
**Authorization:** `[Authorize]`

### Summary
Returns the currently authenticated user's identity and role information.  
Supports both local Identity (password) sessions and Microsoft Entra OIDC sessions.  
Used by `Ben.Web.WebApp` immediately after login to resolve `UserId` and `IsSuperAdmin`.

### `GET /api/me` — Three-step resolution

1. **Entra OID lookup:** Extracts `oid` claim from the token. If present and parseable, calls `FindByLoginAsync("Microsoft", oid)`. Returns the linked local user's data.
2. **Local Identity lookup:** Falls back to `UserManager.GetUserAsync(User)` for password-based sessions. Wrapped in `try-catch(FormatException)` because personal Microsoft accounts (MSA) issue tokens whose `sub` claim is a non-GUID string; `UserStoreBase.ConvertIdFromString` throws `FormatException` when parsing it. The catch falls through to step 3.
3. **Unlinked Entra user:** Returns `{ UserId = Guid.Empty }` — the WebApp interprets this as a signal to show the `/entra/complete-profile` page.

### Response: `MeResponse`

```csharp
public record MeResponse(Guid UserId, string Email, bool IsSuperAdmin);
```

---

## `OrganizationSecurityController`

**Route:** `api/organizations/{organizationId:guid}/security`  
**File:** [`Ben.Data.WebApi/Controllers/OrganizationSecurityController.cs`](../../../Ben.Data.WebApi/Controllers/OrganizationSecurityController.cs)  
**Authorization:** `[Authorize]`

### Summary
Manages organisation membership, access grants, and permission checks for a specific organisation.  
Depends on `Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService`.

### Endpoints

| Method | Route (relative) | Description |
|---|---|---|
| `GET` | `my-access?table=&action=` | Checks the calling user's access for a given table/action. |
| `POST` | `check-access` | Checks a specified user's access (caller must be the target or an admin). |
| `GET` | `users` | Returns all membership rows for the organisation. |
| `PUT` | `users/{targetUserId:guid}/membership` | Creates or updates a user's membership and role. |
| `PUT` | `users/{targetUserId:guid}/grants` | Creates or updates an access grant for a table/action. |

### Inner Types

**`UpsertOrganizationMembershipRequest`**  
- `Role` — [`OrganizationMemberRole`](../Ben.Data.Common/Enums.md#organizationmemberrole) (default: `Member`)
- `IsActive` — `bool` (default: `true`)

**`OrganizationUserMembershipResponse`**  
- `MembershipId`, `OrganizationId`, `AppUserId`, `Role`, `IsActive`, `DateCreated`, `DateUpdated`

**`SetOrganizationGrantRequest`**  
- `Table` — [`OrganizationSecurityTable`](../Ben.Data.Common/Enums.md#organizationsecuritytable)
- `Actions` — [`OrganizationSecurityAction`](../Ben.Data.Common/Enums.md#organizationsecurityaction) (bitmask — combine Create/Read/Update/Delete flags)

**`OrganizationAccessGrantResponse`**  
- `GrantId`, `OrganizationId`, `AppUserId`, `Table`, `Actions`, `DateCreated`, `DateUpdated`

---

## `OrganizationMembershipController`

**Route:** `api/security/organizations`  
**File:** [`Ben.Data.WebApi/Controllers/OrganizationMembershipController.cs`](../../../Ben.Data.WebApi/Controllers/OrganizationMembershipController.cs)  
**Authorization:** `[Authorize]`

### Summary
Membership discovery and organisation registration for the calling user.

### Endpoints

| Method | Route (relative) | Description |
|---|---|---|
| `GET` | `users/search?q=&skip=&take=` | Searches users visible to the caller. SuperAdmins see all; others see only users in shared orgs. |
| `GET` | `mine` | Returns all organisations the authenticated user is a member of. |
| `POST` | `register` | Creates a new organisation with the caller as Owner. |

---

## `EntraAuthController`

**Route:** `api/auth/entra`  
**File:** [`Ben.Data.WebApi/Controllers/EntraAuthController.cs`](../../../Ben.Data.WebApi/Controllers/EntraAuthController.cs)

### Summary
Handles Microsoft Entra account registration and linking.

### Endpoints

| Method | Route | Auth | Description |
|---|---|---|---|
| `POST` | `/api/auth/entra/register` | Anonymous | Creates a new `AppUser` from Entra claims and links the Entra OID via `AspNetUserLogins`. |
| `POST` | `/api/auth/entra/link` | Local bearer | Links the Entra OID to the currently authenticated local user account. |

**`EntraRegisterRequest`**: `EntraOid`, `EntraEmail`, `DisplayName`  
**`EntraLinkRequest`**: `EntraOid`

---

## `ImpersonateController`

**Route:** `api/admin/impersonate`  
**File:** [`Ben.Data.WebApi/Controllers/Admin/ImpersonateController.cs`](../../../Ben.Data.WebApi/Controllers/Admin/ImpersonateController.cs)  
**Authorization:** `[Authorize(Roles = RoleNames.SuperAdmin)]`

### Summary
Issues a bearer token for a target user so the SuperAdmin can act on their behalf.

### Endpoints

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/admin/impersonate/{id:guid}` | Calls `SignInManager.SignInAsync` for the target user and issues an `IdentityConstants.BearerScheme` token. The calling client replaces its current token with the impersonation token. |
