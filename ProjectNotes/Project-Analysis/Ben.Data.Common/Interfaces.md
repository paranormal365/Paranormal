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


---

## `IFileStorageService`

**Namespace:** `Ben.Data.Common.Interfaces`  
**File:** [`Ben.Data.Common/Interfaces/IFileStorageService.cs`](../../../Ben.Data.Common/Interfaces/IFileStorageService.cs)  
**Type:** Interface  
**Added:** 2026-07-21

### Summary

Abstracts binary file storage so the application can swap providers (local filesystem → Azure Blob / S3) without changing controllers. Registered as a singleton in the WebApi DI container.

### Methods

| Method | Returns | Description |
|---|---|---|
| `WriteAsync(relativePath, data, ct)` | `Task` | Writes stream to storage at the relative path. Creates intermediate directories as needed. |
| `OpenReadAsync(relativePath, ct)` | `Task<Stream>` | Opens a read stream for the file. Throws `FileNotFoundException` if absent. |
| `DeleteAsync(relativePath, ct)` | `Task` | Deletes the file. No-op if absent. |
| `Exists(relativePath)` | `bool` | Returns `true` if the file exists at the path. |
| `UserFilePath(userId, storedFileName)` | `string` | Builds the canonical relative path: `"users/{userId}/{storedFileName}"`. |

### Implementation

- **Dev/production:** `Ben.Data.WebApi.Services.LocalFileStorageService` — writes to the path configured in `FileStorage:RootPath` (e.g. `/Users/ben/Source/Ben/.uploads` in dev).
- Storage is keyed by `StoredFileName` (a GUID-based name) not the display `FileName`, so renames never conflict.

### Future Swap

```json
"FileStorage": { "Provider": "AzureBlob", "ConnectionString": "...", "Container": "ben-uploads" }
```
Swap the DI registration in `Program.cs`; no controller code changes required.
