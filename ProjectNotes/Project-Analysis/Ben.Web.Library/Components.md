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

---

## Organisation Components (`Organization/` folder)

### `OrganizationList.razor`

**Route:** `/organizations`  
**File:** [`Ben.Web.Library/Organization/OrganizationList.razor`](../../../Ben.Web.Library/Organization/OrganizationList.razor)

#### Summary
Displays all organisations visible to the current user with per-row permission flags.

#### Features
- `TelerikGrid` with Name, URL Slug, Created date columns.
- **CMS** button (Info colour) — always visible; navigates to `/organizations/{id}/cms`. *(added 2026-07-18)*
- **Edit** button (shown when `CanEdit`) — navigates to `/organizations/{id}/edit`.
- **Delete** button (shown when `CanDelete`) — opens `TelerikDialog` confirmation.
- **New Organization** button at top (shown for SuperAdmin only).

---

### `OrganizationCreateEdit.razor`

**Routes:** `/organizations/create`, `/organizations/{OrgId:guid}/edit`  
**File:** [`Ben.Web.Library/Organization/OrganizationCreateEdit.razor`](../../../Ben.Web.Library/Organization/OrganizationCreateEdit.razor)

#### Summary
Form for creating or editing an organisation's Name and UrlName.

---

## CMS Components (`Organization/Cms/` folder) *(added 2026-07-18)*

### `OrgCmsEditor.razor`

**Route:** `/organizations/{OrgId:guid}/cms`  
**File:** [`Ben.Web.Library/Organization/Cms/OrgCmsEditor.razor`](../../../Ben.Web.Library/Organization/Cms/OrgCmsEditor.razor)

#### Summary
Main CMS hub page for an organisation. Entry point from the "CMS" button in `OrganizationList`.

#### Dependencies
- `IBenAdminClient` — CMS page and logo methods
- `IBenUserState` — auth check
- Telerik Blazor components (`TelerikTabStrip`, `TelerikGrid`, `TelerikDialog`, `TelerikWindow`)

#### Tabs

| Tab | Content |
|---|---|
| **Pages** | `TelerikGrid` with page list — Status badge, Visibility, section count, per-row actions |
| **Logos** | `TelerikGrid` with logo list — Active badge, alt text, file ID |

#### Pages Tab Actions

| Action | Behaviour |
|---|---|
| Edit | Opens `TelerikDialog` with page metadata form |
| Sections | Navigates to `OrgCmsPageEdit` |
| Preview | Opens `TelerikWindow` rendering full page HTML + active sections |
| Delete | Opens `TelerikDialog` confirmation; children re-parented |

#### Page Form (New / Edit dialog)
`TelerikTextBox` for Title and URL slug (auto-lowercased); `TelerikEditor` field for summary HTML; `TelerikNumericTextBox` for sort order; `TelerikDropDownList` for parent page; `TelerikCheckBox` for Is Public and Is Published.

#### Logos Tab
Add logo by pasting an UploadFile GUID; "Set Active" deactivates others; "Remove" hard-deletes.

---

### `OrgCmsPageEdit.razor`

**Route:** `/organizations/{OrgId:guid}/cms/pages/{PageId:guid}`  
**File:** [`Ben.Web.Library/Organization/Cms/OrgCmsPageEdit.razor`](../../../Ben.Web.Library/Organization/Cms/OrgCmsPageEdit.razor)

#### Summary
Detailed CMS page editor. Manages page metadata, the `PageHtml` summary, and an ordered list of `CmsSection` items.

#### Layout
Three Bootstrap cards stacked vertically:
1. **Page Settings** — title, URL slug, sort order, Published/Public toggles; Save button calls `UpdateCmsPageAsync`
2. **Summary / Intro** — `TelerikEditor` bound to `PageHtml`
3. **Sections** — ordered list of section rows with ↑↓ reorder buttons, type badge, content preview, Edit/Delete per row; "Add Section" button opens the section dialog

**Inline preview:** Toggle button splits the view 50/50. The right column re-renders immediately as content changes using `@((MarkupString)html)` for rich-text sections.

#### Section Dialog
`TelerikDialog` with:
- `TelerikDropDownList` for section type (shown only when creating)
- `TelerikTextBox` for optional title
- `TelerikCheckBox` for IsActive
- `<CmsSectionEditor>` component for type-specific content editing

#### Parameters

| Parameter | Type | Description |
|---|---|---|
| `OrgId` | `Guid` | Route — organisation. |
| `PageId` | `Guid` | Route — page to edit. |

---

### `CmsSectionEditor.razor`

**File:** [`Ben.Web.Library/Organization/Cms/CmsSectionEditor.razor`](../../../Ben.Web.Library/Organization/Cms/CmsSectionEditor.razor)

#### Summary
Reusable component that renders the appropriate content editor for a given `CmsSectionType`. Used inside the section dialog in `OrgCmsPageEdit`.

#### Parameters

| Parameter | Type | Description |
|---|---|---|
| `SectionType` | `CmsSectionType` | Determines which editor is shown. |
| `ContentJson` | `string` | Current JSON content — bound two-way via `ContentJsonChanged`. |
| `ContentJsonChanged` | `EventCallback<string>` | Fires with new JSON whenever content changes. |

#### Type → Editor Mapping

| `SectionType` | Editor | ContentJson schema |
|---|---|---|
| `RichText` | `TelerikEditor` | `{ "html": "..." }` |
| `CustomHtml` | `TelerikEditor` | `{ "html": "..." }` |
| `ImageBanner` | `TelerikTextBox` fields (fileId, altText, linkUrl) | `{ "uploadFileId": "...", "altText": "...", "linkUrl": "..." }` |
| `ContactInfo` | `TelerikCheckBox` grid (showAddresses, showEmails, showPhones, showLinks) | `{ "showAddresses": bool, ... }` |
| `FileGallery` | Raw JSON `TelerikTextArea` | `{ "uploadFileIds": [...] }` |
| `MemberRoster` | Raw JSON `TelerikTextArea` | `{ "memberIds": [...], "showRole": bool, "showBio": bool }` |

ContentJson is parsed via `JsonDocument.Parse` on load and serialised via `JsonSerializer.Serialize` on change.

---

### `CmsFileThumbnail.razor` *(added 2026-07-18)*

**File:** [`Ben.Web.Library/Organization/Cms/CmsFileThumbnail.razor`](../../../Ben.Web.Library/Organization/Cms/CmsFileThumbnail.razor)

#### Summary
Reusable lazy-loading thumbnail component. Fetches raw file bytes from `IBenAdminClient.GetFileDataAsync`, converts to a `data:` URI (base64), and renders as an `<img>`. Used in the logo picker gallery and the logos grid in `OrgCmsEditor`.

#### Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `FileId` | `Guid` | — | ID of the file to display. Pass `Guid.Empty` to skip loading. |
| `Width` | `string` | `"100px"` | CSS width applied to the element. |
| `Height` | `string` | `"75px"` | CSS height applied to the element. |
| `Alt` | `string?` | `null` | Alt text for the `<img>` tag. |

#### States

| State | Rendered as |
|---|---|
| Loading | Bootstrap `spinner-border-sm` centred in the bounding box |
| Loaded | `<img src="data:…;base64,…">` with `object-fit:cover` |
| Failed / no data | Grey placeholder box with "No preview" label |

#### Lifecycle
Data is fetched in `OnParametersSetAsync` and the component re-renders on `FileId` change. Each instance issues one `GetFileDataAsync` call; results are **not** shared across instances — cache at the parent level if needed for galleries.
