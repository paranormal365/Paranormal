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
