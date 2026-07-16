# Ben.Service.Models — People & Entity Records

---

## People Records (`Ben.Service.Models.People`)

### `AppUserRecord`

**Endpoint usage:** `GET /api/users`  
**Mapped from:** `AppUser` by `AppUserProfile`

Lightweight user projection for non-admin callers.

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | User PK |
| `UserName` | `string?` | Login username |
| `DisplayName` | `string?` | Human-readable display name |
| `DateCreated` | `DateTime` | Account creation date |
| `DateUpdated` | `DateTime?` | Last update |
| `Email` | `string?` | Email address |
| `IsEmailConfirmed` | `bool` | |
| `PhoneNumber` | `string?` | |
| `IsPhoneNumberConfirmed` | `bool` | |
| `IsTwoFactorEnabled` | `bool` | |

---

## Entity Records (`Ben.Service.Models.Entities`)

Entity records mirror their source entities and are used by standard (non-admin) API endpoints.

### `OrganizationRecord`

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `Name` | `string` |
| `UrlName` | `string` |
| `DateCreated` | `DateTime` |
| `DateUpdated` | `DateTime?` |
| `CreatedByAppUserId` | `Guid` |
| `UpdatedByAppUserId` | `Guid?` |

### `OrganizationUserMembershipRecord`

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationId` | `Guid` | |
| `AppUserId` | `Guid` | |
| `Role` | [`OrganizationMemberRole`](../Ben.Data.Common/Enums.md#organizationmemberrole) | |
| `IsActive` | `bool` | |
| `DateCreated` | `DateTime` | |
| `DateUpdated` | `DateTime?` | |
| `CreatedByAppUserId` | `Guid` | |
| `UpdatedByAppUserId` | `Guid?` | |

### `UploadFileTypeRecord`

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `Name` | `string` |
| `Description` | `string?` |
| `IconClass` | `string?` |
| `ColorClass` | `string?` |
| `SortOrder` | `int` |
| `IsActive` | `bool` |
| `IsPublic` | `bool` |
| `AllowAllExtensions` | `bool` |

### `UploadFileTypeExtensionRecord`

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `UploadFileTypeId` | `Guid` |
| `Pattern` | `string` |

### `UploadFileRecord`

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `UploadFileTypeId` | `Guid` |
| `FileName` | `string` |
| `ContentType` | `string` |
| `FileSizeBytes` | `long` |
| `Description` | `string?` |
| `IsPublic` | `bool` |

### `UploadFileOrganizationShareRecord`

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `UploadFileId` | `Guid` |
| `OrganizationId` | `Guid` |
| `Visibility` | [`FileShareVisibility`](../Ben.Data.Common/Enums.md#filesharevisibility) |
| `IsActive` | `bool` |

### `UploadFilePermissionRequestRecord`

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `UploadFileId` | `Guid` |
| `RequestedByAppUserId` | `Guid` |
| `PermissionType` | [`FilePermissionType`](../Ben.Data.Common/Enums.md#filepermissiontype) |
| `Status` | [`FilePermissionRequestStatus`](../Ben.Data.Common/Enums.md#filepermissionrequeststatus) |

### Other Entity Records (same shape as their entities)

| Record | Entity |
|---|---|
| `OrganizationAccessGrantRecord` | `OrganizationAccessGrant` |
| `OrganizationAddressRecord` | `OrganizationAddress` |
| `OrganizationAddressTypeRecord` | `OrganizationAddressType` |
| `OrganizationEmailRecord` | `OrganizationEmail` |
| `OrganizationEmailTypeRecord` | `OrganizationEmailType` |
| `OrganizationLinkRecord` | `OrganizationLink` |
| `OrganizationLinkTypeRecord` | `OrganizationLinkType` |
| `OrganizationNoteRecord` | `OrganizationNote` |
| `OrganizationNoteTypeRecord` | `OrganizationNoteType` |
| `OrganizationPageRecord` | `OrganizationPage` |
| `OrganizationPhoneRecord` | `OrganizationPhone` |
| `OrganizationPhoneTypeRecord` | `OrganizationPhoneType` |
| `UserAddressRecord` | `UserAddress` |
| `UserAddressTypeRecord` | `UserAddressType` |
| `UserEmailRecord` | `UserEmail` |
| `UserEmailTypeRecord` | `UserEmailType` |
| `UserLinkRecord` | `UserLink` |
| `UserLinkTypeRecord` | `UserLinkType` |
| `UserMessageRecord` | `UserMessage` |
| `UserMessageToRecord` | `UserMessageTo` |
| `UserMessageTypeRecord` | `UserMessageType` |
| `UserNoteRecord` | `UserNote` |
| `UserNoteTypeRecord` | `UserNoteType` |
| `UserPhoneRecord` | `UserPhone` |
| `UserPhoneTypeRecord` | `UserPhoneType` |

---

## Identity Records (`Ben.Service.Models.Identity`)

Thin projections of ASP.NET Core Identity join-table entities used by Admin endpoints.

| Record | Source |
|---|---|
| `IdentityRoleRecord` | `IdentityRole<Guid>` |
| `IdentityRoleClaimRecord` | `IdentityRoleClaim<Guid>` |
| `IdentityUserClaimRecord` | `IdentityUserClaim<Guid>` |
| `IdentityUserLoginRecord` | `IdentityUserLogin<Guid>` |
| `IdentityUserRoleRecord` | `IdentityUserRole<Guid>` |
| `IdentityUserTokenRecord` | `IdentityUserToken<Guid>` |
