# Ben.Service.Models — Admin Records

All records are in the `Ben.Service.Models.Admin` namespace and use `record` type with `init`-only properties.

---

## `AppUserAdminRecord`

Full user profile returned by `GET /api/admin/app-users` and `PUT /api/admin/app-users/{id}/profile`.  
Mirrors the full `AppUser` + `IdentityUser<Guid>` surface.

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | User PK |
| `UserName` | `string?` | Login username |
| `NormalizedUserName` | `string?` | Uppercase normalised username |
| `Email` | `string?` | Email address |
| `NormalizedEmail` | `string?` | Uppercase normalised email |
| `IsEmailConfirmed` | `bool` | Whether email has been verified |
| `PhoneNumber` | `string?` | |
| `IsPhoneNumberConfirmed` | `bool` | |
| `IsTwoFactorEnabled` | `bool` | |
| `LockoutEnd` | `DateTimeOffset?` | When lockout expires (null = not locked) |
| `IsLockoutEnabled` | `bool` | Whether lockout feature is enabled for the account |
| `AccessFailedCount` | `int` | Failed login attempts since last successful login |
| `DisplayName` | `string?` | Human-readable display name |
| `DateCreated` | `DateTime` | Account creation date |
| `DateUpdated` | `DateTime?` | Last profile update |

---

## `AppUserDetailAdminRecord`

**Endpoint:** `GET /api/admin/app-users/{id}/detail`

Aggregate record combining the user profile with all related sub-entity lists.  
Allows a single API call to populate the full user detail page in the SuperAdmin UI.

| Property | Type | Description |
|---|---|---|
| `User` | `AppUserAdminRecord` | Core user profile |
| `Addresses` | `IReadOnlyList<UserAddressAdminRecord>` | All user addresses |
| `Emails` | `IReadOnlyList<UserEmailAdminRecord>` | All user email addresses |
| `Phones` | `IReadOnlyList<UserPhoneAdminRecord>` | All user phone numbers |
| `Links` | `IReadOnlyList<UserLinkAdminRecord>` | All user web links |
| `Notes` | `IReadOnlyList<UserNoteAdminRecord>` | All user notes |
| `Messages` | `IReadOnlyList<UserMessageAdminRecord>` | Messages sent by the user |
| `Memberships` | `IReadOnlyList<OrganizationUserMembershipAdminRecord>` | Organisation memberships |
| `UploadFiles` | `IReadOnlyList<UploadFileAdminRecord>` | Files uploaded by the user |

---

## `OrganizationUserMembershipAdminRecord`

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Membership PK |
| `OrganizationId` | `Guid` | FK → Organisation |
| `AppUserId` | `Guid` | FK → AppUser |
| `Role` | [`OrganizationMemberRole`](../Ben.Data.Common/Enums.md#organizationmemberrole) | User's role in the organisation |
| `IsActive` | `bool` | Active membership flag |
| `DateCreated` | `DateTime` | |
| `DateUpdated` | `DateTime?` | |
| `CreatedByAppUserId` | `Guid` | |
| `UpdatedByAppUserId` | `Guid?` | |

---

## Other Admin Records

All following records mirror their entity counterpart with full audit columns and are used by SuperAdmin CRUD endpoints.

| Record | Entity | Key Fields |
|---|---|---|
| `AppRoleAdminRecord` | `IdentityRole<Guid>` | `Id`, `Name`, `NormalizedName` |
| `OrganizationAdminRecord` | `Organization` | `Id`, `Name`, `UrlName`, audit |
| `OrganizationAccessGrantAdminRecord` | `OrganizationAccessGrant` | `Id`, `OrganizationId`, `AppUserId`, `TableName`, `Actions` |
| `OrganizationAddressAdminRecord` | `OrganizationAddress` | Address components, `IsPublic`, lat/lng |
| `OrganizationAddressTypeAdminRecord` | `OrganizationAddressType` | `Id`, `Name` |
| `OrganizationEmailAdminRecord` | `OrganizationEmail` | `Id`, `EmailAddress`, `IsPublic` |
| `OrganizationEmailTypeAdminRecord` | `OrganizationEmailType` | `Id`, `Name` |
| `OrganizationLinkAdminRecord` | `OrganizationLink` | `Id`, `LinkUrl`, `IsPublic` |
| `OrganizationLinkTypeAdminRecord` | `OrganizationLinkType` | `Id`, `Name` |
| `OrganizationNoteAdminRecord` | `OrganizationNote` | `Id`, `NoteSubject`, `NoteBody`, `IsPublic` |
| `OrganizationNoteTypeAdminRecord` | `OrganizationNoteType` | `Id`, `Name` |
| `OrganizationPageAdminRecord` | `OrganizationPage` | `Id`, `Title`, `Slug`, `Content`, `IsPublic` |
| `OrganizationPhoneAdminRecord` | `OrganizationPhone` | `Id`, `PhoneNumber`, `IsPublic` |
| `OrganizationPhoneTypeAdminRecord` | `OrganizationPhoneType` | `Id`, `Name` |
| `UploadFileAdminRecord` | `UploadFile` | `Id`, `FileName`, `ContentType`, `FileSizeBytes`, `IsPublic` |
| `UploadFileOrganizationShareAdminRecord` | `UploadFileOrganizationShare` | `Id`, `UploadFileId`, `OrganizationId`, `Visibility`, `IsActive` |
| `UploadFilePermissionRequestAdminRecord` | `UploadFilePermissionRequest` | `Id`, `UploadFileId`, `RequestedByAppUserId`, `PermissionType`, `Status` |
| `UploadFileTypeAdminRecord` | `UploadFileType` | `Id`, `Name`, `AllowAllExtensions`, display metadata |
| `UserAddressAdminRecord` | `UserAddress` | Address + geocoding fields |
| `UserAddressTypeAdminRecord` | `UserAddressType` | `Id`, `Name` |
| `UserEmailAdminRecord` | `UserEmail` | `Id`, `EmailAddress`, `IsPublic` |
| `UserEmailTypeAdminRecord` | `UserEmailType` | `Id`, `Name` |
| `UserLinkAdminRecord` | `UserLink` | `Id`, `LinkUrl`, `IsPublic` |
| `UserLinkTypeAdminRecord` | `UserLinkType` | `Id`, `Name` |
| `UserMessageAdminRecord` | `UserMessage` | `Id`, `Subject`, `Body`, `DateSent` |
| `UserMessageToAdminRecord` | `UserMessageTo` | `MessageId`, `ToAppUserId`, `DateLastRead`, `LastReadCount` |
| `UserMessageTypeAdminRecord` | `UserMessageType` | `Id`, `Name` |
| `UserNoteAdminRecord` | `UserNote` | `Id`, `NoteSubject`, `NoteBody`, `IsPublic` |
| `UserNoteTypeAdminRecord` | `UserNoteType` | `Id`, `Name` |
| `UserPhoneAdminRecord` | `UserPhone` | `Id`, `PhoneNumber`, `IsPublic` |
| `UserPhoneTypeAdminRecord` | `UserPhoneType` | `Id`, `Name` |
