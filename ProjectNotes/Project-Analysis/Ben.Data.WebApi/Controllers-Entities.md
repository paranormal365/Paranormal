# Ben.Data.WebApi — Entity Controllers & Seed Data

---

## Entity Controllers

All controllers in `Ben.Data.WebApi/Controllers/Entities/` require `[Authorize]` (any authenticated user).  
Each extends [`EntityReadControllerBase<TEntity, TRecord>`](Controllers-Base.md#entityreadcontrollerbase) providing GET (all + by ID) only.

| Controller | Route | Entity | Record |
|---|---|---|---|
| `AppUserController` | `api/users` | `AppUser` | `AppUserRecord` |
| `OrganizationController` | `api/organizations` | `Organization` | `OrganizationRecord` |
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

### Specialised Entity Controllers

#### `UploadFileController` — `api/upload-files`

Full file CRUD: upload (multipart), list, update metadata, download (`GET .../download`), delete.

#### `UploadFileTypeController` — `api/upload-file-types`

Read-only list of active/public file types for upload selection dialogs.

#### `UploadFileShareController` — `api/upload-files/{fileId}/shares`

Manage organisation shares: list, add share, update visibility, remove.

#### `UploadFilePermissionRequestController` — `api/upload-files/{fileId}/permission-requests`

Submit, list, and review permission requests. Approval requires the file owner or SuperAdmin.

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
**Idempotent:** Yes — skips existing users and organisations.

**What it creates:**
1. Seed users defined in `SeedData:SeedOrganization:Users`.
2. The seed organisation (`SeedData:SeedOrganization:OrgName`).
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
