# Ben.Web.Library — Project Summary

**Type:** Razor Class Library (`Microsoft.NET.Sdk.Razor`)  
**Target Framework:** net10.0  
**Packages:** `Telerik.UI.for.Blazor 14.0.0`

## Purpose

A shared Blazor component library containing SuperAdmin UI components and user detail pages that can be consumed by the `Ben.Web.WebApp` host without coupling the host to the specific UI implementation.

## Dependencies

| Direction | Project |
|---|---|
| Depends on | Ben.Data.Common, Ben.Service.Models |
| Referenced by | Ben.Web.WebApp |

## Routing

Library pages are discovered in the WebApp via:
```razor
<Router AppAssembly="typeof(Program).Assembly"
        AdditionalAssemblies="new[] { typeof(LibraryAssemblyMarker).Assembly }">
```
`LibraryAssemblyMarker` is a marker class in the library that provides a stable assembly reference.

## Contents

| File | Description |
|---|---|
| [Services.md](Services.md) | `IBenAdminClient`, `IBenUserState` — the two interfaces library components depend on |
| [Components-SuperAdmin.md](Components-SuperAdmin.md) | `AdminSidePanel`, `AdminFileTypes` — SuperAdmin UI components |
| [Components-User.md](Components-User.md) | `AdminUsers`, `AdminUserDetail` — user management pages |
