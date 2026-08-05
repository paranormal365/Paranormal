# IsHaunted Platform — System Architecture

**Last updated:** 2026-08-05
**Branch:** `feature/ben-video-integration` (off develop)
**Tests:** 1,324 passing (374 repo + 950 web)

---

## Overview

IsHaunted is a full-stack .NET web platform for paranormal investigation organizations. It enables ghost-hunting groups to manage memberships, accept client investigation requests, document cases with evidence and timelines, schedule investigations, and publish findings to the public.

---

## Technical Stack

| Layer | Technology |
|---|---|
| **Backend API** | ASP.NET Core 10, EF Core 10, SQL Server |
| **Frontend** | Blazor Server (ASP.NET Core 10) |
| **UI Components** | Telerik UI for Blazor 14.1.0 |
| **Authentication** | ASP.NET Core Identity (opaque bearer tokens) + Microsoft Entra OIDC |
| **Database** | SQL Server — `IsHauntedDb` on 192.168.1.71:1433 (dev) |
| **File Storage** | Local disk via `LocalFileStorageService` (`.uploads/` folder) |
| **Maps/Geocoding** | geocod.io (HTTP), Telerik Map (Kendo MapLayer) |
| **Audio** | WaveSurfer.js v7 (rollup ESM build) |

---

## Solution Structure (14 projects)

```
Ben.slnx
├── Ben.Data.Common/          Shared enums, interfaces (IIDStd, IFileStorageService, IAuditableEntity)
├── Ben.Data.Source/          EF Core entities, BenDataContext, migrations
├── Ben.Data.WebApi/          ASP.NET Core Web API — all endpoints
├── Ben.Service.Mappings/     AutoMapper profiles (Entity → Record)
├── Ben.Service.Models/       DTOs / records (Entities/, Admin/, People/, Identity/)
├── Ben.Service.RepositoryService/    Repository pattern over BenDataContext
├── Ben.Service.RepositoryService.Tests/  374 xUnit tests
├── Ben.Service.Security/     Org-level tenant security service
├── Ben.Web.Library/          Razor Class Library — all reusable Blazor components
├── Ben.Web.Tests/            950 xUnit tests for WebApp services and controllers
├── Ben.Web.WebApp/           Blazor Server application
├── Ben.Web.Playwright/       Playwright front-end tests (NUnit + Microsoft.Playwright)
└── [external] ../Github-BenVideo/Ben.Video.Editor/   Video editor RCL (cross-repo project reference)
```

> **Ben.Video.Editor** lives in `/Users/ben/Source/Github-BenVideo/` — a sibling folder to this repo.
> Referenced by `Ben.Web.Library.csproj`; visible in the solution under `/Media Projects/`.
> Changes to the RCL are picked up by Ben on the next build with no publish step needed.

---

## How to Start the System

### Prerequisites
- .NET 10 SDK
- SQL Server instance (dev: `IsHauntedDb` on 192.168.1.71)
- `Ben.Data.WebApi/appsettings.Development.json` with connection string + seed credentials (gitignored)

### Start command
```bash
# VS Code task (recommended)
Ctrl+Shift+P → Tasks: Run Task → start-web-app

# Or manually
cd /Users/ben/Source/Ben
bash scripts/start-webapp-with-api.sh
```

This script:
1. Checks if WebApi is already running at `http://localhost:5252`
2. If not, starts it (logs to `.vscode/webapi.log`); waits up to 60s
3. Opens a browser tab at `http://localhost:5078` once the WebApp is ready
4. Runs `exec dotnet run` for the WebApp (foreground)

### Default credentials
| Email | Password | Role |
|---|---|---|
| `haveben@msn.com` | `Y@ung615` | SuperAdmin + BenCo Owner |
| `sarah.mitchell@benco.dev` | `S@rah!Mitchell26` | BenCo Org Admin |
| `james.thornton@benco.dev` | `J@mes!Thornton26` | BenCo Member |

---

## Authentication Flow

```
Browser → POST /login → WebApi returns opaque bearer token
                              ↓
                        GET /api/me → { UserId, IsSuperAdmin }
                              ↓
                    WebApp stores token in ProtectedLocalStorage
                    All API calls via WebApiBearerTokenHandler (auto-injects bearer header)
```

**Important:** Opaque bearer tokens are invalidated when the WebApi restarts. Users must re-login after any WebApi restart.

**Entra OIDC:** `haveben@msn.com` (Microsoft account) is linked to the local SuperAdmin account. The `EntraClaimsTransformation` injects `app_user_id` + role claims after token validation.

---

## Database

### Connection (dev)
```
Server=192.168.1.71,1433;Database=IsHauntedDb;User Id=IsHaunted;Password=ishaunted;
Encrypt=True;TrustServerCertificate=True;
```

### Migration command
```bash
dotnet ef migrations add <Name> \
  --project Ben.Data.Source \
  --startup-project Ben.Data.WebApi \
  --output-dir Migrations
dotnet ef database update --project Ben.Data.Source --startup-project Ben.Data.WebApi
```

### Applied migrations (33 total, as of 2026-08-05)
```
InitialCreate → AddIdentitySchema → AddGeocodingMetadata → AddOrganizationSecurityModel
→ AddUploadFileEntities → AddUploadFileSharing → AddUploadFileTypeExtensions
→ ReplaceIsOrganizationAdminWithRole → ReplaceActionNameWithActionsBitmask
→ AddFileStoragePath → AddOrganizationRoles → FixLatLonPrecision
→ AddAddressVisibilityAndOrgSettings → AddExperienceTaxonomy
→ AddOrgClientAcceptanceAndAreaOfOperation → AddClientRequest → AddCaseManagement
→ AddCaseNumberAndYear → AddMembershipPhase3 → AddMessagingAndCalendar
→ AddInvestigationAndEvidenceVoting → AddCaseTransferAndPublicDiscovery
→ AddOrganizationMembershipRequestsAndFiles → AddOrgFilePublishingAndDeleteLog
→ AddOrganizationAddressMapConfig → AddInvestigationWorkflowFields
→ AddInvestigationScheduling → AddCaseClientAccess → AddFileMetadata
→ AddCaseReportBuilder → AddCaseResearch → AddCaseNotes
→ AddOrganizationPublicContact → AddVideoProjects
```

---

## File Storage

All uploaded files go to disk — **never stored as `FileData` bytes in the database**.

```
.uploads/
  users/{userId}/{storedFileName}   ← user-uploaded files
  orgs/{orgId}/{storedFileName}     ← org-owned files
```

The `LocalFileStorageService` is registered as singleton. The download endpoint falls back to `FileData` for legacy rows (handled by `FileMigrationService` at startup).

---

## API Overview

### Public endpoints (no auth required)
| Route | Description |
|---|---|
| `GET /api/public/organizations/search?lat=&lon=` | Org proximity search for clients |
| `GET /api/public/organizations/{urlName}/cases` | Public cases for an org |
| `GET /api/public/organizations/{urlName}/cases/{ref}` | Public case detail (pseudonym applied) |
| `GET /api/experience-categories/with-types` | Approved taxonomy (global) |
| `GET /api/evidence-votes/{fileId}/summary` | Vote counts only (no voter IDs) |
| `GET /api/upload-files/{id}/download` | Download public files |

### Authenticated endpoints (bearer token required)
- `/api/me` — current user info
- `/api/organizations/{orgId}/*` — org CRUD, cases, members, calendar, messaging
- `/api/client-requests/*` — client intake workflow
- `/api/evidence-votes/*` — full vote details + cast/remove votes
- `/api/admin/*` — SuperAdmin only

### Authorization model
- **SuperAdmin** — site role, bypasses all org permission checks
- **Org Owner / Administrator** — full access to their org (bypasses per-table checks)
- **Named roles** — `OrganizationRole` → `OrganizationRolePermission` (CRUD bitmask per table)
- **Direct grants** — `OrganizationAccessGrant` (individual exception)
- Always deny-by-default; explicit grant required at one of the above sources

---

## Key Platform Workflows

### Client Investigation Request Flow
```
1. Client creates account
2. Client fills out intake wizard (/my-requests/new):
   - Address + geocoding
   - Personal details (gender, birth year)
   - Description of experiences (Telerik editor)
   - Select up to 2 orgs from proximity search
3. Org(s) receive notification
4. First org to accept → ClientRequest.Status = Assigned
   → Case created (#2026-042 — Smith, Nashville TN)
   → 4 CMS pages auto-generated
   → Other org applications cancelled
5. Case manager assigned; investigation(s) scheduled
6. Evidence collected; timeline entries added
7. Case summarized; published as Public or Haunted (with pseudonym)
```

### Case Reference Format
```
#2026-042 — Smith, Nashville TN
│    │       │
│    │       └─ Title: "{Surname}, {City} {State}" (auto-generated, editable)
│    └─────── Zero-padded sequential number per org per year
└──────────── Year opened
```

### Membership Application Flow
```
1. User applies (message + answers to org's custom questions)
2. Reviewer can:
   a. Accept directly → membership created + welcome notification
   b. Deny → CanReapply flag + DenialReason stored + notification sent
   c. Open to committee vote → deadline set → members vote (Approve/Deny/Abstain)
      → auto-resolves on deadline (majority determines outcome)
```

### Evidence Voting Privacy Model
```
Public endpoint GET /summary → only counts (Confirms/Disputes/Inconclusive)
Authenticated endpoint GET / → full voter list with identities
Voting → cast or update (upsert per file+voter)
Public voters (non-members) tracked separately from org-member voters
```

---

## Component Folder Conventions

All reusable Blazor components live in `Ben.Web.Library/`:

| Feature | Folder | Key files |
|---|---|---|
| SuperAdmin | `SuperAdmin/` | AdminFileTypes, AdminRoles, AdminSidePanel, AdminExperienceTaxonomy |
| User management | `User/` | AdminUsers, AdminUserDetail, AdminUserCreate |
| Organizations | `Organization/` | OrganizationList, OrganizationCreateEdit, OrganizationMembers |
| Cases | `Organization/Cases/` | CaseList, CaseDetail, CaseTimeline, InvestigationPanel, EvidenceVoteWidget |
| Client intake | `Client/` | ClientRequestWizard, ClientRequests |
| Messaging | `Messaging/` | OrgMessages, MessageList |
| Scheduler | `Manage/Calendar/` | OrgScheduler (TelerikScheduler) |
| Audio player | `Manage/Audio/` | WaveSurferPlayer |
| Video editor | `Manage/Video/` | VideoEditorPage (standalone `/video-editor`), CaseVideoEditorPage (case-scoped, in progress) |
| Maps | `Manage/Maps/` | AddressMapPlayer |
| CMS | `Organization/Cms/` | OrgCmsEditor, OrgCmsPageEdit |

---

## Known Gotchas

1. **Bearer token expiry** — Opaque tokens invalidated on WebApi restart. Re-login required.
2. **TelerikMap tile URL** — Must be a JS function name on `window`, registered before map renders.
3. **TelerikDialog ShouldRender()=false** — When `Visible=true`, use `@key` or self-contained child component to force updates.
4. **TelerikScheduler `OnEdit`** — Cancel it (`args.IsCancelled=true`) to show custom form. The `OnCreate`/`OnUpdate` events won't fire if `OnEdit` is cancelled — handle saves manually.
5. **EF Core InMemory does not enforce unique indexes** — Use model config tests to verify; test DB-level enforcement separately.
6. **Haversine in WebApi** — Duplicated from `Ben.Web.Library/Manage/Maps/AddressMapOptions.cs` since Web.Library isn't referenced by WebApi.
7. **Cascade cycles** — SQL Server rejects multiple cascade paths to the same table. Use `DeleteBehavior.NoAction` on secondary FKs and handle cleanup manually.
8. **`OrganizationUserMembershipResponse`** — Defined in TWO places. Update both.
9. **geocod.io `preview` endpoint** — Rejects empty city/state/zip. Use `GET /api/geocode/search?q=` for freeform input.
10. **Ben.Video.Editor cross-repo reference** — Both repos must be sibling folders (`Source/Ben/` and `Source/Github-BenVideo/`). If the path breaks on another machine, publish to a private NuGet feed instead.
11. **ffmpeg.wasm `@` in Razor** — CDN URLs containing `@` (e.g. `@ffmpeg/ffmpeg@0.12.15`) must be escaped as `@@` in `.razor` files.
12. **`IMediaLibraryProvider` scoped override** — `BenMediaLibraryProvider` is registered *after* `AddBenVideoEditor()` to override the default. A `DelegatingHandler` cannot carry the bearer token because `IWebApiTokenStore` is circuit-scoped while handlers are resolved from the root DI scope.

---

## Future Phases

### In Progress — Ben.Video Integration (branch: feature/ben-video-integration)

| Step | Status | Description |
|---|---|---|
| RCL project reference + DI | ✅ | Ben.Video.Editor wired into Ben.Web.Library and Program.cs |
| BenMediaLibraryProvider | ✅ | Auth-aware IMediaLibraryProvider using circuit bearer token |
| VideoProject entity + controller | ✅ | CRUD at `/api/cases/{caseId}/video-projects` |
| CaseVideoEditorPage.razor | ⬜ Next | `/organizations/{orgId}/cases/{caseId}/video-editor` — case-scoped with project list and DocumentPostUrl |
| IBenAdminClient VideoProject methods | ⬜ Next | `GetCaseVideoProjectsAsync` + `GetCaseVideoProjectAsync` |
| Nav link from case detail | ⬜ Next | "Video Editor" button on case pages |

### Phase — Image Editor

Full spec in `ProjectNotes/Media-Editor-Phase.md`.

| Sub-phase | Description |
|---|---|
| A — Foundation | Fabric.js CDN, `ImageEditorPlayer.razor` + `.razor.js`, `AddImageEditorMetadata` migration (`EditStateJson` + `IsEditedVersion` on `UploadFile`), `PUT /api/upload-files/{id}/edit-state`, "Edit" button on evidence images |
| B — Full feature set | Drawing/annotation tools (pen, arrow, shapes, text, redaction box), layers panel, evidence-specific tools (anomaly halo, timestamp stamp, grid, measurement ruler, magnify lens), perspective correction, save as derived `UploadFile` |

### Phase — Enhanced Audio Editor

Extends `WaveSurferPlayer` in `Ben.Web.Library/Manage/Audio/`.

| Sub-phase | Description |
|---|---|
| C — Spectrogram & EQ | Frequency ruler, mel-scale toggle, colormap selector (Jet/Viridis/Inferno/Grayscale), frequency brush tool for surgical notch filtering, 10-band graphic EQ, high/low-pass filter, noise gate (`AudioWorkletProcessor`), compressor |
| D — EVP Tools | `AudioMarker` entity + `AddAudioEditorAndMarkers` migration, EVP marker tool with confidence ratings (Possible/Probable/Confirmed), silence detection shading, voice frequency overlay (300 Hz–3 kHz band), trim/cut/silence offline render + export, normalize, gain, fade, speed change (SoundTouchJS), pitch shift, reverse |
| E — Multi-track Mixer | 8-track timeline grid, drag from case evidence panel, per-track controls (mute/solo/gain/pan), offline render, save as case `UploadFile` at `/cases/{caseId}/audio-mix` |

### Phase — Ben.Video Full Integration

Once image and audio editors are complete, Ben.Video becomes the capstone tool for evidence video production:

- Annotated images → video title cards, thumbnails, still frames (referenced by `UploadFileId` — no duplication)
- EVP clips + mixed audio tracks → drag directly into Ben.Video timeline
- `AudioMarker` timestamps → Ben.Video chapter/cue points (auto-import)
- Report Builder → "Export as Video" option pre-loads case evidence into the video editor
- `EditStateJson` on `UploadFile` → re-open any editor output, refine, and the video project picks up the new version automatically

### Deployment Considerations

- **Multi-thread ffmpeg.wasm** — Requires `SharedArrayBuffer` → COOP/COEP headers. Scope headers to `/video/*` only (path middleware) to avoid breaking CDN resources and Entra OIDC. Currently running single-thread mode.
- **Self-host WASM binaries** — Removes the cross-origin constraint entirely and enables multi-thread without header restrictions.
- **Ben.Video.Editor → private NuGet** — When the RCL stabilizes or a second developer joins who works only on Ben, publish to GitHub Packages and replace the cross-repo project reference.
