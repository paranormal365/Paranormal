# Ben.Service.RepositoryService — Project Summary

**Type:** Class Library (`Microsoft.NET.Sdk`)  
**Target Framework:** net10.0

## Purpose

Implements the repository pattern over `BenDataContext`.  
Provides typed repository interfaces and concrete implementations for all 26 entities, plus business-logic services for organisation security, audit logging, and geocoding.

## Dependencies

| Direction | Project |
|---|---|
| Depends on | Ben.Data.Common, Ben.Data.Source |
| Referenced by | Ben.Data.WebApi, Ben.Service.RepositoryService.Tests |

## Contents

| File | Description |
|---|---|
| [Interfaces-Generic.md](Interfaces-Generic.md) | `IRepositoryBase<T>`, `IRepositoryManager`, `IAuditLogService`, `IOrganizationSecurityService` |
| [Interfaces-Entity.md](Interfaces-Entity.md) | All 26 typed entity repository interfaces |
| [Services.md](Services.md) | `OrganizationSecurityService`, `AuditLogService`, `AddressGeocodingService` |
| [RepositoryManager.md](RepositoryManager.md) | `RepositoryManager`, `AppUserRepositoryManager`, `OrganizationRepositoryManager` |

## Key Design Decisions

- **`IDbContextFactory<BenDataContext>`** is used everywhere (not `BenDataContext` directly) so each operation creates a short-lived, independent context — safe for concurrent requests.
- **`OrganizationSecurityService`** implements **two** interfaces: `Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService` (used by controllers) and `Ben.Service.Security.Services.IOrganizationSecurityService` (used by the attribute layer). Both must be registered in DI.
- **Audit failures are silently swallowed** in `AdminEntityControllerBase` — an audit log write failure never rolls back a successful entity operation.
