# Ben.Service.Security — Services

---

## `OrganizationSecurityService`

**Namespace:** `Ben.Service.Security.Services`  
**File:** [`Ben.Service.Security/Services/OrganizationSecurityService.cs`](../../../Ben.Service.Security/Services/OrganizationSecurityService.cs)  
**Implements:** [`IOrganizationSecurityService`](Interfaces.md)  
**Registered as:** `Scoped`

### Summary
The concrete implementation of the **security-layer** `IOrganizationSecurityService`.  
Used by the `OrganizationSecurityAuthorizeAttribute` to evaluate permission decisions during request authorization.

> **Important:** `Ben.Service.RepositoryService.Services.OrganizationSecurityService` is the **same concrete class** but registered against a second interface (`Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService`). Both DI registrations are required in the WebApi `Program.cs`.

### Constructor

```csharp
public OrganizationSecurityService(IDbContextFactory<BenDataContext> dbContextFactory)
```

Creates a new context per operation using `IDbContextFactory` — safe for concurrent requests.

### Method Implementations

| Method | Behaviour |
|---|---|
| `HasPermissionAsync(userId, orgId, table, action)` | 1. Short-circuits `true` for owners. 2. Casts `SecurityTable → DataCommonTable` and queries `OrganizationAccessGrants` using a bitwise flag check: `(g.Actions & action) != None`. |
| `IsMemberAsync(userId, orgId)` | Finds any `OrganizationUserMembership` row (active or inactive) for the user. |
| `GetUserOrganizationsAsync(userId)` | Returns all `OrganizationId` values for **active** memberships. |
| `GetUserRoleAsync(userId, orgId)` | Returns `membership.Role` for an active membership, or `null` if not a member. |
| `IsOwnerAsync(userId, orgId)` | Delegates to `GetUserRoleAsync` and compares to `OrganizationMemberRole.Owner`. |
| `GetOrganizationMembersAsync(orgId)` | Returns `(UserId, Role)` tuples for all **active** members of the org. |
| `GrantAccessAsync(orgId, userId, table, actions, grantedBy)` | Upserts the grant row. **OR**s new flags into `Actions` on an existing row; creates a new row on first grant. |
| `RevokeAccessAsync(orgId, userId, table)` | Deletes the entire grant row for the (user, org, table) tuple. Removes all actions. |
| `AddMemberAsync(orgId, userId, role)` | Creates membership if absent; sets `Role` and `IsActive = true` on existing row. |
| `RemoveMemberAsync(orgId, userId)` | Soft-deletes by setting `IsActive = false`. Row retained for audit. |

### Table Cast

`OrganizationSecurityTable` (Security.Enums, service layer) and `OrganizationSecurityTable` (Data.Common.Enums, entity layer) have **different integer values**. The service casts before querying:

```csharp
var dataTable = (DataCommonTable)table;
```

This means the integer value of `SecurityTable.X` is stored and queried as-is — matching is consistent within the service as long as both sides use the same cast.

---
