# Ben.Data.Source — Organisation Sub-Entities

All entities implement [`IAuditableEntity`](../Ben.Data.Common/Interfaces.md#iauditableentity) and follow the two-file pattern.

## Type Entities (lookup tables)

| Entity | Table | Purpose |
|---|---|---|
| `OrganizationAddressType` | `OrganizationAddressTypes` | Category for an organisation address. |
| `OrganizationEmailType` | `OrganizationEmailTypes` | Category for an organisation email. |
| `OrganizationPhoneType` | `OrganizationPhoneTypes` | Category for an organisation phone. |
| `OrganizationLinkType` | `OrganizationLinkTypes` | Category for an organisation web link. |
| `OrganizationNoteType` | `OrganizationNoteTypes` | Category for organisation notes. |

All share the same shape as User type entities (Id, Name, audit columns).

---

## Data Entities

### `OrganizationAddress`

**Table:** `OrganizationAddresses`

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | FK → `Organization` |
| `OrganizationAddressTypeId` | `Guid` | FK → `OrganizationAddressType` |
| `StreetAddress1`, `StreetAddress2?` | `string` | Street lines. |
| `City`, `State`, `ZipCode`, `Country` | `string` | Address components. |
| `Latitude?`, `Longitude?` | `double?` | Geocoding data. |
| `IsPublic` | `bool` | Visibility flag. |
| Audit columns | — | |

### `OrganizationEmail`

**Table:** `OrganizationEmails`

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | FK → `Organization` |
| `OrganizationEmailTypeId` | `Guid` | FK → `OrganizationEmailType` |
| `EmailAddress` | `string` | |
| `IsPublic` | `bool` | |
| Audit columns | — | |

### `OrganizationPhone`

**Table:** `OrganizationPhones`

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | FK → `Organization` |
| `OrganizationPhoneTypeId` | `Guid` | FK → `OrganizationPhoneType` |
| `PhoneNumber` | `string` | |
| `IsPublic` | `bool` | |
| Audit columns | — | |

### `OrganizationLink`

**Table:** `OrganizationLinks`

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | FK → `Organization` |
| `OrganizationLinkTypeId` | `Guid` | FK → `OrganizationLinkType` |
| `LinkUrl` | `string` | |
| `IsPublic` | `bool` | |
| Audit columns | — | |

### `OrganizationNote`

**Table:** `OrganizationNotes`

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | FK → `Organization` |
| `OrganizationNoteTypeId` | `Guid` | FK → `OrganizationNoteType` |
| `NoteSubject` | `string` | |
| `NoteBody` | `string` | |
| `IsPublic` | `bool` | |
| Audit columns | — | |

### `OrganizationPage`

**Table:** `OrganizationPages`  
An organisation-owned content page.

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | FK → `Organization` |
| `Title` | `string` | Page title. |
| `Slug` | `string?` | URL-safe page identifier. |
| `Content` | `string?` | Page body (HTML or Markdown). |
| `IsPublic` | `bool` | Whether the page is publicly visible. |
| Audit columns | — | |
