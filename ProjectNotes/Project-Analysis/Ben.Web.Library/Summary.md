# Ben.Web.Library — Project Summary

**Type:** Razor Class Library (`Microsoft.NET.Sdk.Razor`)  
**Target Framework:** net10.0  
**Packages:** `Telerik.UI.for.Blazor 14.1.0`

## Purpose

A shared Blazor component library containing SuperAdmin UI, user management, organization management, CMS editor components, and audio player components. Consumed by `Ben.Web.WebApp`.

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
| [Components.md](Components.md) | All Blazor components grouped by folder |

## Folder Map

| Folder | Components |
|---|---|
| `SuperAdmin/` | `AdminSidePanel`, `AdminFileTypes`, `AdminRoles` |
| `User/` | `AdminUsers`, `AdminUserCreate`, `AdminUserDetail`, `UserMediaPreview` |
| `Organization/` | `OrganizationList`, `OrganizationCreateEdit`, `OrganizationView` |
| `Organization/Cms/` | `OrgCmsEditor`, `OrgCmsPageEdit`, `CmsSectionEditor`, `CmsFileThumbnail` |
| `Manage/` | `UploadFileVoteBar` |
| `Manage/Audio/` | `WaveSurferPlayer` (WaveSurfer.js v7.12.11 wrapper), `WaveSurferOptions.cs` (config records), `AudioFilePreview` (compact + full-view modal with regions, spectrogram, clip, child-clips), `WsRegionExplorer` (region audio + notes + sub-regions), `AudioFormatUtils` |
