# Ben.Data.Source — Upload File Entities

---

## `UploadFileType`

**Files:** [`BenDataModel.UploadFileType.cs`](../../../Ben.Data.Source/Entities/BenDataModel.UploadFileType.cs) · [`BenDataModel.UploadFileType.Generated.cs`](../../../Ben.Data.Source/Entities/BenDataModel.UploadFileType.Generated.cs)  
**Implements:** [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity)  
**Table:** `UploadFileTypes`

### Summary
Defines a category for uploaded files (e.g. "Document", "Image", "Video") including display metadata and whether all extensions are accepted or a restricted pattern list applies.  
Managed exclusively by SuperAdmin via `/api/admin/upload-file-types`.

### Properties

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | PK |
| `Name` | `string` | Display name of the file type category. |
| `Description` | `string?` | Optional description shown in the UI. |
| `IconClass` | `string?` | CSS icon class for display (e.g. FontAwesome class). |
| `ColorClass` | `string?` | CSS color class for display. |
| `SortOrder` | `int` | Display order in lists. |
| `IsActive` | `bool` | Whether this type is available for upload selection. |
| `IsPublic` | `bool` | Whether non-admin users can see this type. |
| `AllowAllExtensions` | `bool` | `true` = all extensions accepted; `false` = only patterns in `AllowedExtensions` are valid. |
| Audit columns | — | See [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity) |

### Navigation
- `AllowedExtensions` → `ICollection<UploadFileTypeExtension>`

### Extension validation logic

```csharp
bool ok = type.AllowAllExtensions
       || FileExtensionPatternMatcher.IsAllowedByPatterns(
              type.AllowedExtensions.Select(e => e.Pattern),
              Path.GetExtension(fileName));
```

See [`FileExtensionPatternMatcher`](../Ben.Data.Common/Helpers.md#fileextensionpatternmatcher).

---

## `UploadFileTypeExtension`

**Files:** [`BenDataModel.UploadFileTypeExtension.cs`](../../../Ben.Data.Source/Entities/BenDataModel.UploadFileTypeExtension.cs) · [`BenDataModel.UploadFileTypeExtension.Generated.cs`](../../../Ben.Data.Source/Entities/BenDataModel.UploadFileTypeExtension.Generated.cs)  
**Implements:** [`IIDStd`](../Ben.Data.Common/Interfaces.md#iidstd) only — create-only entity, no update columns  
**Table:** `UploadFileTypeExtensions`  
**Unique index:** `(UploadFileTypeId, Pattern)`

### Summary
Stores a single allowed extension pattern for a file type.  
Patterns are stored lowercase and support exact match (`.txt`) or suffix wildcard (`.tx*`).

### Properties

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | PK |
| `UploadFileTypeId` | `Guid` | FK → `UploadFileType` (cascade delete) |
| `Pattern` | `string` | Extension pattern, e.g. `.txt`, `.doc*`. |
| `DateCreated` | `DateTime` | When the pattern was added. |
| `CreatedByAppUserId` | `Guid` | FK → `AppUser` (NoAction — no cascade). |

---

## `UploadFile`

**Files:** [`BenDataModel.UploadFile.cs`](../../../Ben.Data.Source/Entities/BenDataModel.UploadFile.cs) · [`BenDataModel.UploadFile.Generated.cs`](../../../Ben.Data.Source/Entities/BenDataModel.UploadFile.Generated.cs)  
**Implements:** [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity)  
**Table:** `UploadFiles`

### Summary
Stores the actual file binary alongside metadata. Files can be shared with organizations and have permission requests submitted against them.

### Properties

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | PK |
| `UploadFileTypeId` | `Guid` | FK → `UploadFileType` |
| `FileName` | `string` | Original file name including extension. |
| `ContentType` | `string` | MIME type (e.g. `"application/pdf"`). |
| `FileData` | `byte[]` | Raw file bytes — stored as `varbinary(max)`. |
| `FileSizeBytes` | `long` | File size in bytes. |
| `Description` | `string?` | Optional description. |
| `IsPublic` | `bool` | Whether the file is publicly accessible. |
| Audit columns | — | |

### Navigation
- `OrganizationShares` → `ICollection<UploadFileOrganizationShare>`
- `PermissionRequests` → `ICollection<UploadFilePermissionRequest>`

---

## `UploadFileOrganizationShare`

**Files:** [`BenDataModel.UploadFileOrganizationShare.cs`](../../../Ben.Data.Source/Entities/BenDataModel.UploadFileOrganizationShare.cs)  
**Implements:** [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity)  
**Table:** `UploadFileOrganizationShares`  
**Unique index:** `(UploadFileId, OrganizationId)`

### Summary
Records a file being shared with an organization, controlling who within that organization can see it.

### Properties

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | PK |
| `UploadFileId` | `Guid` | FK → `UploadFile` |
| `OrganizationId` | `Guid` | FK → `Organization` |
| `Visibility` | [`FileShareVisibility`](../Ben.Data.Common/Enums.md#filesharevisibility) | `OrgAdminsOnly`, `OrgMembers`, or `Public`. |
| `IsActive` | `bool` | Soft delete — `false` when the share is revoked. |
| Audit columns | — | |

---

## `UploadFilePermissionRequest`

**Files:** [`BenDataModel.UploadFilePermissionRequest.cs`](../../../Ben.Data.Source/Entities/BenDataModel.UploadFilePermissionRequest.cs)  
**Implements:** [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity)  
**Table:** `UploadFilePermissionRequests`

### Summary
Records a user's request to gain additional permissions (Use, Share, Display) for a file they can see but cannot fully act on.

### Properties

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | PK |
| `UploadFileId` | `Guid` | FK → `UploadFile` |
| `RequestedByAppUserId` | `Guid` | FK → `AppUser` (requester) |
| `PermissionType` | [`FilePermissionType`](../Ben.Data.Common/Enums.md#filepermissiontype) | Flags enum — requested permissions. |
| `Status` | [`FilePermissionRequestStatus`](../Ben.Data.Common/Enums.md#filepermissionrequeststatus) | Lifecycle state. |
| `RequestMessage` | `string?` | Optional message from requester. |
| `ReviewMessage` | `string?` | Optional response from reviewer. |
| `ReviewedByAppUserId` | `Guid?` | FK → `AppUser` (reviewer) |
| `ReviewedAt` | `DateTime?` | When the request was reviewed. |
| Audit columns | — | |
