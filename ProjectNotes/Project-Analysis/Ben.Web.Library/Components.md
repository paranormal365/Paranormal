# Ben.Web.Library — Components

---

## SuperAdmin Components (`SuperAdmin/` folder)

### `AdminSidePanel.razor`

**Route:** *(no route — embedded component)*  
**File:** [`Ben.Web.Library/SuperAdmin/AdminSidePanel.razor`](../../../Ben.Web.Library/SuperAdmin/AdminSidePanel.razor)

#### Summary
A right-side slide-in drawer panel toggled from the top app bar "Administration" button in `MainLayout`.  
Visible only when `IBenUserState.IsSuperAdmin && !IBenUserState.IsImpersonating`.

#### Parameters

| Parameter | Type | Description |
|---|---|---|
| `IsOpen` | `bool` | Two-way bindable open/close state (`@bind-IsOpen`). |
| `IsOpenChanged` | `EventCallback<bool>` | Called when the panel opens or closes. |

#### Design
- Fixed-position panel, 260 px wide, slides in from the right.
- Semi-transparent backdrop — clicking it closes the panel.
- ✕ close button in the panel header.
- CSS transitions via `transform: translateX(100%)` ↔ `translateX(0)`.
- Fully themed with Telerik CSS custom properties for light/dark mode compatibility.

#### Navigation Links

| Section | Link | Route |
|---|---|---|
| Users | Manage Users | `/admin/users` |
| Users | Site Roles | `/admin/roles` |
| Organizations | Manage Organizations | `/organizations` |
| Content | File Types | `/admin/file-types` |

---

### `AdminFileTypes.razor`

**Route:** `/admin/file-types`  
**File:** [`Ben.Web.Library/SuperAdmin/AdminFileTypes.razor`](../../../Ben.Web.Library/SuperAdmin/AdminFileTypes.razor)

#### Summary
SuperAdmin page for managing `UploadFileType` records and their allowed extension patterns.

#### Dependencies
- `IBenAdminClient` — all file type and extension operations
- `IBenUserState` — visibility/auth checks
- Telerik UI for Blazor components (`TelerikGrid`, `TelerikDialog`, etc.)

#### Features
- **Grid**: Lists all file types with columns for Name, Description, Icon, Active flag, Public flag, extension count.
- **New / Edit dialog**: Form for all `UploadFileType` fields including `AllowAllExtensions` toggle.
- **Extensions dialog**: Inline CRUD for `UploadFileTypeExtension` patterns — add/edit/delete pattern strings.
- **Delete dialog**: Confirmation before deletion (which cascades to all patterns).

---

## User Components (`User/` folder)

### `AdminUsers.razor`

**Route:** `/admin/users`  
**File:** [`Ben.Web.Library/User/AdminUsers.razor`](../../../Ben.Web.Library/User/AdminUsers.razor)

#### Summary
SuperAdmin user list with search/pagination and impersonation action.

#### Features
- Telerik Grid with paging and search filter.
- Columns: Display Name, Email, Date Created, Active status.
- **Impersonate** action button — calls `IBenAdminClient.ImpersonateUserAsync` and navigates to home.
- Link to `AdminUserDetail` for each user.

---

### `AdminUserDetail.razor`

**Route:** `/admin/users/{UserId:guid}`  
**File:** [`Ben.Web.Library/User/AdminUserDetail.razor`](../../../Ben.Web.Library/User/AdminUserDetail.razor)

#### Summary
Full user detail page with tabbed sections for all related data.  
Fetches `AppUserDetailAdminRecord` from `IBenAdminClient.GetUserDetailAsync`.

#### Parameters

| Parameter | Type | Description |
|---|---|---|
| `UserId` | `Guid` | Route parameter — user to display. |

#### Tabs

| Tab | Content | Editable |
|---|---|---|
| Profile | All `AppUserAdminRecord` fields including audit, lockout, 2FA | ✅ via Save Profile |
| Addresses | Grid of `UserAddressAdminRecord` | ❌ (read-only) |
| Emails | Grid of `UserEmailAdminRecord` | ❌ |
| Phones | Grid of `UserPhoneAdminRecord` | ❌ |
| Links | Grid of `UserLinkAdminRecord` | ❌ |
| Notes | Grid of `UserNoteAdminRecord` | ❌ |
| Memberships | Grid of `OrganizationUserMembershipAdminRecord` with `Role` column | ❌ |
| Files | Grid of `UploadFileAdminRecord` | ❌ |
