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

**Mapped from:** [`Organization`](../Ben.Data.Source/Entities-Core.md) by [`OrganizationProfile`](../Ben.Service.Mappings/Profiles.md#entities-folder)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `Name` | `string` | Display name |
| `UrlName` | `string` | URL slug |
| `DateCreated` | `DateTime` | |
| `DateUpdated` | `DateTime?` | |
| `CreatedByAppUserId` | `Guid` | |
| `UpdatedByAppUserId` | `Guid?` | |

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

**Mapped from:** [`UploadFileType`](../Ben.Data.Source/Entities-Upload.md) by [`UploadFileTypeProfile`](../Ben.Service.Mappings/Profiles.md#entities-folder)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `Name` | `string` | |
| `Description` | `string?` | |
| `IconClass` | `string?` | CSS class string (e.g. `"t:Home"`, `"bi bi-file-earmark"`, `"fas fa-music"`) |
| `ColorClass` | `string?` | CSS class for color styling |
| `SortOrder` | `int` | |
| `IsActive` | `bool` | |
| `IsPublic` | `bool` | |
| `AllowAllExtensions` | `bool` | If true, any extension is accepted regardless of patterns |
| `AllowedPatterns` | `IReadOnlyList<string>` | Extension pattern strings (e.g. `".mp3"`, `".tx*"`) from linked `UploadFileTypeExtension` rows |
| `DateCreated` | `DateTime` | |
| `DateUpdated` | `DateTime?` | |
| `CreatedByAppUserId` | `Guid` | |
| `UpdatedByAppUserId` | `Guid?` | |

### `UploadFileTypeExtensionRecord`

| Property | Type |
|---|---|
| `Id` | `Guid` |
| `UploadFileTypeId` | `Guid` |
| `Pattern` | `string` |

### `UploadFileRecord`

**Mapped from:** [`UploadFile`](../Ben.Data.Source/Entities-Upload.md) by [`UploadFileProfile`](../Ben.Service.Mappings/Profiles.md#entities-folder)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `UploadFileTypeId` | `Guid` | FK → [`UploadFileType`](../Ben.Data.Source/Entities-Upload.md) |
| `AppUserId` | `Guid` | Owning user |
| `FileName` | `string` | Original display name |
| `StoredFileName` | `string` | UUID-based filename on disk |
| `ContentType` | `string` | MIME type |
| `FileSize` | `long` | Bytes |
| `StoragePath` | `string?` | Relative path in file storage (e.g. `"users/{userId}/{storedFileName}"`) |
| `Description` | `string?` | |
| `IsPublic` | `bool` | |
| `SortOrder` | `int` | |
| `DateCreated` | `DateTime` | |
| `DateUpdated` | `DateTime?` | |
| `CreatedByAppUserId` | `Guid` | |
| `UpdatedByAppUserId` | `Guid?` | |
| `ParentFileId` | `Guid?` | Set when created by clipping a parent audio file |
| `RegionStart` | `double?` | Clip start time in the parent file (seconds) |
| `RegionEnd` | `double?` | Clip end time in the parent file (seconds) |

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

### `OrganizationAccessGrantRecord`

**Mapped from:** [`OrganizationAccessGrant`](../Ben.Data.Source/Entities-Core.md) by [`OrganizationAccessGrantProfile`](../Ben.Service.Mappings/Profiles.md#entities-folder)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationId` | `Guid` | |
| `AppUserId` | `Guid` | The user this grant applies to |
| `TableName` | [`OrganizationSecurityTable`](../Ben.Data.Common/Enums.md#organizationsecuritytable) | Which logical table is being granted |
| `Actions` | [`OrganizationSecurityAction`](../Ben.Data.Common/Enums.md#organizationsecurityaction) | `[Flags]` bitmask of permitted operations |
| `DateCreated` | `DateTime` | |
| `DateUpdated` | `DateTime?` | |
| `CreatedByAppUserId` | `Guid` | |
| `UpdatedByAppUserId` | `Guid?` | |

### `OrganizationAddressRecord`

**Mapped from:** [`OrganizationAddress`](../Ben.Data.Source/Entities-Org.md) by [`OrganizationAddressProfile`](../Ben.Service.Mappings/Profiles.md#entities-folder)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationId` | `Guid` | |
| `OrganizationAddressTypeId` | `Guid` | |
| `StreetAddress1` | `string` | |
| `StreetAddress2` | `string?` | |
| `ZipCode` | `string` | |
| `City` | `string` | |
| `State` | `string` | |
| `Country` | `string` | |
| `IsPublic` | `bool` | |
| `Latitude` | `decimal?` | WGS-84 |
| `Longitude` | `decimal?` | WGS-84 |
| `SortOrder` | `int` | |
| Audit cols | — | DateCreated, DateUpdated, CreatedByAppUserId, UpdatedByAppUserId |

### `UserMessageRecord`

**Mapped from:** [`UserMessage`](../Ben.Data.Source/Entities-User.md) by [`UserMessageProfile`](../Ben.Service.Mappings/Profiles.md#entities-folder)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `UserMessageTypeId` | `Guid` | |
| `MessageSubject` | `string?` | |
| `MessageBody` | `string` | |
| `ParentMessageId` | `Guid?` | Thread parent |
| `DateArchived` | `DateTime?` | Set when user archives the message |
| Audit cols | — | DateCreated, DateUpdated, CreatedByAppUserId, UpdatedByAppUserId |

### `UserMessageToRecord`

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `MessageId` | `Guid` | FK → UserMessage |
| `ToAppUserId` | `Guid` | Recipient |
| `DateLastRead` | `DateTime?` | When the recipient last read it |
| `LastReadCount` | `int` | How many times read |

### Standard Entity Records (same shape as their source entities)

All records below are flat mirrors of their source entities with standard audit columns (`DateCreated`, `DateUpdated`, `CreatedByAppUserId`, `UpdatedByAppUserId`). Each is mapped by the profile in [`Ben.Service.Mappings/Profiles.md`](../Ben.Service.Mappings/Profiles.md).

| Record | Source Entity | Key Fields |
|---|---|---|
| `OrganizationAddressTypeRecord` | [`OrganizationAddressType`](../Ben.Data.Source/Entities-Org.md) | Name, Description, SortOrder, IsActive, IsPublic |
| `OrganizationEmailRecord` | [`OrganizationEmail`](../Ben.Data.Source/Entities-Org.md) | OrganizationId, OrganizationEmailTypeId, EmailAddress, IsPublic, SortOrder |
| `OrganizationEmailTypeRecord` | [`OrganizationEmailType`](../Ben.Data.Source/Entities-Org.md) | Name, Description, SortOrder, IsActive, IsPublic |
| `OrganizationLinkRecord` | [`OrganizationLink`](../Ben.Data.Source/Entities-Org.md) | OrganizationId, OrganizationLinkTypeId, Url, Label, IsPublic, SortOrder |
| `OrganizationLinkTypeRecord` | [`OrganizationLinkType`](../Ben.Data.Source/Entities-Org.md) | Name, Description, SortOrder, IsActive, IsPublic |
| `OrganizationNoteRecord` | [`OrganizationNote`](../Ben.Data.Source/Entities-Org.md) | OrganizationId, OrganizationNoteTypeId, NoteText (HTML), IsPublic, SortOrder |
| `OrganizationNoteTypeRecord` | [`OrganizationNoteType`](../Ben.Data.Source/Entities-Org.md) | Name, Description, SortOrder, IsActive, IsPublic |
| `OrganizationPageRecord` | [`OrganizationPage`](../Ben.Data.Source/Entities-Org.md) | OrganizationId, Title, UrlSlug, IsPublic, SortOrder |
| `OrganizationPhoneRecord` | [`OrganizationPhone`](../Ben.Data.Source/Entities-Org.md) | OrganizationId, OrganizationPhoneTypeId, PhoneNumber, IsPublic, SortOrder |
| `OrganizationPhoneTypeRecord` | [`OrganizationPhoneType`](../Ben.Data.Source/Entities-Org.md) | Name, Description, SortOrder, IsActive, IsPublic |
| `UserAddressRecord` | [`UserAddress`](../Ben.Data.Source/Entities-User.md) | AppUserId, UserAddressTypeId, street, city, state, zip, country, IsPublic, SortOrder |
| `UserAddressTypeRecord` | [`UserAddressType`](../Ben.Data.Source/Entities-User.md) | Name, Description, SortOrder, IsActive, IsPublic |
| `UserEmailRecord` | [`UserEmail`](../Ben.Data.Source/Entities-User.md) | AppUserId, UserEmailTypeId, EmailAddress, IsPublic, SortOrder |
| `UserEmailTypeRecord` | [`UserEmailType`](../Ben.Data.Source/Entities-User.md) | Name, Description, SortOrder, IsActive, IsPublic |
| `UserLinkRecord` | [`UserLink`](../Ben.Data.Source/Entities-User.md) | AppUserId, UserLinkTypeId, Url, Label, IsPublic, SortOrder |
| `UserLinkTypeRecord` | [`UserLinkType`](../Ben.Data.Source/Entities-User.md) | Name, Description, SortOrder, IsActive, IsPublic |
| `UserNoteRecord` | [`UserNote`](../Ben.Data.Source/Entities-User.md) | AppUserId, UserNoteTypeId, NoteText (HTML), IsPublic, SortOrder |
| `UserNoteTypeRecord` | [`UserNoteType`](../Ben.Data.Source/Entities-User.md) | Name, Description, SortOrder, IsActive, IsPublic |
| `UserPhoneRecord` | [`UserPhone`](../Ben.Data.Source/Entities-User.md) | AppUserId, UserPhoneTypeId, PhoneNumber, IsPublic, SortOrder |
| `UserPhoneTypeRecord` | [`UserPhoneType`](../Ben.Data.Source/Entities-User.md) | Name, Description, SortOrder, IsActive, IsPublic |
| `UploadFileTypeExtensionRecord` | [`UploadFileTypeExtension`](../Ben.Data.Source/Entities-Upload.md) | UploadFileTypeId, Pattern (e.g. `".mp3"`, `".tx*"`) |
| `UploadFileOrganizationShareRecord` | [`UploadFileOrganizationShare`](../Ben.Data.Source/Entities-Upload.md) | UploadFileId, OrganizationId, `Visibility` ([`FileShareVisibility`](../Ben.Data.Common/Enums.md#filesharevisilbity)), IsActive |
| `UploadFilePermissionRequestRecord` | [`UploadFilePermissionRequest`](../Ben.Data.Source/Entities-Upload.md) | UploadFileId, RequestedByAppUserId, `PermissionType` ([`FilePermissionType`](../Ben.Data.Common/Enums.md#filepermissiontype)), `Status` ([`FilePermissionRequestStatus`](../Ben.Data.Common/Enums.md#filepermissionrequeststatus)) |

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

---

### `UploadFileAudioConfigRecord` *(added 2026-07-18)*

**Mapped from:** [`UploadFileAudioConfig`](../Ben.Data.Source/Entities-Upload.md) by [`UploadFileAudioConfigProfile`](../Ben.Service.Mappings/Profiles.md#new-profiles-added-2026-07-1819)

Per-file WaveSurfer player settings. Null color/string fields mean "use Telerik theme CSS defaults at runtime".

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `UploadFileId` | `Guid` | |
| `WaveColor` | `string?` | Hex/CSS; null = `--kendo-color-primary` |
| `ProgressColor` | `string?` | Null = `--kendo-color-primary-emphasis` |
| `CursorColor` | `string?` | Null = `--kendo-body-text` |
| `CursorWidth` | `int?` | |
| `Height` | `int?` | Null = auto |
| `BarWidth` | `int?` | Null = solid waveform |
| `BarGap` | `int?` | |
| `BarRadius` | `int?` | |
| `BarHeight` | `double?` | |
| `BarAlign` | `string?` | `"top"`, `"bottom"`, or null (center) |
| `Normalize` | `bool` | Scale amplitude to fill height |
| `DragToSeek` | `bool` | |
| `HideScrollbar` | `bool` | |
| `AudioRate` | `double?` | Playback speed multiplier |
| `EnableHover` | `bool` | |
| `EnableTimeline` | `bool` | |
| `EnableZoom` | `bool` | |
| `EnableMinimap` | `bool` | |
| `EnableSpectrogram` | `bool` | |
| `EnableSpectrogramWindowed` | `bool` | |
| `EnableEnvelope` | `bool` | |
| `EnableRegions` | `bool` | |
| `HoverOptionsJson` | `string?` | WsHoverOptions serialized |
| `TimelineOptionsJson` | `string?` | WsTimelineOptions serialized |
| `ZoomOptionsJson` | `string?` | WsZoomOptions serialized |
| `MinimapOptionsJson` | `string?` | WsMinimapOptions serialized |
| `SpectrogramOptionsJson` | `string?` | WsSpectrogramOptions serialized |
| `SpectrogramWindowedOptionsJson` | `string?` | |
| `EnvelopeOptionsJson` | `string?` | WsEnvelopeOptions serialized |
| `InitialHeight` | `string` | Default `"200px"` |
| `MinHeight` | `string` | Default `"80px"` |
| `MaxHeight` | `string` | Default `"800px"` |
| `ShowControls` | `bool` | |
| `MinZoom` | `double` | Default 10 |
| `MaxZoom` | `double` | Default 1000 |
| Audit cols | — | DateCreated, DateUpdated, CreatedByAppUserId, UpdatedByAppUserId |

**Request:** `UpsertAudioConfigRequest` — full replacement PUT; same fields with matching defaults. Endpoint: `PUT /api/upload-files/{id}/audio-config`.

---

### `UploadFileRegionNoteRecord` (added 2026-07-19)

| Field | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `UploadFileId` | `Guid` | Parent file |
| `RegionStart` | `double` | Absolute start time in original file (seconds) |
| `RegionEnd` | `double` | Absolute end time in original file (seconds) |
| `RegionLabel` | `string?` | Optional WaveSurfer region label |
| `TimeOffset` | `double?` | Null = whole-region note; value = absolute file time for point-in-time note |
| `NoteHtml` | `string` | Rich text (TelerikEditor output) |
| `IsPublic` | `bool` | |
| Audit cols | — | |

**Requests:** `CreateRegionNoteRequest`, `UpdateRegionNoteRequest`.

---

### `UploadFileVoteRecord` (added 2026-07-19)

| Field | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `UploadFileId` | `Guid` | |
| `AppUserId` | `Guid` | |
| `Score` | `int` | 1 = upvote, -1 = downvote |
| `DateCreated` | `DateTime` | |
| `DateUpdated` | `DateTime?` | |

**Summary:** `UploadFileVoteSummary(UploadFileId, UpvoteCount, DownvoteCount, TotalScore, TotalVotes, UserScore?)`.  
**Request:** `UpsertVoteRequest(Score)`.

---

### `ClipAudioRequest` / `UploadFileClip`

`ClipAudioRequest(Start, End, Label?, IsPublic, UploadFileTypeId)` — passed to `POST /api/upload-files/{id}/clip`.  
Result is a new `UploadFileRecord` with `ParentFileId`, `RegionStart`, `RegionEnd` set. No separate record type — the clipped file is a regular `UploadFileRecord`.

---

## CMS & Group Records (`Ben.Service.Models.Entities`) *(added 2026-07-18)*

### `CmsSectionRecord`

**Mapped from:** [`CmsSection`](../Ben.Data.Source/Entities-Org.md#cmssection) by [`CmsSectionProfile`](../Ben.Service.Mappings/Profiles.md#entities-folder)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationPageId` | `Guid` | Parent page |
| `SectionType` | [`CmsSectionType`](../Ben.Data.Common/Enums.md#cmssectiontype) | RichText, ImageBanner, FileGallery, ContactInfo, MemberRoster, CustomHtml |
| `Title` | `string?` | Display heading |
| `ContentJson` | `string` | Section-type-specific JSON (default `{}`) |
| `SortOrder` | `int` | |
| `IsActive` | `bool` | |
| Audit cols | — | DateCreated, DateUpdated, CreatedByAppUserId, UpdatedByAppUserId |

### `CmsPagePermissionRecord`

**Mapped from:** [`CmsPagePermission`](../Ben.Data.Source/Entities-Org.md#cmsPagePermission)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationPageId` | `Guid` | |
| `AppUserId` | `Guid?` | Null when the grant targets a member group |
| `OrgMemberGroupId` | `Guid?` | Null when the grant targets an individual |
| `Actions` | [`CmsPageAction`](../Ben.Data.Common/Enums.md#cmspageaction) | `[Flags]` — View=1, Edit=2, Delete=4 |
| Audit cols | — | |

### `OrgMemberGroupRecord`

**Mapped from:** [`OrgMemberGroup`](../Ben.Data.Source/Entities-Org.md#orgmembergroup)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationId` | `Guid` | |
| `Name` | `string` | |
| `Description` | `string?` | |
| `IsActive` | `bool` | |
| `SortOrder` | `int` | |
| Audit cols | — | |

### `OrgMemberGroupMembershipRecord`

Junction between a member group and an org member.

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `OrgMemberGroupId` | `Guid` | |
| `OrganizationUserMembershipId` | `Guid` | |
| `DateCreated` | `DateTime` | |
| `CreatedByAppUserId` | `Guid` | |

### `OrganizationLogoRecord`

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationId` | `Guid` | |
| `StoredFileName` | `string` | UUID-based name on disk |
| `ContentType` | `string` | `image/svg+xml`, `image/png`, etc. |
| `FileSizeBytes` | `long` | |
| `IsActive` | `bool` | |
| `SortOrder` | `int` | |
| Audit cols | — | |

---

## Organization Participation Records *(added 2026-07-22)*

### `OrganizationMembershipRequestRecord`

**Mapped from:** [`OrganizationMembershipRequest`](../Ben.Data.Source/Entities-Org.md#organizationmembershiprequest) by [`OrganizationMembershipRequestProfile`](../Ben.Service.Mappings/Profiles.md#new-profiles-added-2026-07-22)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationId` | `Guid` | |
| `OrganizationName` | `string` | Denormalized snapshot from `Organization.Name` |
| `AppUserId` | `Guid` | Applicant user ID |
| `ApplicantDisplayName` | `string` | From `Applicant.DisplayName` |
| `ApplicantEmail` | `string` | From `Applicant.Email` |
| `RequestMessage` | `string?` | Applicant's cover message |
| `Status` | [`OrganizationMembershipRequestStatus`](../Ben.Data.Common/Enums.md#organizationmembershiprequeststatus) | Pending/Accepted/Denied/Withdrawn |
| `RespondedByDisplayName` | `string?` | Display name of the admin who accepted or denied; null while Pending |
| `DateCreated` | `DateTime` | When the application was submitted |
| `DateResponded` | `DateTime?` | When it was accepted, denied, or withdrawn (`DateUpdated`) |

---

### `OrganizationFileRecord`

**Mapped from:** [`OrganizationFile`](../Ben.Data.Source/Entities-Org.md#organizationfile) by [`OrganizationFileProfile`](../Ben.Service.Mappings/Profiles.md#new-profiles-added-2026-07-22)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationId` | `Guid` | |
| `UploadFileTypeId` | `Guid` | |
| `FileTypeName` | `string` | Denormalized from `UploadFileType.Name` |
| `FileName` | `string` | Original display name |
| `ContentType` | `string` | MIME type |
| `FileSize` | `long` | Bytes |
| `Description` | `string?` | |
| `IsPublic` | `bool` | |
| `SortOrder` | `int` | |
| `SourceUploadFileId` | `Guid?` | If copied from a user file, the original `UploadFile.Id` |
| `PublishedByDisplayName` | `string?` | Display name of the approver; null if not yet published |
| `DatePublished` | `DateTime?` | UTC when the file was approved for public access |
| `CreatedByDisplayName` | `string` | Who uploaded/created the file |
| `DateCreated` | `DateTime` | |
| `DateUpdated` | `DateTime?` | |

---

### `OrganizationFileDeleteLogRecord`

**Mapped from:** [`OrganizationFileDeleteLog`](../Ben.Data.Source/Entities-Org.md#organizationfiledeleterelog)

Immutable snapshot returned from `GET /api/organizations/{orgId}/files/delete-log`. Every field is a point-in-time copy — no FKs on the source entity.

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationId` | `Guid` | Stored value (no FK) |
| `OrganizationName` | `string` | Snapshot |
| `OriginalFileId` | `Guid` | ID of the now-deleted `OrganizationFile` row |
| `FileName` | `string` | |
| `ContentType` | `string` | |
| `FileSize` | `long` | |
| `StoragePath` | `string?` | Storage path used when deleting from disk |
| `SourceUploadFileId` | `Guid?` | If the file was copied from a user file |
| `WasPublic` | `bool` | |
| `WasPublishedByDisplayName` | `string?` | Snapshot of publisher name |
| `WasDatePublished` | `DateTime?` | |
| `DeletedByAppUserId` | `Guid` | |
| `DeletedByDisplayName` | `string` | Snapshot |
| `DateDeleted` | `DateTime` | UTC timestamp |

---

### `AddressMapConfigRecord`

**Mapped from:** [`OrganizationAddressMapConfig`](../Ben.Data.Source/Entities-Org.md#organizationaddressmapconfig) by [`OrganizationAddressMapConfigProfile`](../Ben.Service.Mappings/Profiles.md#new-profiles-added-2026-07-22)

| Property | Type | Default | Description |
|---|---|---|---|
| `Id` | `Guid` | | |
| `OrganizationAddressId` | `Guid` | | Parent address (1-to-1) |
| `IsOnMap` | `bool` | false | Master toggle for map display |
| `ShowMarker` | `bool` | true | Render a pin marker |
| `ShowRegion` | `bool` | false | Render a filled circle region |
| `RegionRadiusMiles` | `double` | 1.0 | Radius of the region |
| `MarkerColor` | `string` | `"#e63535"` | Hex color |
| `MarkerIconKey` | `string?` | null | Telerik `SvgIcon` property name (see `AddressMapIconRegistry`) |
| `RegionFillColor` | `string` | `"#3388ff"` | |
| `RegionFillOpacity` | `double` | 0.2 | 0–1 |
| `RegionStrokeColor` | `string` | `"#1155cc"` | |
| `RegionStrokeOpacity` | `double` | 0.8 | 0–1 |
| `RegionStrokeWidth` | `double` | 2.0 | Pixels |

---

### `OrgFileCopyClientResult`

Returned from [`IBenAdminClient.CopyFileFromUserAsync`](../Ben.Web.Library/Services.md#organization-file-methods-added-2026-07-22). Positional record.

| Parameter | Type | Description |
|---|---|---|
| `File` | [`OrganizationFileRecord`](#organizationfilerecord) | The newly created org file |
| `CanPublishImmediately` | `bool` | True when the caller has `OrganizationFiles-Update` permission — the "Publish immediately" checkbox should be shown |
| `PublishedImmediately` | `bool` | True when `PublishImmediately=true` was requested and the file was auto-published |
