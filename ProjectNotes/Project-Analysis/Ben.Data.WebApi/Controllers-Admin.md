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
