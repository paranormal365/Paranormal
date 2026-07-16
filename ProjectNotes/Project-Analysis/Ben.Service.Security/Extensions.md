# Ben.Service.Security — Extensions

---

## `SecurityExtensions`

**Namespace:** `Ben.Service.Security.Extensions`  
**File:** [`Ben.Service.Security/Extensions/SecurityExtensions.cs`](../../../Ben.Service.Security/Extensions/SecurityExtensions.cs)  
**Type:** `public static class`

### Summary
Extension methods for the security model classes.  
Provides fluent helpers for common permission checks on the service-layer models.

### Methods

| Method | Signature | Description |
|---|---|---|
| `HasPermission` | `this OrganizationAccessGrant grant, OrganizationSecurityAction action → bool` | Returns `true` if the given action flag is set in `grant.Actions`. Equivalent to `(grant.Actions & action) == action`. |

### Usage Example

```csharp
// Instead of: (grant.Actions & OrganizationSecurityAction.Read) == OrganizationSecurityAction.Read
if (grant.HasPermission(OrganizationSecurityAction.Read))
{
    // user can read
}
```

---
