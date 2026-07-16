# Ben.Data.Source — User Sub-Entities

All entities implement [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity) and follow the two-file pattern.

## Type Entities (lookup tables)

These entities define the *categories* or *types* available for user contact records.

| Entity | Table | Purpose |
|---|---|---|
| `UserAddressType` | `UserAddressTypes` | Category for a user address (e.g. Home, Work, Billing). |
| `UserEmailType` | `UserEmailTypes` | Category for a user email (e.g. Personal, Business). |
| `UserPhoneType` | `UserPhoneTypes` | Category for a user phone number. |
| `UserLinkType` | `UserLinkTypes` | Category for a user web link (e.g. LinkedIn, GitHub). |
| `UserMessageType` | `UserMessageTypes` | Category for internal messages. |
| `UserNoteType` | `UserNoteTypes` | Category for user notes. |

All type entities share the same shape:

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | PK |
| `Name` | `string` | Display name of the type. |
| `DateCreated`, `DateUpdated?` | `DateTime` | Audit timestamps. |
| `CreatedByAppUserId`, `UpdatedByAppUserId?` | `Guid` | Audit user references. |

---

## Data Entities

These entities store actual contact/content records for a user.

### `UserAddress`

**Table:** `UserAddresses`

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | PK |
| `AppUserId` | `Guid` | FK → `AppUser` |
| `UserAddressTypeId` | `Guid` | FK → `UserAddressType` |
| `StreetAddress1` | `string` | |
| `StreetAddress2` | `string?` | |
| `City` | `string` | |
| `State` | `string` | |
| `ZipCode` | `string` | |
| `Country` | `string` | |
| `IsPublic` | `bool` | Whether the address is visible to other users. |
| `Latitude` | `double?` | Geocoded latitude (added in migration `AddGeocodingMetadata`). |
| `Longitude` | `double?` | Geocoded longitude. |
| `GeocodingResponseJson` | `string?` | Raw geocoding API response (stored in stub, not generated). |
| `GeocodingResultType` | `string?` | Result type from geocoding response. |
| Audit columns | — | See [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity) |

### `UserEmail`

**Table:** `UserEmails`

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | PK |
| `AppUserId` | `Guid` | FK → `AppUser` |
| `UserEmailTypeId` | `Guid` | FK → `UserEmailType` |
| `EmailAddress` | `string` | The email address value. |
| `IsPublic` | `bool` | Visibility flag. |
| Audit columns | — | |

### `UserPhone`

**Table:** `UserPhones`

| Property | Type | Description |
|---|---|---|
| `AppUserId` | `Guid` | FK → `AppUser` |
| `UserPhoneTypeId` | `Guid` | FK → `UserPhoneType` |
| `PhoneNumber` | `string` | |
| `IsPublic` | `bool` | |
| Audit columns | — | |

### `UserLink`

**Table:** `UserLinks`

| Property | Type | Description |
|---|---|---|
| `AppUserId` | `Guid` | FK → `AppUser` |
| `UserLinkTypeId` | `Guid` | FK → `UserLinkType` |
| `LinkUrl` | `string` | The URL. |
| `IsPublic` | `bool` | |
| Audit columns | — | |

### `UserMessage`

**Table:** `UserMessages`

| Property | Type | Description |
|---|---|---|
| `SentByAppUserId` | `Guid` | FK → `AppUser` (sender) |
| `UserMessageTypeId` | `Guid` | FK → `UserMessageType` |
| `Subject` | `string` | Message subject. |
| `Body` | `string` | Message body. |
| `DateSent` | `DateTime` | When the message was sent. |
| Audit columns | — | |
| `UserMessageTos` (nav) | `ICollection<UserMessageTo>` | Recipients |

### `UserMessageTo`

**Table:** `UserMessageTos` — join table between message and recipient.  
**Implements:** [`IIDStd`](../Ben.Data.Common/Interfaces.md#iidstd) only (no audit columns)

| Property | Type | Description |
|---|---|---|
| `MessageId` | `Guid` | FK → `UserMessage` |
| `ToAppUserId` | `Guid` | FK → `AppUser` (recipient) |
| `DateLastRead` | `DateTime?` | When the recipient last opened the message. |
| `LastReadCount` | `int` | Number of times the message was opened. |

### `UserNote`

**Table:** `UserNotes`

| Property | Type | Description |
|---|---|---|
| `AppUserId` | `Guid` | FK → `AppUser` |
| `UserNoteTypeId` | `Guid` | FK → `UserNoteType` |
| `NoteSubject` | `string` | |
| `NoteBody` | `string` | |
| `IsPublic` | `bool` | |
| Audit columns | — | |
