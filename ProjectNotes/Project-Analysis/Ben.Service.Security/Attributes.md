# Ben.Service.Security — Attributes

---

## `OrganizationSecurityAuthorizeAttribute`

**Namespace:** `Ben.Service.Security.Attributes`  
**File:** [`Ben.Service.Security/Attributes/OrganizationSecurityAuthorizeAttribute.cs`](../../../Ben.Service.Security/Attributes/OrganizationSecurityAuthorizeAttribute.cs)  
**Implements:** `Attribute`, `IAsyncAuthorizationFilter`  
**Applied to:** `Class` or `Method`

### Summary
Custom MVC authorization filter that enforces organisation-level permission checks.  
Decorates controller classes or action methods to require a user to hold a specific action grant for a specific table within the organisation identified by a named route/query parameter.

### Constructor

```csharp
[OrganizationSecurityAuthorize("organizationId", OrganizationSecurityTable.User, OrganizationSecurityAction.Read)]
```

| Parameter | Type | Description |
|---|---|---|
| `organizationIdParameter` | `string` | The route data key or query string key that holds the organisation `Guid`. Typically `"organizationId"`. |
| `table` | [`OrganizationSecurityTable`](Enums.md#organizationsecuritytable) | The domain table the action targets. |
| `action` | `OrganizationSecurityAction` | The single CRUD flag being checked. |

### Authorization Flow

```
Request arrives with Bearer token
  ↓
Extract userId from NameIdentifier claim
  ↓ (missing/invalid → 401)
Extract organisationId from route data or query string
  ↓ (missing/invalid → 400)
Resolve IOrganizationSecurityService from DI
  ↓
Call HasPermissionAsync(userId, orgId, table, action)
  ↓ (denied → 403)
Proceed to controller action
```

### Resolution Order for `organizationId`
1. Route data (`context.RouteData.Values[organizationIdParameter]`)
2. Query string (`context.HttpContext.Request.Query[organizationIdParameter]`)

### Usage Example

```csharp
[HttpGet("{organizationId:guid}/notes")]
[OrganizationSecurityAuthorize("organizationId", OrganizationSecurityTable.OrganizationNote, OrganizationSecurityAction.Read)]
public async Task<IActionResult> GetNotes(Guid organizationId) { ... }
```

### DI Requirement
`IOrganizationSecurityService` from `Ben.Service.Security.Services` must be registered in the WebApi DI container.  
Both security service variants must be registered — see [DI Registrations in WebApi](../../Ben.Data.WebApi/Summary.md).

---
