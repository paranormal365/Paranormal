# Ben.Web.Library — Services

## `IBenUserState`

**Namespace:** `Ben.Web.Library.Services`  
**File:** [`Ben.Web.Library/Services/IBenUserState.cs`](../../../Ben.Web.Library/Services/IBenUserState.cs)  
**Implemented by:** `WebApiTokenStore` in `Ben.Web.WebApp`

### Summary
Exposes the minimum authentication state needed by shared Blazor library components.  
Library components depend on this interface rather than the full `IWebApiTokenStore` so that `Ben.Web.Library` does not need a project reference to `Ben.Web.WebApp`.

### Properties

| Property | Type | Description |
|---|---|---|
| `IsAuthenticated` | `bool` | `true` when the user has a valid access token. |
| `IsSuperAdmin` | `bool` | `true` when the user holds the [`RoleNames.SuperAdmin`](../Ben.Data.Common/Constants.md#rolenames) role. |
| `IsImpersonating` | `bool` | `true` when the current session is a SuperAdmin impersonation session. |
| `UserEmail` | `string?` | Authenticated user's email, or `null`. |
| `UserId` | `Guid?` | Authenticated user's primary key, or `null`. |

---

## `IBenAdminClient`

**Namespace:** `Ben.Web.Library.Services`  
**File:** [`Ben.Web.Library/Services/IBenAdminClient.cs`](../../../Ben.Web.Library/Services/IBenAdminClient.cs)  
**Implemented by:** `BenAdminClientAdapter` in `Ben.Web.WebApp`

### Summary
Defines all SuperAdmin HTTP operations available to library Blazor components.  
`BenAdminClientAdapter` delegates each call to the typed `IWebApiClient` HTTP client in the host project.

### Organization Methods

| Method | Returns | Description |
|---|---|---|
| `GetOrganizationsAsync(token)` | `IReadOnlyList<OrganizationListItemResponse>` | Orgs visible to current user with per-org `CanEdit`/`CanDelete` flags. |
| `GetOrganizationAsync(id, token)` | `OrganizationAdminRecord?` | Single org for edit form pre-fill. |
| `CreateOrganizationAsync(request, token)` | `OrganizationAdminRecord?` | Creates org (SuperAdmin only). |
| `UpdateOrganizationAsync(id, request, token)` | `OrganizationAdminRecord?` | Updates Name and UrlName. |
| `DeleteOrganizationAsync(id, token)` | `bool` | Deletes org. |

### Role Methods

| Method | Returns | Description |
|---|---|---|
| `GetRolesAsync(token)` | `IReadOnlyList<AdminRoleWithCountResponse>` | All site roles with user counts. |
| `CreateRoleAsync(roleName, token)` | `AppRoleAdminRecord?` | Creates a new role. |
| `DeleteRoleAsync(roleId, token)` | `bool` | Deletes role (server refuses if users assigned). |

### User Methods

| Method | Returns | Description |
|---|---|---|
| `GetAllUsersAsync(token)` | `IReadOnlyList<AppUserRecord>` | Lightweight list of all users. |
| `GetUserDetailAsync(userId, token)` | `AppUserDetailAdminRecord?` | Full aggregate: profile + 8 related lists. |
| `UpdateUserProfileAsync(userId, request, token)` | `AppUserAdminRecord?` | Updates editable profile fields. |

### Impersonation Methods

| Method | Returns | Description |
|---|---|---|
| `ImpersonateUserAsync(targetUserId, targetUserEmail, token)` | `bool` | Starts impersonation session; saves current token, applies target user token. |
| `StopImpersonating()` | `void` | Synchronous in-memory operation — restores original SuperAdmin token. |

### File Type Methods

| Method | Returns | Description |
|---|---|---|
| `GetFileTypesWithExtensionsAsync(token)` | `IReadOnlyList<AdminFileTypeWithExtensionsResponse>` | All file types + their extension patterns. |
| `CreateFileTypeAsync(request, token)` | `UploadFileTypeRecord?` | Creates a new file type. |
| `UpdateFileTypeAsync(id, request, token)` | `UploadFileTypeRecord?` | Updates existing file type. |
| `DeleteFileTypeAsync(id, token)` | `bool` | Deletes file type (cascades extensions). |

### File Type Extension Methods

| Method | Returns | Description |
|---|---|---|
| `CreateFileTypeExtensionAsync(request, token)` | `UploadFileTypeExtensionRecord?` | Adds an extension pattern. |
| `UpdateFileTypeExtensionAsync(id, pattern, token)` | `UploadFileTypeExtensionRecord?` | Replaces the pattern string. |
| `DeleteFileTypeExtensionAsync(id, token)` | `bool` | Removes a pattern. |

### CMS Page Methods *(added 2026-07-18)*

| Method | Returns | Description |
|---|---|---|
| `GetCmsPagesAsync(orgId, token)` | `IReadOnlyList<CmsPageListItem>` | Pages for the org with `CanEdit`/`CanDelete` flags. |
| `GetCmsPageAsync(orgId, pageId, token)` | `CmsPageDetail?` | Full page detail including ordered sections. |
| `CreateCmsPageAsync(orgId, request, token)` | `CmsPageDetail?` | Creates a page. |
| `UpdateCmsPageAsync(orgId, pageId, request, token)` | `CmsPageDetail?` | Updates page metadata. |
| `DeleteCmsPageAsync(orgId, pageId, token)` | `bool` | Deletes the page. |

### CMS Section Methods *(added 2026-07-18)*

| Method | Returns | Description |
|---|---|---|
| `CreateCmsSectionAsync(orgId, pageId, request, token)` | `CmsSectionRecord?` | Creates a section. |
| `UpdateCmsSectionAsync(orgId, pageId, sectionId, request, token)` | `CmsSectionRecord?` | Updates section content. |
| `ReorderCmsSectionsAsync(orgId, pageId, orderedIds, token)` | `bool` | Sets `SortOrder` per the ordered list. Uses `PutVoidAsync`. |
| `DeleteCmsSectionAsync(orgId, pageId, sectionId, token)` | `bool` | Removes a section. |

### Organization Logo Methods *(added 2026-07-18)*

| Method | Returns | Description |
|---|---|---|
| `GetOrgLogosAsync(orgId, token)` | `IReadOnlyList<OrganizationLogoRecord>` | All logos for the org. |
| `CreateOrgLogoAsync(orgId, request, token)` | `OrganizationLogoRecord?` | Associates an upload file as a logo. |
| `UpdateOrgLogoAsync(orgId, logoId, request, token)` | `OrganizationLogoRecord?` | Updates alt text / active state / sort order. |
| `DeleteOrgLogoAsync(orgId, logoId, token)` | `bool` | Removes logo record. |

### Request/Response Records

| Type | Description |
|---|---|
| `AdminCreateOrganizationRequest(Name, UrlName)` | Create org payload. |
| `AdminUpdateOrganizationRequest(Name, UrlName)` | Update org payload. |
| `OrganizationListItemResponse(Id, Name, UrlName, DateCreated, CanEdit, CanDelete)` | Org list row with permission flags. |
| `AdminRoleWithCountResponse(Role, UserCount)` | Role + user count. |
| `AdminCreateUserRequest` | New user payload: Email, Password, DisplayName, UserName, IsEmailConfirmed, IsSuperAdmin. |
| `AdminCreateRoleRequest` | New role payload. |
| `AdminFileTypeWithExtensionsResponse(FileType, Extensions)` | Combined file type + patterns response. |
| `AdminCreateFileTypeRequest` | Create payload including display metadata and `AllowAllExtensions` flag. |
| `AdminUpdateFileTypeRequest` | Update payload. |
| `AdminCreateFileTypeExtensionRequest(UploadFileTypeId, Pattern, CreatedByAppUserId)` | Extension create payload. Pattern format: `.txt` or `.doc*`. |
| `AdminUpdateUserProfileRequest` | All editable profile fields including audit timestamps (SuperAdmin editable). |
| `CmsPageListItem(Id, OrganizationId, ParentPageId, PageTitle, UrlName, IsHome, IsPublished, IsPublic, SortOrder, SectionCount, CanEdit, CanDelete, DateCreated)` | CMS page list row. *(added 2026-07-18)* |
| `CmsPageDetail(Id, ..., Sections)` | Full page including `IReadOnlyList<CmsSectionRecord>`. *(added 2026-07-18)* |
| `CmsCreatePageRequest(PageTitle, UrlName, PageHtml?, IsPublic, ParentPageId?, SortOrder)` | Create page payload. *(added 2026-07-18)* |
| `CmsUpdatePageRequest(PageTitle, UrlName, PageHtml?, IsPublished, IsPublic, ParentPageId?, SortOrder)` | Update page payload. *(added 2026-07-18)* |
| `CmsCreateSectionRequest(SectionType, Title?, ContentJson, SortOrder, IsActive)` | Create section payload. *(added 2026-07-18)* |
| `CmsUpdateSectionRequest(Title?, ContentJson, IsActive)` | Update section payload. *(added 2026-07-18)* |
| `CmsCreateLogoRequest(UploadFileId, AltText?, IsActive, SortOrder)` | Add logo payload. *(added 2026-07-18)* |
| `CmsUpdateLogoRequest(AltText?, IsActive, SortOrder)` | Update logo payload. *(added 2026-07-18)* |

### CMS File Library Methods *(added 2026-07-18)*

Used by [`CmsFileThumbnail.razor`](../../../Ben.Web.Library/Organization/Cms/CmsFileThumbnail.razor) and the logo picker dialog in [`OrgCmsEditor.razor`](../../../Ben.Web.Library/Organization/Cms/OrgCmsEditor.razor).

| Method | Returns | Description |
|---|---|---|
| `GetOrgSharedFilesAsync(orgId, token)` | `IReadOnlyList<UploadFileRecord>` | Files shared with the org — used to populate the logo gallery. Calls `GET /api/upload-files/org/{orgId}`. |
| `GetFileDataAsync(fileId, token)` | `(byte[] Data, string ContentType)?` | Downloads raw file bytes for base64 thumbnail rendering. Returns `null` if unavailable. |
| `GetPublicFileTypesAsync(token)` | `IReadOnlyList<UploadFileTypeRecord>` | Active upload file types — used in the Upload tab dropdown. |
| `UploadImageAsync(fileTypeId, userId, fileName, contentType, data, token)` | `UploadFileRecord?` | Constructs multipart form data and calls `POST /api/upload-files`. Sets `isPublic=true`. Returns the created record, or `null` on failure. |

### Audio Config Methods *(added 2026-07-18)*

Persist and restore `WaveSurferPlayer` display settings per audio `UploadFile`.
One config row per file — absent row means the player uses Telerik theme-derived defaults at runtime.

| Method | Returns | Endpoint | Description |
|---|---|---|---|
| `GetAudioConfigAsync(fileId, token)` | `UploadFileAudioConfigRecord?` | `GET /api/upload-files/{fileId}/audio-config` | Saved config, or `null` if none exists. |
| `UpsertAudioConfigAsync(fileId, request, token)` | `UploadFileAudioConfigRecord?` | `PUT /api/upload-files/{fileId}/audio-config` | Create or fully replace the config; returns the saved record. |
| `DeleteAudioConfigAsync(fileId, token)` | `bool` | `DELETE /api/upload-files/{fileId}/audio-config` | Remove config (idempotent — `204` even when nothing exists). |

**`UpsertAudioConfigRequest` — all fields are optional; omitted = WaveSurfer / Telerik theme default:**

| Field | Type | Default | Meaning |
|---|---|---|---|
| `WaveColor` | `string?` | `null` | Unplayed waveform color. `null` → `--kendo-color-primary` CSS var at runtime. |
| `ProgressColor` | `string?` | `null` | Played-portion color. `null` → `--kendo-color-primary-emphasis`. |
| `CursorColor` | `string?` | `null` | Playback cursor color. `null` → `--kendo-body-text`. |
| `CursorWidth` | `int?` | `null` | Cursor width in pixels. |
| `Height` | `int?` | `null` | Waveform canvas height in pixels. `null` = "auto" (fills container). |
| `BarWidth` | `int?` | `null` | Bar width → bar-style waveform. `null` = solid. |
| `BarGap` | `int?` | `null` | Gap between bars (pixels). |
| `BarRadius` | `int?` | `null` | Bar corner radius. |
| `BarHeight` | `double?` | `null` | Vertical scaling factor (1.0 = normal). |
| `BarAlign` | `string?` | `null` | `"top"` or `"bottom"`. |
| `Normalize` | `bool` | `false` | Stretch peaks to fill full height. |
| `DragToSeek` | `bool` | `false` | Allow dragging cursor to seek. |
| `HideScrollbar` | `bool` | `false` | Hide horizontal scrollbar. |
| `AudioRate` | `double?` | `null` | Initial playback speed. |
| `EnableHover` | `bool` | `true` | Hover timestamp label plugin. |
| `EnableTimeline` | `bool` | `true` | Timeline ruler plugin. |
| `EnableZoom` | `bool` | `false` | Mouse-wheel / pinch zoom plugin. |
| `EnableMinimap` | `bool` | `false` | Navigation thumbnail plugin. |
| `EnableSpectrogram` | `bool` | `false` | Frequency spectrogram plugin. |
| `EnableSpectrogramWindowed` | `bool` | `false` | Memory-efficient spectrogram for long files. |
| `EnableEnvelope` | `bool` | `false` | Volume-fade SVG overlay plugin. |
| `EnableRegions` | `bool` | `false` | Draggable/resizable region marker plugin. |
| `HoverOptionsJson` | `string?` | `null` | JSON-serialized `WsHoverOptions` (lineColor, labelColor, etc.). |
| `TimelineOptionsJson` | `string?` | `null` | JSON-serialized `WsTimelineOptions` (height, intervals, etc.). |
| `ZoomOptionsJson` | `string?` | `null` | JSON-serialized `WsZoomOptions` (scale, maxZoom, etc.). |
| `MinimapOptionsJson` | `string?` | `null` | JSON-serialized `WsMinimapOptions` (height, overlayColor, etc.). |
| `SpectrogramOptionsJson` | `string?` | `null` | JSON-serialized `WsSpectrogramOptions`. |
| `SpectrogramWindowedOptionsJson` | `string?` | `null` | JSON-serialized `WsSpectrogramOptions` (windowed variant). |
| `EnvelopeOptionsJson` | `string?` | `null` | JSON-serialized `WsEnvelopeOptions` (volume, lineColor, points, etc.). |
| `InitialHeight` | `string` | `"200px"` | Starting height of the resizable player wrapper (any CSS length). |
| `MinHeight` | `string` | `"80px"` | Minimum drag-resize height. |
| `MaxHeight` | `string` | `"800px"` | Maximum drag-resize height. |
| `ShowControls` | `bool` | `true` | Show built-in play/pause/stop/volume/zoom/rate bar. |
| `MinZoom` | `double` | `10` | Minimum value for the built-in zoom slider (minPxPerSec). |
| `MaxZoom` | `double` | `1000` | Maximum value for the built-in zoom slider (minPxPerSec). |

**Typical page usage:**

```csharp
// 1. Load (null = not yet configured → use theme defaults)
var record = await AdminClient.GetAudioConfigAsync(fileId);
var source  = WsAudioSource.FromUrl($"/api/upload-files/{fileId}/download");
_config = record is not null ? record.ToWsConfig(source) : WsConfig.Default(source);

// 2. Save
await AdminClient.UpsertAudioConfigAsync(fileId, new UpsertAudioConfigRequest
{
    EnableZoom     = true,
    ZoomOptionsJson = """{"maxZoom":500}""",
    InitialHeight  = "260px",
    EnableHover    = true,
    EnableTimeline = true,
    ShowControls   = true,
    MinHeight = "80px", MaxHeight = "800px",
});

// 3. Reset to defaults
await AdminClient.DeleteAudioConfigAsync(fileId);
```

---

### New `IBenAdminClient` methods (added 2026-07-18/19)

#### Audio Config
| Method | Returns | Description |
|---|---|---|
| `GetAudioConfigAsync(fileId)` | `UploadFileAudioConfigRecord?` | Saved WaveSurfer config for a file |
| `UpsertAudioConfigAsync(fileId, request)` | `UploadFileAudioConfigRecord?` | Create or replace |
| `DeleteAudioConfigAsync(fileId)` | `bool` | Remove saved config |

#### Region Notes
| Method | Returns | Description |
|---|---|---|
| `GetRegionNotesAsync(fileId)` | `IReadOnlyList<UploadFileRegionNoteRecord>` | All notes ordered by region start / time offset |
| `CreateRegionNoteAsync(fileId, request)` | `UploadFileRegionNoteRecord?` | Create note |
| `UpdateRegionNoteAsync(fileId, noteId, request)` | `UploadFileRegionNoteRecord?` | Update |
| `DeleteRegionNoteAsync(fileId, noteId)` | `bool` | Delete |

#### Audio Clip
| Method | Returns | Description |
|---|---|---|
| `ClipAudioAsync(fileId, request)` | `UploadFileRecord?` | Server-clip WAV/MP3 → WAV, saves as new `UploadFile` with parent tracking |
| `GetClipPreviewAsync(fileId, start, end)` | `(byte[] Data, string ContentType)?` | Clip bytes only — no DB record created. Used by `WsRegionExplorer` |
| `GetChildClipsAsync(fileId)` | `IReadOnlyList<UploadFileRecord>` | All child clips ordered by `RegionStart` |

#### Votes
| Method | Returns | Description |
|---|---|---|
| `GetVoteSummaryAsync(fileId)` | `UploadFileVoteSummary?` | Aggregated counts + calling user's score |
| `UpsertMyVoteAsync(fileId, score)` | `UploadFileVoteRecord?` | Create or update vote (score: 1 upvote / -1 downvote) |
| `RemoveMyVoteAsync(fileId)` | `bool` | Remove vote; no-op if none exists |
