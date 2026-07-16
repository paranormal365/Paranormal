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
**Tables:** 41 total (31 entities + 6 Identity tables + `AuditLogs` + `Logs` + migrations history)  
**Migrations:** 9 applied (see [Context.md](Context.md))

## Entity Architecture

Every entity follows a **two-file partial class** pattern:

| File | Purpose | Example |
|---|---|---|
| `BenDataModel.X.cs` | User stub — declares interface, adds hand-written properties | `BenDataModel.Organization.cs` |
| `BenDataModel.X.Generated.cs` | All auto-generated properties and navigation properties | `BenDataModel.Organization.Generated.cs` |

### Interface hierarchy

- **31 entities** implement [`IIDStd`](../Ben.Data.Common/Interfaces.md#iidstd)
- **28 of those** implement [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity) (adds 4 audit columns)
- **3 exceptions:** `AppUser` (no CreatedBy FK), `UserMessageTo` (join table), `UploadFileTypeExtension` (create-only)

## Contents

| File | Description |
|---|---|
| [Context.md](Context.md) | `BenDataContext` — DbContext, model configuration, migration history |
| [Entities-Core.md](Entities-Core.md) | `AppUser`, `Organization`, `OrganizationUserMembership`, `OrganizationAccessGrant`, `AuditLog` |
| [Entities-User.md](Entities-User.md) | User sub-entities: address, email, phone, link, message, note + their type entities |
| [Entities-Org.md](Entities-Org.md) | Organisation sub-entities: address, email, phone, link, note, page + their type entities |
| [Entities-Upload.md](Entities-Upload.md) | `UploadFileType`, `UploadFileTypeExtension`, `UploadFile`, `UploadFileOrganizationShare`, `UploadFilePermissionRequest` |
| [Enums.md](Enums.md) | Data-layer enumerations defined in `Ben.Data.Source.Enums` |
