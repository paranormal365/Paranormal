# Ben.Data.Source — Project Summary

**Type:** Class Library (`Microsoft.NET.Sdk`)  
**Target Framework:** net10.0  
**Namespace root:** `Ben.Data.Source`

## Purpose

The data access layer — contains the EF Core `DbContext`, all 31 entity classes, database migrations, and the design-time factory. This project is the single source of truth for the BenDb schema.

## Role in the Solution

| Direction | Project |
|---|---|
| Depends on | Ben.Data.Common |
| Referenced by | Ben.Data.WebApi, Ben.Service.Mappings, Ben.Service.Models (indirectly), Ben.Service.RepositoryService, Ben.Service.Security, Ben.Service.RepositoryService.Tests |

## Database

**Server:** Docker SQL Server `bendb-sql` (SA, port 1433)  
**Database name:** `BenDb`  
**Tables:** ~50 total (42 entity tables + 6 Identity tables + migrations history)  
**Migrations:** 14 applied (see [Context.md](Context.md))

| Migration | Summary |
|---|---|
| 01–09 | InitialCreate through ReplaceActionNameWithActionsBitmask |
| 10 `AddAuditLogs` | `AuditLogs` table |
| 11 `AddCmsEntities` | `OrganizationLogos`, `OrgMemberGroups`, `OrgMemberGroupMemberships`, `CmsSections`, `CmsPagePermissions` |
| 12 `AddUploadFileAudioConfig` | Per-file WaveSurfer config |
| 13 `AddUploadFileRegionNotesAndParentClip` | `UploadFileRegionNotes` + `ParentFileId`/`RegionStart`/`RegionEnd` on `UploadFiles` |
| 14 `AddUploadFileVotes` | `UploadFileVotes` with unique `(UploadFileId, AppUserId)` |

## Entity Architecture

Every entity follows a **two-file partial class** pattern:

| File | Purpose | Example |
|---|---|---|
| `BenDataModel.X.cs` | User stub — declares interface, adds hand-written properties | `BenDataModel.Organization.cs` |
| `BenDataModel.X.Generated.cs` | All auto-generated properties and navigation properties | `BenDataModel.Organization.Generated.cs` |

### Interface hierarchy

- **42 entities** implement [`IIDStd`](../Ben.Data.Common/Interfaces.md#iidstd)
- **Most** implement [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity)
- **Exceptions:** `AppUser`, `UserMessageTo`, `UploadFileTypeExtension`, `UploadFileVote` (no full audit columns)

## Contents

| File | Description |
|---|---|
| [Context.md](Context.md) | `BenDataContext` — DbContext, model configuration, migration history |
| [Entities-Core.md](Entities-Core.md) | `AppUser`, `Organization`, `OrganizationUserMembership`, `OrganizationAccessGrant`, `AuditLog` |
| [Entities-User.md](Entities-User.md) | User sub-entities: address, email, phone, link, message, note + their type entities |
| [Entities-Org.md](Entities-Org.md) | Organization sub-entities: address, email, phone, link, note, page + their type entities |
| [Entities-Upload.md](Entities-Upload.md) | `UploadFileType`, `UploadFileTypeExtension`, `UploadFile` (+ parent tracking), `UploadFileOrganizationShare`, `UploadFilePermissionRequest`, `UploadFileAudioConfig`, `UploadFileRegionNote`, `UploadFileVote` |
| [Enums.md](Enums.md) | Data-layer enumerations defined in `Ben.Data.Source.Enums` |
