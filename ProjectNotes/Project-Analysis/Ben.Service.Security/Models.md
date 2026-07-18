# Ben.Service.Security — Models

All model classes are in the `Ben.Service.Security.Models` namespace.  
**File:** [`Ben.Service.Security/Models/OrganizationSecurity.cs`](../../../Ben.Service.Security/Models/OrganizationSecurity.cs)

These are **service-layer value objects** (not EF entities) used internally by `OrganizationSecurityService` and the attribute layer.

---

## `OrganizationUserPermission`

### Summary
Represents the resolved permission a user holds for a specific table and action within an organization.  
Returned by permission query helpers in the security service.

### Properties

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | The target organization. |
| `UserId` | `Guid` | The user whose permission this represents. |
| `Table` | [`OrganizationSecurityTable`](Enums.md#organizationsecuritytable) | The domain table. |
| `Actions` | `OrganizationSecurityAction` | Bitmask of granted CRUD actions. |

### Methods

| Method | Returns | Description |
|---|---|---|
| `HasPermission(OrganizationSecurityAction)` | `bool` | Returns `true` if the given action flag is set in `Actions`. |

---

## `OrganizationAccessGrant` (service model)

> This is a **service-layer model**, distinct from the EF entity `Ben.Data.Source.Entities.OrganizationAccessGrant`.

### Summary
A lightweight representation of an access grant used in the service / attribute layer.  
Carries enough information to evaluate a permission decision without reloading the entity.

### Properties

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | The target organization. |
| `UserId` | `Guid` | User who holds the grant. |
| `Table` | [`OrganizationSecurityTable`](Enums.md#organizationsecuritytable) | The domain table this grant covers. |
| `Actions` | `OrganizationSecurityAction` | Bitmask of permitted CRUD operations. |
| `GrantedAt` | `DateTime` | UTC timestamp when the grant was created. |
| `GrantedByUserId` | `Guid` | The user who issued the grant. |

---

## `OrganizationUserMembership` (service model)

> This is a **service-layer model**, distinct from the EF entity `Ben.Data.Source.Entities.OrganizationUserMembership`.

### Summary
A lightweight representation of an org membership used in the service / attribute layer.

### Properties

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | The target organization. |
| `UserId` | `Guid` | The member user. |
| `Role` | `OrganizationMemberRole` | Owner / Administrator / Manager / Member / Viewer. |
| `JoinedAt` | `DateTime` | UTC timestamp when the membership was created. |
| `RemovalDate` | `DateTime?` | UTC timestamp when membership was deactivated, or `null` if still active. |

---
