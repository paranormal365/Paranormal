# Ben.Data.Common — Enums

All enums are in the `Ben.Data.Common.Enums` namespace.

---

## `AuditAction`

**File:** [`Ben.Data.Common/Enums/AuditAction.cs`](../../../Ben.Data.Common/Enums/AuditAction.cs)  
**Used by:** [`AuditLog`](../../Ben.Data.Source/Entities-Audit.md), [`AuditLogService`](../../Ben.Service.RepositoryService/Services.md#auditlogservice), [`AuditChangeTracker`](Helpers.md#auditchangetracker)

### Summary
Identifies the type of CRUD operation recorded in an `AuditLog` entry.

| Value | Int | Description |
|---|---|---|
| `Create` | 1 | A new entity was created. `ChangesJson` contains a full property snapshot. |
| `Update` | 2 | An existing entity was modified. `ChangesJson` contains only changed properties with before/after values. |
| `Delete` | 3 | An entity was permanently removed. `ChangesJson` contains a full snapshot captured before deletion. |

---

## `OrganizationMemberRole`

**File:** [`Ben.Data.Common/Enums/OrganizationMemberRole.cs`](../../../Ben.Data.Common/Enums/OrganizationMemberRole.cs)  
**Stored on:** `OrganizationUserMembership.Role` (int column)  
**Used by:** [`IOrganizationSecurityService`](../../Ben.Service.Security/Interfaces.md), [`OrganizationSecurityService`](../../Ben.Service.RepositoryService/Services.md#organizationsecurityservice)

### Summary
Defines the hierarchical roles a user can hold within an organization.  
Integer values are ordered from **highest privilege (1) to lowest (5)**, allowing range checks:  
`membership.Role <= OrganizationMemberRole.Administrator` → user has elevated access.

| Value | Int | Description |
|---|---|---|
| `Owner` | 1 | Full ownership. One owner per organization, set at registration. |
| `Administrator` | 2 | Administrative rights for day-to-day management. |
| `Manager` | 3 | Can manage content; no access to membership/security settings. |
| `Member` | 4 | Standard membership; permissions governed by `OrganizationAccessGrant` rows. |
| `Viewer` | 5 | Read-only access. |

**Note:** Defined here (not in `Ben.Service.Security`) because the entity layer needs the type. The security project imports it via `using OrganizationMemberRole = Ben.Data.Common.Enums.OrganizationMemberRole;`.

---

## `OrganizationSecurityAction`

**File:** [`Ben.Data.Common/Enums/OrganizationSecurityAction.cs`](../../../Ben.Data.Common/Enums/OrganizationSecurityAction.cs)  
**Stored on:** `OrganizationAccessGrant.Actions` (int bitmask column)

### Summary
Represents the set of CRUD operations permitted on an `OrganizationAccessGrant` row.  
**`[Flags]` enum** — a single grant row stores all permitted actions for a (user, org, table) tuple as a bitmask.

> `Ben.Service.Security.Enums.OrganizationSecurityAction` was a separate [Flags] enum in the security layer; it has been removed. `GlobalUsings.cs` in `Ben.Service.Security` now aliases this Common type.

| Value | Int | Description |
|---|---|---|
| `None` | 0 | No permissions. |
| `Create` | 1 | Permission to create new records. |
| `Update` | 3 | Permission to modify existing records. |
| `Delete` | 4 | Permission to delete records. |

---

## `OrganizationSecurityTable`

**File:** [`Ben.Data.Common/Enums/OrganizationSecurityTable.cs`](../../../Ben.Data.Common/Enums/OrganizationSecurityTable.cs)  
**Stored on:** `OrganizationAccessGrant.TableName` (int column)

### Summary
Identifies a domain table for which per-user access grants can be configured.  
26 values, each corresponding to a physical table in BenDb.

> A parallel enum exists at `Ben.Service.Security.Enums.OrganizationSecurityTable` with different integer values. See [Ben.Service.Security Enums](../../Ben.Service.Security/Enums.md).

| Value | Int |
|---|---|
| `Organization` | 1 |
| `OrganizationAddress` | 2 |
| `OrganizationAddressType` | 3 |
| `OrganizationEmail` | 4 |
| `OrganizationEmailType` | 5 |
| `OrganizationLink` | 6 |
| `OrganizationLinkType` | 7 |
| `OrganizationNote` | 8 |
| `OrganizationNoteType` | 9 |
| `OrganizationPage` | 10 |
| `OrganizationPhone` | 11 |
| `OrganizationPhoneType` | 12 |
| `AppUser` | 13 |
| `UserAddress` | 14 |
| `UserAddressType` | 15 |
| `UserEmail` | 16 |
| `UserEmailType` | 17 |
| `UserLink` | 18 |
| `UserLinkType` | 19 |
| `UserMessage` | 20 |
| `UserMessageTo` | 21 |
| `UserMessageType` | 22 |
| `UserNote` | 23 |
| `UserNoteType` | 24 |
| `UserPhone` | 25 |
| `UserPhoneType` | 26 |
| `CmsSection` | 27 |
| `OrgMemberGroup` | 28 |

> **2026-07-18:** `CmsSection=27` and `OrgMemberGroup=28` added alongside the CMS feature. The parallel `Ben.Service.Security` enum received matching entries at `CmsSection=26` and `OrgMemberGroup=27` (its values differ by 1 throughout).

---

## `CmsSectionType`

**File:** [`Ben.Data.Common/Enums/CmsSectionType.cs`](../../../Ben.Data.Common/Enums/CmsSectionType.cs)  
**Stored on:** `CmsSection.SectionType` (int column)  
**Added:** 2026-07-18

### Summary
Identifies the kind of content stored in a `CmsSection`. Determines how `ContentJson` is interpreted by the UI and preview renderer.

| Value | Int | ContentJson format |
|---|---|---|
| `RichText` | 1 | `{ "html": "<p>...</p>" }` — authored with `TelerikEditor` |
| `ImageBanner` | 2 | `{ "uploadFileId": "guid", "altText": "...", "linkUrl": "..." }` |
| `FileGallery` | 3 | `{ "uploadFileIds": ["guid1", "guid2"] }` |
| `ContactInfo` | 4 | `{ "showAddresses": bool, "showEmails": bool, "showPhones": bool, "showLinks": bool }` |
| `MemberRoster` | 5 | `{ "memberIds": ["guid"], "showRole": bool, "showBio": bool }` |
| `CustomHtml` | 6 | `{ "html": "<div>...</div>" }` — raw HTML block |

---

## `CmsPageAction`

**File:** [`Ben.Data.Common/Enums/CmsPageAction.cs`](../../../Ben.Data.Common/Enums/CmsPageAction.cs)  
**Type:** `[Flags]` enum  
**Stored on:** `CmsPagePermission.Actions` (int bitmask column)  
**Added:** 2026-07-18

### Summary
Bitmask of per-page CMS actions that can be granted to individual org members or member groups via `CmsPagePermission`. Intentionally separate from `OrganizationSecurityAction` to allow fine-grained page-level access independent of org-level table grants.

| Value | Int | Description |
|---|---|---|
| `None` | 0 | No access. |
| `View` | 1 | Permission to view this page even when restricted to specific members. |
| `Edit` | 2 | Permission to edit this page's content and sections. |
| `Delete` | 4 | Permission to delete this page. |

**File:** [`Ben.Data.Common/Enums/FilePermissionType.cs`](../../../Ben.Data.Common/Enums/FilePermissionType.cs)  
**Type:** `[Flags]` enum  
**Stored on:** `UploadFilePermissionRequest.PermissionType`

### Summary
Types of access that can be requested for a shared file. Combinable via bitwise OR.

| Value | Int | Description |
|---|---|---|
| `None` | 0 | No permissions. |
| `Use` | 1 | Embed or reference in content. |
| `Share` | 2 | Re-share with others or a different organization. |
| `Display` | 4 | Display the file publicly. |

---

## `FileShareVisibility`

**File:** [`Ben.Data.Common/Enums/FileShareVisibility.cs`](../../../Ben.Data.Common/Enums/FileShareVisibility.cs)  
**Stored on:** `UploadFileOrganizationShare.Visibility`

### Summary
Controls who can see a file when it is shared with an organization.

| Value | Int | Description |
|---|---|---|
| `OrgAdminsOnly` | 0 | Visible only to organization Owners and Administrators. |
| `OrgMembers` | 1 | Visible to all active organization members. |
| `Public` | 2 | Visible to anyone with access to the organization's content. |

---

## `FilePermissionRequestStatus`

**File:** [`Ben.Data.Common/Enums/FilePermissionRequestStatus.cs`](../../../Ben.Data.Common/Enums/FilePermissionRequestStatus.cs)  
**Stored on:** `UploadFilePermissionRequest.Status`

### Summary
Lifecycle state of a file-permission request.

| Value | Int | Description |
|---|---|---|
| `Pending` | 0 | Submitted; awaiting review. |
| `Approved` | 1 | Permissions were granted. |
| `Denied` | 2 | Permissions were refused. |
| `Cancelled` | 3 | Requester withdrew the request. |

---

## `CryptoModes`

**File:** [`Ben.Data.Common/Enums/CryptoModes.cs`](../../../Ben.Data.Common/Enums/CryptoModes.cs)  
**Used by:** [`CryptoFileService`](Services.md#cryptofileservice)

### Summary
Direction of a cryptographic operation.

| Value | Description |
|---|---|
| `Encrypt` | Encrypt plaintext to ciphertext. |
| `Decrypt` | Decrypt ciphertext to recover plaintext. |

---

## `OrganizationMembershipRequestStatus`

**File:** [`Ben.Data.Common/Enums/OrganizationMembershipRequestStatus.cs`](../../../Ben.Data.Common/Enums/OrganizationMembershipRequestStatus.cs)  
**Stored on:** [`OrganizationMembershipRequest.Status`](../../Ben.Data.Source/Entities-Org.md#organizationmembershiprequest) (int column)  
**Used by:** [`OrganizationMembershipRequestController`](../../Ben.Data.WebApi/Controllers-Entities.md#organizationmembershiprequestcontroller), [`OrganizationMembershipRequestRecord`](../../Ben.Service.Models/Records-Entities.md#organizationmembershiprequestrecord)  
*(added 2026-07-22)*

### Summary
Tracks the lifecycle state of a user's application to join an organization.

| Value | Int | Description |
|---|---|---|
| `Pending` | 0 | Application submitted; awaiting org admin response. |
| `Accepted` | 1 | Application accepted; the applicant has been added as an org member. |
| `Denied` | 2 | Application denied; the user remains outside the organization. |
| `Withdrawn` | 3 | The applicant withdrew their own request before a response was given. |
