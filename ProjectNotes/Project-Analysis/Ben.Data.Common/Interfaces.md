# Ben.Data.Common — Interfaces

## `IIDStd`

**Namespace:** `Ben.Data.Common.Interfaces`  
**File:** [`Ben.Data.Common/Interfaces/IIDStd.cs`](../../../Ben.Data.Common/Interfaces/IIDStd.cs)  
**Type:** Interface

### Summary

Base identity contract satisfied by every entity in the Ben data model.  
Provides the single `Guid` primary-key property that EF Core maps as a clustered index.

### Properties

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Unique primary-key value. Assigned via `Guid.NewGuid()` before insert. |

### Usage

- All 31 `Ben.Data.Source` entity classes implement this interface.
- Generic repository methods (`GetByIdAsync`, `Delete`) accept `Guid id` aligned to this contract.
- Entities with full audit columns implement [`IAuditableEntity`](#iauditableentity) instead, which extends this.

---

## `IAuditableEntity`

**Namespace:** `Ben.Data.Common.Interfaces`  
**File:** [`Ben.Data.Common/Interfaces/IAuditableEntity.cs`](../../../Ben.Data.Common/Interfaces/IAuditableEntity.cs)  
**Type:** Interface  
**Extends:** [`IIDStd`](#iidstd)

### Summary

Extends [`IIDStd`](#iidstd) with four standard audit columns present on 28 of the 31 entities.  
Provides a single interface constraint for any code that needs to read or write audit metadata generically.

### Properties

| Property | Type | Nullable | Description |
|---|---|---|---|
| `Id` | `Guid` | No | Inherited from [`IIDStd`](#iidstd). |
| `DateCreated` | `DateTime` | No | UTC timestamp when the entity was first created. Set once at insert time. |
| `DateUpdated` | `DateTime?` | Yes | UTC timestamp of the most recent modification. `null` if never updated. |
| `CreatedByAppUserId` | `Guid` | No | Primary key of the `AppUser` who created the entity. |
| `UpdatedByAppUserId` | `Guid?` | Yes | Primary key of the `AppUser` who last modified the entity. `null` if never updated. |

### Entities that do NOT implement this interface

| Entity | Reason |
|---|---|
| `AppUser` | Has `DateCreated`/`DateUpdated` but no `CreatedByAppUserId` — the user IS the actor. |
| `UserMessageTo` | Join table with no audit requirement. |
| `UploadFileTypeExtension` | Create-only entity (patterns are deleted/recreated, never edited). Implements [`IIDStd`](#iidstd) directly. |

### Usage

- Used as a generic constraint in `AuditChangeTracker` and any future generic save-interceptors.
- 28 entity stubs in `Ben.Data.Source/Entities/` declare `public partial class X : IAuditableEntity`.
