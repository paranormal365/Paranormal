# Ben.Data.WebApi — Admin Controllers

All controllers in `Ben.Data.WebApi/Controllers/Admin/` require `[Authorize(Roles = RoleNames.SuperAdmin)]`.  
Each is a sealed class that extends [`AdminEntityControllerBase<TEntity, TRecord>`](Controllers-Base.md#adminentitycontrollerbase).

## Pattern

Every concrete admin controller follows this identical pattern:

```csharp
[Route("api/admin/organizations")]
public sealed class AdminOrganizationController
    : AdminEntityControllerBase<Organization, OrganizationAdminRecord>
{
    public AdminOrganizationController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMapper mapper,
        IAuditLogService auditLog)
        : base(dbContextFactory, mapper, auditLog) { }
}
```

The base class provides GET (all + by ID), POST, PUT, DELETE.  
All mutations are **automatically audit-logged** by the base class.

### Specialised Admin Controllers

#### `AdminAppUserController` — `api/admin/app-users`

Extends the base. Additional endpoints:

| Endpoint | Description |
|---|---|
| `POST /api/admin/app-users` | Create user via `UserManager.CreateAsync`. Sets `IsSuperAdmin` role flag. |
| `PUT /api/admin/app-users/{id}/profile` | Update profile fields including audit timestamps. |
| `GET /api/admin/app-users/{id}/detail` | Full user aggregate (profile + 8 related lists). |

#### `AdminRoleController` — `api/admin/roles`

Standalone controller (does not extend base). Uses `RoleManager<IdentityRole<Guid>>`.

| Endpoint | Description |
|---|---|
| `GET /api/admin/roles` | All roles with user counts (`AdminRoleWithCountResponse[]`). |
| `POST /api/admin/roles` | Create a role. |
| `DELETE /api/admin/roles/{id}` | Delete role. Returns `409 Conflict` if users assigned. |

#### `AdminOrganizationController` — `api/admin/organizations`

Extends base. Base `Create(Organization)` suppressed with `[NonAction]`. The preferred create endpoint is `POST /api/organizations` (Entra-compatible).

---

## Controller Inventory

| Controller | Route | Entity | Record |
|---|---|---|---|
| `AdminOrganizationController` | `api/admin/organizations` | `Organization` | `OrganizationAdminRecord` |
| `AdminOrganizationAddressController` | `api/admin/organization-addresses` | `OrganizationAddress` | `OrganizationAddressAdminRecord` |
| `AdminOrganizationAddressTypeController` | `api/admin/organization-address-types` | `OrganizationAddressType` | `OrganizationAddressTypeAdminRecord` |
| `AdminOrganizationEmailController` | `api/admin/organization-emails` | `OrganizationEmail` | `OrganizationEmailAdminRecord` |
| `AdminOrganizationEmailTypeController` | `api/admin/organization-email-types` | `OrganizationEmailType` | `OrganizationEmailTypeAdminRecord` |
| `AdminOrganizationLinkController` | `api/admin/organization-links` | `OrganizationLink` | `OrganizationLinkAdminRecord` |
| `AdminOrganizationLinkTypeController` | `api/admin/organization-link-types` | `OrganizationLinkType` | `OrganizationLinkTypeAdminRecord` |
| `AdminOrganizationNoteController` | `api/admin/organization-notes` | `OrganizationNote` | `OrganizationNoteAdminRecord` |
| `AdminOrganizationNoteTypeController` | `api/admin/organization-note-types` | `OrganizationNoteType` | `OrganizationNoteTypeAdminRecord` |
| `AdminOrganizationPageController` | `api/admin/organization-pages` | `OrganizationPage` | `OrganizationPageAdminRecord` |
| `AdminOrganizationPhoneController` | `api/admin/organization-phones` | `OrganizationPhone` | `OrganizationPhoneAdminRecord` |
| `AdminOrganizationPhoneTypeController` | `api/admin/organization-phone-types` | `OrganizationPhoneType` | `OrganizationPhoneTypeAdminRecord` |
| `AdminAppUserController` | `api/admin/app-users` | `AppUser` | `AppUserAdminRecord` |
| `AdminUserAddressController` | `api/admin/user-addresses` | `UserAddress` | `UserAddressAdminRecord` |
| `AdminUserAddressTypeController` | `api/admin/user-address-types` | `UserAddressType` | `UserAddressTypeAdminRecord` |
| `AdminUserEmailController` | `api/admin/user-emails` | `UserEmail` | `UserEmailAdminRecord` |
| `AdminUserEmailTypeController` | `api/admin/user-email-types` | `UserEmailType` | `UserEmailTypeAdminRecord` |
| `AdminUserLinkController` | `api/admin/user-links` | `UserLink` | `UserLinkAdminRecord` |
| `AdminUserLinkTypeController` | `api/admin/user-link-types` | `UserLinkType` | `UserLinkTypeAdminRecord` |
| `AdminUserMessageController` | `api/admin/user-messages` | `UserMessage` | `UserMessageAdminRecord` |
| `AdminUserMessageToController` | `api/admin/user-message-tos` | `UserMessageTo` | `UserMessageToAdminRecord` |
| `AdminUserMessageTypeController` | `api/admin/user-message-types` | `UserMessageType` | `UserMessageTypeAdminRecord` |
| `AdminUserNoteController` | `api/admin/user-notes` | `UserNote` | `UserNoteAdminRecord` |
| `AdminUserNoteTypeController` | `api/admin/user-note-types` | `UserNoteType` | `UserNoteTypeAdminRecord` |
| `AdminUserPhoneController` | `api/admin/user-phones` | `UserPhone` | `UserPhoneAdminRecord` |
| `AdminUserPhoneTypeController` | `api/admin/user-phone-types` | `UserPhoneType` | `UserPhoneTypeAdminRecord` |

## Specialised Admin Controllers

These controllers in `Controllers/Entities/` also require SuperAdmin but have custom endpoints beyond the base CRUD:

### `AdminUploadFileTypeController`

**Route:** `api/admin/upload-file-types`  
Also provides `GET .../with-extensions` returning types with their extension pattern lists.

### `AdminUploadFileTypeExtensionController`

**Route:** `api/admin/upload-file-type-extensions`  
Full CRUD for individual extension patterns.

### `AdminAppUserController` (extended)

Beyond the base CRUD, has:
- `GET /api/admin/app-users/{id}/detail` — returns `AppUserDetailAdminRecord` aggregate.
- `PUT /api/admin/app-users/{id}/profile` — updates specific profile fields.

---

## `AdminEntityControllerBase` — Create Fix *(2026-07-22)*

The `Create` method now overwrites `CreatedByAppUserId` and `DateCreated` from the authenticated JWT claims via reflection **after** model binding, before `SaveChangesAsync`. This prevents `Guid.Empty` FK violations when the client doesn't send these fields.

New helpers:
- `SetPropertyIfExists(entity, propertyName, value)` — reflection setter with null guard
- `GetPropertyIfNotSet<T>(entity, propertyName, defaultValue)` — returns existing value or default if property is default(T) (used to default `IsActive=true`)

---

## Entity-Scoped Organization Controllers *(added 2026-07-22)*

These controllers live in `Controllers/Entities/` and operate under a parent `{orgId}` route segment. They use org-level `OrganizationSecurityService` permissions rather than the SuperAdmin base class.

### `OrganizationMembershipRequestController` — `api/organizations/{orgId}/membership-requests`

| Endpoint | Auth | Description |
|---|---|---|
| `GET /` | Org member | All requests for the org |
| `GET /my` | Any user | The calling user's pending request |
| `POST /` | Any user | Apply — validates `IsAcceptingApplications=true` and no duplicate pending |
| `PUT /{id}/respond` | Org admin | Accept (→ auto-creates `OrganizationUserMembership` + sends `UserMessage`) or Deny |
| `DELETE /{id}` | Applicant | Withdraw own pending request |

### `OrganizationFileController` — `api/organizations/{orgId}/files`

| Endpoint | Auth | Description |
|---|---|---|
| `GET /` | Org member | List org files |
| `GET /delete-log` | Org admin | Immutable delete audit log |
| `GET /{id}/download` | Org member or public | Signed download |
| `POST /` | Org + Create perm | Direct upload |
| `POST /copy-from-user/{id}` | Org + Create perm | Copy accessible user file to org storage; returns `OrgFileCopyResult` with `CanPublishImmediately` |
| `PUT /{id}/publish` | Org + Update perm | Toggle publish; stamps `PublishedByAppUserId` + `DatePublished` |
| `PUT /{id}` | Org + Update perm | Metadata only (description, sortOrder) |
| `DELETE /{id}` | Org + Delete perm | Writes `OrganizationFileDeleteLog` snapshot first, then deletes storage + DB row |

### `OrganizationAddressMapConfigController` — `api/organizations/{orgId}/addresses/{addressId}/map-config`

| Endpoint | Auth | Description |
|---|---|---|
| `GET /` | Org + OrganizationAddress-Read perm | Load config |
| `PUT /` | Org + OrganizationAddress-Update perm | Upsert (creates or replaces) |
| `DELETE /` | Org + OrganizationAddress-Update perm | Remove config (resets to defaults) |
