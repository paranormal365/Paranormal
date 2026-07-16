# Ben.Data.Common — Project Summary

**Type:** Class Library (`Microsoft.NET.Sdk`)  
**Target Framework:** net10.0  
**Namespace root:** `Ben.Data.Common`

## Purpose

Provides the lowest-level shared contracts, utilities, and data definitions used by every other project in the solution. Nothing in this library depends on any other Ben project — it is the base of the dependency tree.

## Role in the Solution

All projects reference `Ben.Data.Common`. It contains:
- **Entity contracts** that the data layer entities must satisfy
- **Enumerations** shared between the entity layer and the service/API layers  
- **String constants** for roles, application sources, and activity actions
- **Static helpers** for pattern matching, date formatting, predicate building, change tracking, and color manipulation
- **Utility services** for crypto and JSON operations

## Dependencies

| Direction | Project |
|---|---|
| Depends on | *(none — leaf library)* |
| Referenced by | Ben.Data.Source, Ben.Data.WebApi, Ben.Service.Mappings, Ben.Service.Models, Ben.Service.RepositoryService, Ben.Service.Security, Ben.Web.Library, Ben.Web.WebApp |

## Contents

| File | Description |
|---|---|
| [Interfaces.md](Interfaces.md) | `IIDStd`, `IAuditableEntity` — entity identity and audit contracts |
| [Helpers.md](Helpers.md) | `AuditChangeTracker`, `FileExtensionPatternMatcher`, `PredicateHelper`, `DateTimeHelper`, `ColorHelper` |
| [Constants.md](Constants.md) | `RoleNames`, `AppSources`, `Actions` |
| [Enums.md](Enums.md) | All 8 enumerations: `AuditAction`, `OrganizationMemberRole`, `OrganizationSecurityAction`, `OrganizationSecurityTable`, `FilePermissionType`, `FileShareVisibility`, `FilePermissionRequestStatus`, `CryptoModes` |
| [Services.md](Services.md) | `CryptoFileService`, `JsonConvertService` |

## Key Design Decisions

- **No framework dependencies** — only .NET BCL and `System.Drawing`. This keeps every consumer's dependency graph clean.
- **`IIDStd` and `IAuditableEntity`** are split so that create-only entities (`UploadFileTypeExtension`) can implement just `IIDStd` without forcing nullable audit columns.
- **`OrganizationMemberRole`** lives here (not in `Ben.Service.Security`) because the data-layer entity `OrganizationUserMembership` must reference it, and `Ben.Data.Source` cannot reference `Ben.Service.Security` without creating a circular dependency.
