# Ben.Service.Security — Project Summary

**Type:** Class Library (`Microsoft.NET.Sdk`)  
**Target Framework:** net10.0

## Purpose

Provides the **organisation-level security enforcement layer** — the middleware attribute, permission-checking service, and the security-layer `OrganizationSecurityTable` enum.

This project is the **policy enforcement point**: it determines whether an incoming request has sufficient permissions for a given entity operation within an organisation.

> `OrganizationSecurityAction` was previously defined here as a `[Flags]` enum. It has been consolidated into `Ben.Data.Common.Enums` and is imported via a `global using` alias.

## Dependencies

| Direction | Project |
|---|---|
| Depends on | Ben.Data.Common, Ben.Data.Source |
| Referenced by | Ben.Data.WebApi, Ben.Service.RepositoryService.Tests |

## Contents

| File | Description |
|---|---|
| [Interfaces.md](Interfaces.md) | `IOrganizationSecurityService` — the security contract |
| [Services.md](Services.md) | `OrganizationSecurityService` — the security implementation |
| [Enums.md](Enums.md) | `OrganizationSecurityTable` (Security-layer enum with service-layer int values) |
| [Models.md](Models.md) | `OrganizationUserPermission`, `OrganizationAccessGrant`, `OrganizationUserMembership` model classes |
| [Attributes.md](Attributes.md) | `OrganizationSecurityAuthorizeAttribute` |
| [Extensions.md](Extensions.md) | `SecurityExtensions` |
| [Middleware.md](Middleware.md) | Security middleware components (currently empty) |
