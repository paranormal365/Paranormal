# Ben.Data.WebApi — Controller Base Classes

---

## `AdminEntityControllerBase<TEntity, TRecord>`

**Namespace:** `Ben.Data.WebApi.Controllers`  
**File:** [`Ben.Data.WebApi/Controllers/AdminEntityControllerBase.cs`](../../../Ben.Data.WebApi/Controllers/AdminEntityControllerBase.cs)  
**Authorization:** `[Authorize(Roles = RoleNames.SuperAdmin)]`

### Summary
Abstract generic base for all SuperAdmin CRUD controllers.  
Provides GET (all + by ID), POST (create), PUT (update), and DELETE endpoints.  
Integrates with [`IAuditLogService`](../Ben.Service.RepositoryService/Interfaces-Generic.md#iauditlogservice) — every mutating operation writes an audit entry.

### Constructor Dependencies

| Parameter | Type |
|---|---|
| `dbContextFactory` | `IDbContextFactory<BenDataContext>` |
| `mapper` | `IMapper` |
| `auditLog` | `IAuditLogService` |

### Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `[base]` | Returns all entities mapped to `TRecord`. |
| `GET` | `[base]/{id:guid}` | Returns a single entity by PK, or 404. |
| `POST` | `[base]` | Creates entity; auto-assigns `Id` if empty. Logs Create audit. Returns 201. |
| `PUT` | `[base]/{id:guid}` | Loads before-state, applies update, logs Update audit (diff). Returns 200. |
| `DELETE` | `[base]/{id:guid}` | Logs Delete audit (snapshot), then removes entity. Returns 204. |

### Audit Integration

- **Create:** After save, calls `LogCreateAsync` with the saved entity.
- **Update:** Loads the entity with `AsNoTracking()` **before** applying the update to capture the `before` state. Calls `LogUpdateAsync(before, after)`.
- **Delete:** Calls `LogDeleteAsync` with the entity **before** removal.
- All audit calls are wrapped in `TryAuditAsync` which **silently swallows exceptions** — audit failures never surface to the caller.

### Private Helpers

| Method | Description |
|---|---|
| `GetCurrentUserId()` | Parses the user ID from `ClaimTypes.NameIdentifier` or `"sub"` claim. Returns `Guid.Empty` if unavailable. |
| `TryAuditAsync(Task)` | Awaits audit task; catches and discards all exceptions. |
| `GetEntityId(TEntity)` | Reads the `Id` property via reflection. |
| `EnsureEntityId(TEntity)` | Assigns `Guid.NewGuid()` to `Id` if it is `Guid.Empty`. |
| `SetEntityId(TEntity, Guid)` | Writes the `Id` property via reflection. |

---

## `EntityReadControllerBase<TEntity, TRecord>`

**Namespace:** `Ben.Data.WebApi.Controllers`  
**File:** [`Ben.Data.WebApi/Controllers/EntityReadControllerBase.cs`](../../../Ben.Data.WebApi/Controllers/EntityReadControllerBase.cs)  
**Authorization:** `[Authorize]` (any authenticated user)

### Summary
Abstract generic base for read-only (GET) endpoints available to all authenticated users.  
No write operations, no audit logging.

### Constructor Dependencies

| Parameter | Type |
|---|---|
| `dbContextFactory` | `IDbContextFactory<BenDataContext>` |
| `mapper` | `IMapper` |

### Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `[base]` | Returns all entities mapped to `TRecord`. |
| `GET` | `[base]/{id:guid}` | Returns a single entity by PK, or 404. |
