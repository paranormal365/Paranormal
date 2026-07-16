# Ben.Data.Source — Core Entities

All entities below are in the `Ben.Data.Source.Entities` namespace.  
Each follows the two-file partial class pattern described in [Summary.md](Summary.md).

---

## `AppUser`

**Files:** [`BenDataModel.AppUser.cs`](../../../Ben.Data.Source/Entities/BenDataModel.AppUser.cs) · [`BenDataModel.AppUser.Generated.cs`](../../../Ben.Data.Source/Entities/BenDataModel.AppUser.Generated.cs)  
**Implements:** `IdentityUser<Guid>`, [`IIDStd`](../Ben.Data.Common/Interfaces.md#iidstd)  
**Table:** `AppUsers`

### Summary
The application's user account entity. Extends ASP.NET Core Identity's `IdentityUser<Guid>` with a `DisplayName` and audit timestamps.  
Intentionally does **not** implement `IAuditableEntity` because users are the actors — there is no separate "created by" field.

### Properties (beyond IdentityUser)

| Property | Type | Nullable | Description |
|---|---|---|---|
| `DisplayName` | `string?` | Yes | Human-readable name shown in the UI. |
| `DateCreated` | `DateTime` | No | UTC timestamp when the account was created. |
| `DateUpdated` | `DateTime?` | Yes | UTC timestamp of the most recent profile update. |

### Relationships
- One `AppUser` can have many: `UserAddresses`, `UserEmails`, `UserPhones`, `UserLinks`, `UserMessages`, `UserNotes`.
- One `AppUser` can have many `OrganizationUserMembership` rows.
- One `AppUser` appears as `CreatedByAppUser`/`UpdatedByAppUser` on most other entities (NoAction FK).

---

## `Organization`

**Files:** [`BenDataModel.Organization.cs`](../../../Ben.Data.Source/Entities/BenDataModel.Organization.cs) · [`BenDataModel.Organization.Generated.cs`](../../../Ben.Data.Source/Entities/BenDataModel.Organization.Generated.cs)  
**Implements:** [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity)  
**Table:** `Organizations`

### Summary
A tenant/organisation that groups users and content.  
Created via [`RegisterOrganizationAsync`](../Ben.Service.RepositoryService/Services.md#organizationsecurityservice) which also seeds an Owner membership.

### Properties

| Property | Type | Nullable | Description |
|---|---|---|---|
| `Id` | `Guid` | No | PK |
| `Name` | `string` | No | Human-readable display name. |
| `UrlName` | `string` | No | URL-safe slug — must be unique. Used in routes and API. |
| `DateCreated` | `DateTime` | No | Audit — creation timestamp. |
| `DateUpdated` | `DateTime?` | Yes | Audit — last update timestamp. |
| `CreatedByAppUserId` | `Guid` | No | Audit — creator. |
| `UpdatedByAppUserId` | `Guid?` | Yes | Audit — last modifier. |

### Navigation Properties
- `OrganizationAddresses`, `OrganizationEmails`, `OrganizationPhones`, `OrganizationLinks`, `OrganizationNotes`, `OrganizationPages`

---

## `OrganizationUserMembership`

**Files:** [`BenDataModel.OrganizationUserMembership.cs`](../../../Ben.Data.Source/Entities/BenDataModel.OrganizationUserMembership.cs) · [`BenDataModel.OrganizationUserMembership.Generated.cs`](../../../Ben.Data.Source/Entities/BenDataModel.OrganizationUserMembership.Generated.cs)  
**Implements:** [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity)  
**Table:** `OrganizationUserMemberships`  
**Unique index:** `(OrganizationId, AppUserId)`

### Summary
Links a user to an organisation with a role. One row per user-per-organisation.  
`IsActive = false` marks a soft-deleted/removed membership (the row is retained for audit history).

### Properties

| Property | Type | Nullable | Description |
|---|---|---|---|
| `Id` | `Guid` | No | PK |
| `OrganizationId` | `Guid` | No | FK → `Organization` |
| `AppUserId` | `Guid` | No | FK → `AppUser` |
| `Role` | [`OrganizationMemberRole`](../Ben.Data.Common/Enums.md#organizationmemberrole) | No | The user's role in the organisation. Stored as `int`. |
| `IsActive` | `bool` | No | `false` after `RemoveMemberAsync` is called. |
| `DateCreated` | `DateTime` | No | Audit |
| `DateUpdated` | `DateTime?` | Yes | Audit |
| `CreatedByAppUserId` | `Guid` | No | Audit |
| `UpdatedByAppUserId` | `Guid?` | Yes | Audit |

---

## `OrganizationAccessGrant`

**Files:** [`BenDataModel.OrganizationAccessGrant.cs`](../../../Ben.Data.Source/Entities/BenDataModel.OrganizationAccessGrant.cs) · [`BenDataModel.OrganizationAccessGrant.Generated.cs`](../../../Ben.Data.Source/Entities/BenDataModel.OrganizationAccessGrant.Generated.cs)  
**Implements:** [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity)  
**Table:** `OrganizationAccessGrants`  
**Unique index:** `(OrganizationId, AppUserId, TableName)`

### Summary
Stores the set of permitted actions for a user within an organisation for a specific table, as a single integer bitmask.  
One row per (user, org, table); the `Actions` field combines multiple CRUD flags.  
Used by [`OrganizationSecurityService.HasAccessAsync`](../Ben.Service.RepositoryService/Services.md) to determine whether a non-Owner/Admin member can perform an operation.

### Properties

| Property | Type | Nullable | Description |
|---|---|---|---|
| `Id` | `Guid` | No | PK |
| `OrganizationId` | `Guid` | No | FK → `Organization` |
| `AppUserId` | `Guid` | No | FK → `AppUser` |
| `TableName` | [`OrganizationSecurityTable`](../Ben.Data.Common/Enums.md#organizationsecuritytable) | No | The domain table this grant applies to. |
| `Actions` | [`OrganizationSecurityAction`](../Ben.Data.Common/Enums.md#organizationsecurityaction) | No | Bitmask of permitted CRUD operations (Create=1, Read=2, Update=4, Delete=8). |
| `DateCreated` | `DateTime` | No | Audit |
| `DateUpdated` | `DateTime?` | Yes | Audit |
| `CreatedByAppUserId` | `Guid` | No | Audit |
| `UpdatedByAppUserId` | `Guid?` | Yes | Audit |

---

## `AuditLog`

**Files:** [`BenDataModel.AuditLog.cs`](../../../Ben.Data.Source/Entities/BenDataModel.AuditLog.cs) · [`BenDataModel.AuditLog.Generated.cs`](../../../Ben.Data.Source/Entities/BenDataModel.AuditLog.Generated.cs)  
**Implements:** [`IIDStd`](../Ben.Data.Common/Interfaces.md#iidstd) *(not `IAuditableEntity` — audit records are not themselves audited)*  
**Table:** `AuditLogs`  
**Indexes:** `(EntityType, EntityId)`, `UserId`, `OccurredAt`

### Summary
Immutable record of a CRUD action performed by a user on any entity.  
Written by [`AuditLogService`](../Ben.Service.RepositoryService/Services.md#auditlogservice).  
**No FK to `AppUser`** — audit records survive user deletion.

### Properties

| Property | Type | Nullable | Description |
|---|---|---|---|
| `Id` | `Guid` | No | PK |
| `UserId` | `Guid` | No | ID of the user who performed the action (no FK constraint). |
| `Action` | [`AuditAction`](../Ben.Data.Common/Enums.md#auditaction) | No | Create, Update, or Delete. |
| `EntityType` | `string` | No | Name of the entity type (e.g. `"Organization"`). Max 128 chars. |
| `EntityId` | `Guid` | No | Primary key of the affected entity. |
| `Source` | `string` | No | Application that originated the action (see [`AppSources`](../Ben.Data.Common/Constants.md#appsources)). Max 64 chars. |
| `OccurredAt` | `DateTime` | No | UTC timestamp of the operation. |
| `ChangesJson` | `string?` | Yes | JSON payload. For Create/Delete: property snapshot dict. For Update: array of `{Property, Before, After}`. |
