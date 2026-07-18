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
An organisation-owned CMS page in a recursive hierarchy.

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | FK → `Organization` (NoAction) |
| `ParentPageId` | `Guid?` | FK → `OrganizationPage` (NoAction, self-ref, nullable = top-level) |
| `IsHome` | `bool` | Marks the home page for the org. |
| `PageTitle` | `string` | Human-readable page title. |
| `UrlName` | `string` | URL slug — unique per org. |
| `PageHtml` | `string` | Summary/intro HTML shown above sections. |
| `IsPublished` | `bool` | Published (visible) flag. |
| `IsPublic` | `bool` | Public vs members-only visibility. *(added 2026-07-18)* |
| `SortOrder` | `int` | Display order within the parent. |
| Audit columns | — | |

**Navigation:** `ParentPage`, `ChildPages`, `CmsSections`, `PagePermissions`.

> **2026-07-18:** Added `IsPublic` (bool), `ParentPageId` (Guid? self-ref for unlimited hierarchy), and nav collections for `CmsSections`, `PagePermissions`, `ChildPages`.

---

## CMS Entities *(added 2026-07-18)*

### `OrganizationLogo`

**Table:** `OrganizationLogos`  
**Implements:** `IAuditableEntity`

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | FK → `Organization` (Cascade) |
| `UploadFileId` | `Guid` | FK → `UploadFile` (NoAction) — the uploaded image |
| `AltText` | `string?` | HTML alt text for accessibility. |
| `IsActive` | `bool` | Only one logo should be active at a time. |
| `SortOrder` | `int` | Display order when multiple logos exist. |
| Audit columns | — | |

**Business rule:** when a new logo is created/updated with `IsActive=true`, the API automatically sets all other logos for the org to `IsActive=false`.

---

### `CmsSection`

**Table:** `CmsSections`  
**Implements:** `IAuditableEntity`

| Property | Type | Description |
|---|---|---|
| `OrganizationPageId` | `Guid` | FK → `OrganizationPage` (Cascade) |
| `SectionType` | `CmsSectionType` | Determines how `ContentJson` is interpreted. |
| `Title` | `string?` | Optional heading displayed above the section. |
| `ContentJson` | `string` | nvarchar(max) — JSON payload whose schema varies by `SectionType`. |
| `SortOrder` | `int` | Display order on the page. |
| `IsActive` | `bool` | Hidden when false. |
| Audit columns | — | |

See [`CmsSectionType`](../Ben.Data.Common/Enums.md#cmssectiontype) for ContentJson formats per type.

---

### `OrgMemberGroup`

**Table:** `OrgMemberGroups`  
**Implements:** `IAuditableEntity`  
Formal named group of org members used for per-page CMS permission grants.

| Property | Type | Description |
|---|---|---|
| `OrganizationId` | `Guid` | FK → `Organization` (Cascade) |
| `Name` | `string` | Group display name (e.g. "Editorial Team"). |
| `Description` | `string?` | Optional description. |
| `IsActive` | `bool` | Active flag. |
| `SortOrder` | `int` | Display order. |
| Audit columns | — | |

**Navigation:** `Members` (`ICollection<OrgMemberGroupMembership>`)

---

### `OrgMemberGroupMembership`

**Table:** `OrgMemberGroupMemberships`  
**Implements:** `IIDStd` (create-only junction — no update columns)

| Property | Type | Description |
|---|---|---|
| `OrgMemberGroupId` | `Guid` | FK → `OrgMemberGroup` (Cascade) |
| `OrganizationUserMembershipId` | `Guid` | FK → `OrganizationUserMembership` (NoAction — prevents cascade cycle) |
| `DateCreated` | `DateTime` | |
| `CreatedByAppUserId` | `Guid` | |

**Unique index:** `(OrgMemberGroupId, OrganizationUserMembershipId)`.  
**Cascade note:** The `OrganizationUserMembershipId` FK is `NoAction` because both `OrgMemberGroup → Organization` and `OrganizationUserMembership → Organization` are cascades; SQL Server forbids two cascade paths to the same table.

---

### `CmsPagePermission`

**Table:** `CmsPagePermissions`  
**Implements:** `IAuditableEntity`  
Grants per-page CMS actions to a specific org member OR a member group.

| Property | Type | Description |
|---|---|---|
| `OrganizationPageId` | `Guid` | FK → `OrganizationPage` (Cascade) |
| `AppUserId` | `Guid?` | FK → `AppUser` (NoAction). Null when the grant targets a group. |
| `OrgMemberGroupId` | `Guid?` | FK → `OrgMemberGroup` (NoAction). Null when the grant targets an individual. |
| `Actions` | `CmsPageAction` | `[Flags]` bitmask (View=1, Edit=2, Delete=4). |
| Audit columns | — | |

**Constraint (enforced in service):** at least one of `AppUserId` or `OrgMemberGroupId` must be non-null.
