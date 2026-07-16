# Ben.Service.Security — Interfaces, Services, Enums & Models

---

## `IOrganizationSecurityService`

**Namespace:** `Ben.Service.Security.Services`  
**File:** [`Ben.Service.Security/Services/IOrganizationSecurityService.cs`](../../../Ben.Service.Security/Services/IOrganizationSecurityService.cs)  
**See also:** [RepositoryService version](../Ben.Service.RepositoryService/Interfaces-Generic.md#iorganizationsecurityservice)

### Summary
The **policy-enforcement contract** — used by `OrganizationSecurityAuthorizeAttribute` and the middleware layer to gate access to organisation-scoped resources.

### Methods

| Method | Returns | Description |
|---|---|---|
| `HasPermissionAsync(userId, organizationId, table, action, ct)` | `bool` | `true` if the user holds an explicit grant for the table/action. Owners always return `true`. |
| `IsMemberAsync(userId, organizationId, ct)` | `bool` | `true` if an active membership row exists. |
| `GetUserOrganizationsAsync(userId, ct)` | `IReadOnlyList<Guid>` | IDs of all organisations the user is an active member of. |
| `GetUserRoleAsync(userId, organizationId, ct)` | `OrganizationMemberRole?` | User's role or `null` if not a member. |
| `IsOwnerAsync(userId, organizationId, ct)` | `bool` | `true` if the user's role is `Owner`. |
| `GetOrganizationMembersAsync(organizationId, ct)` | `IReadOnlyList<(Guid UserId, OrganizationMemberRole Role)>` | All active members and their roles. |
| `GrantAccessAsync(organizationId, userId, table, actions, grantedBy, ct)` | `Task` | ORs `actions` into the existing grant row (or creates one). Additive — does not remove previously granted flags. |
| `RevokeAccessAsync(organizationId, userId, table, ct)` | `Task` | Deletes the entire grant row for the given (user, org, table) tuple. |
| `AddMemberAsync(organizationId, userId, role, ct)` | `Task` | Creates or reactivates a membership with the specified role. |
| `RemoveMemberAsync(organizationId, userId, ct)` | `Task` | Soft-deletes by setting `IsActive = false`. Row retained for audit. |

---

## `OrganizationSecurityService` (Ben.Service.Security)

**Namespace:** `Ben.Service.Security.Services`  
**File:** [`Ben.Service.Security/Services/OrganizationSecurityService.cs`](../../../Ben.Service.Security/Services/OrganizationSecurityService.cs)  
**Implements:** `IOrganizationSecurityService` (this namespace)

The concrete security service. See also the **same class** in [`Ben.Service.RepositoryService`](../Ben.Service.RepositoryService/Services.md#organizationsecurityservice) — both interfaces are implemented by `Ben.Service.RepositoryService.Services.OrganizationSecurityService`.

Key implementation: `GetUserRoleAsync` returns `membership.Role` directly from the `OrganizationUserMembership` entity.

---

## Enums

### `OrganizationSecurityAction` (Flags)

**Namespace:** `Ben.Service.Security.Enums`  
**File:** [`Ben.Service.Security/Enums/OrganizationSecurityAction.cs`](../../../Ben.Service.Security/Enums/OrganizationSecurityAction.cs)

Used at the **permission-evaluation layer** — combinable with bitwise OR. Distinct from the plain (non-flags) [`OrganizationSecurityAction`](../Ben.Data.Common/Enums.md#organizationsecurityaction) in `Ben.Data.Common`.

| Value | Int |
|---|---|
| `None` | 0 |
| `Create` | 1 |
| `Read` | 2 |
| `Update` | 4 |
| `Delete` | 8 |
| `All` | 15 |

### `OrganizationSecurityTable`

**Namespace:** `Ben.Service.Security.Enums`  
**File:** [`Ben.Service.Security/Enums/OrganizationSecurityTable.cs`](../../../Ben.Service.Security/Enums/OrganizationSecurityTable.cs)

25-value enum used at the permission-evaluation layer. Integer values differ from the Data.Common version — a cast mapping is applied in `OrganizationSecurityService`.

---

## Models

**Namespace:** `Ben.Service.Security.Models`  
**File:** [`Ben.Service.Security/Models/OrganizationSecurity.cs`](../../../Ben.Service.Security/Models/OrganizationSecurity.cs)

### `OrganizationUserPermission`

In-memory model for a user's evaluated permissions on a table.

| Property | Type |
|---|---|
| `OrganizationId` | `Guid` |
| `UserId` | `Guid` |
| `Table` | `OrganizationSecurityTable` |
| `Actions` | `OrganizationSecurityAction` (flags) |

**Method:** `HasPermission(OrganizationSecurityAction action)` — returns `true` if the flags include the requested action.

### `OrganizationAccessGrant` *(model, not entity)*

In-memory grant model (distinct from `Ben.Data.Source.Entities.OrganizationAccessGrant`).

| Property | Type |
|---|---|
| `OrganizationId` | `Guid` |
| `UserId` | `Guid` |
| `Table` | `OrganizationSecurityTable` |
| `Actions` | `OrganizationSecurityAction` |
| `GrantedAt` | `DateTime` |
| `GrantedByUserId` | `Guid` |

### `OrganizationUserMembership` *(model, not entity)*

In-memory membership model used by the security service layer.

| Property | Type |
|---|---|
| `OrganizationId` | `Guid` |
| `UserId` | `Guid` |
| `Role` | `OrganizationMemberRole` |
| `JoinedAt` | `DateTime` |
| `RemovalDate` | `DateTime?` |

---

## `SecurityExtensions`

**File:** [`Ben.Service.Security/Extensions/SecurityExtensions.cs`](../../../Ben.Service.Security/Extensions/SecurityExtensions.cs)

### `HasPermission(this OrganizationAccessGrant grant, OrganizationSecurityAction action)`

Extension method that checks whether a grant's `Actions` flags include the specified action:
```csharp
return (grant.Actions & action) == action;
```
