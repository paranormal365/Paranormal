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

## Organization Components (`Organization/` folder)

### `OrganizationList.razor`

**Route:** `/organizations`  
**File:** [`Ben.Web.Library/Organization/OrganizationList.razor`](../../../Ben.Web.Library/Organization/OrganizationList.razor)

#### Summary
Displays all organizations visible to the current user with per-row permission flags.

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
Form for creating or editing an organization's Name and UrlName.

---

## CMS Components (`Organization/Cms/` folder) *(added 2026-07-18)*

### `OrgCmsEditor.razor`

**Route:** `/organizations/{OrgId:guid}/cms`  
**File:** [`Ben.Web.Library/Organization/Cms/OrgCmsEditor.razor`](../../../Ben.Web.Library/Organization/Cms/OrgCmsEditor.razor)

#### Summary
Main CMS hub page for an organization. Entry point from the "CMS" button in `OrganizationList`.

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
| `OrgId` | `Guid` | Route — organization. |
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

---

### `UserMediaPreview.razor` *(added 2026-07-18)*

**File:** [`Ben.Web.Library/User/UserMediaPreview.razor`](../../../Ben.Web.Library/User/UserMediaPreview.razor)

#### Summary
Renders a contextual preview for an `UploadFile` based on its `ContentType`. Fetches raw bytes server-side via `IBenAdminClient.GetFileDataAsync` and renders as a base64 data URL — no bearer token needed in the browser.

#### Parameters

| Parameter | Type | Description |
|---|---|---|
| `FileId` | `Guid` | ID of the file to preview. |
| `ContentType` | `string?` | MIME type — determines render mode. |
| `FileName` | `string?` | Used for alt text and fallback badge. |

#### Render modes

| ContentType prefix | Rendered as |
|---|---|
| `image/*` | `<img>` — click to open fullscreen overlay |
| `video/*` | `<video controls>` player |
| `audio/*` | `<audio controls>` player |
| Anything else | Grey badge showing filename |

#### WaveSurfer (planned)
WaveSurfer.js v7.12.11 source has been added to `Ben.Web.WebApp/wwwroot/ts/wavesurfer/`. Future work: replace the `<audio>` element with a WaveSurfer waveform visualization via Blazor JS interop.

---

### `AdminUserDetail.razor` — Updated 2026-07-18

Complete rewrite. Added full CRUD on all sub-entity tabs and media preview on the Files tab.

#### New capabilities

| Tab | Add | Edit | Delete | Notes |
|---|---|---|---|---|
| Profile | — | ✅ | — | Same as before |
| Addresses | ✅ | ✅ | ✅ | Type dropdown + "New Type" button |
| Emails | ✅ | ✅ | ✅ | Type dropdown + Primary/Public flags |
| Phones | ✅ | ✅ | ✅ | Type dropdown + Primary/Cellular flags |
| Links | ✅ | ✅ | ✅ | Type dropdown + Active flag |
| Notes | ✅ | ✅ | ✅ | Type dropdown + Subject/Body |
| Memberships | — | — | — | Read-only (role shown) |
| Files | — | — | ✅ | **`UserMediaPreview`** column for images/video/audio |

#### Type management
Every type dropdown has a ✚ **New** button that opens a mini `TelerikDialog` to create a new type inline, then reloads the dropdown without navigating away.

---

## Audio Components (`Manage/Audio/` folder)

### `WaveSurferPlayer.razor`

**Route:** *(no route — embedded component)*  
**File:** [`Ben.Web.Library/Manage/Audio/WaveSurferPlayer.razor`](../../../Ben.Web.Library/Manage/Audio/WaveSurferPlayer.razor)  
**JS Module:** [`Ben.Web.Library/Manage/Audio/WaveSurferPlayer.razor.js`](../../../Ben.Web.Library/Manage/Audio/WaveSurferPlayer.razor.js)  
**Models:** [`Ben.Web.Library/Manage/Audio/WaveSurferOptions.cs`](../../../Ben.Web.Library/Manage/Audio/WaveSurferOptions.cs)

#### Summary
A full-featured Blazor audio waveform player backed by **WaveSurfer.js v7.12.11**. Fills its
container width, supports user drag-resize of height, and adapts colors from the active Telerik theme
at runtime via CSS custom properties. All parameter names use the PascalCase equivalents of the
WaveSurfer JS option names.

#### Audio Sources (`WsAudioSource`)

| Type | Factory | Description |
|---|---|---|
| `Url` | `WsAudioSource.FromUrl(url)` | Direct URL — stream, CDN, or `/api/upload-files/{id}/download` |
| `Bytes` | `WsAudioSource.FromBytes(bytes, contentType)` | Raw bytes fetched from DB via `IBenAdminClient` |
| `Base64` | `WsAudioSource.FromBase64(base64, contentType)` | Pre-encoded base64 audio string |
| `Url` (data URL) | `WsAudioSource.FromDataUrl(dataUrl)` | Full `data:audio/…;base64,…` string |

`ToLoadUrl()` resolves the source to the string WaveSurfer's `load()` accepts. The resolution
happens in C# before any JS interop call — the JS module never converts binary data.

#### Key Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Source` | `WsAudioSource?` | `null` | Audio source (see above) |
| `Options` | `WsOptions` | `new()` | WaveSurfer core options (PascalCase JS names) |
| `Plugins` | `WsPluginConfig` | `new()` | Plugin enable flags + options |
| `ShowControls` | `bool` | `true` | Render built-in play/pause/stop/volume/zoom/rate bar |
| `InitialHeight` | `string` | `"200px"` | Starting height of the component |
| `MinHeight` | `string` | `"80px"` | Minimum drag-resize height |
| `MaxHeight` | `string` | `"800px"` | Maximum drag-resize height |
| `CssClass` | `string?` | `null` | Extra CSS class on the wrapper |
| `ExtraControls` | `RenderFragment?` | `null` | Additional controls rendered inside the controls bar |

#### WsOptions — color defaults (Telerik theme integration)

Colors default to `null`; the JS module reads Telerik CSS custom properties at init time:

| C# property | JS option | CSS variable read | Light fallback | Dark fallback |
|---|---|---|---|---|
| `WaveColor` | `waveColor` | `--kendo-color-primary` | `#3B82F6` | `#93C5FD` |
| `ProgressColor` | `progressColor` | `--kendo-color-primary-emphasis` | `#1D4ED8` | `#2563EB` |
| `CursorColor` | `cursorColor` | `--kendo-body-text` | `#1E293B` | `#F1F5F9` |

#### Plugins (`WsPluginConfig`)

| Plugin flag | Options class | Description |
|---|---|---|
| `Regions` | — | Draggable/resizable audio segments |
| `Hover` | `WsHoverOptions` | Cursor timestamp label on hover |
| `Timeline` | `WsTimelineOptions` | Time ruler |
| `Zoom` | `WsZoomOptions` | Mouse-wheel / pinch zoom |
| `Minimap` | `WsMinimapOptions` | Navigation thumbnail |
| `Spectrogram` | `WsSpectrogramOptions` | Frequency spectrogram |
| `SpectrogramWindowed` | `WsSpectrogramOptions` | Memory-efficient spectrogram for long files |
| `Envelope` | `WsEnvelopeOptions` | Volume-fade SVG overlay with drag handles |

#### Events

| Callback | Signature | Fired when |
|---|---|---|
| `OnReady` | `EventCallback` | WaveSurfer fires `ready` |
| `OnPlay` / `OnPause` / `OnFinish` | `EventCallback` | Playback state changes |
| `OnTimeUpdate` | `EventCallback<double>` | Current time changes during playback |
| `OnLoading` | `EventCallback<int>` | Load progress 0–100 |
| `OnError` | `EventCallback<string>` | WaveSurfer error |
| `OnZoom` | `EventCallback<double>` | Zoom level changed |
| `OnSeeking` | `EventCallback<double>` | User seeks to a new position |
| `OnRegionCreated/Updated/Removed/Clicked/In/Out` | `EventCallback<WsRegionData>` or `<string>` | Region lifecycle |
| `OnEnvelopePointsChanged` | `EventCallback<List<WsEnvelopePoint>>` | Envelope control points changed |
| `OnEnvelopeVolumeChanged` | `EventCallback<double>` | Envelope volume changed |

#### Public Methods (programmatic control)

`PlayAsync`, `PauseAsync`, `PlayPauseAsync`, `StopAsync`, `SeekToAsync(double progress)`,
`SetVolumeAsync(double)`, `SetMutedAsync(bool)`, `SetPlaybackRateAsync(double)`, `SetZoomAsync(double)`,
`LoadAsync(string url)`, `IsPlayingAsync()`, `GetCurrentTimeAsync()`, `GetDurationAsync()`,
`GetVolumeAsync()`,
`AddRegionAsync(WsRegionParams)`, `RemoveRegionAsync(string)`, `ClearRegionsAsync()`,
`GetRegionsAsync()`, `PlayRegionAsync(string)`,
`SetEnvelopePointsAsync(List<WsEnvelopePoint>)`, `AddEnvelopePointAsync(WsEnvelopePoint)`,
`RemoveEnvelopePointAsync(string)`, `SetEnvelopeVolumeAsync(double)`, `GetEnvelopePointsAsync()`.

#### JS Module (`WaveSurferPlayer.razor.js`)

Served at `/_content/Ben.Web.Library/Manage/Audio/WaveSurferPlayer.razor.js`.

- WaveSurfer core + each plugin are lazy-loaded via dynamic `import()` on first use and cached
- `resolveTelerikColors()` reads Kendo CSS vars via `getComputedStyle(document.documentElement)`
  and detects dark mode by perceived luminance of `--kendo-body-bg`
- `ResizeObserver` on the wrapper div debounces (50 ms) and calls `ws.setOptions({ height: 'auto' })`
  to trigger WaveSurfer's internal resize logic after a user drag-resize
- `destroy(containerId)` disconnects the `ResizeObserver` and calls `ws.destroy()`

#### Build Notes

ESM bundles are pre-built and committed at `Ben.Web.WebApp/wwwroot/js/wavesurfer/`.

To rebuild (e.g. after updating WaveSurfer source):

```bash
cd Ben.Web.WebApp/wwwroot/ts/wavesurfer
npm install --legacy-peer-deps   # first time only
npm run build:blazor
```

`node_modules/` and `dist/` are git-ignored (`wwwroot/ts/wavesurfer/node_modules/`,
`wwwroot/ts/wavesurfer/dist/`).

---

### `AudioFilePreview.razor`

**Route:** *(no route — embedded component)*  
**File:** [`Ben.Web.Library/Manage/Audio/AudioFilePreview.razor`](../../../Ben.Web.Library/Manage/Audio/AudioFilePreview.razor)

#### Summary
A compact waveform preview widget designed to sit inside the Files-tab grid of `AdminUserDetail`.
Fetches audio bytes via `IBenAdminClient.GetFileDataAsync` on first render and displays an 80 px
WaveSurfer waveform with a ▶ play overlay. Right-clicking opens a Telerik context menu; one of the
menu items opens a full-featured modal window.

#### Parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `FileId` | `Guid` | ✅ | `UploadFile.Id` — used to fetch bytes and construct the `WsAudioSource`. |
| `FileName` | `string` | ✅ | Shown as the modal window title and in the fallback badge. |
| `ContentType` | `string` | ✅ | MIME type displayed in the full-view info bar. |
| `FileSize` | `long` | | Byte count formatted by `AudioFormatUtils.FormatSize` in the info bar. |

#### Compact View (grid cell)

- **Height:** 80 px, no controls bar, `interact: false` (click-to-seek disabled)
- **▶ overlay:** shown whenever `_ready && !_playing` — disappears while audio plays
- **Click anywhere on waveform:** calls `WaveSurferPlayer.PlayPauseAsync()`
- **Right-click:** opens `TelerikContextMenu` at cursor position

#### Context Menu Items

| Item | Action |
|---|---|
| ▶ Play | `_player.PlayAsync()` |
| ⏸ Pause | `_player.PauseAsync()` |
| ⏮ Rewind | `_player.StopAsync()` (seeks to 0, stops) |
| ⤢ Open Full View | Pauses compact player; opens modal window |

#### Full-View Modal (`TelerikWindow`)

- **Size:** 92 vw × 88 vh, modal, centered
- **Title:** `FileName`
- **Close:** `TelerikWindow` × action

**Info bar** (live-updating):

| Field | Value |
|---|---|
| Size | `AudioFormatUtils.FormatSize(FileSize)` — e.g. `3.14 MB  (3,293,184 bytes)` |
| Type | Raw MIME type string |
| Duration | `AudioFormatUtils.FormatTime(_modalDuration)` — updates on `OnReady` |
| Position | `AudioFormatUtils.FormatTime(_modalTime)` — live via `OnTimeUpdate` |

**Full WaveSurfer player** inside the modal:

| Setting | Value |
|---|---|
| Plugins | Hover, Timeline, Zoom, Spectrogram, Regions |
| `InitialHeight` | 280 px |
| `MinHeight` / `MaxHeight` | 120 px / 600 px (user drag-resize) |
| `ShowControls` | `true` |

#### `AudioFormatUtils` — shared helpers

**File:** [`Ben.Web.Library/Manage/Audio/AudioFormatUtils.cs`](../../../Ben.Web.Library/Manage/Audio/AudioFormatUtils.cs)

| Method | Returns | Description |
|---|---|---|
| `IsAudioContentType(string?)` | `bool` | `true` when MIME starts with `audio/` (case-insensitive). Used by `AdminUserDetail` to choose between `AudioFilePreview` and `UserMediaPreview`. |
| `FormatTime(double seconds)` | `string` | `m:ss.f` or `h:mm:ss.f` format for player time displays. |
| `FormatSize(long bytes)` | `string` | Human-readable size + raw byte count in parens (e.g. `3.14 MB  (3,293,184 bytes)`). |
| `FormatSizeCompact(long bytes)` | `string` | Short form without raw byte count (e.g. `3.1 MB`). Used in upload dialogs. |

#### Dependencies
- `IBenAdminClient.GetFileDataAsync` — fetches audio bytes on first render
- `WaveSurferPlayer` — both compact and modal instances share the same `WsAudioSource`
- `TelerikContextMenu<AudioContextMenuItem>` — right-click menu (compact waveform)
- `TelerikContextMenu<RegionContextMenuItem>` — right-click menu for waveform regions (full-view modal)
- `TelerikWindow` — full-view modal
- `WsRegionExplorer` — opened when user clicks "Explore Region" in the region context menu

#### Region Context Menu (added 2026-07-19)
Right-clicking any WaveSurfer region in the full-view modal shows:  
**Play Region** / **Explore Region** / **Edit Label** / **Delete Region**.  
After a clip is saved from `WsRegionExplorer`, `OnChildClipSaved` refreshes the overlay; child clips appear as green locked regions and badge chips above the player.

---

### `WsRegionExplorer.razor`

**File:** [`Ben.Web.Library/Manage/Audio/WsRegionExplorer.razor`](../../../Ben.Web.Library/Manage/Audio/WsRegionExplorer.razor)

Full-featured modal window for exploring, annotating, and clipping a selected audio region.

#### Parameters

| Parameter | Type | Notes |
|---|---|---|
| `FileId` | `Guid` | Source file ID |
| `FileName` | `string` | Display name |
| `ContentType` | `string` | MIME type of the source file |
| `FileSize` | `long` | Byte length of source |
| `Region` | `WsRegionData?` | The region to explore (`Id`, `Start`, `End`, `Label`) |
| `Visible` / `VisibleChanged` | `bool` / `EventCallback<bool>` | Two-way bind for open/close |
| `OnClipSaved` | `EventCallback<UploadFileRecord>` | Fires after a successful save-as-clip |

#### Features
- Loads the **full audio** and overlays the selected region as a locked blue region
- **Speed slider** (0.25× – 3.0×) calls `SetPlaybackRateAsync` live
- **Save Region as WAV** — posts `ClipAudioRequest` to `/api/upload-files/{id}/clip` (NAudio WAV/MP3 support); fires `OnClipSaved`
- **Region Notes** — create/edit/delete rich-text notes via `TelerikEditor`; can be overall (no time) or point-in-time (pinned to `_currentTime`)
- **Sub-region exploration** — right-click user-drawn regions inside the explorer → nested `WsRegionExplorer`

---

### `UploadFileVoteBar.razor` (added 2026-07-19)

**File:** `Ben.Web.Library/Manage/UploadFileVoteBar.razor`  
**Usage:** `<UploadFileVoteBar FileId="@id" />`

Reusable vote bar for any `UploadFile`. Renders 👍 N | ±score | 👎 M. Active vote is highlighted (Primary/Error colour). Clicking an active vote removes it (toggle-off). Unauthenticated users see "Sign in to vote" with buttons disabled.

**Calls:** `GetVoteSummaryAsync`, `UpsertMyVoteAsync`, `RemoveMyVoteAsync`.

---

### `OrganizationView.razor` (added 2026-07-19)

**File:** `Ben.Web.Library/Organization/OrganizationView.razor`  
**Route:** `/organizations/{OrgId:guid}`  
**Query param:** `?returnUrl=` — back button returns to caller page

Read-only view of an organization's name, URL slug, ID, and timestamps. Shows an **Edit** button when `UserState.IsSuperAdmin || listItem.CanEdit` (fetched via `GetOrganizationsAsync`). Edit mode is inline (Name + UrlName fields) with Save/Cancel.

Used from `AdminUserDetail` Memberships tab via the **View** command button.

---

### WsRegionExplorer — updated behaviour (2026-07-19)

Key behaviour changes from original implementation:

| Aspect | Updated behaviour |
|---|---|
| Audio loaded | Only the region's bytes (`GetClipPreviewAsync` — no DB record) |
| Waveform | Shows only the selected region (time 0 = Region.Start in original file) |
| Focus overlay | Removed — the whole clip is the region |
| Notes shown | Filtered to exact region match or point-in-time within [Region.Start, Region.End] |
| Point-in-time storage | Stored as absolute file time (`Region.Start + _currentTime`) |
| Sub-region explore | Converts clip-relative times to absolute before opening nested explorer |
| Sub-region draw | `RegionsDragToCreate = true` — users can drag to create sub-regions |

---

### AudioFilePreview — full-view modal additions (2026-07-19)

- **Spectrogram toggle**: "Show/Hide Spectrogram" button in info bar. Web Worker (`spectrogram-worker.js`) computes FFT off the main thread; viridis colormap canvas inserted after waveform. Right-click → context menu with "Toggle Labels".
- **Region draw**: Click-and-drag creates a blue preview overlay, then a WaveSurfer region on release. Single click still seeks. Right-click context menu: Play / Explore & Notes / Create Audio File / Edit Label / Delete.
- **Child clip overlay**: Saved clips overlaid as green locked regions on the parent waveform; shown as `AudioFilePreview` components below the player.
- **Resizable**: Full-view player has `Resizable="true"` — bottom edge can be dragged.
