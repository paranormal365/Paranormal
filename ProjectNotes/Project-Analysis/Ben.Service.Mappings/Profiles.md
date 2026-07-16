# Ben.Service.Mappings — Profiles

All profiles extend `AutoMapper.Profile` and are in the `Ben.Service.Mappings` namespace.

## Profile Inventory

### Root-level

| Profile Class | Mapping |
|---|---|
| `AppUserProfile` | `AppUser` → `AppUserRecord` |

### `Admin/` folder

| Profile Class | Source Entity | Target Admin Record |
|---|---|---|
| `AppUserAdminProfile` | `AppUser` | `AppUserAdminRecord` |
| `AppRoleAdminProfile` | `IdentityRole<Guid>` | `AppRoleAdminRecord` |
| `AppRoleClaimAdminProfile` | `IdentityRoleClaim<Guid>` | `AppRoleClaimAdminRecord` |
| `AppUserClaimAdminProfile` | `IdentityUserClaim<Guid>` | `AppUserClaimAdminRecord` |
| `AppUserLoginAdminProfile` | `IdentityUserLogin<Guid>` | `AppUserLoginAdminRecord` |
| `AppUserRoleAdminProfile` | `IdentityUserRole<Guid>` | `AppUserRoleAdminRecord` |
| `AppUserTokenAdminProfile` | `IdentityUserToken<Guid>` | `AppUserTokenAdminRecord` |
| `OrganizationAdminProfile` | `Organization` | `OrganizationAdminRecord` |
| `OrganizationAccessGrantAdminProfile` | `OrganizationAccessGrant` | `OrganizationAccessGrantAdminRecord` |
| `OrganizationAddressAdminProfile` | `OrganizationAddress` | `OrganizationAddressAdminRecord` |
| `OrganizationAddressTypeAdminProfile` | `OrganizationAddressType` | `OrganizationAddressTypeAdminRecord` |
| `OrganizationEmailAdminProfile` | `OrganizationEmail` | `OrganizationEmailAdminRecord` |
| `OrganizationEmailTypeAdminProfile` | `OrganizationEmailType` | `OrganizationEmailTypeAdminRecord` |
| `OrganizationLinkAdminProfile` | `OrganizationLink` | `OrganizationLinkAdminRecord` |
| `OrganizationLinkTypeAdminProfile` | `OrganizationLinkType` | `OrganizationLinkTypeAdminRecord` |
| `OrganizationNoteAdminProfile` | `OrganizationNote` | `OrganizationNoteAdminRecord` |
| `OrganizationNoteTypeAdminProfile` | `OrganizationNoteType` | `OrganizationNoteTypeAdminRecord` |
| `OrganizationPageAdminProfile` | `OrganizationPage` | `OrganizationPageAdminRecord` |
| `OrganizationPhoneAdminProfile` | `OrganizationPhone` | `OrganizationPhoneAdminRecord` |
| `OrganizationPhoneTypeAdminProfile` | `OrganizationPhoneType` | `OrganizationPhoneTypeAdminRecord` |
| `OrganizationUserMembershipAdminProfile` | `OrganizationUserMembership` | `OrganizationUserMembershipAdminRecord` |
| `UploadFileAdminProfile` | `UploadFile` | `UploadFileAdminRecord` |
| `UploadFileOrganizationShareAdminProfile` | `UploadFileOrganizationShare` | `UploadFileOrganizationShareAdminRecord` |
| `UploadFilePermissionRequestAdminProfile` | `UploadFilePermissionRequest` | `UploadFilePermissionRequestAdminRecord` |
| `UploadFileTypeAdminProfile` | `UploadFileType` | `UploadFileTypeAdminRecord` |
| `UserAddressAdminProfile` | `UserAddress` | `UserAddressAdminRecord` |
| `UserAddressTypeAdminProfile` | `UserAddressType` | `UserAddressTypeAdminRecord` |
| `UserEmailAdminProfile` | `UserEmail` | `UserEmailAdminRecord` |
| `UserEmailTypeAdminProfile` | `UserEmailType` | `UserEmailTypeAdminRecord` |
| `UserLinkAdminProfile` | `UserLink` | `UserLinkAdminRecord` |
| `UserLinkTypeAdminProfile` | `UserLinkType` | `UserLinkTypeAdminRecord` |
| `UserMessageAdminProfile` | `UserMessage` | `UserMessageAdminRecord` |
| `UserMessageToAdminProfile` | `UserMessageTo` | `UserMessageToAdminRecord` |
| `UserMessageTypeAdminProfile` | `UserMessageType` | `UserMessageTypeAdminRecord` |
| `UserNoteAdminProfile` | `UserNote` | `UserNoteAdminRecord` |
| `UserNoteTypeAdminProfile` | `UserNoteType` | `UserNoteTypeAdminRecord` |
| `UserPhoneAdminProfile` | `UserPhone` | `UserPhoneAdminRecord` |
| `UserPhoneTypeAdminProfile` | `UserPhoneType` | `UserPhoneTypeAdminRecord` |

### `Entities/` folder

| Profile Class | Source Entity | Target Record |
|---|---|---|
| `OrganizationAccessGrantProfile` | `OrganizationAccessGrant` | `OrganizationAccessGrantRecord` |
| `OrganizationAddressProfile` | `OrganizationAddress` | `OrganizationAddressRecord` |
| `OrganizationAddressTypeProfile` | `OrganizationAddressType` | `OrganizationAddressTypeRecord` |
| `OrganizationEmailProfile` | `OrganizationEmail` | `OrganizationEmailRecord` |
| `OrganizationEmailTypeProfile` | `OrganizationEmailType` | `OrganizationEmailTypeRecord` |
| `OrganizationLinkProfile` | `OrganizationLink` | `OrganizationLinkRecord` |
| `OrganizationLinkTypeProfile` | `OrganizationLinkType` | `OrganizationLinkTypeRecord` |
| `OrganizationNoteProfile` | `OrganizationNote` | `OrganizationNoteRecord` |
| `OrganizationNoteTypeProfile` | `OrganizationNoteType` | `OrganizationNoteTypeRecord` |
| `OrganizationPageProfile` | `OrganizationPage` | `OrganizationPageRecord` |
| `OrganizationPhoneProfile` | `OrganizationPhone` | `OrganizationPhoneRecord` |
| `OrganizationPhoneTypeProfile` | `OrganizationPhoneType` | `OrganizationPhoneTypeRecord` |
| `OrganizationProfile` | `Organization` | `OrganizationRecord` |
| `OrganizationUserMembershipProfile` | `OrganizationUserMembership` | `OrganizationUserMembershipRecord` |
| `UploadFileOrganizationShareProfile` | `UploadFileOrganizationShare` | `UploadFileOrganizationShareRecord` |
| `UploadFilePermissionRequestProfile` | `UploadFilePermissionRequest` | `UploadFilePermissionRequestRecord` |
| `UploadFileProfile` | `UploadFile` | `UploadFileRecord` |
| `UploadFileTypeExtensionProfile` | `UploadFileTypeExtension` | `UploadFileTypeExtensionRecord` |
| `UploadFileTypeProfile` | `UploadFileType` | `UploadFileTypeRecord` |
| `UserAddressProfile` | `UserAddress` | `UserAddressRecord` |
| `UserAddressTypeProfile` | `UserAddressType` | `UserAddressTypeRecord` |
| `UserEmailProfile` | `UserEmail` | `UserEmailRecord` |
| `UserEmailTypeProfile` | `UserEmailType` | `UserEmailTypeRecord` |
| `UserLinkProfile` | `UserLink` | `UserLinkRecord` |
| `UserLinkTypeProfile` | `UserLinkType` | `UserLinkTypeRecord` |
| `UserMessageProfile` | `UserMessage` | `UserMessageRecord` |
| `UserMessageToProfile` | `UserMessageTo` | `UserMessageToRecord` |
| `UserMessageTypeProfile` | `UserMessageType` | `UserMessageTypeRecord` |
| `UserNoteProfile` | `UserNote` | `UserNoteRecord` |
| `UserNoteTypeProfile` | `UserNoteType` | `UserNoteTypeRecord` |
| `UserPhoneProfile` | `UserPhone` | `UserPhoneRecord` |
| `UserPhoneTypeProfile` | `UserPhoneType` | `UserPhoneTypeRecord` |

### `Identity/` folder

| Profile Class | Source | Target |
|---|---|---|
| *(see Admin profiles above — Identity types are mapped there)* | | |
