# Ben.Data.Source — Context

## `BenDataContext`

**Namespace:** `Ben.Data.Source.Context`  
**File:** [`BenDataContext.cs`](../../../Ben.Data.Source/Context/BenDataContext.cs)  
**Inherits:** `IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>`  

### Summary

The application's primary EF Core database context.  
Registered as a **pooled factory** (`IDbContextFactory<BenDataContext>`) in all consuming projects so that each request/operation creates a short-lived, independent context.

### DbSets (entity tables)

| Property | Entity | Notes |
|---|---|---|
| `AppUsers` | `AppUser` | Mapped to `"AppUsers"` table (overrides Identity default). |
| `UserAddressTypes` | `UserAddressType` | |
| `UserEmailTypes` | `UserEmailType` | |
| `UserPhoneTypes` | `UserPhoneType` | |
| `UserLinkTypes` | `UserLinkType` | |
| `UserMessageTypes` | `UserMessageType` | |
| `UserNoteTypes` | `UserNoteType` | |
| `UserAddresses` | `UserAddress` | |
| `UserEmails` | `UserEmail` | |
| `UserPhones` | `UserPhone` | |
| `UserLinks` | `UserLink` | |
| `UserMessages` | `UserMessage` | |
| `UserMessageTos` | `UserMessageTo` | |
| `UserNotes` | `UserNote` | |
| `Organizations` | `Organization` | |
| `OrganizationAddresses` | `OrganizationAddress` | |
| `OrganizationEmails` | `OrganizationEmail` | |
| `OrganizationPhones` | `OrganizationPhone` | |
| `OrganizationLinks` | `OrganizationLink` | |
| `OrganizationNotes` | `OrganizationNote` | |
| `OrganizationAddressTypes` | `OrganizationAddressType` | |
| `OrganizationEmailTypes` | `OrganizationEmailType` | |
| `OrganizationLinkTypes` | `OrganizationLinkType` | |
| `OrganizationPhoneTypes` | `OrganizationPhoneType` | |
| `OrganizationNoteTypes` | `OrganizationNoteType` | |
| `OrganizationPages` | `OrganizationPage` | |
| `OrganizationUserMemberships` | `OrganizationUserMembership` | Unique index on `(OrganizationId, AppUserId)`. |
| `OrganizationAccessGrants` | `OrganizationAccessGrant` | Unique index on `(OrganizationId, AppUserId, TableName)`. |
| `UploadFileTypes` | `UploadFileType` | |
| `UploadFileTypeExtensions` | `UploadFileTypeExtension` | Unique index on `(UploadFileTypeId, Pattern)`. |
| `UploadFiles` | `UploadFile` | `FileData` column is `varbinary(max)`. |
| `UploadFileOrganizationShares` | `UploadFileOrganizationShare` | Unique index on `(UploadFileId, OrganizationId)`. |
| `UploadFilePermissionRequests` | `UploadFilePermissionRequest` | |
| `AuditLogs` | `AuditLog` | Indexes on `(EntityType, EntityId)`, `UserId`, `OccurredAt`. No FK to AppUser. |

**Identity tables** (from `IdentityDbContext` base): `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`.

### Model Configuration Conventions (OnModelCreating)

- **Audit FKs** (`CreatedByAppUserId`, `UpdatedByAppUserId`) use `DeleteBehavior.NoAction` — deleting a user does not cascade to their created/updated records.
- **Ownership FKs** (e.g. `OrganizationUserMembership → Organization`) use the EF Core default cascade (`Cascade`).
- **`UploadFile.FileData`** is stored as `varbinary(max)` via explicit column-type configuration.

### `BenDataContextDesignTimeFactory`

**File:** [`Ben.Data.Source/BenDataContextDesignTimeFactory.cs`](../../../Ben.Data.Source/BenDataContextDesignTimeFactory.cs)

Creates a `BenDataContext` during EF Core design-time operations (`dotnet ef migrations add`).  
Reads the connection string from environment or falls back to the development SA connection string.  
Required because `Ben.Data.Source` does not have its own `Program.cs` — `Ben.Data.WebApi` is used as the `--startup-project`.

### Migration History

| Migration | Date | Description |
|---|---|---|
| `InitialCreate` | 2026-07-09 | 26 entity tables |
| `AddIdentitySchema` | 2026-07-09 | Identity columns on AppUsers + 6 AspNet* tables |
| `AddGeocodingMetadataToAddresses` | 2026-07-11 | Lat/lng columns on address tables |
| `AddOrganizationSecurityModel` | 2026-07-11 | `OrganizationUserMemberships` + `OrganizationAccessGrants` tables |
| `AddUploadFileEntities` | 2026-07-13 | `UploadFileTypes`, `UploadFiles` tables |
| `AddUploadFileSharing` | 2026-07-13 | `UploadFileOrganizationShares`, `UploadFilePermissionRequests` tables |
| `AddUploadFileTypeExtensions` | 2026-07-14 | `AllowAllExtensions` column + `UploadFileTypeExtensions` table |
| `ReplaceIsOrganizationAdminWithRole` | 2026-07-14 | Drops `IsOrganizationAdmin` bool; adds `Role` int (with data migration SQL) |
| `ReplaceActionNameWithActionsBitmask` | 2026-07-14 | Drops `IsAllowed` + renames `ActionName → Actions`; updates unique index to `(OrgId, UserId, TableName)` |
| `AddAuditLogs` | 2026-07-14 | `AuditLogs` table with indexes |

**Migration command:**
```bash
dotnet ef migrations add <Name> \
  --project Ben.Data.Source \
  --startup-project Ben.Data.WebApi \
  --output-dir Migrations
```
