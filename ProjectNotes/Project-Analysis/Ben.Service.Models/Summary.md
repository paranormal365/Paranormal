# Ben.Service.Models — Project Summary

**Type:** Class Library (`Microsoft.NET.Sdk`)  
**Target Framework:** net10.0

## Purpose

Defines the **Data Transfer Objects (DTOs)** — immutable C# `record` types — returned by API endpoints and consumed by Blazor components.  
These records are the payload layer: the bridge between EF Core entities and the HTTP/Blazor surface.

## Dependencies

| Direction | Project |
|---|---|
| Depends on | Ben.Data.Common (for shared enums) |
| Referenced by | Ben.Data.WebApi, Ben.Service.Mappings, Ben.Web.Library, Ben.Web.WebApp |

## Namespace Structure

```
Ben.Service.Models
├── Admin/       — Full-detail records for SuperAdmin inspection and editing
├── Entities/    — Standard records for authenticated-user operations
├── Identity/    — ASP.NET Core Identity join-table projections
└── People/      — Simplified user record for non-admin views
```

## Contents

| File | Description |
|---|---|
| [Records-People.md](Records-People.md) | `AppUserRecord` — public user projection |
| [Records-Admin.md](Records-Admin.md) | All 27 Admin records including `AppUserDetailAdminRecord` |
| [Records-Entities.md](Records-Entities.md) | All 26 Entity records |
| [Records-Identity.md](Records-Identity.md) | All 6 Identity records |

## Design Conventions

- All records use `record` type with `init`-only properties — they are immutable once created.
- All `Guid` primary keys are named `Id`.
- Admin records mirror the full entity schema; Entity records expose only the data needed by standard users.
- `AppUserDetailAdminRecord` is the only "aggregate" record — it nests multiple lists for a single API call.
