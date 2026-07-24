# Ben.Data.WebApi — Entity Controllers & Seed Data

---

## Entity Controllers

All controllers in `Ben.Data.WebApi/Controllers/Entities/` require `[Authorize]` (any authenticated user).  
Each extends [`EntityReadControllerBase<TEntity, TRecord>`](Controllers-Base.md#entityreadcontrollerbase) providing GET (all + by ID) only.

| Controller | Route | Entity | Record | Notes |
|---|---|---|---|---|
| `AppUserController` | `api/users` | `AppUser` | `AppUserRecord` | |
| `OrganizationController` | `api/organizations` | `Organization` | `OrganizationListItemResponse` | Extended — see below |
| `OrganizationAddressController` | `api/organization-addresses` | `OrganizationAddress` | `OrganizationAddressRecord` | |
| `OrganizationAddressController` | `api/organization-addresses` | `OrganizationAddress` | `OrganizationAddressRecord` |
| `OrganizationAddressTypeController` | `api/organization-address-types` | `OrganizationAddressType` | `OrganizationAddressTypeRecord` |
| `OrganizationEmailController` | `api/organization-emails` | `OrganizationEmail` | `OrganizationEmailRecord` |
| `OrganizationEmailTypeController` | `api/organization-email-types` | `OrganizationEmailType` | `OrganizationEmailTypeRecord` |
| `OrganizationLinkController` | `api/organization-links` | `OrganizationLink` | `OrganizationLinkRecord` |
| `OrganizationLinkTypeController` | `api/organization-link-types` | `OrganizationLinkType` | `OrganizationLinkTypeRecord` |
| `OrganizationNoteController` | `api/organization-notes` | `OrganizationNote` | `OrganizationNoteRecord` |
| `OrganizationNoteTypeController` | `api/organization-note-types` | `OrganizationNoteType` | `OrganizationNoteTypeRecord` |
| `OrganizationPageController` | `api/organization-pages` | `OrganizationPage` | `OrganizationPageRecord` |
| `OrganizationPhoneController` | `api/organization-phones` | `OrganizationPhone` | `OrganizationPhoneRecord` |
| `OrganizationPhoneTypeController` | `api/organization-phone-types` | `OrganizationPhoneType` | `OrganizationPhoneTypeRecord` |
| `UserAddressController` | `api/user-addresses` | `UserAddress` | `UserAddressRecord` |
| `UserAddressTypeController` | `api/user-address-types` | `UserAddressType` | `UserAddressTypeRecord` |
| `UserEmailController` | `api/user-emails` | `UserEmail` | `UserEmailRecord` |
| `UserEmailTypeController` | `api/user-email-types` | `UserEmailType` | `UserEmailTypeRecord` |
| `UserLinkController` | `api/user-links` | `UserLink` | `UserLinkRecord` |
| `UserLinkTypeController` | `api/user-link-types` | `UserLinkType` | `UserLinkTypeRecord` |
| `UserMessageController` | `api/user-messages` | `UserMessage` | `UserMessageRecord` |
| `UserMessageToController` | `api/user-message-tos` | `UserMessageTo` | `UserMessageToRecord` |
| `UserMessageTypeController` | `api/user-message-types` | `UserMessageType` | `UserMessageTypeRecord` |
| `UserNoteController` | `api/user-notes` | `UserNote` | `UserNoteRecord` |
| `UserNoteTypeController` | `api/user-note-types` | `UserNoteType` | `UserNoteTypeRecord` |
| `UserPhoneController` | `api/user-phones` | `UserPhone` | `UserPhoneRecord` |
| `UserPhoneTypeController` | `api/user-phone-types` | `UserPhoneType` | `UserPhoneTypeRecord` |

### `OrganizationController` — `api/organizations` (extended)

Base `GetAll`/`GetById` suppressed via `[NonAction]`. Adds full permission-aware CRUD:

| Endpoint | Auth | Description |
|---|---|---|
| `GET /api/organizations` | Any auth | Returns `OrganizationListItemResponse[]` — each row includes `CanEdit` and `CanDelete` flags. SuperAdmins see all; others see their member orgs. |
| `GET /api/organizations/{id}` | Any auth + Read permission | Returns `OrganizationAdminRecord` for edit form pre-fill. |
| `POST /api/organizations` | SuperAdmin (DB role check) | Creates a new org. Uses `UserManager.IsInRoleAsync` so Entra tokens work. |
| `PUT /api/organizations/{id}` | Update permission or SuperAdmin | Updates Name and UrlName. |
| `DELETE /api/organizations/{id}` | Delete permission or SuperAdmin | Deletes the org. |

All user ID resolution uses `GetCurrentUserId()` which checks the `app_user_id` claim (set by `EntraClaimsTransformation`) before falling back to `ClaimTypes.NameIdentifier`.

### Specialised Entity Controllers

#### `UploadFileController` — `api/upload-files`

Full file CRUD: upload (multipart), list, update metadata, download (`GET .../download`), delete.

#### `UploadFileTypeController` — `api/upload-file-types`

Read-only list of active/public file types for upload selection dialogs.

#### `UploadFileShareController` — `api/upload-files/{fileId}/shares`

Manage organization shares: list, add share, update visibility, remove.

#### `UploadFilePermissionRequestController` — `api/upload-files/{fileId}/permission-requests`

Submit, list, and review permission requests. Approval requires the file owner or SuperAdmin.

---

## CMS Controllers *(added 2026-07-18)*

**Namespace:** `Ben.Data.WebApi.Controllers.Cms`  
**Auth:** `[Authorize]` on base class — all endpoints require an authenticated session.  
**Security model:** `IsCmsAuthorizedAsync(userId, orgId, table, action)` returns `true` when the user is a site-level SuperAdmin OR `IOrganizationSecurityService.HasAccessAsync` returns true (org Owners/Admins always satisfy this).

All user ID resolution uses `GetCurrentUserId()` — checks `app_user_id` claim first (Entra), then `ClaimTypes.NameIdentifier` (local Identity).

### `OrgCmsControllerBase`

Abstract base class shared by all CMS controllers.

| Method | Description |
|---|---|
| `GetCurrentUserId()` | Returns `Guid?` from claims. |
| `IsCmsAuthorizedAsync(userId, orgId, table, action, ct)` | `true` if SuperAdmin OR `HasAccessAsync` passes. |

### `OrgCmsPageController` — `api/organizations/{orgId}/pages`

Permission-aware CMS page CRUD.

| Endpoint | Auth check | Response | Key behaviour |
|---|---|---|---|
| `GET /` | Read | `CmsPageListItemResponse[]` | Each row has `CanEdit`/`CanDelete` flags. SuperAdmin skips per-page checks. |
| `GET /{pageId}` | Read | `CmsPageDetailResponse` | Includes ordered sections. |
| `POST /` | Create | `CmsPageDetailResponse` 201 | Validates duplicate UrlName; normalises to lowercase. |
| `PUT /{pageId}` | Update | `CmsPageDetailResponse` | Self-parent guard (`ParentPageId != pageId`); UrlName uniqueness check. |
| `DELETE /{pageId}` | Delete | 204 | Children are re-parented to the deleted page's parent before removal. |

### `CmsSectionController` — `api/organizations/{orgId}/pages/{pageId}/sections`

| Endpoint | Description |
|---|---|
| `GET /` | Lists sections ordered by `SortOrder`. 404 if page not found. |
| `POST /` | Creates section. ContentJson defaulted to `{}` if empty. |
| `PUT /{sectionId}` | Updates Title, ContentJson, IsActive. |
| `PUT /reorder` | Accepts `{ OrderedSectionIds: [...] }` — assigns `SortOrder 1..N`. Returns 204. |
| `DELETE /{sectionId}` | Removes section. |

### `OrganizationLogoController` — `api/organizations/{orgId}/logos`

| Endpoint | Key behaviour |
|---|---|
| `POST /` | Verifies UploadFile exists; if `IsActive=true`, deactivates all other org logos. |
| `PUT /{logoId}` | Same active-logo deactivation on `IsActive=true`. |

### `OrgMemberGroupController` — `api/organizations/{orgId}/groups`

Full group CRUD plus member sub-resource:

| Endpoint | Key behaviour |
|---|---|
| `POST /{groupId}/members` | Validates membership belongs to the same org; 409 on duplicate. |
| `DELETE /{groupId}/members/{membershipId}` | Removes a specific `OrgMemberGroupMembership` row. |

### `CmsPagePermissionController` — `api/organizations/{orgId}/pages/{pageId}/permissions`

| Validation | Enforced on |
|---|---|
| At least one of `AppUserId`/`OrgMemberGroupId` must be set | Create |
| `Actions != None` | Create and Update |

---

## Seed Data

### `SuperAdminSeeder`

**File:** [`Ben.Data.WebApi/SeedData/SuperAdminSeeder.cs`](../../../Ben.Data.WebApi/SeedData/SuperAdminSeeder.cs)  
**When called:** At WebApi startup, before `app.Run()`.  
**Idempotent:** Yes — skips if the role and user already exist.

**What it creates:**
1. `RoleNames.SuperAdmin` Identity role (if it doesn't exist).
2. SuperAdmin `AppUser` using credentials from `appsettings.Development.json → SeedData:SuperAdmin`.
3. Adds the user to the `SuperAdmin` role.

**Configuration keys:**
- `SeedData:SuperAdmin:Email`
- `SeedData:SuperAdmin:DisplayName`  
- `SeedData:SuperAdmin:Password`

---

### `OrganizationSeeder`

**File:** [`Ben.Data.WebApi/SeedData/OrganizationSeeder.cs`](../../../Ben.Data.WebApi/SeedData/OrganizationSeeder.cs)  
**When called:** After `SuperAdminSeeder`, at startup.  
**Idempotent:** Yes — skips existing users and organizations.

**What it creates:**
1. Seed users defined in `SeedData:SeedOrganization:Users`.
2. The seed organization (`SeedData:SeedOrganization:OrgName`).
3. Owner membership for the SuperAdmin account (`Role = Owner`).
4. Memberships for each seed user (`IsOrgAdmin: true` → `Administrator`, `false` → `Member`).

**Current seed users (dev):**

| Email | Role |
|---|---|
| `haveben@msn.com` (SuperAdmin) | Owner |
| `sarah.mitchell@benco.dev` | Administrator |
| `james.thornton@benco.dev` | Member |
| `emma.rodriguez@benco.dev` | Member |
| `daniel.park@benco.dev` | *(not seeded into org)* |

---

### `UploadFileTypeSeeder` *(added 2026-07-18)*

**File:** [`Ben.Data.WebApi/SeedData/UploadFileTypeSeeder.cs`](../../../Ben.Data.WebApi/SeedData/UploadFileTypeSeeder.cs)  
**When called:** After `OrganizationSeeder`, at startup.  
**Idempotent:** Yes — checks by name before creating; checks by pattern before adding extensions.

**What it creates:**

A built-in **"Logo"** upload file type for organization logo images:

| Property | Value |
|---|---|
| `Name` | `"Logo"` |
| `Description` | `"Organization logo images — JPEG, PNG, GIF, WebP, SVG"` |
| `IsActive` | `true` |
| `IsPublic` | `true` |
| `AllowAllExtensions` | `false` |
| `SortOrder` | `1` |
| `CreatedByAppUserId` | SuperAdmin (`haveben@msn.com`) |

**Seeded extension patterns:** `.jpg` · `.jpeg` · `.png` · `.gif` · `.webp` · `.svg`

**Why needed:** The Add Logo dialog in `OrgCmsEditor.razor` uploads via `POST /api/upload-files` which requires a `uploadFileTypeId`. This seeder ensures a suitable type always exists and the dialog auto-selects it by name.

---

## Authorization (`Ben.Data.WebApi/Authorization/`) *(added 2026-07-18)*

### `SuperAdminRequirement` + `SuperAdminHandler`

**File:** [`Ben.Data.WebApi/Authorization/SuperAdminRequirement.cs`](../../../Ben.Data.WebApi/Authorization/SuperAdminRequirement.cs)

Replaces `[Authorize(Roles = "SuperAdmin")]` attribute-based checks. All admin controllers now use `[Authorize(Policy = RoleNames.SuperAdmin)]` backed by this DB-querying handler.

#### Why it was needed

`[Authorize(Roles = "SuperAdmin")]` relies on `ClaimTypes.Role` being present in the JWT principal. For Entra JWTs, the `IClaimsTransformation` is supposed to inject these claims, but due to authentication scheme caching behaviour the enriched claims were not reliably seen by the `RolesAuthorizationRequirement` check. A custom handler that queries `UserManager` directly bypasses the claim pipeline entirely.

#### Three authentication paths

| Path | Mechanism | Used for |
|---|---|---|
| 1 | `context.User.IsInRole("SuperAdmin")` | Local Identity bearer tokens (role claim present) |
| 2 | `app_user_id` claim → `UserManager.FindByIdAsync` | Entra tokens enriched by `EntraClaimsTransformation` |
| 3 | `oid` claim → `UserManager.FindByLoginAsync("Microsoft", oid)` | Entra tokens where `app_user_id` not present |

#### Registration (Program.cs)

```csharp
builder.Services.AddScoped<IAuthorizationHandler, SuperAdminHandler>();

options.AddPolicy(RoleNames.SuperAdmin, policy =>
    policy.AddAuthenticationSchemes(schemes)
          .RequireAuthenticatedUser()
          .AddRequirements(new SuperAdminRequirement()));
```

---

## `UploadFileAudioConfigController`

**Route:** `api/upload-files/{fileId:guid}/audio-config` | **Auth:** `[Authorize]`

| Method | Route | Description |
|---|---|---|
| `GET` | `/audio-config` | Returns saved WaveSurfer config or null |
| `PUT` | `/audio-config` | Create or replace (upsert) via `UpsertAudioConfigRequest` |
| `DELETE` | `/audio-config` | Remove saved config; player reverts to theme defaults |

---

## `UploadFileRegionNoteController`

**Route:** `api/upload-files/{fileId:guid}/region-notes` | **Auth:** `[Authorize]`

| Method | Route | Description |
|---|---|---|
| `GET` | `/region-notes` | All notes for file, ordered by `RegionStart` then `TimeOffset` |
| `GET` | `/region-notes/{noteId}` | Single note |
| `POST` | `/region-notes` | Create (`CreateRegionNoteRequest`) |
| `PUT` | `/region-notes/{noteId}` | Update note text, public flag, time offset |
| `DELETE` | `/region-notes/{noteId}` | Delete |

---

## `UploadFileAudioClipController`

**Route:** `api/upload-files/{fileId:guid}/clip` | **Auth:** `[Authorize]`

| Method | Route | Description |
|---|---|---|
| `POST` | `/clip` | Clip audio to time range; creates new `UploadFile` with `ParentFileId` set |
| `GET` | `/clip/preview?start=&end=` | Returns clipped WAV bytes **without** creating a DB record |

**Supported input formats:** WAV, MP3. Output is always WAV. Returns 400 for unsupported formats.

---

## `UploadFileVoteController`

**Route:** `api/upload-files/{fileId:guid}/votes` | **Auth:** `[Authorize]`

Business rule: one vote per `(UploadFileId, AppUserId)` — enforced by unique DB index. Upsert pattern prevents duplicates at the application level too.

| Method | Route | Description |
|---|---|---|
| `GET` | `/votes` | Returns `UploadFileVoteSummary` (UpvoteCount, DownvoteCount, TotalScore, TotalVotes, UserScore?) |
| `PUT` | `/votes/my-vote` | Create or update own vote (`UpsertVoteRequest { Score }`). 201 on first vote, 200 on update. |
| `DELETE` | `/votes/my-vote` | Remove own vote. 204 whether or not vote existed. |

---

## Organization Participation Controllers *(added 2026-07-22)*

These controllers use org-level `OrganizationSecurityService` permission checks (not the SuperAdmin base class). All require `[Authorize]`.

---

### `OrganizationMembershipRequestController`

**Route:** `api/organizations/{orgId}/membership-requests`

| Method | Route | Permission Required | Request Body | Response | Description |
|---|---|---|---|---|---|
| `GET` | `/` | [`MembershipRequests`](../Ben.Data.Common/Enums.md#organizationsecuritytable)-Read or Org Admin | — | [`OrganizationMembershipRequestRecord[]`](../Ben.Service.Models/Records-Entities.md#organizationmembershiprequestrecord) | All requests for the org |
| `GET` | `/my` | Any authenticated user | — | [`OrganizationMembershipRequestRecord?`](../Ben.Service.Models/Records-Entities.md#organizationmembershiprequestrecord) | The calling user's own request |
| `POST` | `/` | Any authenticated user | `ApplyForMembershipRequest` | [`OrganizationMembershipRequestRecord`](../Ben.Service.Models/Records-Entities.md#organizationmembershiprequestrecord) | Submit an application. Fails `400` if `Organization.IsAcceptingApplications=false` or a `Pending` request already exists |
| `PUT` | `/{id}/respond` | [`MembershipRequests`](../Ben.Data.Common/Enums.md#organizationsecuritytable)-Update or Org Admin | `RespondToMembershipRequest` | `bool` | Accept (→ auto-creates [`OrganizationUserMembership`](../Ben.Data.Source/Entities-Core.md) + sends [`UserMessage`](../Ben.Data.Source/Entities-User.md) notification) or Deny |
| `DELETE` | `/{id}` | Applicant (own request only) | — | `bool` | Withdraw a `Pending` request |

**Request types:**

`ApplyForMembershipRequest(string? Message)` — optional cover message.

`RespondToMembershipRequest(OrganizationMembershipRequestStatus Status, string? ResponseNote)` — `Status` must be `Accepted` or `Denied`. `ResponseNote` is included in the auto-generated `UserMessage`.

---

### `OrganizationFileController`

**Route:** `api/organizations/{orgId}/files`

| Method | Route | Permission Required | Request Body | Response | Description |
|---|---|---|---|---|---|
| `GET` | `/` | [`OrganizationFiles`](../Ben.Data.Common/Enums.md#organizationsecuritytable)-Read | — | [`OrganizationFileRecord[]`](../Ben.Service.Models/Records-Entities.md#organizationfilerecord) | List all org files |
| `GET` | `/delete-log` | `OrganizationFiles`-Delete | — | [`OrganizationFileDeleteLogRecord[]`](../Ben.Service.Models/Records-Entities.md#organizationfiledeletelogrecord) | Immutable delete audit log |
| `GET` | `/{id}/download` | `OrganizationFiles`-Read or `IsPublic=true` | — | File stream | Signed download |
| `POST` | `/` | `OrganizationFiles`-Create | `OrgFileUploadRequest` (multipart, 200 MB limit) | [`OrganizationFileRecord`](../Ben.Service.Models/Records-Entities.md#organizationfilerecord) | Direct upload |
| `POST` | `/copy-from-user/{uploadFileId}` | `OrganizationFiles`-Create | `CopyFromUserRequest` | `OrgFileCopyResult` | Copy an accessible user file. Checks ownership or public/shared access |
| `PUT` | `/{id}/publish` | `OrganizationFiles`-Update | `PublishOrgFileRequest(bool IsPublic)` | [`OrganizationFileRecord`](../Ben.Service.Models/Records-Entities.md#organizationfilerecord) | Toggle public access; stamps `PublishedByAppUserId` + `DatePublished` |
| `PUT` | `/{id}` | `OrganizationFiles`-Update | `OrgFileUpdateRequest` | [`OrganizationFileRecord`](../Ben.Service.Models/Records-Entities.md#organizationfilerecord) | Metadata-only update (Description, SortOrder) |
| `DELETE` | `/{id}` | `OrganizationFiles`-Delete | — | `bool` | Writes [`OrganizationFileDeleteLog`](../Ben.Data.Source/Entities-Org.md#organizationfiledeleterelog) snapshot first, then deletes storage file + DB row |

**Request types:**

`OrgFileUploadRequest(IFormFile? File, Guid UploadFileTypeId, string? Description, bool IsPublic, int SortOrder)` — multipart form.

`OrgFileUpdateRequest(string? Description, int SortOrder)` — metadata only; IsPublic is controlled via the `/publish` endpoint.

`CopyFromUserRequest(string? Description, bool PublishImmediately = false)` — if `PublishImmediately=true` and caller has Update permission, the copied file is auto-published; result `PublishedImmediately=true`.

**API-only result type (not in Service.Models):**  
`OrgFileCopyResult(OrganizationFileRecord File, bool CanPublishImmediately, bool PublishedImmediately)` — the client-layer equivalent is [`OrgFileCopyClientResult`](../Ben.Service.Models/Records-Entities.md#orgfilecopyclientresult).

---

### `OrganizationAddressMapConfigController`

**Route:** `api/organizations/{orgId}/addresses/{addressId}/map-config`

| Method | Route | Permission Required | Request Body | Response | Description |
|---|---|---|---|---|---|
| `GET` | `/` | `OrganizationAddress`-Read | — | [`AddressMapConfigRecord?`](../Ben.Service.Models/Records-Entities.md#addressmapconfigrecord) | Load config; `404` if none exists |
| `PUT` | `/` | `OrganizationAddress`-Update | `UpsertAddressMapConfigRequest` | [`AddressMapConfigRecord`](../Ben.Service.Models/Records-Entities.md#addressmapconfigrecord) | Create or replace the config |
| `DELETE` | `/` | `OrganizationAddress`-Update | — | `bool` | Remove config; address reverts to no-map-display |

**Request type:**

`UpsertAddressMapConfigRequest(bool IsOnMap, bool ShowMarker, bool ShowRegion, double RegionRadiusMiles, string? MarkerColor, string? MarkerIconKey, string? RegionFillColor, double RegionFillOpacity, string? RegionStrokeColor, double RegionStrokeOpacity, double RegionStrokeWidth)` — full replacement PUT; null color strings use server-side defaults.
