# Ben.Data.Source — Audit Entity

All entities below are in the `Ben.Data.Source.Entities` namespace.  
Each follows the two-file partial class pattern described in [Summary.md](Summary.md).

---

## `AuditLog`

**Files:** [`BenDataModel.AuditLog.cs`](../../../Ben.Data.Source/Entities/BenDataModel.AuditLog.cs) · [`BenDataModel.AuditLog.Generated.cs`](../../../Ben.Data.Source/Entities/BenDataModel.AuditLog.Generated.cs)  
**Implements:** [`IIDStd`](../Ben.Data.Common/Interfaces.md#iidstd)  
**Table:** `AuditLogs`

### Summary
Immutable record of a CRUD action performed by a user on any entity.  
Intentionally has **no FK to `AppUser`** — audit records survive user deletion.  
Written by [`AuditLogService`](../Ben.Service.RepositoryService/Services.md#auditlogservice) via `IAuditLogService.LogCreateAsync`, `LogUpdateAsync`, and `LogDeleteAsync`.

### Properties

| Property | Type | Nullable | Description |
|---|---|---|---|
| `Id` | `Guid` | No | PK — generated on insert. |
| `UserId` | `Guid` | No | ID of the user who performed the action. No FK — intentionally decoupled from `AppUsers`. |
| `Action` | [`AuditAction`](../Ben.Data.Common/Enums.md#auditaction) | No | The CRUD operation performed (`Create`, `Update`, `Delete`). |
| `EntityType` | `string` | No | Display name of the entity type, e.g. `"Organization"`. Typically `typeof(TEntity).Name`. |
| `EntityId` | `Guid` | No | Primary key of the entity that was created, updated, or deleted. |
| `Source` | `string` | No | Application that originated the action. See [`AppSources`](../Ben.Data.Common/Constants.md#appsources). |
| `OccurredAt` | `DateTime` | No | UTC timestamp of the action. |
| `ChangesJson` | `string?` | Yes | JSON payload. For **Create** — full scalar snapshot. For **Update** — only changed properties (`{ "Field": { "Before": x, "After": y } }`). For **Delete** — full scalar snapshot at time of deletion. |

### Database Indexes

| Index | Columns | Unique |
|---|---|---|
| `IX_AuditLogs_EntityType_EntityId` | `EntityType`, `EntityId` | No |
| `IX_AuditLogs_UserId` | `UserId` | No |
| `IX_AuditLogs_OccurredAt` | `OccurredAt` | No |

### Notes

- No FK to `AppUsers` — this is by design so audit history is preserved if a user is deleted.
- No FK to any target entity either — records are self-contained and survive entity deletion.
- `ChangesJson` is built by [`AuditChangeTracker`](../Ben.Data.Common/Helpers.md#auditchangetracker) which serialises scalar properties and excludes navigation properties.
- Audit write failures are silently swallowed in `AdminEntityControllerBase` — an audit log failure never rolls back a successful CRUD operation.

### Relationships

`AuditLog` has no outbound navigation properties. It is a write-only append-only table queried via direct SQL or `BenDataContext.AuditLogs`.

---
