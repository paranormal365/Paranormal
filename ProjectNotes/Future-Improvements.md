# Future Improvements — Backlog

Ideas captured for later work. Nothing in this file has been scoped, designed, or approved for implementation — these are raw notes to revisit and plan properly when picked up.

---

## 1. Withdraw request → offer resubmission ✅ Complete (2026-08-06)

When a client withdraws a submitted investigation request, the client-facing UI should either:
- Ask if they want to submit the request to another organization, or
- Show a button to resubmit it.

Related: the Declined-status resubmit flow already built for `ClientRequestController`/`ClientRequests.razor` (2026-08-05, `AddOrganization` endpoint + "Choose Another Organization" picker) is the natural pattern to extend here — a withdrawal is a different trigger than a decline, but the same "pick another org" UI could likely be reused.

> **Shipped on `feature/withdraw-request-resubmit`:** `AddOrganization` now accepts `Withdrawn` in addition to `Declined`; `ClientRequests.razor` shows the same picker (relabeled "Resubmit to an Organization") for withdrawn requests. Also fixed a related gap found along the way: `Withdraw` previously left any still-open `ClientRequestOrganization` rows untouched, so a withdrawn request kept appearing in the organization's pending-requests list — it now cancels them, mirroring the pattern already used when one org accepts and the others are superseded.

---

## 2. SuperAdmin visibility into all cases and investigations ✅ Complete (2026-08-06)

A user with the SuperAdmin role should be able to view all cases and investigations across every organization, not just ones they're a member of.

> **Shipped on `feature/superadmin-all-cases-investigations`:** per-org case/investigation reads already bypassed the membership check for SuperAdmin (`CanReadAsync`), but there was no way to discover which orgs to look at without already knowing. Added `AdminCaseController`/`AdminInvestigationController` (`api/admin/cases`, `api/admin/investigations`, SuperAdmin-only) that join across every org, plus two new admin pages (`/admin/cases`, `/admin/investigations`) linked from the Administration side panel, each opening straight into the org's case detail page.

---

## 3. "My Investigations" — clickable detail view ✅ Complete (2026-08-06)

Under "My Investigations," each investigation should be clickable and open a detail view showing whatever the current user has permission to see: other members' notes, research, media, evidence, votes, etc.

> **Shipped on `feature/my-investigations-detail-view`:** rather than building a new consolidated view (which would mean re-implementing every existing permission check), each investigation card now navigates to the existing `CaseDetail.razor` page — it already gates every tab (Notes, Research, Files, Reports, etc.) with the same case-manager-or-org-member checks used everywhere else, so "whatever the user has permission to see" was already correct there. Added `?tab=` query-param support to `CaseDetail.razor` so the link lands directly on the Investigations tab instead of Overview.

---

## 4. Client-facing case log (timeline, sub-clients, evidence) — ✅ Complete (2026-08-11)

Clients should be able to return to their submitted request/case after submission and:
- ✅ Add new timeline entries for new experiences or evidence. *(already existed — `LogOccurrence`/`UpdateOccurrence`/`DeleteOccurrence`.)*
- ✅ Add media (audio/photo/video) as evidence. *(already existed — `AttachFile`/`DetachFile` on occurrences.)*
- Add other people who have had experiences at the property as **sub-clients**:
  - ✅ **Shipped:** the primary client invites someone by email to create an account, which gets linked to the case as a sub-client. See the phase writeup below.
  - ✅ **Shipped:** without requiring an account, add basic info about a person (name, age, relationship, whether they live there) so they can be referenced in notes/timeline entries. New `CaseRelatedPerson` entity + `api/my-cases/{caseId}/related-people` (primary-client-only) + a "People at the Property" section in `MyCaseDetail.razor`. Scrubbed-when-public is satisfied structurally — it's never returned by any public-facing endpoint — rather than by an explicit scrub toggle.
- ✅ Every client-side write to the case log should create an audit log entry capturing user info. Added `IAuditLogService` calls (the same mechanism used by ~45 other controllers) to every write in `MyCaseController`: occurrence create/update/delete, co-client add/remove, occurrence-file attach/detach, related-person add/delete. IP address specifically is captured only for occurrences, via `CaseTimelineEntry.IpAddress` (a pre-existing dedicated column) — the generic `AuditLog` table has no IP column at all, and adding one is a bigger cross-cutting schema change affecting every other controller that logs through it, so it was left out of this slice rather than done partially/inconsistently.

> **Shipped on `feature/client-case-log`.** Phase 2 (`feature/client-case-log-invite-sub-client`) closed the item in full.
>
> **The email-invite flow**: `POST /api/my-cases/{caseId}/invites` is a single unified entry point — the primary client just types an email. An existing account is linked immediately (identical to the pre-existing `AddCoClient` path, kept untouched alongside it); no account yet mints a 14-day, revocable `CaseClientInvite` token and, if `IEmailService.IsConfigured`, emails it — either way the primary client gets a copyable link back, since email delivery is never guaranteed. New `GET .../invites` (pending list) and `DELETE .../invites/{id}` (revoke) round it out. The accept side is a new, unauthenticated `CaseInviteController` (`api/case-invites`): `GET {token}` for status/inviter/case info, `POST {token}/accept` creates a brand-new local account via `UserManager<AppUser>` and links it, `POST {token}/accept-existing` (authorized) links whoever is currently signed in — token possession is the credential, deliberately not a strict email match, which also covers "registered under a different email than the one invited." New `InviteAccept.razor` (`/invite/{token}`) drives register-vs-sign-in based on whether the invited email already has an account, with `Login.razor` gaining `ReturnUrl` support so a sign-in from an invite link lands back on it.
>
> **Built two things from scratch that didn't exist anywhere in the app**: an `IEmailService`/`SmtpEmailService` (MailKit, `Smtp` config section, `IsConfigured` false when unset — true in every environment today, since no SMTP credentials exist yet) mirroring `IFileStorageService`'s interface-in-Common/impl-in-WebApi split; and the app's first self-service account-registration path (previously accounts came only from SuperAdmin creation or the Entra OIDC callback).
>
> **Found and fixed two real, pre-existing bugs surfaced by actually using the new flow end-to-end** (not introduced by this phase — the old `AddCoClient` "Share Case Access" hit the same gaps, they were just unreachable/unnoticed before): (1) `MyCaseController.GetMyCases`/`GetMyCase` — the case list and case-detail endpoints `MyCaseDetail.razor` depends on — filtered strictly by `ClientRequest.AppUserId == userId` (primary client only), never checking `CaseClientAccess` at all, so a co-client's grant did nothing for browsing even though it worked for individual occurrence actions; a freshly-accepted invite landed on "Case not found." Fixed by unioning primary + `CaseClientAccess` membership, matching the `IsCaseClient` helper already used elsewhere in the same file. (2) `MyCaseDetail.razor`'s `_isPrimaryClient` flag was *inferred* from whether `GetCoClients` happened not to throw — but the generic HTTP client returns an empty list on a 403 instead of throwing, so every co-client silently saw the primary-only "Shared Access" admin controls too. Fixed by adding a real `IsPrimaryClient` field to the case-detail response, computed server-side where the answer is already known with certainty.
>
> Live-verified end-to-end against the dedicated SQL server via the app's real client-request → org-accepts → case pipeline (not shortcuts): invited a brand-new email, confirmed the dev "email not configured" copy-link path, registered through `/invite/{token}` in a real browser session, confirmed auto-login landed on the case, confirmed the case now appears under `/my-cases` for the new user with `IsPrimaryClient=false` and no Shared Access card, and confirmed the primary client's own view shows both the new sub-client and a same-session invite to an existing account (linked immediately, no invite row created). Hit the `dotnet run` stale-process trap again mid-verification — a leftover process serving pre-fix code briefly made the real fix look broken; caught by checking process start times against the source-edit time.

This is effectively a client-owned "case log" surface, distinct from the org/investigator-side case tools that already exist.

---

## 5. Replace raw IDs with names + contextual links ✅ Complete (2026-08-06)

Several screens (organization detail, various grids) currently display raw GUIDs for users and organizations instead of names. Should instead show:
- **Users:** display name, linked to the user's profile/detail page if the current viewer has permission to view that user; otherwise plain text (no link).
- **Organizations:** display name, linked based on the viewer's relationship to the org:
  - Not a member → link to the org's public home page.
  - Member → link to a member-facing org page, with sub-navigation/access scoped to the viewer's role/claims within that org.

This is a cross-cutting UI convention change, likely touching many grids/detail pages — worth auditing for every place an `AppUserId`/`OrganizationId` is rendered directly.

> **Shipped on `feature/id-to-name-links`.** New shared components `UserNameLink.razor`/`OrgNameLink.razor` (`Ben.Web.Library/Shared/`). Scoped to what the current permission model actually supports rather than inventing new infrastructure: there is no member-facing user profile page today, only the SuperAdmin-only `/admin/users/{id}`, so `UserNameLink` links only for SuperAdmin viewers and shows plain text otherwise; `OrgNameLink` links to `/organizations/{id}` (member page) or `/o/{urlName}` (public page) based on an `IsMember` flag the caller supplies (no per-row API call). Wired into `AdminAuditLog.razor` (added `AuditLogRecord.UserDisplayName`, joined server-side), `OrganizationMembers.razor` (was ignoring an already-available `DisplayName` field), `InvestigationPanel.razor`, and `MyInvestigations.razor`'s org name. `CaseVideoEditorPage.razor`'s "By" column turned out to always be the viewer's own project (the API only returns your own projects) — replaced with a plain "You" instead of a lookup. Intentionally left alone: `OrganizationView.razor`'s own org-ID reference field (not a cross-reference), `AdminAuditLog.razor`'s `EntityId` column (polymorphic — varies by `EntityType`, can't resolve to a name generically), and a couple of internal dev/reference-only ID displays (`OrganizationSecurity.razor` test harness page, `OrgAddressManager.razor` dropdown option labels).

---

## 6. Universal media library component — ✅ Complete (2026-08-11)

A general-purpose media component/picker, usable both standalone (a personal media library page) and embedded (e.g. as the media picker inside the Ben.Video editor). Requirements:

- **Views:** thumbnail grid (OS-folder style) or detail list (file size, name, date, etc.), user-toggleable.
- **Scope of files shown:**
  - Files the current user uploaded/created.
  - Files shared with the user by others — shown with a badge indicating it's shared, plus who shared it (name, not ID) and when.
  - Files shared within any organization the user is a member of, at the org, case, or investigation level.
- **Sharing model per file (owner-controlled):**
  - Share with: specific person, investigation team, entire organization, or public.
  - Optional voting: let viewers rate the file's evidentiary strength (e.g. least-likely to most-likely evidence of a haunting, or likely fake).
  - Optional commenting, independently toggleable per audience: investigation team, client, organization, general public (if the file is public).
- **Investigation-file copy semantics:** when a file is attached to an investigation, a copy/record is made into the case's investigation folder so it remains available for the case summary even if the original uploader later deletes or changes their personal copy. If the owner re-uploads a file, prompt whether they're replacing it — if yes, update the investigation's copy too.
- **Reuse target:** this component should become the universal media-selection tool wherever media is picked — including as the asset picker inside Ben.Video, since it already indexes all investigation- and user-owned media.

This is the largest item here — likely its own multi-phase effort (data model for sharing/voting/commenting permissions, the copy-on-attach semantics, the UI component itself, and the Ben.Video integration point).

> **Phase 1 shipped on `feature/media-library`:** the aggregated cross-scope viewer, the reusable grid/picker component, and the full 4-target sharing model. Scoped by four decisions made up front:
> - **Ben.Video integration is API-level only.** `Ben.Web.Library` has a one-way `ProjectReference` to `Ben.Video.Editor` with nothing pointing back, so the new grid's UI can never be embedded inside Ben.Video's editor — instead `MediaLibraryController`/`BenMediaLibraryProvider.cs` were enriched while Ben.Video's own picker UI stays untouched. Its exact prior payload (owned/published, video+audio+image only) is preserved via an explicit `contentTypePrefixes` query param — the new default (no param) returns every content type, a deliberate behavior change for the general-purpose case.
> - **Sharing ships the full 4-target model now**, not a phased org-only start: new `UploadFileShare` entity/`ShareTargetType` enum (Person/InvestigationTeam/Organization/Public), additive alongside the existing tiered `UploadFileOrganizationShare` rather than replacing it (that table backs a live feature with its own callers). New `UploadFileShareV2Controller` (`api/upload-files/{id}/shares-v2`, `api/upload-file-shares-v2/{id}`) handles create/list/soft-delete with ownership + investigation-team-membership checks.
> - **Voting reuses the existing `EvidenceVote`/`EvidenceVoteWidget`** (Confirms/Disputes/Inconclusive), not the orphaned `UploadFileVote`/`UploadFileVoteBar` (dead code, no live callers) or a new entity. `EvidenceVoteController.CastVote` already worked with no case at all — case context is fully optional — so this needed only a regression test, not a controller change.
> - **Commenting and copy-on-attach-to-investigation are Phase 2**, not built here.
>
> `MediaLibraryController.GetFiles` was rewritten to union six scopes into one deduplicated query: owned; shared-with-me (Person); shared with an investigation I attend (InvestigationTeam); shared with my org (both the tiered table and the new Organization-target shares, respecting `Visibility` vs. the caller's org role); public (`IsPublic` flag or a Public-target share); and case-linked (`CaseFile`, `CaseTimelineEntryFile`, or a published `VideoProject` — a file counts as case-scoped if it matches any of the three). New `MediaLibraryGrid.razor` (`Ben.Web.Library/Media/`) is the reusable component — standalone browse mode or an embeddable `PickerMode` with multi-select — used both by the new `/media-library` page and as an "Attach from Library" picker on the case Files tab (new `CaseFileController.Link` endpoint links an existing `UploadFileId` into a case without copying bytes). Grid thumbnails eager-load only for images and lazy-load video/audio on click, since `UserMediaPreview` has no thumbnail mode and fetches full file bytes — a real thumbnail endpoint is a good Phase 1.5/2 fast-follow.
>
> Found and fixed two bugs along the way, unrelated to this feature but discovered while verifying it: (1) `Ben.Web.Playwright/BenTestBase.cs`'s `LoginAsync` had a stale email-field selector and an unscoped submit-button click that was silently clicking the wrong button, breaking every Playwright test's login flow app-wide; (2) an app-wide auth-timing bug where a hard navigation (full page load/refresh) to any authenticated page incorrectly showed the login form even though the user was still signed in, because `OnInitialized`/`OnInitializedAsync` guards checked `IsAuthenticated`/`IsSuperAdmin` before the circuit had finished restoring auth state on first render. Fixed with a new `IBenUserState.AuthReady`/`IWebApiTokenStore.AuthReady` gate (a `TaskCompletionSource` signalled once by `MainLayout` after its first-render auth restore) that every affected page now awaits before checking auth state — audited and applied across all 25 affected pages app-wide, not just the new Media Library page.
>
> **Follow-up (2026-08-06):** user reported the media library "takes up a lot of space in the video editor" with "excess padding." Investigating this surfaced two unrelated, pre-existing crashes fixed the same day: (1) `CaseDetail.razor` threw on every load — `TelerikTabStrip.ActiveTabIndex` (int) doesn't exist in the installed Telerik.UI.for.Blazor 14.1.0, replaced by `ActiveTabId`/`TabStripTab.Id` (string) — fixed by switching to string tab IDs. (2) The actual "video editor" media picker the user meant is Ben.Video.Editor's own `MediaLibraryPicker`/`ClipBrowser`, hosted via `CaseVideoEditorPage.razor` → `<VideoEditor />` — separate repo, see [[reference_ben_video_editor_repo]]. Its floating "Media & Properties" `TelerikWindow` had three compounding CSS bugs in `VideoEditor.razor.css`: `.bv-editor` had no `position` set (TelerikWindow's `ContainmentSelector` requires one, so every drag/resize threw a JS exception and was silently dead); `.bv-asset-browser-panel`'s opening brace was never closed, so five subsequent rules nested inside it under the CSS Nesting spec instead of applying at top level; and even after fixing the brace, `.bv-media-panel-window ::deep .k-window-content` still didn't match because `TelerikWindow` doesn't forward the Blazor scoped-CSS attribute to its own root element — fixed by rooting the `::deep` at `.bv-editor` (a plain authored `<div>` that reliably carries the scope attribute) instead. All three fixed and verified live (drag, resize, and full-height content fill all confirmed working with zero console errors) — shipped directly to `develop` in both repos, not on a feature branch, since these were bug fixes rather than backlog features.
>
> **Known follow-up, discovered but not fixed (2026-08-06):** hard-navigating directly to some pages still shows wrong content despite the `AuthReady` fix above — `CaseVideoEditorPage.razor` shows "You must be signed in" and `OrganizationList.razor`/the org Cases list show empty data, both while the nav bar correctly shows the user as signed in. SPA-click navigation from an already-connected circuit works fine in both cases; only a fresh hard navigation (typed URL, refresh) triggers it. Not yet root-caused — worth a dedicated investigation pass before assuming it's the same class of bug as the original `AuthReady` fix.
>
> **Phase 2 shipped on `feature/media-library-phase2-comments-copy-on-attach`:** commenting and copy-on-attach, the two pieces deliberately deferred from Phase 1.
> - **Copy-on-attach**: `CaseFileController.Link` now makes a real, independent byte-copy of the source `UploadFile` (new `UploadFile.CaseCopyOfUploadFileId` points back at the source) instead of just referencing it — the case's copy survives the source being deleted or replaced. Closed a pre-existing hole along the way: `Link` had never checked whether the caller could actually see the source file at all, so any org member of the target case could reference an arbitrary `UploadFileId` by guessing its GUID; now gated by the same `FileAudienceAccess.CanViewFileAsync` check comments use.
> - **Commenting**: new `UploadFileComment` entity/`UploadFileCommentController` (`api/upload-files/{id}/comments`) with full CRUD, gated by a new shared `FileAudienceAccess` helper (`Ben.Data.WebApi/Controllers/Entities/`) that computes which of the four audiences (owner, investigation team, client, organization, public) a viewer currently matches for a file, ANDed against the file's own `Allow*Comments` toggles (`AllowInvestigationTeamComments`/`AllowClientComments`/`AllowOrganizationComments`/`AllowPublicComments`, new columns on `UploadFile`). Toggles turn on discussion for an audience that already has Phase-1 visibility of the file — not a second, independent grant. Comments are hard-deleted (matching `UploadFileRegionNote`), not soft-deleted like `UploadFileShare`, since they're free-form content rather than an access grant needing a revocation trail.
> - **UI**: two new reusable components in `Ben.Web.Library/Media/` — `FileCommentThread.razor` (post/edit/delete, author name + audience badge, gated compose box that surfaces a 403 as a plain message rather than trying to predict permission client-side) and `FileCommentSettings.razor` (owner-only per-audience toggles). Wired into `MediaLibraryGrid.razor` via a new "Comments" button that opens a `TelerikWindow` detail dialog (preview + settings + thread), and into `CaseFiles.razor` via an inline expand/collapse section per attached file.
> - Found and fixed a real bug while wiring the UI: a native `<details>/<summary>` disclosure for "Comment settings" doesn't reliably toggle open via synthetic/automated clicks in this Telerik/Bootstrap-themed app (and is inconsistent with the rest of the app's controls regardless) — replaced with a plain `TelerikButton` + bool-flag toggle in both components, live-verified end-to-end afterward (post, edit, and an owner settings save confirmed to actually hit the database via a live `UPDATE [UploadFiles]` in the request log).
> - **Not built this phase**: the Phase-1-spec'd "if the owner re-uploads a file, prompt whether they're replacing it — if yes, update the investigation's copy too" flow. There is no re-upload/replace feature anywhere in the app today (personal file upload only ever creates new `UploadFile` rows), so this is a new flow to build from scratch, not a small addition — deliberately scoped out rather than built partially. Left open for a future phase.
>
> **Phase 3 shipped on `feature/media-library-phase3-replace-file`, closing item #6 in full.** The deferred re-upload/replace flow: `POST /api/upload-files/{id}/replace` rewrites a file's bytes **in place** — same `UploadFile.Id`, so every existing comment, vote, share, and `CaseFile` link stays attached — while every case copy (`CaseCopyOfUploadFileId == id`) is overwritten at its own existing `StoragePath`, so nothing about the copy's row (or anything pointing at it) changes either. Design settled with the user up front: (1) two entry points — an explicit "Replace" row action on the Upload Files grid, and same-filename detection on normal upload prompting "Replace it / Keep both"; (2) a `GET .../replace-impact` preview before confirming, listing every affected case and flagging copies that already carry comments/votes, since these files are evidence people vote on; (3) the **old bytes are archived, not discarded** — a new `UploadFile` row inherits the prior `StoragePath` as-is (no byte copy needed) with a new `ArchivedFromUploadFileId` self-reference, deliberately separate from `CaseCopyOfUploadFileId`/`ParentFileId` for the same reason those two are separate from each other (`GetChildClips` and the case-copy "already linked" check both need to keep ignoring rows that aren't their own kind of lineage). Archived rows are excluded from `UploadFileController.GetAll` and `MediaLibraryController.GetFiles`. `UploadFileMetadata` (1-to-1, EXIF/GPS/dimensions) is refreshed delete-then-add for the source and every updated copy, fire-and-forget like the original upload-time extraction, so stale metadata doesn't stay attached to bytes it no longer describes.
>
> Live-verified end-to-end over real HTTP against the dedicated SQL server and local file storage (the Blazor UI's native OS file-picker isn't drivable by either browser tool available this session — Claude_Browser has no file-upload capability and claude-in-chrome's real Chrome can't reach this sandbox's localhost — so the actual `<input type=file>` step was exercised via direct authenticated API calls instead, hitting the exact same controller code the UI calls): attached a personal file to a real case, commented on the copy, replaced the source's bytes, and confirmed — via direct file-storage reads, not just DB state — that both the source **and** the case copy now serve the new bytes at their original paths, the comment and `CaseFile` link survived untouched, the archived version is invisible to both listing endpoints, and the metadata rows were refreshed with no duplicates. Also confirmed the extension-mismatch (400) and non-owner (403) guards live. Test artifacts were cleaned up and the demo file's original content was restored afterward from its own archived copy.
>
> Found and flagged (out of scope for the replace-file branch itself), then **fixed immediately after as a fast-follow on `fix/upload-file-authorization-gaps`**: `UploadFileController.GetAll` had no owner filter at all — any authenticated user could list every file in the system — and `Download`/`GetById` only checked `IsPublic` (or nothing at all), with no ownership/sharing-audience check, so any authenticated (or even anonymous, for public files) user could download any file, or read its full metadata, by ID. Together these bypassed the entire Phase 1/2 sharing and comment-audience model. Fixed: `GetAll` now scopes to the caller's own files (it backs the personal "Upload Files" page, not a browse-everything view — that's `MediaLibraryController.GetFiles`); `GetById` and `Download` now both gate on the same `FileAudienceAccess.CanViewFileAsync` union every other read path in the app respects. Live-verified over real HTTP with two real seeded users: a non-owner gets 404/403 on a private file, the owner still gets 200, and a public file stays downloadable by an anonymous caller. Hit a real tooling trap along the way — `pkill` on the `dotnet run` wrapper doesn't kill the actual Kestrel child process, so a "clean restart" kept serving stale code with no obvious signal until process start times and the new process's own crash log were checked directly.

---

## 8. Media & Properties panel — dedicated Preview tab + real thumbnails — ✅ Complete (2026-08-08)

In the video editor's floating "Media & Properties" window (`Ben.Video.Editor/Components/VideoEditor.razor`):
- ~~Add a third tab and move the preview window into it~~ — **superseded 2026-08-07**: instead of moving the preview into the Media & Properties window, put it in its own height-adjustable `div` positioned below the toolbar and directly above the timeline (i.e. its own resizable region in the main editor layout, not a tab inside the floating panel). It still renders a **small**/compact version of the composition — export/final render stays at the normal/full canvas size regardless of how small this preview area is resized to. **✅ Shipped** as item #14.
- `ClipBrowser.razor`'s Video/Audio/Image/Server tabs currently show icon-only placeholder rows (`SvgIcon.FileVideo`/`FileAudio`/`Image`) for every clip — replace with small thumbnails or a list view like the main website's media library (`MediaLibraryGrid.razor`, `Ben.Web.Library/Media/`) already does: eager-load thumbnails for images, lazy-load-on-click for video/audio (since there's no dedicated thumbnail endpoint yet — same constraint noted in item #6). **No voting UI** — unlike `MediaLibraryGrid`'s optional `ShowVoting`/`EvidenceVoteWidget`, this bin is media-only, no vote controls or vote results. **✅ Shipped** (`feature/phase-60-clipbrowser-thumbnails`) — imported Video/Image clips already had real thumbnails; only the Server tab (files not yet downloaded) needed new work.
- ~~The Properties tab's content doesn't dynamically expand when the user resizes the Media & Properties window larger~~ — **✅ Shipped** (`feature/phase-73-...`): removed stale `min-width`/`max-width`/fixed-`width` CSS left over from when `ClipEditor`/`AudioClipEditor`/`ImageClipEditor`/`CalloutEditor`/`MotionKeyframeEditor` were built as fixed-width side panels — they now reflow to fill the window like `TextOverlayEditor`/`TransitionEditor` always did.
- ~~Override native HTML5 drag-and-drop on the timeline with a custom pointer-based system~~ — **mostly already done, rest ✅ shipped 2026-08-08**: research found playhead drag-to-scrub and clip-edge trim were already fully pointer-based from earlier phases (not native DnD at all — this bullet was stale). Clip **repositioning** was also already pointer-based but only reachable for `VideoClip`/`ImageClip`; `AudioClip` (and any other non-Video/Image chip) could still fall through to the old native-DnD swap-reorder path because the `draggable`-suppression condition was type-gated. **Fixed**: widened the condition so any chip with an active pointer-drag suppresses native `draggable`, regardless of item type — live-verified the audio clip's `draggable` attribute now toggles correctly and the chip moves continuously like video/image clips. **Deferred, logged separately as item #24**: the one remaining native-DnD spot — dragging a *new* clip from `ClipBrowser` onto a timeline track — is a genuinely cross-component rebuild, out of scope for this cleanup pass. The `WaveSurferPlayer.razor.js` reference in the old text was stale — that file doesn't exist in this repo; audio waveforms use `AudioWaveform.razor` + `waveformInterop.js` instead.
- ~~Minor visual polish: minimized titlebar corners~~ — **✅ Shipped 2026-08-08**: Telerik's theme only ever gave `.k-window-titlebar` top-corner radius (relies on the outer window's own radius+overflow for the bottom edge while content shows); added the missing bottom-corner override for the minimized state, where the titlebar rectangle *is* the whole visible window.

> Requested 2026-08-06, right after the panel's drag/resize/fill-height crash was fixed (see item #6's follow-up above) — the user wants this done once that panel is confirmed working correctly, which it now is. Confirmed already working as of item #9's test pass and does *not* need further work: splitting a clip produces two independently selectable/deletable clips on the timeline.

---

## 7. Drag-to-scrub playhead on audio file records ✅ Complete (2026-08-06)

On audio file playback/preview (e.g. `AudioFilePreview.razor`/`WaveSurferPlayer.razor`), let the user click-and-drag the mouse on the waveform's playhead to scrub through the audio, not just click-to-seek.

> **Shipped on `feature/drag-scrub-playhead`, merged to `develop`.** Click-and-drag on the full-view waveform now has two runtime-switchable modes via a toolbar toggle (icon-only, tooltip "Create Region"/"Scrub Playhead", defaults to region mode): region mode draws a selection region as before; scrub mode moves the playhead live and plays audio audibly while dragging (starts playback immediately if paused, restores the prior paused/playing state on release). Implemented as one unified pointer-event system in `WaveSurferPlayer.razor.js` rather than a separate playhead-only hit-test, so the two gestures never conflict. Also added an "Explore Region" action to the region right-click menu that opens the existing `WsRegionExplorer` for a freshly-drawn region — reusing its already-built playback/save-as-clip/notes panel (`UploadFileRegionNote`) wholesale rather than building new note infrastructure, once investigation surfaced that component already did exactly what "add a note to a region" needed.
>
> Two unrelated pre-existing bugs were found and fixed on `develop` while verifying this feature: a hard-navigation auth redirect (pages showed the login form on refresh even when signed in) and a missing `.AddAdditionalAssemblies()` call that 404'd every `Ben.Web.Library`-hosted page on a fresh page load.

---

## 9. Thoroughly test the Ben.Video component 🟡 In progress (2026-08-06)

Need a dedicated pass to thoroughly test the Ben.Video.Editor component and verify all aspects of it — not just the specific bugs found incidentally while working on other items (see item #6's follow-up and item #8). Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote), not this one.

> **In progress.** Bugs found and fixed so far while working through the test plan (import → timeline → editing → overlays → export → persistence):
> - ffmpeg.wasm was **completely broken** — `App.razor` loaded the main `@ffmpeg/ffmpeg` UMD bundle from unpkg instead of the locally-vendored copy, so the library's own worker chunk (`814.ffmpeg.js`) failed a same-origin check on every single load. Nothing requiring ffmpeg (import, preview, export) could ever have worked in this app. Fixed by pointing at `_content/Ben.Video.Editor/js/ffmpeg.umd.js`, matching what the two standalone host apps already did.
> - `CaseVideoEditorPage.razor` had the same hard-nav `AuthReady` gap as item #6's follow-up (missed by the original 25-page audit) — fixed with the standard guard.
> - Clicking a media-library file in `ClipBrowser`'s Server tab imported it into the timeline **twice** from one click — added a reentrancy guard.
> - `getMetadata()` in `ffmpegInterop.js` only ever read the **video** stream's duration via ffprobe, so every audio-only file (mp3, wav, ...) always reported `duration: 0` — the imported clip rendered as a near-invisible sliver on the timeline. Fixed to fall back to the audio stream's duration when there's no video stream.
> - The Media & Properties floating window's outline didn't shrink when minimized (stayed pinned at its `MinHeight="200px"` floor, leaving dead space below the titlebar) — overrode height/min-height specifically for the `.k-window-minimized` state.
> - User-reported "no play or pause buttons" — they existed in the markup (and spacebar was already wired to the same toggle) but were rendered below the visible/clipped viewport area, caused by two compounding layout bugs: `.bv-preview__screen-row` had no CSS rule at all so it used its aspect-ratio-driven content size instead of shrinking to fit, and separately `CaseVideoEditorPage.razor`'s `height:100vh` wrapper ignored the 58px `TelerikAppBar` sitting above it, overshooting the true available height on every page load. Fixed both.
> - Split (the `S` key / the Split button) only ever worked on video clips — `ClipStore.SplitClip` threw for audio/image, and the keyboard handler never checked `_selectedAudioClip`/`_selectedImageClip`. Generalized it to all three clip types (with volume-automation-keyframe redistribution and fade-clearing for audio), added a "Split at" control to `AudioClipEditor`/`ImageClipEditor`, and fixed a second pre-existing gap surfaced along the way: split was never undoable at all, for any clip type, before this fix. 10 new tests added.
> - **Preview and Export were both permanently disabled for any image-only timeline** — the toolbar's `HasClips` flag only checked `Clips.Clips` (a video-only accessor), so the buttons stayed greyed out no matter what those handlers actually supported. Fixed to also check image clips (matching `ExportService`'s own "no clips" check). Once enabled, a second, deeper bug surfaced: `BuildImageSegmentArgs` emits an empty `-vf ""` argument whenever `outputWidth`/`outputHeight` are 0 — which they always are, because `ImageClip.Width`/`Height` are never populated on import. ffmpeg.wasm's `exec()` never checks the exit code, so that invalid argument failed silently, only surfacing later as an unrelated "FS error" when the (never-written) output file was read back. **This second bug also affects the real export pipeline for any project containing an image clip, not just Preview** — fixed at the shared `ExportArgBuilders` level so both paths benefit. Verified live end-to-end (image-only timeline → Preview → real blob: URL → plays back correctly).
>
> Confirmed working: import (video/audio/image via media-library), multi-track add (video + audio tracks), timeline fit-to-width, clip select/delete/undo/redo, split (all 3 types)/undo, spacebar play/pause wiring, preview of an image-only timeline. **Not verifiable via browser automation:** native HTML5 drag-to-reposition on the timeline — synthetic mouse events don't trigger it; needs manual or Playwright-based testing with real drag simulation.
>
> **2026-08-10 pass:** confirmed frame-accurate preview scrubbing works — stepping via the
> "Step forward one frame" button advanced `<video>.currentTime` from `0` to exactly `0.041666…s`
> (1/24s at the project's 24fps setting), i.e. real frame-accurate seeking, not just a UI counter.
> The Telerik scrub slider itself couldn't be reliably drag-tested via browser automation this
> pass (same drag-unreliability already noted for other Telerik sliders this session — frame-step
> is a reliable proxy for the same underlying seek path). Volume automation UI testing was
> attempted but blocked by a slow/stuck audio import in the Playground this pass (recurring
> environment flakiness, not a product bug) — still open for a future pass.
>
> **2026-08-13 pass (phase 172)** — opened with the one list entry that was a *known defect* rather
> than an untested area, and the ticket named the wrong cause. Populating `ImageClip.Width`/`Height`
> would NOT have fixed the "images render at native resolution" symptom. The real bug:
> `ExportService.RenderImageSegmentsAsync` passed `clip.Width`/`clip.Height` as
> `BuildImageSegmentArgs`' `outputWidth`/`outputHeight` — but those are the canvas to letterbox
> *onto*, not a description of the input, so the filter became `scale={imgW}:{imgH},pad={imgW}:{imgH}`,
> a **no-op**. With the fields at 0 the builder emitted no `-vf` at all: same outcome, different road.
> Every other segment path already used `ParseResolution(s.Resolution)`; the image path was the only
> one that didn't. Fixed in BOTH encoders (wasm + `NativeClipEncoder`, which must agree or a
> sidecar-encoded and a wasm-encoded image segment land on different canvases in one export).
> The dimensions gap was real too and is also fixed: the **media-library** import never set
> Width/Height at all; the local-file path only started working in phase 167 (the eval removal
> replaced a `naturalWidth` read taken before decode). 4 new `ImageSegmentCanvasTests` — a
> source-level guard, since the builder was always correct and only the *caller* was wrong;
> confirmed the guard genuinely fails when the fix is reverted. See `README-phase-172.md`.
>
> ✅ **Mixed video+image export VERIFIED end-to-end (2026-08-14, clean-slate pass).** Cleared
> localStorage + OPFS first, then imported a 640×360 16:9 video and a **600×600 square** image (a
> genuinely mismatched aspect) onto one 1280×720 timeline and ran a real Export.
> - The live ffmpeg argv confirms the phase-172 fix in the real pipeline: the image segment ran
>   `-vf scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2` —
>   the PROJECT canvas, not `600:600`. The video segment used the identical scale/pad, which is what
>   makes the concat valid.
> - Export completed in 77.5s (2.8 MB). Output measured **1280×720, 18.917s** (13.867 video + 5.0
>   image ✓).
> - Pixel-sampled the output mid-image (t=16s): x=40/150/279 → **rgb(0,0,0)**; x=300/640/980 →
>   image content; x=1001/1130/1240 → **rgb(0,0,0)**. Predicted pillars for a square on 16:9 were at
>   280 and 1000 — measured black ≤279, content ≥300, content ≤980, black ≥1001, bracketing both
>   edges exactly. Mid-video (t=5s) showed no pillars, i.e. the 16:9 source fills the frame.
>
> ⚠ **Preview not pixel-verified.** The preview proxy loaded with correct dimensions (960×540) and
> the correct concatenated duration (18.9167s), so the mixed timeline does assemble into one
> coherent stream — but the preview blob would not seek in a detached `<video>` (seek timeout), so
> the image's pillarboxing was NOT confirmed by pixels on that path. Export is the authoritative
> path and is proven; treat preview as structurally-but-not-visually verified.
>
> ✅ **Prior "Remove clip deleted the wrong clip" lead — NOT a bug, closed.** That button lives in
> `.bv-clip-card__actions` inside `.bv-browser__grid`: it is the **media-library card's** remove,
> paired with "Add to timeline", not the properties panel's remove for the timeline selection. The
> earlier scripted `.click()` matched by button text and hit the library card's. No selection
> desync exists.
>
> Still to test: mixed video+image timeline **preview** pixel-check (export ✅ done, see above), volume automation UI details, clip art, motion keyframes, export dialog/queue, project save/open (server variant — device variant now confirmed, see below), subtitle export, error log panel, asset browser, remaining keyboard shortcuts. Also noted but not yet investigated: `ImageClip.Width`/`Height` are never populated on import, so image clips always render at their native resolution in Preview/Export instead of being scaled/padded to match the project's output resolution — fine for a single image matching project aspect ratio, but will look wrong once a mismatched-aspect image is mixed into a real project.
>
> **2026-08-12 pass** (right after item #38 closed — deliberately re-verified core paths since
> phases 121-124 touched `ExportService`/`FfmpegService`/the render backends significantly).
> Confirmed working: split + undo/redo (undo correctly reverted the split, redo correctly restored
> it — clip count round-tripped 1→2→1→2), effects (added Grayscale via the effect dropdown +
> "Add selected effect", chip appeared correctly), transitions (Fade transition chip placed
> correctly between two clips), text overlay + callout (both add correctly, each renders in its
> own stacked layer row per item #39's fix, still holding), audio import (real waveform rendered
> from a real mp3), audio clip properties panel (trim in/out, volume, left/right balance sliders
> all present and structurally correct), **export — both with and without the native sidecar
> paired**, confirmed on a clean minimal project (both produced a genuine "Export Complete"), and
> **project save/load round-trip** (File → Save, hard page reload, File → Open lists the saved
> project with correct name/timestamp/size, Open restores the clip with the correct "media
> missing, needs re-link" indicator — expected/correct since MEMFS doesn't survive a reload, not a
> bug). Rich-text overlay content editing (typing into the Telerik/ProseMirror iframe editor)
> couldn't be reliably driven via this session's browser-automation tool — consistent with this
> session's other Telerik-widget-driving difficulties, not evidence of a product issue (this exact
> feature already has 22+ dedicated automated tests from phases 115/116).
>
> **One real (minor) bug found — ✅ Fixed 2026-08-12.** When background rendering (item #36) and
> an export/native-clip-encode both needed the single shared main `FfmpegService` instance at the
> same moment, the loser's raw internal exception —
> `"FfmpegService is not ready (current state: Processing). Call LoadAsync() first."` — leaked
> directly into the user-visible warning banner instead of being retried/absorbed quietly like
> background rendering's other transient-failure handling. Reproduced identically in both the
> wasm-only and native-sidecar-paired runs; the export itself still completed successfully both
> times despite the message — purely cosmetic, not a functional regression. Root cause:
> `RenderWorkerBackend.ResolveSourceAsync`'s non-OPFS MEMFS-copy fallback called
> `FfmpegService.ReadFileAsync` directly against the main instance, whose `EnsureReady()` guard
> throws immediately rather than waiting. Fixed with a new `FfmpegService.ReadFileWhenReadyAsync`
> (mirrors the existing `WriteFileWhenReadyAsync`) that retries every 250ms until Ready instead of
> throwing. This fix was originally written and tested by an earlier background session that was
> abandoned before committing — rescued from the orphaned working-tree diff, verified it matched
> this bug's exact exception message/call site, confirmed 1420/1420 tests passing (including its
> own pinned regression test), and committed directly to `develop`.
>
> Also hit, once, in a long single-tab session that had accumulated many consecutive operations
> (split/undo/redo/overlay-add/audio-import-then-remove/effect-add/transition-add/marker-add)
> without a reload: Export got stuck at "Processing… 0%" indefinitely alongside a burst of blob-URL
> `net::ERR_FILE_NOT_FOUND` errors. A fresh tab + clean storage reset immediately made the exact
> same export (and every other export this session) succeed normally, matching the
> already-documented, not-yet-root-caused "Playground ffmpeg-Import Flakiness" pattern exactly
> (recurring hangs only after several back-to-back operations in one tab, gone after a clean
> reset) — treated as that known issue recurring, not a new regression, since both clean-baseline
> export paths worked correctly on the first try.
>
> **2026-08-12 pass, continued — clip art, motion keyframes, export queue, subtitles.** Confirmed
> working: clip art add (built-in "Star" from the Game Icons asset browser tab lands on its own
> timeline lane), its full properties panel (Duration, Position X/Y, Size, **Rotation** and **Tint
> color** controls from item #56 both present and structurally intact), and motion keyframe
> add-at-playhead (panel refreshed immediately showing the new keyframe's Position/Scale/Opacity/
> Easing/Bezier-handle controls with no close-reopen needed — item #40's fix still holds).
> Subtitle export confirmed gated correctly (`HasSubtitles` requires at least one text overlay,
> derives from `TextOverlay` items via `SubtitleBuilder`) — not yet exercised end-to-end
> (blocked by the bug below before a queue/subtitle test could run). Rich-text overlay content
> editing again couldn't be reliably driven via this session's browser-automation tool (same
> Telerik/ProseMirror iframe limitation as the first pass) — not attempted again.
>
> **One real bug found, functional not cosmetic — ✅ hardened 2026-08-13, phase 138 (Github-BenVideo repo):**
> adding a clip-art asset to the timeline permanently stalls the editor. `BackgroundRenderService.LoopAsync`
> only caught `OperationCanceledException` at the outer loop level — any other unhandled exception in a
> single iteration silently killed background rendering for the rest of the session, matching this
> symptom exactly. Fixed to catch-log-continue per iteration (new `OnLoopError` event → `ErrorLogService`)
> instead of dying; also found and fixed a second bug while writing the regression test (a region stuck
> mid-render got permanently stuck in `RenderingRough` state, invisible to future work, on this exact
> failure path). Verified via 2 new tests exercising the real production loop directly — **the exact
> clip-art trigger itself could not be reproduced live** (blocked by the separate, already-tracked
> Server-tab import flakiness below, persistent across two full clean-reset attempts this session), so
> this targets the confirmed architectural weakness rather than a pinned-down clip-art-specific cause;
> if the stall recurs, `OnLoopError` will now surface the real exception for the first time. Full
> writeup: `README-phase-138.md`.
>
> **Repro (fresh session, minimal — reproduced cleanly twice):**
> 1. Load the editor (`/demo/full`), click Initialize, wait for ffmpeg.wasm to load.
> 2. Import one plain video clip (any short mp4).
> 3. Open the asset browser (toolbar toggle) → Assets tab → click "+ Add" on the built-in "Star"
>    (Game Icons) — it lands on its own timeline lane.
> 4. Watch the toolbar busy indicator and the Preview/Export buttons.
>
> **Observed:** toolbar shows "Processing…" and Preview/Export correctly disable while
> `BackgroundRenderService`'s render worker runs a real Rough-pass encode (confirmed via
> `[render-worker-cmd]` console logs — genuine frame-by-frame libx264 progress, not hung). That
> encode completes normally. Then **nothing else ever happens** — no further console activity, no
> error, no exception anywhere (checked with the console error filter — genuinely zero) — and
> Preview/Export stay permanently disabled for the rest of the session. Since the real host app
> (`Ben.Web.WebApp`) also runs with `BackgroundRendering = true` (item #36 phase E's rollout), this
> would block real users from previewing or exporting any project that uses clip art, not just a
> demo-page quirk.
>
> **Root-cause hypothesis, narrowed by actually reading the code (not yet fixed or live-tested):**
> `RenderStatusService.Resync()` (`Ben.Video.Editor/Services/RenderStatusService.cs`) builds its
> `RenderRegionInput` list from `PrimaryVideoTrack.VideoClips` and `.ImageClips` only — grepped the
> whole file for `ClipArt`/`AllClipArtClips` and found zero matches, confirming `ClipArtClip` items
> are never synced into `RenderRegionTracker` as their own region. That alone wouldn't explain the
> *stall* though (a region that's never tracked would just never render, not break other regions).
> The more likely mechanism: `BackgroundRenderService.LoopAsync`
> (`Ben.Video.RenderService/BackgroundRenderService.cs`) only catches `OperationCanceledException`
> at its outer `while` loop level — confirmed by reading the file directly, only one `catch
> (OperationCanceledException)` guards the whole loop body. `ProcessOneAsync`'s own broader `catch
> (Exception ex)` only wraps the `_backend.RenderAsync(...)` call specifically — it does **not**
> wrap `PickNext()`, which `LoopAsync` calls directly and unguarded on every iteration. If adding a
> `ClipArtClip` (a `TrackItem` type `RenderRegionTracker`/`PickNext()` was never written to expect)
> triggers *any* exception in that unguarded path — most likely inside `PickNext()` itself while
> evaluating `RenderRegionTracker.Regions` after a `ClipStore.OnChange`-triggered `Resync()`, or
> inside `RenderRegionTracker.Sync()`'s own change-handling — the exception would propagate straight
> out of `LoopAsync`, silently terminating `_loopTask` for the rest of the session. Nothing ever
> observes that faulted task's exception (it's only awaited in `DisposeAsync()`, which normal
> operation never calls), so it fails completely silently — exactly matching "one job completes,
> then permanently nothing, zero errors anywhere."
>
> **Suggested fix, in order:** (1) confirm the hypothesis by wrapping `PickNext()`'s call site in
> `LoopAsync` with a try/catch and logging/asserting whether it actually throws when a `ClipArtClip`
> is added — this alone would prove or disprove the theory in one small, safe, temporary diagnostic
> change. (2) If confirmed: make `LoopAsync`'s loop resilient to a single bad iteration instead of
> dying — wrap the per-iteration body (or at minimum `PickNext()`) in a try/catch that logs via
> `ErrorLogService` and continues the loop rather than letting any single exception kill the whole
> background-render subsystem for the rest of the session; this is good defensive hardening
> regardless of the specific clip-art trigger. (3) Separately, decide whether `ClipArtClip` items
> *should* get their own tracked region (so their Rough/Fine background-render benefit actually
> applies to clip art content too, not just video/image clips) or are deliberately out of scope for
> item #36's region system (matching item #46/#102's existing pattern of raster clip art being
> handled entirely at export time via `RasterClipArtAnimationExporter`, not through the background
> preview pipeline) — if deliberately out of scope, `RenderRegionTracker`/`PickNext()` should
> explicitly and safely ignore `ClipArtClip` `TrackItem`s rather than choke on them.
>
> **How to verify a fix:** re-run the exact repro above; confirm the toolbar returns to "Ready" and
> both Preview and Export work normally after adding clip art (with and without a motion keyframe
> on it too, per this session's separate keyframe test). Add a regression test if
> `BackgroundRenderService`/`RenderRegionTracker` has a reasonable seam for constructing a
> `ClipArtClip`-bearing project and asserting the loop survives a sync including one.
>
> Also noticed, unrelated to the app itself: the Playground's `dotnet run` process (this session's
> test server) shut down on its own mid-session with a clean "Application is shutting down…" in its
> log and no preceding error — happened once, right around when the stuck clip-art state above was
> sitting idle for an extended period. Possibly environment resource pressure from the stuck
> browser tab, possibly unrelated; noted in case it recurs and turns out to matter.

---

## 10. Stereo channel separation for audio editing — ✅ Fixed (2026-08-09, phase 90)

Ben.Video's audio editing should support separating left and right audio channels so each can be edited independently.

> Requested 2026-08-06, during the item #9 test pass. Scoped, per the user, to independent per-channel
> **volume/gain** (not full independent trim/effects/waveform-UI per channel). Shipped as
> `AudioClip.LeftVolume`/`RightVolume`, applied via a `pan=stereo|c0=<L>*c0|c1=<R>*c1` ffmpeg filter
> layered on top of the existing scalar/automated `Volume`, with new `AudioClipEditor` sliders and full
> project-JSON round-trip. Along the way, fixed a much bigger pre-existing bug: standalone `AudioClip`s
> on an Audio track weren't being trimmed, filtered, faded, or positioned at export at all — every clip
> played raw from `t=0` regardless of its actual timeline position. See [[project_video_editor_phase90_audio_channel_volume]].

---

## 11. Real-time, editable preview window ✅ Complete (2026-08-08)

Once every layer type (video, audio, image, text overlays, callouts, transitions, motion keyframes) is rendering correctly and passing all tests, revisit the preview window's location and purpose:
- The preview should regenerate live as changes are made on the timeline, rather than requiring an explicit render/"Preview" step.
- The preview should become interactive — usable to make changes directly with the mouse on the canvas itself, e.g. dragging a motion keyframe placed at a marker, or resizing a layer (callout, text, image) directly on the preview instead of only through the Properties panel's sliders.

> Requested 2026-08-08. Depends on the Ben.Video.Editor QA sweep's remaining layer-animation gaps being resolved first (resize handles, color/shape-over-time — see [[project_video_editor_qa_sweep]] item #14), per the user's own sequencing. Related to item #8's preview-placement work.
>
> **Shipped on `feature/phase-65-auto-preview-refresh`: the "no explicit render step" half only.** A new
> opt-in (default off) toolbar toggle debounces `ClipStore.OnChange` (which fires unthrottled on every
> mutation, including every pointermove during a drag) and auto-triggers the existing Preview render ~1s
> after edits settle, collapsing a whole burst of edits into one re-encode instead of one per edit.
> **Still open**: the on-canvas interactivity half (drag a motion keyframe to reposition it — only
> selection and Bezier-handle dragging exist today; resize a text overlay or image clip directly on the
> preview — neither has any position/size model in `TextOverlay`/`ImageClip` at all yet, so that needs new
> data-model work first, not just a new overlay component). Depended on item #12 (now shipped, phase 64)
> for a cheap-enough render path to make auto-refresh viable at all.
>
> **Shipped the rest on `feature/phase-67-preview-interactivity`.** Fixed a real, pre-existing coordinate
> bug shared by every on-canvas overlay first (neither the callout nor motion-path overlay accounted for
> `object-fit: contain` letterboxing — one used the native export resolution instead of the actual
> rendered size, the other a hardcoded 800×450 guess). Then: `ClipArtClip` already had `CalloutClip`'s
> exact geometry and was already live in export, just missing an overlay — added one reusing the existing
> resize math as-is (not independently click-tested live — no clip-art catalog configured in the demo
> environment, but same verified code path as callouts). Motion keyframes can now be dragged by their dot
> to reposition (previously selection/Bezier-handles only), plus a bonus fix: double-click-to-add now
> lands where you actually click instead of always at dead-center. Text overlays gained an additive,
> optional on-canvas position override that doesn't touch existing alignment-based projects unless
> dragged. Two unrelated pre-existing bugs found and logged (not fixed) along the way — see items #17
> and #18.

---

## 12. Real-time preview rendering pipeline, decoupled from output resolution ✅ Complete (2026-08-08)

Build the actual pipeline behind item #11's live preview:
- Preview can render at a lower resolution than the real output — it doesn't need a full-resolution render to be useful while scrubbing/editing.
- Add a dropdown (near wherever the output/screen size is already configured) to pick the on-screen preview's render resolution — affects only what's shown while editing, never the exported file's actual resolution.

> Requested 2026-08-08, same session as item #11 — split out separately since #11 is the editing UX and #12 is how it's actually rendered under the hood.
>
> **Shipped on `feature/phase-64-preview-render-resolution`.** Repurposed an existing-but-decorative
> "canvas size" dropdown in the preview controls (it looked functional but did nothing real — a latent
> bug fixed along the way) into a real preview-quality picker (Full/75%/50%/25%, default Full = unchanged
> behavior). Below 100%, the export resolution is scaled down and a `scale`+`pad` filter is appended to
> the existing concat/re-encode step that already builds the Preview output — no new ffmpeg pass. Live-
> verified with a real clip: 50% quality measured exactly 960×540 against a 1920×1080 export setting.
> Deliberately does not wire anything to auto-refresh on edits — that's item #11's "no explicit render
> step" half, still open, and now genuinely unblocked (has both this phase's resolution-decoupled render
> path and phase 59's resize-handle code, merged into develop by phase 63).

---

## 13. Timeline render-progress bar, Premiere-style — ✅ Shipped, scoped down (2026-08-09)

A thin (1–2px) bar just above the timeline tracks showing which frames of the live preview have been
rendered vs. not yet rendered — non-blocking, purely a visual indicator. When an edit changes part of the
timeline, the affected span turns gray again (unrendered) while background rendering catches up there.
Modeled directly on Adobe Premiere Pro's render bar.

> Requested 2026-08-09. Depends on item #12 (a real-time preview rendering pipeline) existing first —
> there's nothing to show progress *for* until frames are actually being generated incrementally in the
> background rather than as one blocking render.
>
> **Shipped on `feature/phase-69-render-progress-indicator`, deliberately scoped down.** True per-region
> tracking turned out not to be honestly buildable: `ClipStore.OnChange` carries no information about
> *which* clip changed (would mean instrumenting ~40 mutation call sites), and it wouldn't even be
> accurate — the preview currently concatenates each video clip's raw, untrimmed source file, so trim/
> effects edits don't actually change the render at all today (see item #20). Shipped instead as a
> whole-timeline binary indicator: one bar, green/"up to date" right after a successful Preview render,
> gray/"stale" the instant any edit happens. Live-verified end to end including staying pixel-aligned
> with the ruler across zoom changes.

---

## 14. In-app preview: placement, resizing, and a popout window ✅ Complete (2026-08-08)

Several related asks about the in-app timeline preview:
- **Placement:** below the toolbar row (where Initialize lives) and above the timeline tracks, centered
  horizontally within its row.
- **Resizing:** that row should be resizable (drag to resize), and the preview's displayed size follows the
  row's height — up to a max height/width capped at the final export resolution's actual pixel size (never
  upscale the preview past the real output's 1:1 size). Resizing the preview triggers a re-render, since the
  quality/scaling calculations change with size.
- **Context menu:** right-click on the preview opens a menu for common preview actions. One specific item
  requested: **Popout**, which moves the preview into its own Telerik window.
  - The popout window is movable/draggable.
  - It always keeps the export's aspect ratio when resized.
  - Its max resize size is capped as a *percentage of the user's screen/window size*, not a fixed pixel
    value — e.g. a 300×300px export could pop out at up to its full 300×300 if that fits on screen, but a
    4000×4000px export on a 1920×1080 screen should cap at some percentage of the available screen space,
    never attempt to render at full native resolution on screen.

> Requested 2026-08-09. Overlaps with and refines item #8's preview-placement note (already superseded once,
> 2026-08-07: "own height-adjustable div... not a tab inside the floating panel") — this adds the
> resize-triggers-re-render behavior and the popout window on top of that placement decision.
>
> **Shipped on `feature/phase-62-preview-placement-resize-popout`.** Placement and resize both revived
> existing-but-dead plumbing from an earlier phase (`LayoutService`, `ResizableDivider.razor`) that had
> been built but never wired up. Size cap required a new small shared service, since the export resolution
> previously lived only on a private field inside the Export dialog. Popout reuses the existing
> `VideoPreview` component wholesale inside a floating window rather than building a second synced player.
> **Not built**: resize-triggered re-rendering — the preview already scales for free via CSS, and nothing
> in the render pipeline is resolution-dependent yet, so that's properly item #12's job once it exists.
> See [[project_video_editor_phase62_preview_placement_resize_popout]] for the full detail, including a
> real bug (missing close button) caught by live verification, not by the build.

---

## 15. Project save/load as JSON, local-vs-server choice, project list + delete ✅ Complete (2026-08-08)

- Save the full project (settings, layers/clips, edits) to a JSON file so a user can come back later and
  rebuild it — assuming the referenced media files are still present on the user's machine.
- On export, the project is saved locally by default; the user should be able to choose to also/instead push
  it to the configured "server."
- A UI to list existing saved projects, so the user can decide whether to delete any of them.
- The JSON format explicitly does **not** persist undo/redo history — only the end-state settings/layers.

> Requested 2026-08-09. Note: `ProjectService`/`ProjectStore` already exist and do *some* of this — see
> [[project_video_editor_qa_sweep]] item #24 (File→Save persists to `localStorage`; a separate `.benvideo`
> JSON download also exists). Scope this as a review/audit of what's already built against these specific
> requirements (server-push choice, list+delete UI, explicit non-persistence of undo/redo) rather than
> assuming it needs building from scratch.
>
> **Shipped on `feature/phase-61-project-json-audit`.** The audit confirmed nearly everything was already
> built: full serialization (every clip type, tracks, markers, motion paths — undo/redo correctly never
> included), local save (in-app Project Manager + `.benvideo` file download), and server save/load (gated on
> a configured `DocumentPostUrl`) were all already wired into the File menu and a toolbar button; the list
> UI already had open/delete/rename. Found and fixed two real gaps: (1) renaming a saved project in the list
> didn't persist — it mutated the in-memory row only, never wrote back to `localStorage` — added
> `ProjectStore.RenameAsync`. (2) Export and Save were fully disconnected flows; added a small post-export
> prompt (`ExportSavePrompt.razor`) offering Save Locally/Save to Server/Skip, shown only after an immediate
> "Export Now" completes (a pre-existing timing quirk means "Add to Queue" fires the same completion event at
> enqueue time, not job-finish time, so the prompt doesn't yet fire for queued exports — left for later).

---

## 16. Rich text properties for text overlays and callout text — 🟡 Slice D complete (2026-08-12)

Text — both a standalone text overlay and text inside a callout — should support:
- ~~Size and color~~ + font family — **✅ Slice A shipped** (`feature/phase-74-rich-text-fonts-slice-a`):
  callout text is a brand-new capability (`CalloutClip` previously had no text at all) — `Text`/
  `FontFamily`/`FontSize`/`FontColor` fields, rendered centered on the shape's bounding box (multi-line via
  `<tspan>`), full persistence, live `OnChange` UI in `CalloutEditor`. Text overlays' size/color/font-family
  already existed but font selection was **silently broken on every OS** — `ToDrawtextFilter()`'s
  `fontfile=/System/Library/Fonts/{FontFamily}.ttf` was a literal macOS path that ffmpeg.wasm's in-memory
  filesystem never had a file at. Fixed by unifying all text-overlay export (static + animated) onto the
  per-frame SVG rasterization pipeline — the browser resolves font names against its own installed fonts,
  no bundled font file needed on any OS. Also closed item #23 (background box now renders on the SVG path,
  approximate padding-based sizing) as a side effect. 22 new unit tests.
- ~~Font weight/bold, underline~~ — **✅ Slice B shipped** (`feature/phase-111-bold-underline-text`):
  whole-block `FontBold`/`FontUnderline` on both `TextOverlay` and `CalloutClip`, rendered via SVG
  `font-weight`/`text-decoration` on the same shared per-frame pipeline (so both live preview and export
  get it for free). Found and fixed a real bug live-testing: `ClipStore.UpdateTextOverlay()` copies fields
  onto the existing instance via a whitelist that predated these two fields, so the Apply button was
  silently dropping them — the checkboxes ticked but nothing was actually saved. 9 new unit tests, 2 more
  added to the pre-existing `UpdateTextOverlay_UpdatesAllProperties` test to guard against the same class
  of bug recurring for a future field.
- ~~Subscript/superscript~~ + ~~inline mixed formatting~~ — **✅ Slice C shipped**
  (`feature/phase-115-inline-rich-text-runs`): both close together, since the user chose a full
  rich-text toolbar (not typed markup) for inline formatting, and a toolbar naturally includes
  Subscript/Superscript buttons alongside Bold/Underline/Color. New `TextRun` model
  (`Text`/`Bold`/`Underline`/`Subscript`/`Superscript`/`Color`) + `List<TextRun>? Runs` on both
  `TextOverlay`/`CalloutClip` (`null`/empty = the exact prior whole-block path, fully backward
  compatible, no migration). Both renderers now emit one `<tspan>` per (line, run) instead of per
  line, using SVG's own text-chunk semantics (only the first run of each line needs an explicit
  `x`) for correct multi-run alignment with zero client-side text measurement.
  `TelerikEditor` (Telerik's own rich-text component, Bold/Underline/SubScript/SuperScript/ForeColor
  tools) replaces the plain textarea in both editors; the now-redundant whole-block Bold/Underline
  checkboxes are removed since the toolbar subsumes them. New `richTextRunsInterop.js`
  (browser-`DOMParser`-based HTML→Runs, so parsing exactly matches what the editor produced) +
  `RichTextRunParserService`; `TextRun.ToHtml` (pure C#) for the reverse direction, including a
  synthetic single-run fallback so old saved content still displays its existing Bold/Underline the
  first time it's reopened. 27 new tests; a real bug (raw C# string literals silently mis-escaping
  SVG attribute quotes, truncating them one character early) was caught by the new renderer tests
  before ever reaching the app. Live-verified two ways: the real shipped JS module called directly
  against 8 representative HTML strings, and a full real UI pass (typed text, applied Bold to one
  word and Subscript to another via the actual toolbar buttons, clicked Apply, read the live
  preview's actual rendered SVG) confirming exactly the expected per-run `<tspan>` output.
  **Found in passing, fixed in the same pass**: `ProjectTextOverlay`/`ProjectCalloutClip` never
  actually included `FontBold`/`FontUnderline` in serialization — phase 111 shipped those fields
  but never wired them into `ProjectFile`/`ProjectService`/`ClipStore`, so Bold/Underline was
  silently lost on every project save/reload; fixed alongside adding `Runs` serialization in the
  same DTOs/mapping methods. See README-phase-115.md in the Ben.Video.Editor repo.
- ~~Font selection from Google Fonts~~ — **✅ Slice D shipped** (`feature/phase-116-google-fonts`).
  Turned out simpler than the original 2026-08-09 note assumed ("needs base64-embedding or
  FontFace-API design"): rasterization already happens in-browser (`createImageBitmap`/canvas in
  `svgFrameRenderer.js`), so a normal dynamically-loaded web font is visible to it — no embedding
  needed. New `GoogleFonts.cs` (curated ~15-font list, `IsGoogleFont`) concatenated into the
  existing font dropdowns alongside `StandardFonts.Names`; new `googleFontsInterop.js`
  (`ensureFontLoaded`: idempotent `<link>` injection, `document.fonts.load()` for both regular and
  bold weight with a bounded 3s timeout so a slow/offline network degrades to the system fallback
  font instead of hanging the UI) + `GoogleFontService` (a no-op for system fonts, so the common
  case costs nothing); called from both editors' font dropdowns (so the live preview picks it up
  immediately) and from `ExportService` right before rasterizing any frame using a Google Font
  (what actually matters for correctness in a fresh session where the `<link>` was never injected).
  13 new tests; 1373/1373 passing. Live-verified against the real Google Fonts CDN — the real
  shipped JS module correctly injected the exact expected `<link>` URL (including correct
  multi-word `+`-encoding for "Open Sans"), confirmed idempotent, and confirmed both font weights
  genuinely loaded via `document.fonts.check()`; then a full real UI pass confirmed the dropdown
  shows all 7 system fonts followed by all 15 Google Fonts in order, and selecting "Roboto" +
  clicking Apply changed the live preview's actual rendered SVG `font-family` attribute end to end.
  Real, stated-plainly constraint: Google Fonts export needs network access to
  `fonts.googleapis.com` at render time, unlike the fully-offline system-font path. See
  README-phase-116.md in the Ben.Video.Editor repo.
- **Still open:** Direct in-preview editing — click into the text on the canvas and type/format it
  there, not only through the side-panel form. Deliberately deferred (2026-08-12) as its own future
  item — the largest, most different piece of this backlog entry (new canvas click-to-edit
  interaction, caret handling, sync back to `ClipStore`), and nothing is broken without it since the
  side-panel editor fully works today.

> Requested 2026-08-09. Slice A (phase 74) shipped 2026-08-08. Slice B (phase 111) shipped
> 2026-08-10. Slice C (phase 115) shipped 2026-08-12. Slice D (phase 116) shipped 2026-08-12.

---

## 17. Media & Properties window loses its resized dimensions when dragged ✅ Fixed (2026-08-08)

Bug: in the video editor, resize the floating "Media & Properties" window (`VideoEditor.razor`'s
`TelerikWindow`), then drag it to reposition — the resize doesn't stick, the window reverts to its prior
width/height once moved.

> Requested 2026-08-08. Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).
>
> **Fixed on `feature/phase-68-panel-and-selection-fixes`.** Root cause: `Top`/`Left` were two-way bound
> but `Width`/`Height` were plain literal attributes — dragging (which updates the bound `Top`/`Left`)
> triggered a re-render that reapplied the literal size. Fixed by two-way binding `Width`/`Height` too,
> the same pattern already working for `Top`/`Left`. Not independently live-verified via the actual
> resize gesture — Kendo's native resize handles don't respond to synthetic pointer events in this
> session's browser tooling (same limitation as the phase-62 popout window and native HTML5 drag-and-drop
> elsewhere); high confidence by construction, but flagging the live-test gap.

---

## 18. Selecting a layer while a motion path is active keeps showing the motion editor ✅ Fixed (2026-08-08)

Bug found during phase 67's live verification: in `VideoEditor.razor`'s Properties-panel if/else-if
selection chain, `_motionLayerId.HasValue` is checked before `_selectedTextOverlay` (and likely before
other post-motion branches too). Once a layer's motion path is activated ("⏱ Animate"), selecting a
*different* layer on the timeline (tested with a text overlay) doesn't clear `_motionLayerId` — the
Properties panel keeps showing "No keyframes yet for this layer" / the motion keyframe editor instead of
switching to the newly-selected layer's own editor, until the motion editor is explicitly closed. This is
the same class of bug phase 59 already fixed in the other direction (`ActivateMotionPath` not clearing
`_selectedCallout`/`_selectedClipArt`/`_selectedTextOverlay`) — this is the reverse case, not yet fixed.

> Found 2026-08-08. Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).
>
> **Fixed on `feature/phase-68-panel-and-selection-fixes`.** `_motionLayerId`/`_motionLayerType` now
> clear once at the top of `OnTimelineItemSelected`, covering every selection path uniformly instead of
> duplicating the clear into each branch. Live-verified: activated a motion path on a callout, selected a
> text overlay, confirmed the Properties panel correctly switched away from the motion editor.

---

## 19. Configurable, animatable drop shadow for text/callouts/other objects — ✅ Complete (2026-08-08)

Text overlays, shape callouts, and any other object type that could reasonably have one should support a
configurable drop shadow in the Properties panel:
- The shadow never affects the shape/text's own size (it's a separate rendered layer, not part of the
  bounding box).
- If the shadow falls outside the video's own frame bounds, only the portion inside the bounds renders —
  it clips at the edges rather than being visible outside the video.
- The shadow itself should be animatable over time (via the existing motion-keyframe system) — at least
  opacity, color, and size/blur, possibly other shadow settings too.

> Requested 2026-08-08. Note: `CalloutClip` already has static shadow fields (`ShadowColor`/
> `ShadowOffsetX`/`ShadowOffsetY`/`ShadowBlur`) per the existing model — this item is about extending
> shadow support to other layer types that don't have it yet (text overlays at least), confirming/adding
> the clip-to-video-bounds rendering behavior, and making shadow properties keyframe-animatable, not
> necessarily building shadow support from scratch. Lives in the separate Ben.Video.Editor repo
> (Github-BenVideo remote).

**Phase 1 shipped 2026-08-08** (`feature/phase-70-shadow-foundation-callout-fixes`): Shadow fields added
to the motion-keyframe system (`MotionKeyframe`/`MotionFrame`), interpolated with full easing support for
free (reuses the same eased-progress value already computed for position/scale/color). Fixed a real bug
found during this work — `CalloutClip`'s shadow rendered on Arrow/Line shapes only; Rectangle/Ellipse/Star
built the SVG filter but never applied it, so they silently had zero shadow despite full UI support — now
fixed across all 5 shapes. Also fixed a pre-existing persistence bug where `FillColor`/`StrokeColor`/
`ControlPointValues` keyframe values silently didn't survive project save/reload (the new Shadow fields
would have inherited the same bug). Text overlays gained static shadow config (color/offset/blur) via
ffmpeg's native `drawtext` shadow params, plus full Shadow UI in both the layer editor and the
motion-keyframe editor. Bounds-clipping is satisfied by construction (both the SVG-raster and native
ffmpeg paths inherently clip to the frame) — no extra work needed there.

**Phase 2 shipped 2026-08-08** (`feature/phase-71-animated-text-shadow-pipeline`): gave `TextOverlay` the
same per-frame SVG animated-export pipeline callouts already had — once a text overlay has any motion
path, its position, size, opacity, and shadow are all now driven by it (eased, not just linear), rendered
via a new SVG `<text>` renderer and composited the same way animated callouts already are. A real
simplification found versus the original design: SVG's own `text-anchor`/`dominant-baseline` attributes
are resolved by the browser's own SVG renderer, so no JS text-measurement infrastructure was needed at
all (the originally-flagged font-metric-parity risk turned out to be avoidable, not just acceptable). Also
a beneficial side effect: existing animated text now renders with correctly eased motion in the exported
video, not just the live preview, matching callouts. Not click-verified end-to-end (a Telerik "Add
keyframe" button couldn't be driven through browser automation in this session, same limitation hit in
phase 1) — correctness relies on 22 new unit tests exercising the real production code directly.

---

## 20. Preview doesn't reflect video-clip trim or effects — ✅ Complete (2026-08-08)

Bug found while scoping item #13: `PreviewTimelineAsync` (the ffmpeg pipeline behind the editor's own
"Preview" button) concatenates each `VideoClip`'s **raw, untrimmed** source file directly — no `-ss`/`-t`
trim step is applied before the concat, unlike image clips, which do get a proper per-clip segment build.
Effects/color grading are similarly never referenced anywhere in that pipeline. Practical effect: trimming
a video clip's start/end, or applying an effect to it, changes nothing about what the in-app Preview
actually shows — only the real Export pipeline (a separate code path) reflects those edits. Likely
confusing/misleading for anyone trimming or grading a clip and expecting to see it in Preview.

> Found 2026-08-09 during phase 69. Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).

**Shipped 2026-08-08** (`feature/phase-72-preview-trim-effects`): `PreviewTimelineAsync` now builds a real
per-clip trimmed segment for each video clip before concatenating, the same way image clips already did —
live-verified directly: a 13.8s test clip trimmed to 10.8s now renders a 10.8s preview. Also found and
fixed a second, deeper bug while tracing this: the extensible `AppliedEffects` system was never applied to
video clips in the real Export pipeline either, not just Preview — fixed there too, so both pipelines
reach real parity.

---

## 21. Adding a keyframe mid-scrub silently captures the interpolated value, not a fresh target (✅ shipped)

When scrubbing between two keyframes, the Properties panel shows the live *interpolated* value at the
playhead (e.g. a shadow animating 5px → 10px shows ~7px at the halfway frame). If the user adds a new
keyframe at that scrubbed position without changing anything, `MotionKeyframeEditor.AddKeyframeAtPlayhead`
seeds the new keyframe from that displayed interpolated value (by design — this avoids the layer visibly
"jumping" the instant a keyframe is added). The surprising part: since the new keyframe's value equals
what was already showing, there's no visible change at that instant, but everything *after* it now holds
flat at that captured value instead of continuing to interpolate toward the original end keyframe — a
silent, easy-to-miss change to the animation curve unless the user explicitly edits the value at the new
keyframe afterward.

Fixed 2026-08-09 (phase 78, `feature/phase-78-midscrub-keyframe-warning`, merged to `develop`) with the
UI affordance the backlog text asked for, rather than removing the by-design capture behavior: a new
`IsAddingMidInterpolation()` check (true when the playhead sits strictly between two existing
keyframes) shows a warning above the "+ Keyframe" button — *"Adding here captures the interpolated
value — the curve will hold flat past this point until you adjust it."* Made it live as the user
scrubs (not just on next panel open) by subscribing to `PlaybackService.OnStateChanged`, which the
component didn't previously do at all. Live-verified with real playhead scrubbing: hint shows strictly
between keyframes, correctly absent exactly on a keyframe or past the last one, updates live without
reopening the panel.

> Requested 2026-08-08, described using a shadow going 5px → 10px animated over 5 frames as the example:
> adding a keyframe at frame 1 (interpolated ~7px) locks the animation at 7px onward unless the user also
> changes that keyframe's value to 10px. Applies to the whole motion-keyframe system generically
> (position, scale, color, shadow), not just shadow specifically. Shipped 2026-08-09 (phase 78). Lives
> in the separate Ben.Video.Editor repo (Github-BenVideo remote).

---

## 22. Unescaped FontColor in animated-text SVG export — ✅ Fixed (2026-08-08)

Code-review finding from phase 71/72's wrap-up: `TextOverlayRenderer.Render()` (the SVG renderer used for
text overlays with a motion path) interpolates `TextOverlay.FontColor` directly into the `fill="..."`
attribute unescaped, while the sibling `FontFamily` and `Text` values two lines away are both passed
through the same method's `EscapeXml()` helper. `FontColor` is a plain `string` with no format validation
on the model itself — today's color picker UI always writes a safe `#RRGGBB` hex string, so there's no
live exploit path via normal editing. But if `FontColor` ever contains a `"` character (a hand-edited or
otherwise-tampered `.benvideo` project file reloaded via `ProjectService`, or any future direct-input path
that bypasses the color picker), the value breaks out of the SVG attribute and injects arbitrary markup
into the string that gets rasterized via `createImageBitmap` for animated-text export. Small, mechanical
fix: wrap `overlay.FontColor` in the same `EscapeXml()` call already used for the other two string fields.

> Found 2026-08-08 via a post-phase-72 code review of the session's shadow/preview work. Lives in the
> separate Ben.Video.Editor repo (Github-BenVideo remote), `Models/TextOverlayRenderer.cs`.

**Fixed same day**: `fill="{overlay.FontColor}"` → `fill="{EscapeXml(overlay.FontColor)}"`, one-line change,
matching the escaping already applied to `FontFamily`/`Text` in the same method. Build + all 1058 tests
pass.

---

## 23. Animated text overlays don't render their background box (BoxColor) ✅ Fixed (2026-08-08)

**Fixed as part of phase 74** (backlog item #16 slice A): `TextOverlayRenderer` now renders `BoxColor` as a
`<rect>` behind the text with approximate padding-based sizing (average-character-width heuristic × text
length) — deliberately approximate rather than glyph-measured, strictly better than the previous
nothing-at-all. Applies to every overlay now that phase 74 unified static AND animated text export onto the
SVG pipeline. Exact `getBBox()`-measured sizing remains a possible future refinement.

Original description:

Deliberate scope cut from phase 71 (backlog item #19's animated-text-shadow pipeline): the new
`TextOverlayRenderer` (used only once a text overlay has a motion path) doesn't draw `TextOverlay.BoxColor`
— the optional background box behind text — at all. The *static* (non-animated) `drawtext`-based rendering
path still supports it correctly; only text overlays that are both animated (any motion path) and have a
background box enabled are affected, silently losing the box the moment the overlay starts animating.
Deliberately scoped out at the time to avoid needing text-measurement infrastructure for correctly sizing
the box — worth reconsidering now that a simpler, no-measurement approach exists (see item #22's file):
`TextOverlayRenderer` could measure the SVG `<text>` element's own rendered bounding box via `getBBox()`
after inserting it into the DOM/canvas context, rather than needing to replicate ffmpeg's `text_w`/`text_h`
calculation by hand.

> Found 2026-08-08 during phase 71 planning, deliberately deferred rather than fixed at the time. Lives in
> the separate Ben.Video.Editor repo (Github-BenVideo remote).

---

## 24. Convert ClipBrowser→timeline-track drop to pointer events — ✅ Fixed (2026-08-09, phase 91)

The last remaining spot of native HTML5 drag-and-drop on the timeline: dragging a *new* clip from
`ClipBrowser` onto a timeline track (`window.__bvDragClipId` global handoff, `ClipBrowser.razor:686-693`,
consumed via `OnTrackDragEnterAsync`/`OnTrackDragOver`/`OnDropAsync` in `VideoTimeline.razor:1167-1280`).
Unlike the playhead-scrub, clip-trim, and clip-reposition mechanisms (all already pointer-based, and the
same-track swap-reorder path fixed alongside this item — see item #8), this one is genuinely
cross-component: `ClipBrowser` and `VideoTimeline` are separate sibling components under `VideoEditor.razor`
with no shared DOM ancestor, so converting it needs new infrastructure — global pointer tracking, JS-side
`document.elementFromPoint` hit-testing to find which track is under the cursor mid-drag, and a new
cross-component callback bridge. A currently-working, currently-clean feature with no concrete consumer
today (no Playwright/browser-test infra exists in this repo to benefit from synthetic-event compatibility).
`VideoTimeline.razor.js`'s existing (currently unused) `capturePointer(el, pointerId)` helper is the
primitive a rebuild would use.

> Shipped as a new module-scoped JS bridge (`clipDragBridge.js`) mediating between the two sibling
> components via `DotNetObjectReference`: `ClipBrowser` calls `startClipDrag()` on `pointerdown`,
> `VideoTimeline` registers as the drop target and receives `HandlePointerDropFromJs` on release, reusing
> the same `TimelineDropCalculator`/`SnapEngine` pipeline as before. The same-track reorder gesture (native
> drag) was left untouched. Found and fixed a real bug along the way: the clip thumbnail `<img>` is
> natively draggable by default in Chrome, which silently hijacked the gesture via a native image-drag
> before the pointer bridge ever saw a `pointerup` — fixed with an explicit `draggable="false"`. Live-verified
> via claude-in-chrome with real pointer events (traced actual Blazor JS-interop calls). See
> [[project_video_editor_phase91_clipbrowser_pointer_drag]].

> Found and deliberately deferred 2026-08-08 during phase 73 (backlog item #8's cleanup). Lives in the
> separate Ben.Video.Editor repo (Github-BenVideo remote).

---

## 25. Timeline snapping — expand/verify coverage — ✅ Fixed (2026-08-10)

`Snapping` already exists as a feature flag and has real implementation (`_snapGuidePx` in
`VideoTimeline.razor`, a visible snap-guide line) — not a from-scratch feature. Noted as worth a fresh look:
confirm snap points cover the cases users actually want (clip edges, playhead, markers, other clips'
start/end) during the various drag interactions (clip reposition, trim, marker drag), and consider whether
coverage should expand.

> Noted by the user 2026-08-08, no specific complaint yet. Scoped precisely by the user 2026-08-10 into
> 5 concrete parts:
> 1. Dragging a clip to a different track/lane moves it there ("higher" = a different row).
> 2. Dragging a clip near another clip on the timeline snaps to that clip's end (or start, if closer).
> 3. After a placement, the timeline auto-scrolls into view if the result isn't already visible.
> 4. A new clip that fits in existing timeline space (given playhead position / clip-edge proximity)
>    just drops in there directly.
> 5. A new clip that doesn't fit where it would land prompts to make room (ripple subsequent clips)
>    before placing; a new clip dropped in an empty lane past the current end just extends the timeline.
>
> Investigation found the underlying gap is bigger than "snapping": neither drag system (native-HTML5
> reorder-swap, nor the pointer-based body-drag everyone actually uses) supported cross-track moves at
> all, body-drag had zero snapping AND zero collision detection (`ClipStore.CommitDraggedPosition` just
> accepted whatever raw position the drag landed on), there was no auto-scroll-into-view anywhere in the
> codebase, and new-clip-drop's overlap fallback just dumped the clip at the end of the whole track with
> no "make room" option. Being worked in 5 verified steps matching the scope above.
>
> **Part 1 shipped**: phase 103 (`feature/phase-103-snap-while-dragging`, merged to `develop`, pushed).
> `SnapEngine.CollectSnapTargets` gained an `excludeItemId` param (a dragged clip's own edges otherwise
> always out-snap everything else), wired into body-drag's position computation + the existing snap-guide
> line. Live-verified: dragging a clip to ~0.3s past another clip's end landed it exactly touching (27.7s
> total for two 13.8s clips), not at the raw ~27.9s an unsnapped drag would produce. Found and logged a
> real, separate pre-existing bug along the way — item #48.
>
> **Part 2 shipped**: phase 104 (`feature/phase-104-cross-track-move`, merged to `develop`, pushed).
> Neither existing drag system supported cross-track moves at all before this. New
> `ClipStore.CommitDraggedPositionAndTrack(targetTrackId)` + `MoveClipToTrackCommand` give the track
> change its own undo/redo entry, gated to `VideoClip`/`AudioClip` moving between same-type tracks
> (MultiTrack only — overlay items stay pinned to their owning video track's lane). First
> implementation attempt moved the item's track membership *live* during the drag and immediately
> broke — relocating a `TrackItem` into a different track's `Items` list moves its chip into a
> different `@foreach`, so Blazor destroys/recreates its DOM element and silently kills the pointer
> capture the rest of the drag needs (same underlying bug class as item #27, a new trigger). Fixed by
> only tracking the hovered row for a visual highlight during the drag and doing the actual move once,
> at drop. Live-verified: chip stays on its original track's DOM during the drag (hover highlight
> shown on the target row), moves to the target track exactly on drop, Undo button correctly reads
> "Undo: Move clip to another track" and correctly reverts.
>
> **Part 3 shipped**: phase 105 (`feature/phase-105-auto-scroll-into-view`, merged to `develop`,
> pushed). New `scrollItemIntoView` JS helper + `ScrollItemIntoViewAsync` C# wrapper, called
> fire-and-forget from both the ClipBrowser-drop path and the existing-clip move path, scrolls the
> affected chip back into view (smooth, both axes) if it landed outside the visible timeline area.
> Live verification initially showed no scroll happening at all; root-caused via DOM-attribute
> breadcrumbs (not console logs — the console tool was returning stale/cached messages this
> session) to two compounding browser behaviors under the test tab's persistent `document.hidden`
> state: `requestAnimationFrame` never fires while hidden, and `scrollIntoView`'s `smooth` animation
> makes no progress while hidden either, even once the callback itself fires. Fixed by deferring
> via `setTimeout` instead of `requestAnimationFrame` (a macrotask isn't gated on the rendering
> pipeline); kept `smooth` for the actual scroll since real drag gestures only complete in a
> foreground/visible tab. Confirmed the full call chain was correct throughout by temporarily
> switching to `behavior: 'instant'`, which immediately showed the container's `scrollLeft` jump to
> the expected value.
>
> **Part 4 shipped**: phase 106 (`feature/phase-106-ripple-insert-confirmation`, merged to
> `develop`, pushed). `VideoEditor.AddClipToTimeline` now anchors a newly-added clip to the
> playhead (new `TimelineDropCalculator.ResolvePlayheadAnchoredPosition`), snapping to touch an
> adjacent clip's edge when the playhead sits right at one, instead of always appending at the end
> of the track. When the resolved position would overlap an existing clip (new
> `TimelineDropCalculator.Overlaps`), a new `RippleInsertPrompt` dialog asks whether to shift the
> clips after that point later to make room; confirming calls a new, undoable
> `ClipStore.InsertClipWithRipple`. Live-verified end to end in the Playground: importing a second
> clip while the playhead sat squarely inside the first clip showed the "Not Enough Room" dialog
> with the correct clip name; **Make Room** placed it touching the first clip's end with no
> overlap/gap and a correct "Undo: Insert test-video.mp4 (ripple)" undo entry that reverted
> cleanly; a later import with the playhead sitting at that clip's edge landed touching it with no
> dialog at all, confirming the edge-snap branch too.
>
> **Part 5 verified (no code change needed)**: "a new clip dropped in an empty lane past the current
> end just extends the timeline." `TimelineTrack.TotalDuration`/`ClipStore.TotalDuration` are plain
> computed properties (`max(item.TimelinePosition + effective duration)`, no cap anywhere in the
> codebase) — any placement past the current end already grows the reported total automatically,
> with no separate "extend" step required. Live-confirmed in the same Playground session as part 4:
> after the two-clip ripple-insert above, the toolbar read "TIMELINE 0:27.7", the ruler correctly
> extended through 27.0s, and the zoom auto-fit to 0.8× to fit the new total — all without any
> further action. All 5 parts of this item are now shipped. Lives in the separate Ben.Video.Editor
> repo.

---

## 26. Removed item leaves stale Properties-tab content — ✅ Fixed (2026-08-09, phase 92)

Right-click → remove a layer (callout, text overlay, etc.) while the Media & Properties window's Properties
tab is open and showing that layer: the tab keeps displaying the removed item's editor/fields instead of
clearing to the empty state. The selection-clearing logic needs to also fire on removal via the context-menu
path, not only on ordinary re-selection.

> Found by the user 2026-08-08 during manual testing. Lives in the separate Ben.Video.Editor repo
> (Github-BenVideo remote).
>
> Shipped: new `VideoTimeline.OnItemRemoved` callback fired from all three single-item removal paths
> (context-menu Remove, Ripple Delete, per-chip trash button), clearing whichever of `VideoEditor`'s 7
> selection fields matches the removed id. Same gap fixed in the multi-select bulk-delete path, which
> only cleared 2 of 7 fields. Live-verified: selecting a clip, removing it via right-click and via the
> trash button, confirmed the Properties tab reverts to its empty state both times.

---

## 27. Timeline item drag doesn't stop on mouse-button release (✅ shipped)

Root cause: the clip-chip `<div>` in `VideoTimeline.razor` does double duty — pointer-based free-move
(`OnChipPointerDown`/`_movingItem`) AND native HTML5 drag-and-drop (`draggable="true"` +
`ondragstart`/`ondragover`/`ondragend`, a separate, older same-track swap-reorder mechanism) — and
neither ever took real pointer capture. A fast drag could let native drag hijack the gesture before the
async Blazor render flipped `draggable="false"` (once native drag wins, the browser stops delivering
`pointerup` to the chip at all), or simply move the cursor off the element/trim handle before mouse-up.
Either way, `OnTrimPointerUp` — which clears `_movingItem` and commits the position — never ran, leaving
the clip visibly stuck in "moving" state (and undraggable again) until page reload. A `capturePointer`
JS helper already existed with a doc comment describing exactly this fix, but was never called from
anywhere. Fixed 2026-08-09 (phase 77, `feature/phase-77-clip-drag-stuck`, merged to `develop`): wired the
helper (renamed `capturePointerAt`, using `elementFromPoint` + `closest` instead of needing an
`ElementReference` per chip) into all four drag-initiating pointerdown handlers, and added a
`FinalizeMove` safety net called from `OnDragEnd` so a residual native-DnD win still cleans up state
instead of leaving it stuck.

No automated regression test was feasible — this is a real-OS-pointer-timing race, and synthetic
`PointerEvent`s dispatched via `element.dispatchEvent()` can't establish genuine pointer capture
(confirmed directly: `setPointerCapture` throws `NotFoundError` for them). Live-verified instead with
real OS-level mouse drags via the Claude in Chrome extension (not the Electron-embedded harness
browser, which has the same synthetic-input limitation): three separate drags (two body-move, one
start-trim) all committed cleanly with no stuck state.

> Found by the user 2026-08-08 during manual testing, right after phase 73's swap-reorder-parity fix
> shipped. Shipped 2026-08-09 (phase 77). Lives in the separate Ben.Video.Editor repo (Github-BenVideo
> remote).

---

## 28. Callout/text overlay: drag-to-extend start/end on the timeline — ✅ Fixed (2026-08-10, phase 93)

Callouts and text overlays should support the same click-and-drag start/end trim behavior video/image clips
already have on the timeline — dragging the start edge (when there's room before it) or the end edge to
extend/shrink the item's duration, with a resize cursor (e.g. east-west/left-right arrow) shown when
hovering over an extendable edge to signal the interaction is available.

> Requested by the user 2026-08-08. Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).
>
> Shipped: new start/end trim handles on `CalloutClip`/`TextOverlay` chips, mirroring `ImageClip`'s
> existing handles exactly (no source in/out points — just `TimelinePosition`+`Duration`). End handle
> extends/shrinks duration; start handle shifts position forward and shrinks duration to compensate,
> keeping the end position fixed. Reuses the already-generic `.bv-trim-handle` CSS and existing
> no-undo "live drag" `ClipStore` mutators (`UpdateCallout`/`UpdateTextOverlay`). Live-verified via
> claude-in-chrome: end-drag and start-drag both confirmed by exact pixel/duration math, plus a
> regression check that whole-chip body-move still works. Found and logged two unrelated things along
> the way — see items #42 (overlay row height) and #43 (overlays missing from the small Timeline
> Preview thumbnail).

---

## 29. ✅ ffmpeg.wasm aborts (OOM) during SVG-overlay export — output silently loses its video track (shipped 2026-08-09, phase 75)

Found live-testing phase 74, **confirmed by the user**: exporting a timeline with a callout produced an
"audio-only" output file. Direct MP4 box-structure inspection of the captured export blob confirmed it —
the file has an audio `trak` (`smhd` handler) but **no video track at all**. The browser console showed
`Aborted()` 9 times during a single export, which looked like an Emscripten OOM crash.

**That diagnosis turned out to be wrong.** Live testing in phase 75 disproved it: the single-threaded
ffmpeg.wasm core prints `Aborted()` as part of *every* command's normal exit path, success or failure —
not a crash signal at all. A first attempt at fixing this (treating `Aborted(` as fatal and tearing down
the core) was reverted once that was discovered.

**The real root cause**, found by reproducing the bug with a plain default callout (no OOM involved): three
independent bugs stacked in the native-callout compositing pass, which had — in effect — never actually
run before:
1. The `drawbox` pass emitted `-vf <chain>` next to the audio pass's explicit `-map 0:a?`. An explicit
   `-map` disables ffmpeg's default stream selection, so the output contained *only* audio and exited 0.
   Fixed by wrapping the chain in a `filter_complex` with the video output explicitly mapped, matching
   every sibling composite pass (`ExportArgBuilders.BuildFilteredVideoArgs`).
2. With video actually mapped, the filter ran for the first time and failed: `BuildCalloutFilter` used
   `W`/`H` (overlay-filter variables) where `drawbox` needs `iw`/`ih`.
3. Once running, the shape was invisible: `ColorHelper.ToFfmpegColor` emitted alpha **first**
   (`0xAARRGGBB`, CSS convention) but ffmpeg's colour parser takes alpha **last** (`0xRRGGBBAA`) — the
   default callout fill parsed with alpha `0x00`.

A belt-and-braces fix was added too: `ExportAsync` now ffprobes the final output and throws if it has no
video stream, so this class of silent "success" can't reach the download step again. `FfmpegService`
now keeps a ring buffer of ffmpeg log lines and throws on any non-zero exit code (the log-line content
itself is never inspected for crash signals, per the `Aborted()` finding above).

**Three more real bugs found during the broader "verify text/callout/transitions/shadows/alpha/motion
actually export" pass the user asked for afterward** (not just "a video track exists"):
- `createImageBitmap(svgBlob)` failed consistently in this dev environment (reproducible on a plain
  `<rect>`, not content-specific) — `svgFrameRenderer.js` now falls back to an `Image()`+canvas decode.
- The Playground's demo pages each pass their own `EditorOptions` parameter, but `ExportService` (and
  everything else reading `IOptions<VideoEditorOptions>`) only ever sees the *one* options object
  registered once at host startup, which the Playground left at all-defaults — so Transitions and
  TextOverlays silently never exported in any Playground demo regardless of what its UI showed. Real
  users are unaffected (`Ben.Web.WebApp/Program.cs` already configures these flags globally and its
  pages don't use the per-page override). Fixed by configuring the Playground's global options to match
  production — this also means every *previous* phase that "live-verified" export via the Playground
  never actually exercised Transitions or TextOverlays export, only their UI.
- Video clips were never scaled to the export's selected Resolution setting — only image clips and
  overlay PNGs were. A source clip smaller than the target resolution left overlays composited against
  a canvas-size mismatch (a callout at "10% from top" of a 1080-tall canvas landed past the bottom edge
  of an actual 360-tall frame, fully clipped). `BuildTrimArgs` gained the same scale+pad
  `BuildImageSegmentArgs` already had.

All four fixes were live-verified end-to-end with direct pixel inspection of exported frames (not just
"export succeeded, video track exists") — a video clip + text overlay + Arrow-shape callout (SVG path,
default drop shadow) now exports correctly at the real target resolution with both overlays visible at
their intended positions. **Not verified**: the animated/motion-keyframe overlay path (only static
single-PNG was exercised) and clipart (not wired into any Playground demo's feature flags) — see items
#32 and #33.

> Found 2026-08-08 during phase 74 live verification; user confirmed the audio-only symptom independently.
> Root-caused and fixed 2026-08-09 (phase 75, `feature/phase-75-ffmpeg-oom-silent-failure`, merged to
> `develop`). Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).

---

## 30. Slider tick-label numbers bunch up and become unreadable (✅ fixed 2026-08-13, phase 153)

Fixed across all 70 `TelerikSlider` instances in the editor's Properties panels (8 files:
`ClipEditor`, `AudioClipEditor`, `ImageClipEditor`, `CalloutEditor`, `ClipArtEditor`,
`MotionKeyframeEditor`, `TransitionEditor`, `TextOverlayEditor`). User decision (asked before
starting, since every slider already shows its live value in a label above the track): render only
Min/Max tick labels via `LabelTemplate`, applied uniformly — not the bespoke per-slider treatment
originally envisioned on 2026-08-09.

New `Services/SliderLabelTemplates.cs`: a naive `tick == max` check fails for any slider whose
range isn't an exact multiple of `LargeStep` (true for nearly every duration-bound slider) — Kendo
generates ticks at `Min + n*LargeStep` and stops at the last one ≤ `Max`, never adding a bonus tick
exactly at `Max`. Fixed by labeling the highest tick Kendo actually generates
(`tick + LargeStep > Max`) instead — a real position on the track, not a synthetic boundary.
Generic over `TValue` (`IConvertible`) since the editor mixes `TelerikSlider<double>` and
`TelerikSlider<int>` (font-size sliders).

Also fixed the 3 already-logged `::deep`-missing width bugs (`ClipEditor`/`AudioClipEditor`/
`ImageClipEditor`), plus a 4th, previously-undiscovered instance found in
`TransitionEditor.razor.css` while auditing every slider file for this phase — confirmed via the
compiled `.bundle.scp.css` before/after.

Live-verified: a 13.8s clip's trim sliders (non-step-aligned range) now show clean `["0","13"]`
labels at 259px full-row width (was stuck at Kendo's 200px default); a Callout's 14 rendered
sliders all show exactly 2 correct labels including non-aligned endpoints (`0.01`/`0.91`,
`0.1`/`55.1`) and exact aligned ones (`-180`/`180`). 9 new unit tests. 1645/1645 suite passing.

> See `README-phase-153.md` in Github-BenVideo for full detail.

---

## 31. Callout text-inside parity (✅ shipped 2026-08-13, phase 171) — linking half still open

**Text-inside parity is done** (user chose it over the linking alternative). Scoping first inverted
both of this item's assumptions:

- `CalloutClip` **already had** `Text`, `FontFamily`, `FontSize`, `FontColor`, `FontBold`,
  `FontUnderline`, rich-text `Runs`, `Opacity`, fade (`ComputeFadeAlpha`) and a full shadow group.
  The genuine gaps were only **alignment, wrapping, and shadow-on-text**.
- `TrackItem.LinkedClipId` **exists but propagates nothing** — set/cleared by an undoable
  `LinkClipsCommand`, drives a CSS class and a J/L-cut offset readout, and has a video→audio
  proximity finder, but no move/trim/delete follows a partner. Worth noting against item #52, which
  reads as though grouped editing already works.

Shipped: `TextAlign`, `TextVerticalAlign`, `TextWrap`, `TextShadow`, `TextPadding`, reusing
TextOverlay's own alignment enums; all defaults reproduce the previous dead-centre render exactly.
New `CalloutTextWrapper` uses an explicitly approximate width model (`chars × fontSize × 0.52`) —
real glyph metrics aren't reachable from a pure C# renderer that must emit identical SVG for
preview and export, and a slightly-wrong break beats those two disagreeing.

**Bug caught by live verification, not tests:** the first cut wrapped only the plain-text path, but
the rich-text editor *always* populates `Runs`, so `TextWrap` would have shipped as a checkbox that
did nothing while the plain-text unit tests passed. Fixed via `RichTextTspanBuilder.WrapLines`.
See `README-phase-171.md`.

**Still open — the other design:** linking a text overlay + callout as one logical unit. Needs
grouped move/trim/delete, grouped selection and drag on timeline *and* canvas, shared motion
path/fade, pairing generalized past video→audio, and undo throughout. Original ask below.

<details><summary>Original request (2026-08-08)</summary>

Callouts gained basic centered text-inside support in phase 74 (item #16 slice A), but the fuller ask:
either make text-inside a first-class equal of standalone text overlays (fonts, alignment within the
shape, wrapping, etc.), **or** support linking a text overlay and a callout together into one logical
unit — if one moves, the other moves; if one fades, the other fades; if one resizes, the other resizes,
and so on. Linking may be the more powerful model (any shape + any text placement) but needs design:
a link id between track items, grouped selection/drag on the timeline and canvas, and shared
motion-path/fade application.

> Requested by the user 2026-08-08. Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).

</details>

---

## 32. Animated/motion-keyframe overlay export path unverified — ✅ verified, and a real bug fixed (2026-08-09, phase 86)

Phase 75's live export verification only exercised the *static* (no motion path) single-PNG overlay
path for text overlays and callouts. The animated path (per-frame SVG rasterization for a callout/text
with a motion keyframe) shares the same underlying SVG-decode pipeline (fixed in phase 75 — see item
#29) but was never itself live-tested end-to-end after that fix.

**Turned out not to be low-risk** — actually verifying it (building a real callout with 3 keyframes,
bezier handles, and animated shadow, then sampling pixels from the actual exported video) found the
animation was **completely frozen**: `ExportService`'s per-frame elapsed-time calculation,
`i / s.Fps`, divides two `int`s — for any clip under 1 second at typical fps (e.g. 24 frames at
24fps), this truncates to `0` for every frame, so every animated overlay silently exported using
only its first keyframe's values, no matter how many keyframes existed. A second bug found in the
same pass: `ApplyMotionFrame(CalloutClip, MotionFrame)` never copied the frame's shadow fields, so
even after the freeze was fixed, an animated callout's shadow still wouldn't animate. Both fixed;
live-verified by direct pixel sampling of real rendered frames before (locked position the whole
clip) and after (position sweeps continuously through all 3 keyframes, ending exactly at the last
one). Full detail in `README-phase-86.md` in the Ben.Video.Editor repo.

> Found 2026-08-09 during phase 75 verification, deferred due to time. Lives in the separate
> Ben.Video.Editor repo (Github-BenVideo remote).

---

## 33. Clipart export path unverified — not wired into any Playground demo (✅ shipped)

None of the Playground's demo pages (Default/Multi-Track/Full-Featured/Audio Only) exposed the asset
browser's clipart feature with a real asset source, so the clipart export path
(`ApplyClipArtClipsAsync` in `ExportService.cs`) couldn't be live-tested during phase 75's verification
pass. Fixed in phase 76: added `DemoAssetProvider` (a static PNG clipart fixture, registered in
`Ben.Video.Playground/Program.cs`), then live-verified — added the demo clipart + a video clip to a
timeline, exported, and confirmed via extracted frames that the clipart composites correctly over real
video content during its own time window and correctly disappears afterward.

Three real bugs were found and fixed along the way, all previously unreachable because clipart had
never been exercised in the Playground:
- `AssetBrowser.razor` had a double-`else` block (two unconditional `else` branches on the same `@if`
  chain) — the asset grid could never render.
- `ClipArtEditor.razor`'s "Animate position / scale" button had `Width="100%"` set on a `TelerikButton`,
  which has no such parameter — selecting any clipart clip with `AllowMotion` crashed the render tree
  and took down the whole asset browser tab. Fixed with a CSS class instead.
- The asset browser's inner source tab strip (`Demo Clipart` / `My Imported Files`) never selected a
  default tab (`_activeTabId` started empty, matching neither), so neither sub-tab showed content until
  manually clicked.

> Found 2026-08-09 during phase 75 verification. Shipped 2026-08-09 (phase 76,
> `feature/phase-76-clipart-verification`, merged to `develop`). Lives in the separate
> Ben.Video.Editor repo (Github-BenVideo remote).

---

## 34. `VolumeAutomationLane` JS-interop crash under rapid UI interaction — ✅ Fixed (2026-08-10, phase 97)

Observed during phase 75's scripted browser testing (not necessarily reachable at normal human
interaction speed): `VolumeAutomationLane.OnAfterRenderAsync` calls `volumeAutomationLane.js`'s
`init()`, which does `_ensureElements()` → `.querySelector(...)` on an element reference that was
sometimes still `null`, throwing `TypeError: Cannot read properties of null (reading 'querySelector')`.
In Blazor WASM this only breaks that one component's render (not the whole circuit, unlike Blazor
Server), so the app kept working, but the audio track's volume-automation lane was left broken for
the rest of the session. Reproduced twice with scripted rapid-fire add-track/add-clip clicks; unclear
whether a real user's slower interaction pace can trigger the same race. Needs the JS side to guard
against a null element (retry/defer `init()` until the DOM node exists) rather than assuming
`OnAfterRenderAsync` always fires after the element is mounted.

> Found 2026-08-09 during phase 75 verification (scripted browser automation). Lives in the separate
> Ben.Video.Editor repo (Github-BenVideo remote).
>
> Shipped: `volumeAutomationLane.js`'s `init`/`updateKeyframes`/`destroy` now guard against a null
> `svg` element (Blazor resolves an `ElementReference` lazily at JS-call time — a stale reference
> from `Clip` going null while the module's first-time import was still in flight resolved to
> `null`), turning the crash into a silent no-op. Also re-checks `Clip is null` in C# right after the
> import await, closing off the actual race at its source. Live-verified by calling the fixed JS
> module directly with `svg: null` (exactly what a stale reference resolves to) — all three
> functions now return cleanly where `init` used to throw immediately.

---

## 35. Direct on-canvas motion-keyframe editing — bezier handles, resize, type-in values, drag-to-move — 🟡 Partial (panel sync shipped 2026-08-10, phase 98)

Motion keyframes (position/scale/rotation/alpha/shadow, etc.) are currently edited only through the
Properties-panel form (sliders/number fields, add-keyframe-at-playhead button). Wanted: a true
on-canvas editing experience, similar to the Pen tool in Photoshop/Illustrator —
- Click-drag a keyframe's bezier handles directly on the motion path to shape the interpolation curve
  between two points, not just pick an easing preset. — **already existed** (`MotionPathOverlay`,
  pre-dates this item) before this item was even logged; not new work.
- Drag the object itself to a new position on-canvas, with that becoming the keyframe's stored
  position. — **already existed** (`MotionPathOverlay`'s keyframe-dot dragging calls
  `Motion.UpsertKeyframe` directly).
- The user's real, explicit ask when scoping this item (2026-08-10): canvas edits and playhead
  movement should both drive live values in the Properties panel. — **shipped phase 98**.
  `CalloutEditor.razor` now subscribes to `ClipStore.OnChange`/`MotionKeyframeService.OnChanged`/
  `PlaybackService.OnStateChanged` and shows the motion-interpolated X/Y/Opacity/colors (with a
  "● Live" badge) instead of always showing the clip's static values. Live-verified: playhead
  scrubbing shows genuine smooth interpolation between keyframes, and dragging a keyframe dot on
  canvas immediately updates the panel.
- Resize the object directly on-canvas (drag a corner/edge handle) and have that recorded as the
  keyframe's scale value at the current time. — **still open**. Deliberately not bundled into
  phase 98: `MotionFrame.Scale` is a single uniform multiplier but `CalloutControlPointOverlay`'s
  edge handles resize width/height independently, and that overlay has no "which keyframe does
  this affect" concept (unlike `MotionPathOverlay`, which always acts on an explicitly-selected
  keyframe) — needs a real design decision, not a guess.
- Type a value directly (e.g. click a position/size number and edit it inline) instead of only via a
  slider. — still open, not attempted.
- Extend the phase-98 panel-sync treatment to `TextOverlayEditor`/`ClipArtEditor` for parity with
  `CalloutEditor` — **`TextOverlayEditor` done, phase 100** (Size/Position/Shadow badges, live
  values matching `ApplyMotionFrame(TextOverlay, MotionFrame)`). `ClipArtEditor` deliberately
  skipped — its motion keyframes have no effect anywhere currently, see item #46.

> Requested by the user 2026-08-09. Lives in the separate Ben.Video.Editor repo (Github-BenVideo
> remote).

**Superseded by item #57 (2026-08-12)**, which fully subsumes this item's two remaining open
bullets (canvas resize-as-keyframe, type-in values) as phases P3/P5 of a much larger,
Camtasia-class GUI arc — see #57 below.

---

## 36. Dedicated async rendering service + progressive rough/fine preview + per-region timeline progress — ✅ Complete (2026-08-09, phases A-89)

Pull preview rendering out of the Blazor UI thread into its own service that runs asynchronously
alongside the app, rendering timeline clips in the order requested, at the editing (preview) resolution
— not the full export resolution. To avoid long delays on a single request, it should support a
two-pass strategy: a fast, rough render first, then a finer-quality pass afterward, so the preview
becomes usable quickly and then sharpens.

As each region of the timeline renders, the thin status bar above the timeline tracks (from item #13,
shipped phase 69 as whole-timeline binary freshness only — true per-region tracking was explicitly
deferred then as "not honestly buildable yet") should reflect real per-region progress: gray while a
clip's region is queued/not yet rendered, a lighter shade of green while that region is actively
rendering, and full green (filling like a progress bar) as it completes. This is the deferred
per-region half of item #13, now revisited with a concrete architecture (dedicated async render
service) to make it buildable.

Needs design before implementation: how the render service queues/cancels/reorders work as the user
keeps editing (a region being rendered can become stale mid-render if the user edits it again), how
"rough" vs "fine" passes are defined technically for ffmpeg.wasm-based rendering, and how the service
communicates per-region state back to the timeline UI (likely an event/notification pattern, matching
`PlaybackService.OnStateChanged`'s existing style elsewhere in this codebase).

**Design complete 2026-08-09** — full implementation plan in the Ben.Video repo:
`DESIGN-item36-background-render-service.md` (committed to `develop`). Key decisions: stays fully
client-side (second ffmpeg.wasm Web Worker, no native sidecar — but an `IRenderBackend` seam keeps
that open); queue-based single-consumer loop with hybrid priority (explicit requests FIFO, ambient
work playhead-outward); per-region staleness via content signatures (sidesteps phase 69's
"ClipStore.OnChange carries no change info" blocker — `TimelinePosition` deliberately excluded so
drag-reordering never invalidates cached segments); rough pass = same dimensions/fps at
ultrafast/CRF-35 so mixed rough/fine segments stream-copy concat; WORKERFS zero-copy source mounts
+ 256 MB LRU segment cache for memory; five implementation phases A–E, each independently
shippable, with a post-Phase-C reassessment gate.

**Phase A shipped 2026-08-09** (phase 79, `feature/phase-79-render-service-phase-a`, merged to
`develop`): new `Ben.Video.RenderService` class library (zero Blazor dependency — the pure region
model + signature-reconciliation engine, referenced by `Ben.Video.Editor` which adapts `ClipStore`
into it) plus a real per-region gray/green timeline bar, replacing the old whole-timeline
fresh/stale boolean. No second worker yet — after a (still whole-pipeline) Preview render, every
region matching its current signature is marked rendered; edits after that only gray out the
region(s) actually touched. Found and fixed a real pre-existing CSS bug along the way (the stale
bar's color variable resolved to solid white in this theme, not muted gray). 33 new unit tests;
live-verified both transitions in a real browser.

**Phase B shipped 2026-08-09** (phase 80, `feature/phase-80-render-service-phase-b`, merged to
`develop`): `PreviewTimelineAsync` now caches each clip's encoded preview segment keyed by its
region signature (`PreviewSegmentCache`) — editing one clip in a multi-clip timeline no longer
re-encodes every clip on the next Preview click, only the one(s) actually changed. Orphaned cache
entries (a clip's pre-edit state) are evicted and deleted from MEMFS after each render. Scoped down
from the design doc's "instant" stream-copy concat for this phase specifically: investigating the
arg builders found frame rate and audio-stream presence aren't pinned across segments, both real
correctness risks for blind `-c copy` concat — deferred to when that pinning gets built anyway
(phase C/D's rough-pass work), rather than retrofitting a narrower fix under time pressure. Final
concat still re-encodes (cheap, since it's a small number of already-short preview segments, not
original sources). 11 new tests; live-verified via the ffmpeg console log — editing one of two
clips produced exactly one new encode command, the other clip's segment reused untouched. Phase C
(the actual background render worker) is next.

**Phase C shipped 2026-08-09** (phase 81, `feature/phase-81-render-service-phase-c`, merged to
`develop`): a real second, independent ffmpeg.wasm instance (`renderWorkerInterop.js` +
`RenderWorkerService`/`RenderWorkerBackend`) now autonomously renders stale preview regions in the
background — no Preview click needed — via `BackgroundRenderService`'s hybrid-priority queue
(explicit requests FIFO, then stale regions ordered by distance from the playhead), with
pause/resume around Export, discard-stale-result handling for edits landing mid-render, and
back-off on repeated failure. Found and fixed two real gaps: Server-tab-imported clips have no
OPFS entry at all (only the local file-picker import path populates OPFS), needing a MEMFS
byte-copy fallback; and worker-rendered segments could omit an audio stream entirely, which would
have silently broken the stream-copy concat this whole design is building toward (frame rate,
unlike stated in phase B's notes above, turns out to already be pinned — only audio needed
fixing). Also found and fixed a live-reproduced race where a missed wake-up signal could stall the
queue for 20+ seconds, via a bounded idle-poll fallback with a dedicated regression test. 27 new
tests (1180/1180 total passing); live-verified in the Playground — autonomous background rendering
confirmed via distinct console log tags for the second ffmpeg core, both the OPFS-fallback and
pinned-audio paths engaged correctly, and rapid successive edits all picked up within 1-3 seconds
at fast preview quality with no stuck state. Wiring the Preview button to actually consume the
worker's cached segments (`AssembleAsync` + stream-copy concat) is deliberately deferred to a
focused follow-up — see `README-phase-81.md`.

**Follow-up shipped 2026-08-09** (phase 82, `feature/phase-82-preview-background-consumption`,
merged to `develop`): Preview now actually consumes a clip's background-rendered segment when
one's ready — reading its bytes out of the worker's independent MEMFS and writing them into the
main instance's, then registering it in the existing `PreviewSegmentCache` so later clicks hit the
ordinary cache path. Chose immediate fallback (skip straight to the normal synchronous encode) over
a wait-with-timeout scheme when nothing's ready yet, so Preview can never be made slower or
hang-prone by background-render state; a clip that falls through nudges the background queue to
prioritize it next. Found and fixed a real bug along the way: `RenderStatusService.MarkAllCurrentRendered`
was calling `MarkRendered` without a segment name, silently nulling out every region's `SegmentName`
on every Preview completion — meaning consumption only ever worked on the very first Preview click
after a background render, then silently regressed to full re-encodes forever after. Caught via a
unit test before it could cause a confusing live-session false negative. Live-verified: first
Preview click consumed the background segment (zero trim command in the ffmpeg log, only the final
concat, ~90s at full 1080p quality since the background worker had rendered at full quality);
second click hit the resulting cache entry (~6s). Full detail in `README-phase-82.md`. Phases A, B,
and C are now fully wired end to end. Phase D (rough/fine two-pass) and phase E (rollout) remain
open.

**Phase D shipped 2026-08-09** (phase 83, `feature/phase-83-rough-fine-two-pass`, merged to
`develop`): every region now renders rough (ultrafast/CRF 35) before fine (current preview
quality) — the whole timeline becomes watchable fast, then sharpens per-clip, with all rough
passes prioritized ahead of any fine pass. The render bar's fill color now distinguishes the two
passes exactly per the user's spec: muted green fills with rough-pass progress, bright green fills
with fine-pass progress. Assembly also switched to stream-copy concat (`-c copy`, zero re-encode)
whenever every segment is background-pinned — the user's "only re-render where changes occur, not
when putting it together" requirement, made literal at the assembly step too. Two real races found
and fixed before either shipped as a live bug: reading a segment out of the render worker while
it's mid-encode blocks until that encode finishes (ffmpeg.wasm serializes all calls through one
queue) — would have deadlocked the entire "consume rough while fine renders" behavior — fixed by
transferring completed segments to the main instance's MEMFS at job completion instead of at
consumption time; and a fine pass completing mid-Preview-assembly could delete the rough segment
the concat was about to read — fixed with a re-entrant deletion hold around assembly. Also closes
one symptom of item #38 below (unbounded segment memory growth within a session) via orphaned-
segment collection and deletion. Confirmed with the user along the way: the toolbar's Preview
button should become a full-resolution render played in the existing phase-62 popout window,
distinct from the live-editing "Working Window" — scoped as its own phase 84 rather than folded
into this already-large phase. 18 new tests, 1195/1195 total passing. Full detail in
`README-phase-83.md`.

**Phase 84 shipped 2026-08-09** (`feature/phase-84-fullres-preview-popout`, merged to `develop`):
the toolbar Preview button is now the full-resolution/quality output preview confirmed at the end
of phase 83 — it calls `ExportService.ExportAsync(settings, downloadToDisk: false)` (a new mode
sharing 100% of the real export pipeline, just swapping the final download step for an in-memory
blob URL) and plays the result in its own popout window, genuinely separate from the Working
Window. The Working Window's own auto-refresh is now unconditional — the opt-in toggle from phase
65 is gone, fulfilling item #11's original "no explicit render step" ask for real now that
background rendering makes each refresh cheap. Found and fixed two real bugs live: the pre-existing
relocate-popout blanked its content on toggle (component-instance swap dropped the loaded blob URL
— fixed via a new `VideoPreview.DetachWithoutRevoking()` + rehydrate flag); and a cross-component
`OnAfterRenderAsync` timing race (a parent calling `LoadUrlAsync` right after a `VideoPreview`
first mounts isn't guaranteed to run after that child's own JS module finishes initializing) — fixed
by having `VideoPreview` own applying its own deferred load via an internal pending-load queue
instead of relying on any cross-component ordering. Also from live UI feedback in the same session:
default export resolution/fps lowered to 1280×720/24fps (from 1920×1080/30fps) and the Working
Window's own preview-scale default lowered to 75% (from 100%) for faster defaults throughout;
the output window's now-unused quality/fps dropdowns replaced with a compact inline scrub bar;
its tick labels fixed via Telerik's native `TickPosition="None"` (found via DLL reflection — it's
declared on a base class, `TelerikSliderBase<TValue>`, not on `TelerikSlider<T>` itself, feeding a
confirmed-mechanism update into item #30 below); the audio level meter (`DbMeter`) fixed to stretch
to the video's actual height and its unreadable "L R" label removed entirely (it had no CSS at all
before this). Full detail in `README-phase-84.md`.

**Phase E shipped 2026-08-09** (phase 89, `feature/phase-89-item36-phase-e-rollout`, merged to
`develop`): the design doc's final "polish + rollout" phase. `Ben.Web.WebApp` now sets
`options.BackgroundRendering = true` — the real host app gets the background render worker and
rough/fine two-pass preview by default, not just the Playground demos. Added a genuinely new
`EnableRoughPass` toggle (`BackgroundRenderService`, runtime-mutable) — no such on/off existed
before; when off, a stale region renders straight to its fine pass, skipping rough entirely.
Playground's Settings Lab gained checkboxes for `BackgroundRendering`/`EnableRoughPass`/
`PauseBackgroundRenderDuringExport`, which surfaced and fixed a real gap: `VideoEditor.razor` read
those three flags directly from the DI-registered default, completely bypassing the per-page
`EditorOptions` parameter every other setting already respects — meaning the new checkboxes could
never have worked without this fix, and fixing it in turn surfaced a latent bug where the
Full-Featured demo's own options object never set `BackgroundRendering`, silently relying on the
old DI-only behavior (now made explicit on that page). The design doc's "cap" knob (256MB
segment-cache eviction) was **not** built — it was never actually implemented in the first place,
confirmed while investigating this phase, and building it now would duplicate the separate,
still-open item #38. Live-verified end-to-end: importing a clip into the real-host-app-configured
demo triggered two separate ffmpeg core loads (main + background worker) and the exact expected
rough-then-fine command sequence, with no explicit Preview click. Full detail in
`README-phase-89.md`.

> Requested by the user 2026-08-09. Depends on / revisits items #12 (preview rendering pipeline,
> shipped phase 64) and #13 (render-progress bar, shipped phase 69 with per-region tracking
> explicitly deferred). Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).

## 37. Track row border doesn't extend to the full timeline width — ✅ Fixed (2026-08-09, phase 92)

Found by the user 2026-08-09 while looking at a 13.8s imported clip on a wide/zoomed timeline: each
track row's bottom border (and the row's own background/hit area generally) stops at roughly the
initial visible-viewport width instead of extending across the full scrollable timeline width. The
clip content itself and horizontal scrolling both work correctly — confirmed live via
`getBoundingClientRect()`: `.bv-track__items` (the clip-holding area) correctly reports its full
content width (2219px for a 13.8s clip at the active zoom level, matching
`Timeline.CanvasWidth(Clips.TotalDuration)`), but its parent `.bv-track` (the flex row carrying the
`border-bottom` — `VideoTimeline.razor.css`) only reports ~939px, capped near the viewport's visible
width. Root cause looks like `.bv-track__items { overflow-x: visible; flex: 1; min-width: 0 }`
(`VideoTimeline.razor.css` line ~392) letting its content overflow visually without the overflowing
width counting toward `.bv-track`'s own flex-computed box size, even though the more distant
scrollable ancestor (`.bv-timeline__tracks`, `overflow-x: auto`) does correctly expand to allow
scrolling that far. Every track row is affected equally (video and audio, populated and empty) — not
specific to audio tracks, just easiest to notice on an empty one since there's no clip content
visually distracting from the short border. Needs its own pass: the fix (likely giving `.bv-track` a
`width: max-content` or equivalent so its own box actually grows to match its overflowing child) has
to be checked against the several other elements in this timeline that are positioned via the same
width formulas (ruler, playhead, snap guide, render-progress bar) so nothing drifts out of alignment.

> Shipped: `.bv-track` got `width: max-content; min-width: 100%` — lets it size from its children's
> real content width (already correct, via the existing child min-width) instead of being clamped by
> default flex cross-axis stretch, while still filling the visible area when there's nothing to
> overflow. Applies equally to the item #39-era overlay row, which reuses the same `.bv-track` class.
> Live-verified: narrowed the viewport below the clip's canvas width and confirmed via
> `getBoundingClientRect()` that `.bv-track`'s width now matches `.bv-timeline__tracks.scrollWidth`
> exactly (previously clamped to `clientWidth`), and the border-bottom now visibly extends flush to
> the right edge after scrolling to the end.

**Intended behavior, per the user 2026-08-09**: every lane's background should extend across the
full shared timeline width (i.e. `Clips.TotalDuration`/`Timeline.CanvasWidth`), not just each lane's
own content. A gap between two clips mid-timeline is just empty background within that shared width
— nothing wrong, no separate fix needed there. But the shared width itself must stay dynamically
correct: if the *last* clip on the timeline gets trimmed shorter, every lane should shrink to match
the new, shorter overall extent (since `Clips.TotalDuration` presumably already recomputes correctly
today — worth confirming as part of this fix, not assuming).
Lives in the separate Ben.Video.Editor repo.

## 38. Long-form project memory budget — e.g. three 20-minute 1080p clips (✅ Complete — in-browser mitigations (A-D) + full native sidecar (phases 121-124: foundation, render routing, real exports) shipped; user confirmed 2026-08-12 no further phase is needed, since phase 124's hybrid design already collapsed the planned overlay-export split)

Raised by the user 2026-08-09 while item #36 phase D was being planned: what happens when someone
edits three 20-minute 1080p clips? Honest answer: today that breaks. The whole pipeline lives in
ffmpeg.wasm's 32-bit WASM memory (2–4 GB hard ceiling), and ~60 minutes of 1080p source is roughly
1–3.5 GB of bytes before any encode output. Known contributing gaps, in priority order:

1. **Server/media-library imports land in main-instance MEMFS, not OPFS** (found in phase 81) —
   local file-picker imports are already fine (OPFS + zero-copy WORKERFS mounts, no WASM memory
   cost), but server-imported clips occupy WASM memory outright, so large server clips can blow the
   ceiling at import time. Root fix: make `ClipBrowser.ImportFromLibraryAsync` write to OPFS like
   the local path does, then let everything mount from there.
2. **The item #36 design's 256 MB segment-cache cap + LRU eviction (design doc §8) was never
   implemented** in phases C/82 — background-rendered preview segments (worker MEMFS), their
   transferred copies (main MEMFS), and `PreviewSegmentCache` entries all accumulate unbounded
   within a session. For long clips at preview resolution these are tens-to-hundreds of MB each.
3. **Full-length outputs**: a full-quality export (or the planned full-quality in-memory Preview,
   phase 84) of 60 minutes of 1080p is itself a 1–2 GB MEMFS file before the blob/download step —
   a ceiling today's Export already has for long content; not newly introduced, but worth solving
   in the same pass (e.g. streaming output to OPFS and creating the blob URL from the OPFS File
   handle rather than a MEMFS byte copy).

Longer-term: this is exactly the scenario the item #36 design's rejected-for-now **native sidecar
backend** (real ffmpeg outside the browser, behind the existing `IRenderBackend` seam) was kept
open for — in-browser mitigations raise the ceiling but cannot remove it. Lives in the separate
Ben.Video.Editor repo.

> **Planned 2026-08-12**: full design doc committed to `develop` —
> `DESIGN-item38-long-form-memory.md` in the Ben.Video.Editor repo. Covers the full arc: phases
> A (import OPFS parity) → B (WORKERFS-mounted sources, the headline memory win) → D (export-output
> memory flattening) → C (the item #36 §8 segment-cache cap+LRU, finally built) → E/F/G (a native
> local sidecar render backend + full-export orchestration, preserving the all-local
> files-never-leave-the-machine promise via a `127.0.0.1`-bound companion process). Implementation
> starting with phase A.

> **Phase A shipped 2026-08-12** (phase 117): server/media-library imports now write to OPFS and
> set `OpfsExt`, matching the local-picker path — fixes a reload bug for free and unblocks phase B.

> **Phase B shipped 2026-08-12** (phase 118) — the headline memory win: source clips are now
> zero-copy WORKERFS-mounted into the main ffmpeg instance instead of copied into MEMFS, via a new
> `SourceMounter` service. Two real bugs only surfaced by live verification against a real
> ffmpeg.wasm instance, both found and fixed: the mount directory was nested
> (`/sources/{id}`, but ffmpeg.wasm's `createDir` is non-recursive) so every mount silently failed
> and fell back to a full copy; and `getMetadata`/`extractThumbnails` derived their temp output
> filenames from the input path, which broke once mounting actually started working. Verified
> end-to-end: import, preview, and a full export all succeed against a genuinely mounted source.

> **Phase D shipped 2026-08-12** (phase 119) — export-output memory flattening: native ffmpeg.wasm
> `rename` replaces the old read/write/delete round trip, and eager intermediate deletion was
> extended to every remaining pipeline stage that didn't already have it (per-clip segments,
> cross-track transition intermediates, the clipart compositing loop and its SVG frame sequences,
> pre-audio-mix video and per-clip audio segments, pre-chapter-embed video). Real bug found and
> fixed along the way: the watermark stage runs *after* the pipeline's rename step, so its input
> file was never tracked for cleanup at all — a watermarked export previously leaked the full
> pre-watermark file for the rest of the process's lifetime. The finished output now moves from
> MEMFS into a new OPFS `bv-exports/` area entirely JS-side (no bytes cross into .NET), and both
> download and the full-quality Preview popout read directly from that OPFS copy via a zero-copy
> blob URL instead of a second MEMFS read; falls back gracefully to the old direct-MEMFS path if
> OPFS isn't available. Verified end-to-end: full export completed with a new byte-size readout,
> the OPFS export area confirmed empty afterward (download path cleans up), and a separate
> full-quality Preview run left its own export file in place for the popout as designed, playing
> back a genuine blob URL.

> **Phase C shipped 2026-08-12** (phase 120) — segment-cache cap+LRU, the item #36 §8 design that
> was specified but never built. New pure `SegmentBudget` tracks size/last-touch/pass per
> background-rendered segment; `BackgroundRenderService` evicts least-recently-touched segments
> once a configurable cap (default 256 MB, new `BackgroundRenderMemoryCapMb` option) is exceeded —
> Rough-pass segments before Fine, never the region under the playhead, never a region mid-render,
> deferred entirely while the existing Preview-assembly deletion hold is active. Also fixed a real,
> previously-flagged leak: the render worker's OPFS-unavailable fallback path copied a source into
> its own MEMFS and never deleted it. Deliberately scoped down from the original plan — the budget
> covers background segments only, not `PreviewSegmentCache` (merging them correctly, given a
> segment can be tracked in both simultaneously today, is a real design problem of its own that
> outweighs the benefit, since `PreviewSegmentCache` isn't the actual unbounded-growth problem).
> 24 new tests, 1406/1406 passing; two of them initially failed for a genuine reason (the test
> harness's default playhead accidentally protected the very region being tested — a test-setup
> bug, not an eviction-logic bug) before being fixed. Live-verified the new Settings-Lab control
> wires end-to-end into the generated snippet, and background rendering runs cleanly under a 1 MB
> cap with zero console errors.
>
> **This closes every in-browser mitigation in the plan (phases A–D).** What remains is the native
> sidecar (phases E–G).

> **New hard requirement from the user (2026-08-12): "the sidecar MUST be secure. There can be no
> case where it can be hacked or used to install malware or ransomware or a virus."** This
> invalidated two pillars of the original sidecar design and triggered a full re-plan (see
> `DESIGN-item38-long-form-memory.md` §5, to be rewritten with phase 122): (1) the browser will
> never send ffmpeg argv/filter strings to the sidecar — only typed, structured job specs, which
> the sidecar turns into argv itself using the same shared code the browser uses, so nothing
> string-shaped that could be an injection ever crosses the wire; (2) a one-time pairing code
> (user decision: yes, over a frictionless but weaker Origin-only design) adds a second,
> independent layer of auth beyond the browser's Origin header. User also decided: ship v1
> unsigned (documented Gatekeeper/SmartScreen bypass instructions), with real code signing
> recorded as a requirement before any real user distribution. New phase numbers 121–125 (E-prep,
> E, F, G1, G2) replace the original E/F/G split.
>
> **Phase 121 shipped 2026-08-12** — pure extraction, zero behavior change. New `Ben.Video.Core`
> project (plain classlib, zero package references) now holds `ExportArgBuilders` and its full
> transitive closure (effect plugins, clip effects, the model types they touch) moved out of
> `Ben.Video.Editor`, whose Razor SDK + licensed Telerik package a non-Blazor sidecar process
> can't reference. Original namespaces kept on every moved type, so zero `using` changes were
> needed anywhere in Editor or its tests. Found+fixed a real bug in passing:
> `EscapeMetadataValue` (chapter titles in exported ffmetadata) escaped `\=#;` per spec but not
> newlines, letting a title inject a fabricated second chapter block. 1407/1407 tests passing,
> live-verified end-to-end (import → effect pipeline → full export) with the code now split
> across two assemblies.
>
> **Phase 122 shipped 2026-08-12** — the sidecar app itself: new `Ben.Video.Sidecar` (loopback-only
> ASP.NET minimal API) with the full layered security stack the redesign called for — Host-header
> validation (DNS-rebinding defense), a server-side Origin allowlist enforced on *every* request
> (not just CORS preflight), PNA-aware preflight handling, a one-time pairing token
> (`X-BenVideo-Sidecar-Token`, constant-time SHA-256 compared, rate-limited on repeated failure),
> and ffmpeg supply-chain SHA-256 pinning verified at every startup. ffmpeg itself runs as a
> properly sandboxed child process (`UseShellExecute=false`, real argv array never a shell string,
> per-job working directory, kill-tree timeout) — deliberately process-isolated rather than
> P/Invoked as an in-process library (a user question mid-phase), so a crash or bug in ffmpeg can't
> take down the sidecar process holding the pairing token. Ships detection + pairing + the
> source-upload cache only — **zero render-path behavior change**, job endpoints are phases
> 123-125. Two real bugs found: a `HEAD /v1/sources/{id}` response missing `Content-Length` that
> hung real HTTP/1.1 clients indefinitely, caught only by a genuine separate-process/real-socket
> test (`WebApplicationFactory`'s in-memory `TestServer` can't model real HTTP wire framing); and
> the phase-121 chapter-title newline-injection fix, confirmed still solid. 1407/1407
> `Ben.Video.Tests` + 50/50 new `Ben.Video.Sidecar.Tests` passing. Live-verified full pairing flow
> against a real running sidecar process from the Playground app: unpaired detection, pairing
> success, token persistence across a hard reload with no re-prompt, and correct fallback to
> disconnected when the sidecar process is killed.
>
> **Phase 123 shipped 2026-08-12** — the first phase that routes real render work through the
> sidecar. Browser submits a typed `SegmentRenderSpec` (never argv, never a filter string); the
> sidecar's new `ArgvFactory` rebuilds it into real ffmpeg argv using the *exact same*
> `ExportArgBuilders` code the wasm render worker already calls, so a native segment and a wasm
> segment are byte-compatible under the `bgseg_`-prefixed stream-copy concat gate regardless of
> which backend produced them. New `FallbackRenderBackend` picks the sidecar when connected/paired
> and falls back to the existing wasm worker otherwise, re-checked fresh per job — `NativeSidecar`
> stays off by default, so nothing changes for anyone not opted in. Two deviations from the
> original plan, both found while building/verifying it: dropped the planned SSE progress stream in
> favor of plain ~400ms status polling (Blazor WASM's `HttpClient` can't stream a response without
> a browser-only toggle that a plain `net10.0` Razor class library can't reference); and a real bug
> — `NativeSidecarService` never noticed a dead connection on its own (only an explicit re-pair
> ever updated its state), so a killed sidecar would silently strand every future job against it
> forever instead of falling back. Fixed with `ReportConnectionLost()`, called from a deliberately
> broad catch-all (the source-upload step goes through a JS `fetch()`, not the C# `HttpClient`, so
> its failures are a different exception type entirely). Live-verified end to end against a real
> sidecar process from the real Playground UI (OS file picker unavailable in this sandboxed browser
> tool, so a `File`/`DataTransfer` injection fed the same `<input type="file">` change event a real
> picker would, without touching any product code): a real clip's Rough and Fine background-render
> passes both completed entirely through the sidecar (upload, submit, poll, result, cleanup, with
> the second pass correctly reusing the already-cached source), then — after the
> `ReportConnectionLost` fix — killing the sidecar mid-session produced exactly one failed request
> against it, an immediate chip flip to "No sidecar," and zero further wasted requests for the rest
> of the session, with the queue completing entirely via wasm. 1412/1412 `Ben.Video.Tests` (+5) and
> 90/90 `Ben.Video.Sidecar.Tests` (+40) passing.
>
> **Phase 124 shipped 2026-08-12 — real exports, but a different design than planned.** Mid-build,
> the user asked whether the export pipeline could be broken into tasks — sidecar renders what it
> can, wasm handles the rest, wasm assembles the final file — instead of the plan's original
> approach (sidecar replicates `ExportService`'s *entire* pipeline for overlay-free timelines only,
> falling back 100% to wasm the instant a timeline has any overlay). Before writing code, an agent
> mapped `ExportService`'s real 1340-line pipeline in detail: per-clip trim/encode is the dominant
> CPU cost for long-form content, everything else (concat, transitions, overlay compositing, audio
> mix, chapters, watermark) runs on small already-encoded intermediates. So only the per-clip step
> is offloaded — reusing phase 123's segment-render machinery almost unchanged (`SegmentRenderSpec`
> gained a third `RenderPassKind.Export` carrying explicit quality settings instead of deriving them
> from a hardcoded rough/fine preset) — and every downstream stage stays exactly the existing wasm
> code, unaware of which backend produced any given segment. New `NativeClipEncoder` never throws:
> any failure falls straight through to the unchanged wasm path for that one clip, so a native
> failure never fails the export. This collapses the plan's G1/G2 (no-overlay vs. overlay) split —
> nothing gates on overlay presence anymore, since overlay compositing runs after segment trimming
> and was never touched. Whether a further phase is still needed given that collapse is now an open
> question, not a decided next step. 1412/1412 `Ben.Video.Tests` (unchanged) + 108/108
> `Ben.Video.Sidecar.Tests` (+17) passing. Live-verified a real export end to end through the
> Playground UI against a real sidecar process — new job traffic exactly at export time, source
> reused from the earlier preview render (no re-upload), genuine "Export Complete" success.

## 39. New callout doesn't land at the playhead — ✅ Fixed in full (2026-08-09, phases 85 + 87)

Found by the user 2026-08-09 while live-verifying a callout animation scenario. Two related
timeline issues:

1. **✅ Fixed.** A new callout should appear at the playhead's current time, but landed far from
   it. The initial hypothesis (an async-seek race in `Playback.State.CurrentTime`) was
   investigated and disproven live — `CurrentTime` was confirmed genuinely `0` at the moment of
   adding, yet the callout still landed away from it. Real root cause: a **layout** bug, not a
   timing bug. `VideoTimeline.razor` rendered every track item — video/audio clips *and*
   callouts/text/clip-art — in one sequential flex row spaced by
   `Math.Max(0, (item.TimelinePosition - runningEndSeconds) * PxPerSecond)`; the `Math.Max(0, ...)`
   clamp silently discarded any negative "overlap," so an overlay item whose `TimelinePosition`
   fell inside an already-placed clip rendered packed right after it instead of at its own
   position. Fixed by giving every Callout/TextOverlay/ClipArt item its own stacking lane
   (`TrackItem.LayerIndex`, insertion-ordered, independent of `TimelinePosition`) — which the user
   specified should go further than a one-row fix: "everything added gets its own layer, each
   layer higher than any added before it" (confirmed via AskUserQuestion over a minimal
   single-shared-row alternative). Export compositing order updated to match; a
   `NormalizeLayerIndices()` backward-compat pass keeps projects saved before this field existed
   from collapsing every overlay onto one lane. See `README-phase-85.md` in the Ben.Video.Editor
   repo.
2. **✅ Fixed (phase 87).** Default zoom required horizontal scrolling to see a ~13 second clip in
   full, even at a normal (non-4K) window size. Root cause: `TimelineViewState.ZoomScale` started
   at a fixed `1.0` regardless of content length; the "Fit" button's own fit-to-width math was
   already correct, it just never ran automatically. Fixed by auto-fitting once, the first time
   real content appears (empty timeline → has a duration), without ever overriding a user's own
   manual zoom on later edits. Live-verified: a 13.9s clip now renders with zero horizontal
   overflow (`scrollWidth === clientWidth`) at a computed `1.1×` zoom, no scrolling needed. See
   `README-phase-87.md` in the Ben.Video.Editor repo.

Lives in the separate Ben.Video.Editor repo.

## 40. MotionKeyframeEditor doesn't refresh after adding a layer's first keyframe — ✅ Fixed (2026-08-09, phase 88)

Found 2026-08-09 while verifying item #32/phase 86. Clicking "Add keyframe at playhead" from the
empty "No keyframes yet for this layer" state genuinely added the keyframe to
`MotionKeyframeService` (confirmed — closing and reopening the panel immediately showed it), but
the panel itself kept showing the empty state until closed and reopened. Root cause:
`MotionKeyframeEditor._path`/`_kf` were only recomputed in `OnParametersSet` (fired by the *parent*
passing new `LayerId`/`KeyframeIndex` parameters); the component subscribed to
`Playback.OnStateChanged` (for the mid-interpolation warning, item #21) but never to
`MotionKeyframeService.OnChanged`, so its own internal `AddKeyframeAtPlayhead()` call updated the
service but never re-rendered itself with the fresh state. Fixed by subscribing to
`Motion.OnChanged`, matching the existing `Playback.OnStateChanged` pattern, and extracting the
field-refresh logic into a reusable method both handlers call. Live-verified: "Add keyframe at
playhead" now shows the real keyframe editor immediately, no close/reopen needed; delete and re-add
still behave correctly. See `README-phase-88.md` in the Ben.Video.Editor repo.

---

## 41. Properties panel should show live per-frame interpolated values, not just keyframe values — ✅ Fixed (2026-08-10, phase 98)

As the playhead moves or the video plays, values shown in the Properties panel (and elsewhere) for
an animated layer should update on a per-frame basis to reflect the actual interpolated value at
that point in the show — e.g. if a layer's X position animates from keyframe A to keyframe B, and
the playhead sits partway between them, the panel should show the true in-between value at that
frame (like "X = A+2"), not just the value at the nearest keyframe. Applies to any item currently
shown in the Properties panel that has animatable/keyframed properties (position, size, color,
shadow, etc. via the motion-keyframe system).

Shipped for `CalloutEditor` as part of phase 98 (see item #35) — subscribes to
`PlaybackService.OnStateChanged` and overlays the evaluated `MotionFrame` on the static field
values, badged "● Live". Live-verified: scrubbing the playhead between two keyframes showed
genuine smooth interpolation, not snapping. Extended to `TextOverlayEditor` in phase 100
(Size/Position/Shadow badges). `ClipArtEditor` deliberately skipped — see item #46.

> Requested by the user 2026-08-09. Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).

---

## 42. Overlay timeline rows need named lanes with correct height — ✅ Fixed (2026-08-10, phase 94)

Found by the user 2026-08-10 while looking at a callout and text overlay stacked in their own
timeline rows (item #39's per-item overlay lanes). Each overlay row is a fixed 40px tall
(`.bv-track__overlays`, `VideoTimeline.razor`), but the actual rendered chip content is taller than
that — the user specifically called out the callout's row, then confirmed "same with the text lane."
The row should be an actual **named lane** (not just an anonymous stacked row) sized to fit its
content instead of clipping/overflowing a fixed 40px height.

> Requested by the user 2026-08-10, found while live-verifying item #28 (drag-to-extend trim
> handles). Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).
>
> Shipped: root cause was `.bv-clip-chip` never setting `box-sizing: border-box`, so
> `height: 40px` + padding + border rendered at 50px — 10px taller than the 40px slot each item
> gets, bleeding into the next row. Fixed with `box-sizing: border-box` (per the user's own
> suggested option — shrink the chip to fit, rather than growing every row). The gutter (previously
> a blank spacer) now shows a per-row named-lane label — icon + type name ("Callout"/"Text"/
> "Clipart") — mirroring a real track's own label. Live-verified with two simultaneous text
> overlays: both chips measured exactly 40px, both lane labels landed exactly 40px apart, no
> overlap, still one row per item (item #39's model unchanged).

---

## 43. Callout/text overlays don't appear in the small "Timeline Preview" thumbnail — ✅ Fixed (2026-08-10, phase 95)

Found by the user 2026-08-10 alongside item #42: scrubbing to a point where a callout and text
overlay are both active, neither renders in the small "Timeline Preview" scrub thumbnail near the
top of the editor — only the raw video frame shows. Confirmed live: no callout/text-related DOM
elements exist anywhere inside that preview area at all (not a z-order/stacking bug — they're
simply not composited into that specific preview surface). Unclear whether this is a by-design
limitation of that lightweight scrub thumbnail (as opposed to the full-quality "Preview" popout,
which runs the real ffmpeg compositing pipeline) or a genuine gap — needs scoping before starting.
The user suggested possible directions (fade the video out before the overlay, or reorder video
below the overlay in z-order) but those assume a compositing/z-order cause that hasn't been
confirmed yet.

> Requested by the user 2026-08-10, found while live-verifying item #28. Confirmed unrelated to
> item #28's actual code change (`git diff --stat` showed only `VideoTimeline.razor`, no preview/
> compositing files touched). Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).
>
> Investigation first confirmed the full-quality "Preview" popout already composites overlays
> correctly (real ffmpeg SVG-rasterization passes, verified via its command log) — the gap was
> specifically the Working Window (small thumbnail + main editing canvas share one component),
> which only ever drew a position-handle dot, never the real visual, for whichever item was
> selected. Also resolved the z-order question directly: the video and text track's row order in
> the timeline *panel* has no bearing on compositing order — overlays are, by design, always
> layered on top of the video in the real render, not a bug. Shipped: new `LiveOverlayPreview`
> component reusing the exact same SVG-generation code the real export uses
> (`CalloutShapeRenderer`/`TextOverlayRenderer`) plus the same motion-frame interpolation and
> fade-alpha logic `ExportService` uses for animated overlays, so the live preview matches the real
> render pixel-for-pixel — including mid-fade opacity and interpolated motion-path position. Gated
> on `ShowWorkingWindowControls` so it never double-renders next to the full-quality popout. Found
> and fixed a real bug during verification: the first version only recomputed on `CurrentTime`
> changes, so a newly-added overlay never appeared until the next timeupdate tick — fixed by
> subscribing directly to `ClipStore.OnChange`/`MotionKeyframeService.OnChanged`. Live-verified via
> direct SVG/DOM inspection: correct 0.000→1.000 opacity across the fade-in window, a real
> `<rect>` with fill/stroke/shadow for a callout, and confirmed no duplicate overlay layer on the
> full-quality popout.

---

## 44. Resizable and collapsible timeline row heights — ✅ Fixed (2026-08-10, phase 96)

Timeline track rows should be resizable (drag to make a row taller/shorter), and collapsible down to
just the height of the row's name text in the track header, to reclaim vertical working room. The
collapse toggle should be a small button next to the existing three-dot track-options button in each
row's left-side header, not a separate control elsewhere. Collapsing is purely visual (row height
only) — doesn't affect the row's content or data.

> Requested by the user 2026-08-10, found while live-verifying items #42/#43. Lives in the separate
> Ben.Video.Editor repo (Github-BenVideo remote).
>
> Shipped: reused `ResizableDivider` (already used for the preview-panel divider) for the drag-resize
> handle between rows — no new drag mechanics, just a new per-track clamp-and-store callback. New
> collapse toggle button sits immediately before the existing "⋮" menu button as requested. Both are
> purely visual, UI-only state — untouched tracks get no inline style override at all, so the default
> CSS `min-height:56px` behavior is completely unaffected for any track nobody has touched, avoiding
> any risk of a phase-94-style content-overflow regression. Live-verified: collapse toggles 57px↔29px
> correctly; resize-drag grew a row from 57px to 98px matching the exact pixel math; collapsing a
> resized row correctly overrides to 28px while preserving the custom height underneath for when it's
> re-expanded.

---

## 45. Media Library card thumbnails render at full native size instead of the intended small box — ✅ Fixed (2026-08-10, phase 99)

Server-tab (and Video/Audio/Image tab) media thumbnails in the Media Library rendered at their
native image resolution (e.g. 600×600px) instead of the intended small thumbnail box, blowing out
the panel layout.

Root cause was bigger than the thumbnail: `ClipBrowser.razor`'s four card grids are built with raw
`RenderTreeBuilder` (`__builder.OpenElement(...)`) calls instead of declarative Razor markup, so
Blazor's compile-time CSS-isolation scope-attribute injection — which only recognizes elements in
the parsed markup syntax tree — never stamped those elements with the component's scope attribute.
Every rule in `ClipBrowser.razor.css` targeting `.bv-clip-card*` was silently dead for all four
grids (not just thumbnail sizing — flex layout, padding, border-radius, hover/selected states too).

> Requested by the user 2026-08-10, found while working item #35. Lives in the separate
> Ben.Video.Editor repo (Github-BenVideo remote).
>
> Shipped: added a `CssScope` const holding the file's real scope value (confirmed live via
> devtools against a normally-authored element in the same file) and manually stamped it onto
> every native-HTML element built via `__builder` across all four grids. Live-verified: Server-tab
> thumbnail went from 600×600px to the intended 64×40px with `object-fit:cover`, and the whole card
> row now shows correct `display:flex`/`padding`/`border-radius`.

---

## 48. "Add to timeline" from the Video library tab duplicates a clip's Id instead of cloning it — ✅ Fixed (2026-08-10, phase 109)

Found 2026-08-10 while debugging item #25's snap-while-dragging work — a confusing "snap targets
computed as empty" symptom traced back to this. `ClipBrowser.AddToTimelineAsync(VideoClip clip)`
passes the *same clip object* (same `Id`) up to `VideoEditor.AddClipToTimeline`, which adds it
directly to the target track's `Items` list with no cloning and no new `Guid`. The "Video" tab in
the Media Library lists clips from `Clips.AllVideoClips` — i.e. *every* video clip already known to
the app, including ones already placed on the timeline. Clicking "Add to timeline" on a card for a
clip that's already on the timeline (the normal case once you've imported it once) created a
**second timeline entry sharing the identical `Id`** as the first, rather than either doing nothing
(it's already there) or creating a genuine duplicate with a fresh Id.

This was a real identity bug with broad blast radius, since `Id` equality is used pervasively
across the codebase (selection, removal, snap-target exclusion, undo commands, motion-keyframe
association, etc.) — any of those could misbehave silently when two timeline items shared one Id.

> Found while working item #25 (`feature/phase-103-snap-while-dragging`). Lives in the separate
> Ben.Video.Editor repo, `Components/ClipBrowser.razor` (`AddToTimelineAsync`) and
> `Components/VideoEditor.razor` (`AddClipToTimeline`).
>
> Fixed as a direct prerequisite of item #51 (phase 109, `feature/phase-109-three-point-editing`,
> merged to `develop`, pushed) — discovered while scoping #51 that this app has no separate
> unplaced-clip library at all (every import lands straight on the timeline; the Video tab is just
> a view of already-placed clips), which is exactly what made this bug possible. `AddClipToTimeline`
> now clones (new `Id`, independent copies of `ThumbnailUrls`/`VolumeAutomation`/`AppliedEffects`)
> whenever the incoming clip is already present in `Clips.AllVideoClips`. Live-verified: added the
> same clip to the timeline twice via its library card, confirmed via `data-item-id` that the two
> resulting chips have genuinely distinct Guids, and that removing/trimming one has no effect on
> the other.

---

## 46. ClipArt motion-keyframe animation has no effect at export or in preview — ✅ Fixed (2026-08-10, phases 101-102)

`ClipArtEditor.razor` has a working "⏱ Animate position / scale" button (when
`Clip.Settings.AllowMotion`) that lets a user create motion keyframes for a clipart layer via the
same generic `MotionKeyframeService`/`MotionKeyframeEditor` that Callout and TextOverlay use
(`OnAnimate.InvokeAsync((Clip.Id, "ClipArtClip"))`, and `VideoEditor.razor`'s own
double-click-to-add-keyframe handler works generically for any layer type). Keyframes get created
and stored fine. But nothing downstream ever reads them for ClipArt:
- `ExportService.ApplyClipArtClipsAsync` never calls `Motion.Evaluate` — the raster/static overlay
  branch uses only the clip's static `X`/`Y`/`Width`/`Height`, and the SVG branch
  (`SvgAnimationExporter`) animates via a completely separate `ClipArtClip.ControlPoints`
  mechanism, unrelated to `MotionKeyframeService`.
- `LiveOverlayPreview` (phase 95) only handles `CalloutClip`/`TextOverlay` — ClipArt isn't in its
  type filter at all, so it doesn't show live in the small preview either.

Net effect: adding motion keyframes to a clipart layer today is a dead end — no visible effect
anywhere, export or preview. Either wire `Motion.Evaluate` into `ApplyClipArtClipsAsync` (mirroring
`ApplyMotionFrame` for Callout/TextOverlay) and into `LiveOverlayPreview`, or remove/hide the
"Animate" button for ClipArt until it's real.

> Phase 101 (`feature/phase-101-clipart-motion-panel-sync`, merged to `develop`, pushed):
> `ApplyMotionFrame(ClipArtClip, MotionFrame)` added (mirrors the Callout/TextOverlay overloads —
> X/Y direct, Scale multiplies Width/Height respecting the -1 aspect-ratio sentinel, Alpha
> multiplies Opacity), 5 new unit tests, plus `ClipArtEditor` Properties-panel live-value sync
> (same pattern as phases 98/100) — live-verified: Position/Size/Opacity all show "● Live" with
> correct interpolated values. Extending `LiveOverlayPreview` turned out to require solving a
> bigger, separate problem first — see item #47 — so that part is deferred.
>
> Phase 102 (`feature/phase-102-clipart-export-animation`, merged to `develop`, pushed): the actual
> export-time fix. New `RasterClipArtAnimationExporter` + `rasterClipArtRenderer.js` render one
> full-canvas PNG per output frame (source image decoded once, redrawn per frame at the
> Motion-Evaluate'd geometry) instead of trying to express the full easing/bezier math as ffmpeg
> time-expressions — mirrors `SvgAnimationExporter`'s proven pattern. `ApplyClipArtClipsAsync` now
> checks `Motion.HasPath` before falling into the old static-overlay branch. Covers "simple SVG
> without control points" too, for free (same image-decode path handles SVG blobs fine). Verified
> in three independent parts rather than one end-to-end pixel-sampled export — see
> `README-phase-102.md` / [[project_video_editor_phase102_clipart_export_animation]] for exactly
> why (a timeline-drag environment limitation, not a fix limitation) and what each part proved.
> SVG-clipart's own position/scale-path (as opposed to its existing `ControlPoints` animation)
> remains a narrower, documented gap — not attempted. Lives in the separate Ben.Video.Editor repo.

---

## 47. ClipArt has no live visual representation in the Working Window preview at all — ✅ Fixed (2026-08-11, phase 112)

Found 2026-08-10 while working item #46. Unlike `CalloutClip`/`TextOverlay` (which render as
self-contained SVG strings computed purely from clip properties, no asset loading needed),
`ClipArtClip` references an external asset (raster image or SVG file) cached in OPFS by
`AssetId`. There is currently no code path anywhere that resolves a placed `ClipArtClip`'s cached
asset into a displayable blob URL — `ClipArtControlPointOverlay` (the on-canvas editing overlay)
only draws resize/position handles, never the actual image; `VideoTimeline`'s clip chip shows a
generic 🖼 emoji icon, not a real thumbnail; `LiveOverlayPreview` (phase 95, item #43) doesn't
handle `ClipArtClip` at all. The asset only ever gets rendered for real at export time (via
`ExportService.WriteClipArtToMemFsAsync` + ffmpeg overlay) and in the full-quality Preview popout
(which shares the export pipeline) — so today, a clipart layer is entirely invisible in the raw
Working Window canvas, motion or no motion.

Fixing this needs a new OPFS-asset-to-blob-URL resolution helper (read via
`OPFSService.ReadAsJSFileAsync`, then a small JS interop `URL.createObjectURL` call), cached per
clip/asset so it's only resolved once, with `LiveOverlayPreview` (or a new sibling component)
rendering an absolutely-positioned `<img>` (raster) or inlined SVG markup at the clip's current
(possibly motion-interpolated) X/Y/Width/Height/Opacity — the same general shape as phase 95's fix
for Callout/TextOverlay, but with an added async asset-loading step neither of those needed.

> Found while scoping item #46's `LiveOverlayPreview` extension — deliberately not built as a side
> effect of that fix, since it's a separate, larger, pre-existing gap unrelated to motion
> specifically. Lives in the separate Ben.Video.Editor repo.

> Found 2026-08-10 while investigating whether to extend phase 98's Properties-panel live-value
> sync to `ClipArtEditor` — deliberately not built on top of this, since there'd be nothing real to
> sync. Lives in the separate Ben.Video.Editor repo.

> **Shipped on `feature/phase-112-clipart-live-visual`, merged to `develop`.** `LiveOverlayPreview`
> now also resolves and renders active `ClipArtClip`s: raster formats (Png/Avif/WebP/Gif) via a new
> OPFS-to-blob-URL path (`opfsReadAsBlobUrl` + `OPFSService.ReadAsBlobUrlAsync`), SVG format via the
> existing `ReadAsTextAsync` inlined directly and stretched to fill via a new `SvgStretchHelper`
> (since `ClipArtClip.Width`/`Height` are independent, non-aspect-preserving fractions — matching
> the export overlay filter's own non-uniform ffmpeg `scale`). Resolution is async and cached per
> `AssetId` (not per clip instance — two placements of the same asset resolve it once), with an
> in-flight guard against duplicate concurrent resolves. Motion-path interpolation reuses the
> existing `ExportArgBuilders.ApplyMotionFrame(ClipArtClip, ...)` overload, same as Callout/
> TextOverlay already do. 6 new unit tests for `SvgStretchHelper`; full existing suite (1317 tests)
> stayed green.
>
> **Deliberately excluded: `Rotation` and `TintColor`.** Found while scoping this fix that *neither*
> is actually applied anywhere in the real export pipeline today — both are copied between models
> (`ClipStore`/`ProjectService` serialization) but never fed into an ffmpeg filter or the
> animated-raster canvas renderer (`RasterClipArtAnimationExporter`'s per-frame DTO only carries
> X/Y/Width/Height/Alpha). Reproducing them in the live preview would make it *less* accurate to
> real output, not more — so this fix intentionally matches export's current (incomplete) behavior.
> Logged separately as item #56 rather than silently expanding this item's scope.
>
> **Not live-verified interactively** — no clip-art catalog is configured in this dev environment
> (same constraint noted during the earlier item #46 motion work), and the change lives in a
> separate git repo (`Ben.Video.Editor`) consumed by `Ben.Web.WebApp` as a project reference;
> verified via a clean end-to-end build of the whole `Ben.Web.WebApp` project against the updated
> reference, plus the full `Ben.Video.Tests` suite.

---

## 56. ClipArt `Rotation` and `TintColor` are set in the editor but never actually rendered anywhere — ✅ Fixed (2026-08-12, phase 113)

Found 2026-08-11 while shipping item #47's live-preview fix. `ClipArtClip.Rotation` and `TintColor`
are real, user-settable fields (`ClipArtEditor.razor` presumably exposes controls for them, and
they round-trip through project save/load via `ClipStore`/`ProjectService`), but nothing in the
actual rendering pipeline ever applies them:

- The static raster export overlay (`ExportService`'s `overlay` ffmpeg filter) only does a plain
  `scale`+`overlay`, no rotation or color-matrix/tint filter.
- The animated-raster canvas renderer (`RasterClipArtAnimationExporter`/`rasterClipArtRenderer.js`,
  used for a clip with a motion path) only carries X/Y/Width/Height/Alpha per frame — no rotation or
  tint fields at all in `RasterClipArtFrame`.
- `ExportArgBuilders.ApplyMotionFrame(ClipArtClip, MotionFrame)`'s own doc comment already notes
  "`Rotation`/`TintColor` are not part of `MotionFrame` and are left untouched" — confirming this
  isn't a bug in the motion path specifically, they're just never wired up anywhere, static or
  animated.
- Item #47's new live-preview visual (`LiveOverlayPreview`) also deliberately does not apply them,
  to stay consistent with the above rather than showing something export can't reproduce.

So today, turning either control feels like it does something in the editor's own state, but has
zero visible effect anywhere — preview, export, or otherwise. Needs: a rotation transform (CSS
`transform: rotate()` for the live preview + an ffmpeg `rotate`/`transpose` filter or per-frame
canvas rotation for export) and a tint filter (CSS `filter` for preview + an ffmpeg color-matrix
filter or canvas composite-mode draw for export), applied consistently across all three rendering
paths (static export, animated export, live preview) the way position/size/opacity already are.

> Found 2026-08-11 while scoping [[project_video_editor_phase112_clipart_live_visual|phase 112]]
> (item #47). Lives in the separate Ben.Video.Editor repo.

> **Shipped on `feature/phase-113-clipart-rotation-tint`, merged to `develop`.** Applied both fields
> across all three rendering paths, the same way position/size/opacity already are:
> - **Static export overlay**: new `ExportArgBuilders.BuildClipArtStaticOverlayFilter` (extracted
>   from `ExportService` for testability). Rotation uses ffmpeg's `rotate` filter with `ow=rotw(a):
>   oh=roth(a)` (bounding box auto-expanded so corners aren't clipped), with the overlay's `x:y`
>   recomputed in C# — `ComputeRotatedBounds` — to keep the same center once the box grows. Tint
>   uses a `colorchannelmixer` linear blend (`BuildClipArtTintMixer`): each output channel is
>   `original*(1-t) + (tintChannel/255)*alpha*t`, where `t` is the tint color's own alpha (0 = no
>   tint, 1 = full recolor derived from the source's own alpha shape) — expressible directly as
>   `colorchannelmixer` coefficients since the filter has no constant term, only per-input-channel
>   multipliers.
> - **Animated raster export**: `RasterClipArtFrame` gained `Rotation`/`TintColor` fields (constant
>   per clip, carried per-frame since `renderBatch` has no separate "constant for this call"
>   parameter); `rasterClipArtRenderer.js`'s canvas renderer rotates around the sprite's own center
>   before drawing, then tints via the standard `source-atop` composite technique (draw sprite, then
>   fill its already-drawn silhouette with the tint color at the tint's own alpha as blend strength)
>   — the canvas analogue of the ffmpeg color-matrix approach.
> - **Live preview** (`LiveOverlayPreview.razor`): CSS `transform: rotate()` on the clipart
>   container; tint via a masked overlay div (`mask-image` sourced from the asset's own blob URL, or
>   an inline SVG data-URI) at the tint's alpha — the CSS analogue of the same `source-atop`
>   technique, so all three paths agree visually.
> - `ApplyMotionFrame(ClipArtClip, MotionFrame)` deliberately untouched — both fields are static
>   per-clip, not part of `MotionFrame`, exactly as its existing doc comment already said.
> - 12 new unit tests (`BuildClipArtTintMixer`, `ComputeRotatedBounds`, `BuildClipArtStaticOverlayFilter`,
>   `RasterClipArtFrame`); full suite 1330/1330 passing.
> - Live-verified in the Playground (`DemoAssetProvider` temporarily granted `AllowRecolor` to
>   surface the tint control): placed the demo star clipart, set Rotation to 25° and Tint to
>   `#0078FFFF` via the Properties panel, confirmed the live preview showed a rotated, fully
>   re-colored blue star, and inspected the actual applied DOM styles
>   (`transform: rotate(25.00deg)` + a `mask-image`-driven tint div at `rgb(0,120,255)`) to confirm
>   they matched the design exactly. Also exercised the real shipped `rasterClipArtRenderer.js`
>   module directly against a synthetic sprite: a 45°-rotated frame showed a partially-transparent
>   corner pixel exactly where rotation moves a square's corner outside its own axis-aligned
>   bounding box (255,0,0,140 vs. a solid 255,0,0,255 with no rotation), and a full-alpha tint frame
>   showed the sprite fully recolored (0,0,255,255) at both its center and corner — direct pixel
>   proof the shipped JS, not a reimplementation, behaves correctly. A full ffmpeg-export pixel
>   sample was not obtained — the Playground has no base video clip to export against without an OS
>   file picker (a standing tool limitation), and the floating Media & Properties panel's item #53
>   positioning bug made driving the Export dialog for a clipart-only project impractical this
>   session — but the static overlay filter's exact string output is fully pinned by unit tests, and
>   the animated path's canvas logic is directly pixel-verified above, so export-path confidence is
>   high despite not sampling a final rendered .mp4.
> - **Found in passing, not fixed here** (separate task spawned): the static (non-animated,
>   non-motion-path) raster overlay branch never applied `ClipArtClip.Opacity` at all — a
>   pre-existing, independent bug, distinct from this item.

---

## 49. Insert vs. Overwrite edit modes — ✅ Fixed (2026-08-10, phase 107)

Item #25's ripple-insert (phase 106) only covered half of standard NLE overlap handling: when a
new/moved clip overlaps existing timeline content, it always *ripples* (shifts everything after
the insertion point later). Every mainstream editor (Premiere, Resolve, Final Cut, Kdenlive) also
offers *overwrite*: drop a clip on top of existing content and it trims/replaces whatever's
underneath instead of pushing subsequent clips later. There was no way to do the overwrite-style
edit at all.

> User-requested 2026-08-10 after comparing this editor's timeline behavior against standard NLE
> patterns — asked to build the concrete gaps identified, most relevant first. This is #1 of 4
> (Insert/Overwrite, Slip/Slide/Roll trims, three-point editing, J/L-cuts), prioritized highest
> since it directly extends the ripple-insert-confirmation dialog just shipped in phase 106.
>
> Shipped as phase 107 (`feature/phase-107-insert-overwrite-modes`, merged to `develop`, pushed).
> New pure `OverwriteEditCalculator.Resolve` resolves what happens to one existing clip when a new
> clip is placed on top of it in overwrite mode — unchanged, removed (fully covered), trimmed at
> one edge, or split into a front/back remainder if the new clip lands entirely inside it. New
> `ClipStore.OverwriteInsert` runs every overlapping clip on the track through that and applies the
> result as one atomic undoable `OverwriteInsertCommand`. The "Not Enough Room" dialog (phase 106)
> now offers **Insert (Make Room)** / **Overwrite** / **Cancel**. Live-verified: importing a second,
> identical-duration clip on top of the first showed the 3-button dialog; **Overwrite** correctly
> removed the fully-covered original (since it exactly matched the new clip's span) leaving only
> the new one, with the Undo button correctly reading "Undo: Overwrite with test-video.mp4" and
> reverting cleanly. The partial-trim and mid-clip-split code paths weren't separately click-driven
> through the UI (this session's Playground import flakiness made constructing a precise partial-
> overlap scenario impractical to automate) but are exhaustively covered by 17 new unit tests at
> both the pure-calculator and `ClipStore` levels.

---

## 50. Slip / Slide / Roll trim edits — ✅ Fixed (2026-08-10, phase 108)

Beyond basic trim (drag a clip's edge to change its in/out point) and ripple-move, standard NLEs
offer three more trim variants: **slip** (change a clip's in/out source-media offset without
moving the clip or changing its on-timeline duration — the clip's position and length stay fixed,
only what portion of the source media it shows changes), **slide** (move a clip along the timeline
without changing its own trim points, absorbing the gap by adjusting the neighbors' visible
duration on either side), and **roll** (move the shared edit point between two adjacent clips —
one clip's out-point and the next clip's in-point move together — without changing the total
timeline duration). None of these existed; only single-clip edge-trim and whole-clip ripple-move
were supported.

> User-requested 2026-08-10, #2 of 4 in priority order (see item #49). Lives in the separate
> Ben.Video.Editor repo.
>
> Shipped as phase 108 (`feature/phase-108-slip-roll-slide-trims`, merged to `develop`, pushed).
> New pure `TrimEditCalculator` — `ClampSlipDelta` for slip, `ClampBoundaryShift` for roll/slide
> (they share the same "extend one side, shrink the other" math — slide is really "roll the
> boundary with each neighbor by the same delta, while the moved clip just shifts position"). New
> `ClipStore.SlipClip`/`RollEdit`/`SlideClip`, each an undoable command; `RollEdit`/`SlideClip`
> auto-find the adjacent touching clip(s) rather than needing them passed in. New lightweight
> nudge-button UI (◀/▶, 0.5s steps) in the clip Properties panel — deliberately not a new drag
> gesture, which was out of scope for this pass. Live-verified all three with real before/after
> pixel measurements in the Playground: slip shifted a clip's in/out points by exactly the nudge
> amount with zero change to its timeline position/width; roll grew one adjacent clip and shrank
> the other by the same on-screen amount with the combined span pixel-identical before/after; slide
> moved the middle of three adjacent clips while both neighbors absorbed the change and the full
> three-clip span stayed pixel-identical. All three showed correct Undo button labels
> ("Undo: Roll edit", "Undo: Slide test-video.mp4 B A", etc.) and reverted cleanly.

---

## 51. Three-point editing (mark in/out on source, insert/overwrite at playhead) — ✅ Fixed (2026-08-10, phase 109)

Standard professional editing workflow: mark an in-point and out-point on a clip *in the media
library* (before it's on the timeline), then insert or overwrite it into the timeline at the
current playhead position — rather than dragging whole, untrimmed clips onto the timeline and
trimming them afterward. Needed new mark-in/mark-out UI on the ClipBrowser's preview, and for
`AddClipToTimeline` to respect the marked range instead of always using the source clip's full
duration.

> User-requested 2026-08-10, #3 of 4 in priority order (see item #49). Lives in the separate
> Ben.Video.Editor repo.
>
> Shipped as phase 109 (`feature/phase-109-three-point-editing`, merged to `develop`, pushed).
> Mid-phase discovery reshaped the plan: this app has no separate unplaced-clip library — every
> import lands directly on the timeline, and the Video tab is a view of already-placed clips —
> which meant item #48 (duplicate-Id bug) had to be fixed first as a hard prerequisite (see that
> item's entry; fixed in this same phase). Three-point editing itself: a "Mark In"/"Mark Out" bar
> appears while previewing a clip from its library card; marks are held in new component-level
> state (not written onto the clip's own trim, which would corrupt its existing timeline
> placement) and applied to the *clone* the next time "Add to Timeline" runs for that clip, feeding
> the existing playhead-anchored/insert-overwrite pipeline (items #25/#49) unchanged. Live-verified
> end to end: marked in/out via real preview scrubbing (summary correctly showed "0:02 → 0:08"),
> re-added the clip — the original stayed at its full 13.8s (proving no corruption) and the new
> clip landed at ~5.5s (matching the marked range), with the overlap-confirmation dialog and Undo
> label both behaving correctly against the trimmed sub-range.

---

## 52. J-cuts / L-cuts (split audio/video edit points) — ✅ Fixed (2026-08-10, phase 110)

A split edit where a clip's audio and video tracks lead or trail each other at a cut — e.g. the
next scene's audio starts before its video appears (J-cut), or the current scene's audio continues
after its video has cut away (L-cut). Video and audio clips already had fully independent
`TimelinePosition`/trim, so the mechanics of *creating* the offset already existed — what was
missing was the *concept*: nothing recorded that two clips were "the same take," so there was no
way to see their relative offset or treat them as a pair.

> User-requested 2026-08-10, #4 of 4 in priority order (see item #49). Lives in the separate
> Ben.Video.Editor repo.
>
> Shipped as phase 110 (`feature/phase-110-j-l-cuts-clip-linking`, merged to `develop`, pushed) —
> **this closes out all 4 of the standard-NLE gap items scoped this session (#49-52)**. New
> `TrackItem.LinkedClipId` (symmetric — both sides point at each other) + undoable
> `ClipStore.LinkClips`/`UnlinkClip`. New `FindNearbyLinkCandidate` auto-suggests the closest
> unlinked audio clip within ~1s of a video clip's edges, reusing phase 108's proximity-detection
> pattern instead of needing dual-type selection. New Link/Unlink button + live offset readout in
> the clip Properties panel, and a `bv-clip-chip--linked` border in the timeline. Deliberate scope
> cut, documented in the README: linking never moves either clip and doesn't keep a pair in sync
> during later drags — extending that risk to a new "move two items in lockstep" gesture would
> touch the same cross-`@foreach` DOM-relocation hazard fixed in phase 104, so each side stays
> independently trimmable and the link's job is purely to label the relationship + show the
> offset. Live-verified end to end: linked a video and audio clip, saw the correct offset readout
> and Undo label ("Undo: Link test-video.mp4 + test-audio.mp3"), both chips gained the linked
> border, and Unlink correctly removed it from both.

---

## 53. "Media & Properties" floating window resets to a mostly off-screen position — ✅ Fixed (2026-08-12, phase 114)

Found live-testing phase 111 (item #16 slice B) in the Playground. The floating `Media &
Properties` window (`bv-media-panel-window`, a `TelerikWindow`) opened with its `left`/`top`
already computed as strongly negative — e.g. `x: -29, y: -149` for a 240×200 default size —
leaving only a small sliver of the header visible/clickable in the bottom-right corner of where
it should be. This is distinct from item #17 (resize-then-drag reverting size, fixed phase 68):
here the *position itself* is wrong from first render, before any user drag. It also appeared to
reset back toward that same off-screen position after some re-renders even after being manually
repositioned (observed after clicking a tab inside the window), though that part wasn't isolated
carefully enough this session to be certain it's the same root cause rather than a second bug.

> Found 2026-08-10 during phase 111 live verification (Ben.Video.Editor repo). Not investigated —
> flagging for a future pass. Worth checking whether the default position is computed from
> `window.innerWidth`/`innerHeight` before Blazor's layout has stabilized (a similar race to other
> "default position wrong on first render" bugs), and whether anything re-triggers that same
> computation later. Workaround used for testing: `document.querySelector('.bv-media-panel-window').style.left/top`
> via JS to force it back on-screen.

> **Shipped on `feature/phase-114-media-panel-position`, merged to `develop`.** Root cause
> confirmed live: `TelerikWindow`'s `ContainmentSelector=".bv-editor"` drives its own mount-time
> (and later, re-clamp-time) JS measurement of the containment element; when `.bv-editor` hasn't
> settled to its real flex-computed size yet — reproduced this session as literally `0×0` — the
> clamp result is nonsensical (`top:-200px; left:-240px; width:0px; height:0px`, i.e. the window's
> own `MinWidth`/`MinHeight` negated) and flows straight back through the `@bind-Top`/`@bind-Left`/
> `@bind-Width`/`@bind-Height` two-way bindings into the app's own default fields, overwriting them
> — with nothing to ever reset it, the corruption stuck across every later render, which also
> explains the original report's "resets again after manual repositioning" observation (any later
> re-clamp against another bad transient measurement recorrupts the already-fixed state the same
> way). Fixed with two defensive corrections in `VideoEditor.razor`'s `OnAfterRenderAsync`, both on
> the existing bound fields, no new JS: (1) on `firstRender`, unconditionally reassert the intended
> defaults — safe by construction, since there's no legitimate prior drag to preserve at that exact
> point; (2) on every render, self-heal if width/height ever equal the unambiguous `"0px"` signature
> (never a legitimate resting size given `MinWidth`/`MinHeight` of `240px`/`200px`), without needing
> to guess at timing or risk fighting a deliberate later user drag. Live-verified by reading the
> panel's actual `style` attribute (not just visually) across a fresh page load repeated multiple
> times, a minimize→restore cycle, and a tab switch within the panel — all landed/stayed at the
> correct `68px/8px/320px/420px`, vs. reliably reproducing the broken negative/zero state on the
> same repro before the fix. See README-phase-114.md in the Ben.Video.Editor repo. Lives in the
> separate Ben.Video.Editor repo.

---

## 54. Member photos (not started)

Let members add a profile photo, with a per-photo choice of visibility: public (visible to anyone,
e.g. on a public case page or org roster) or members-only (visible only to other active members of
the same organization).

> Raised 2026-08-11 alongside item #55 (equipment tracking) — Ben WebApi/WebApp, not Ben.Video.Editor.
> Not scoped: needs a storage decision (reuse the existing `UploadFile`/Media Library pipeline
> rather than a bespoke upload path — item #6 already built a general-purpose, audience-aware file
> system with public/org/private visibility tiers via `FileAudienceAccess`, which is very likely the
> right mechanism to reuse here rather than inventing a second one), plus a profile-photo field on
> `AppUser` or a dedicated join, and UI for setting/changing the visibility choice.

## 55. Equipment inventory & checkout tracking (not started)

Two related but distinct systems:

**Personal equipment list** — any user with an account can log their own paranormal-investigation
equipment: photos, brand, model #, serial #, display name, and acquisition date. Entries are
categorized (e.g. by equipment type/brand/model), and the catalog of brands/models that accumulates
as users add gear becomes browsable/searchable by anyone — but each individual entry's *ownership*
(whose it is) and *serial number* stay private to the owner. A user can optionally share their own
equipment list with a specific organization so fellow members can see what gear is available for an
investigation.

**Organization-owned equipment** — a separate but similar catalog for gear the org itself owns
(same category/brand/model/serial/photo shape). For org-owned items, the org can track who
currently has a given piece, and support a checkout/loan workflow: members can request to check
out equipment, a new org-creatable role called **Equipment Management** (using the existing
org-roles/permissions system — see `OrgRolesManager`) manages approvals and hand-offs, and the
system tracks who has it now, when it was last serviced, and any noted defects. Checkouts can also
be logged against a specific investigation, so it's recorded which gear was actually in use on a
given visit.

> Raised 2026-08-11 alongside item #54 (member photos) — Ben WebApi/WebApp, not Ben.Video.Editor.
> Not scoped: this is the biggest item in the backlog by far — needs new entities for equipment
> items, categories/brand-model taxonomy, ownership + serial-number field-level privacy (a
> narrower, per-field version of what item #54 needs at the record level), a checkout/loan state
> machine (requested → approved → checked out → returned, plus condition/defect notes and a
> new "Equipment Management" org role wired into the existing `OrganizationSecurityService`
> role-hierarchy model from Phase A), and a link from a checkout record to an `Investigation`.
> Worth splitting into its own multi-phase plan (personal list first, sharing-to-org second,
> org-owned + checkout workflow third) rather than attempting as one piece, given the scope.

---

## 57. Camtasia-class direct-manipulation GUI — preview canvas + timeline transitions (✅ Complete — all 11 phases shipped 2026-08-12, phases 125–136, including the optional T5 stretch)

Full Camtasia-style direct manipulation: everything about an overlay's animation editable on the
preview canvas itself (click to select, drag to move, resize, keyframes created/updated by
manipulating the object at the playhead, bezier handles pulled out by dragging), plus
Camtasia-style transitions (drag a style from a gallery onto a clip junction, a saddle-shaped chip
overlapping the cut, drag its edges to change duration). Supersedes item #35's two remaining open
bullets (canvas resize-as-keyframe, type-in values).

Locked decisions (2026-08-12): full Camtasia overlap model for the timeline (transition chip
overlays the junction; timeline width reflects true output duration, not clip-chip-width + gap);
the Working Window preview renders transitions WYSIWYG (accepted junction re-encode cost);
rotation keyframes for ClipArt only (callout/text SVG renderers have no rotation support at all).

**Part 1 — preview canvas (6 phases, P1–P6):** click-to-select + unified body-drag; keyframe-aware
editing semantics (drag at playhead upserts a keyframe when a motion path exists, instead of
writing static fields); per-keyframe ScaleX/ScaleY (closes item #35) + ClipArt-only Rotation
keyframes; bezier handle creation/editing on canvas (alt-drag, double-click-remove, right-click
easing menu); canvas snapping/nudge/type-in HUD (closes item #35's last bullet); keyframe diamonds
visible/draggable on the timeline chip.

**Part 2 — transitions (5 phases, T0–T4, T5 stretch):** ~~T0~~ ✅ correctness prerequisites
(below); the risky overlap visual model; WYSIWYG preview transitions; a drag-and-drop transitions
gallery; transition chip edge-drag resize + editor polish; stretch: curated extra xfade styles.

Sequencing: `T0 → P1 → P2 → P3 → T1 → T2 → T3 → T4 → P4 → P5 → P6 → (T5 stretch)`, with reassess
points after T1 (the overlap model) and after P3 (ScaleX/Y/Rotation touch every render path).

**T0 shipped 2026-08-12 (phase 125)** — standalone, GUI-independent correctness prerequisite,
found during planning research (not assumed):
- Fixed a real export bug: `ExportArgBuilders.BuildXfadeFilterComplex` hardcoded every timeline
  segment as exactly 5 seconds long when computing chained xfade junction offsets
  (`cumOffset += 5.0 - dur`) — any real project (i.e. every project) exported same-track
  transitions at the wrong offset. Now threads real per-segment durations through from
  `ExportService` and uses the correct chained-xfade recurrence. Pinned regression tests assert
  the exact offset math.
- Deleted `Transition.CustomFilterExpression` — a free-form ffmpeg filter string field, read
  nowhere in the codebase, that violated the item-38 no-raw-filter-strings trust-boundary rule.
- Made `AddTransition`/`UpdateTransition` undoable (`RemoveTransition` already was).
- 1419/1419 tests passing. No live Playground verification this phase — pure backend logic, no
  UI surface touched (`TransitionEditor.razor` unchanged).

**P1 shipped 2026-08-12 (phase 126)** — click-to-select + unified body-drag on the preview canvas
for Callout/ClipArt/TextOverlay:
- New `CanvasSelectionOverlay.razor` (invisible full-body hit-rects, correctly layered below the
  existing resize/bezier/keyframe overlays so their handles keep winning clicks at a layer's
  edges — verified live via `elementsFromPoint`, not assumed). New shared
  `MotionEffectiveGeometry` (extracted from 3 previously-duplicated inline copies) and
  `CanvasHitTester` pure classes.
- A JS pointer-capture helper the plan assumed would be needed turned out unnecessary — the
  existing sibling overlays' plain full-canvas-SVG-catches-move/up pattern was already sufficient.
- 1446/1446 tests passing. Live-verified end to end (select/drag/undo/deselect/resize-priority).
- Found+documented (not fixed) a real pre-existing bug incidentally: selecting any Callout/
  TextOverlay throws an unhandled `TelerikEditor.OnChange` cast exception — item #58, below.

**P2 shipped 2026-08-12 (phase 127)** — keyframe-aware editing semantics, closing P1's known gap:
- Canvas body-drag now upserts a keyframe at the playhead (seeded from the interpolated frame)
  instead of writing a static field, whenever the layer already has a motion path — the actual
  "Camtasia rule." New `MotionKeyframeService.EditLayer` is the single decision point; a lower-level
  `UpsertKeyframeFromCurrent` also now backs `MotionKeyframeEditor.AddKeyframeAtPlayhead`,
  consolidating three previously-independent "seed a keyframe from current values" implementations
  and fixing a real bug found along the way: that panel method never resolved `ClipArtClip`, so a
  ClipArt layer's first keyframe silently defaulted to X=Y=0.5 instead of its real position.
- **Found and fixed a second real pre-existing bug live**: `VideoPreview._currentTime` never
  updated on a timeline-ruler seek when no video clip was loaded (only overlay items) —
  `SeekAsync` no-ops without a `<video>` element, and `OnSeekRequested` never set `_currentTime`
  directly the way frame-stepping already did. This silently broke `LiveOverlayPreview`'s
  time-accuracy too (latent since item #43), only became *visible* once P2 made canvas edits
  actually write data based on the current time. Fixed to match the frame-stepping convention.
- 1465/1465 tests passing. Live-verified: static drag unchanged from P1, animated drag at an
  exact keyframe time updates that keyframe in place, animated drag mid-interpolation creates a
  correctly-seeded new keyframe, both confirmed via the motion path overlay's live keyframe dots.
- Deliberately deferred: resize-handle/shape-control-point keyframe-awareness (entangled with P3's
  ScaleX/Y work below, and a separate pre-existing effective-vs-static display gap in
  `CalloutControlPointOverlay`/`ClipArtControlPointOverlay`) and an on-canvas "⏱ Animate" toggle
  (panel-only today) — both small, cleanly separable follow-ups, not new blockers.

**P3 shipped 2026-08-12 (phase 128)** — per-keyframe `ScaleX`/`ScaleY` (replacing uniform `Scale`
for Callout/ClipArt) and ClipArt-only rotation keyframes:
- Additive model change: nullable on `MotionKeyframe`, always-resolved on `MotionFrame` (defaults
  to the legacy `Scale` via a record property initializer referencing the primary constructor
  parameter) — every pre-P3 saved project and construction site behaves identically unless it
  explicitly opts in.
- **Zero changes needed anywhere in the export pipeline** (`ExportService.cs`,
  `SvgAnimationExporter.cs`, `RasterClipArtAnimationExporter.cs`) or `LiveOverlayPreview.razor` —
  every per-frame animated path already consumes `ApplyMotionFrame`'s output directly (confirmed
  by reading each call site first), so updating the two `ApplyMotionFrame` overloads alone
  threads ScaleX/Y/Rotation all the way through to real exported video, not just live preview.
- `MotionKeyframeEditor.razor` gains conditional Scale X/Scale Y sliders (Callout/ClipArt) vs. the
  unchanged single Scale slider (TextOverlay — one axis, FontSize), plus a ClipArt-only Rotation
  slider. Caught and fixed a real bug during implementation, before it shipped: `Apply()` had to
  explicitly guard `Rotation = null` for non-ClipArt layers — writing a spurious `0.0` instead
  would have flipped `Evaluate()`'s "not animated on this path" signal to "animated, holds at 0,"
  corrupting every future evaluation of that keyframe.
- 1482/1482 tests passing (+17 new, covering the full ScaleX/Y/Rotation interpolation truth
  table). Live Playground verification partial: confirmed the Properties panel correctly renders
  the new conditional UI for a ClipArt keyframe (Scale X/Scale Y/Rotation all present, gated
  correctly by layer type) — could not get a full end-to-end confirmation of a dragged slider
  value taking visible effect, a pre-existing Telerik-slider automation limitation in this
  environment (independently documented during phases 112/113's own work with the same
  component), not something this phase's code introduced. The actual interpolation/export math is
  fully covered by unit tests, which is where the real correctness risk lived.
- Unblocks (but doesn't itself attempt) the resize-handle/control-point keyframe-awareness
  deferred from P2 — scoped to the model/evaluation/export layer only this phase.

**T1 shipped 2026-08-12 (phase 129)** — the plan's own "risky one," Part 2 (transitions) begins:
transitions now render as a saddle chip overlapping the real cut between their two clips (matching
Camtasia), instead of the old inline pill that consumed its own flex-row space unrelated to its
actual duration. `Transition` items excluded from the sequential gap-accumulation loop entirely;
rendered separately, absolutely positioned from `TimelinePosition`/`Duration` directly, z-index
tuned so the underlying clips' own trim handles stay grabbable exactly where a transition overlaps
their edge (proven via `elementsFromPoint`, not assumed). Worked through the plan's own "must
audit" list (`TotalDuration`, Fit, auto-fit, `RenderStatusService`, `SegmentBudget`) before
touching anything — proved (and pinned with a regression test) that `TotalDuration`'s existing
formula was already correct with transitions present, given `TransitionEditor`'s own duration
clamp; **zero changes needed** anywhere on that list. 1483/1483 tests passing (+1). Live-verified
cleanly: contiguous clips, correctly-overlapping/duration-proportional transition chip, trim-handle
z-index priority, a real trim-handle drag, and transition select/remove/undo all confirmed working.
Found and documented (not fixed, confirmed unrelated) a real pre-existing bug while setting up live
verification: "Insert (Make Room)" doesn't renumber clip `Order` to match the new
`TimelinePosition` after a ripple-insert, desyncing the rendered timeline layout — reproduced with
zero transitions present, ruling out this phase's changes — item #59, below.

> Requested by the user 2026-08-12: "Can you create an accurate and detailed plan to create a GUI
> for all aspects of the preview window such as adding keyframes and moving items and in the
> timeline adding and modifying transitions like they do in Camtasia... And the bezier curve
> handles etc... So, if a callout moves from one keyframe to another, it shows a dashed line and
> it moves with the playhead. That is what the preview window is for." Full plan:
> `also-the-sidecar-must-generic-whisper.md`. Lives in the separate Ben.Video.Editor repo
> (Github-BenVideo remote).

---

## 58. Selecting a Callout crashes the Properties panel with an unhandled `TelerikEditor.OnChange` cast exception (✅ fixed 2026-08-13, phase 137)

**Fixed.** Root cause confirmed via direct reflection against the real `Telerik.Blazor.dll`:
`TelerikEditor.OnChange` is `EventCallback<object>`; pairing it with an explicit `OnChange="Apply"`
alongside `@bind-Value` hit a struct-generic unboxing mismatch in Blazor's own parameter-setting
code. Fixed by switching `CalloutEditor.razor` to `@bind-Value:after="Apply"` (Blazor's built-in
post-bind-update hook), which never touches the `OnChange` parameter at all. TextOverlay was never
actually affected — see the correction below. Full root-cause writeup in `README-phase-137.md`
(Github-BenVideo repo). Found in passing: TextOverlayEditor's rich-text edits don't commit at all
(different bug, logged as item #64).

Original entry (kept for history):

Found incidentally while live-verifying item #57's phase 125/P1 (canvas click-to-select) — **not
caused by that work**: reproduces identically via the pre-existing, unmodified timeline-chip
selection path too, confirmed by reproducing both ways in the same session.

**Repro (fresh session, minimal):**
1. Load `/demo/full`, click Initialize, wait for ffmpeg.wasm to load.
2. Click "📌 Callout" (or "T + Text") in the timeline toolbar to add a new callout/text overlay.
3. Select it — either by clicking its timeline chip, or (new, item #57 P1) clicking its body on
   the preview canvas.

**Observed:** the browser console logs a `crit`-level unhandled exception from
`WebAssemblyRenderer`:
```
Unable to set property 'OnChange' on object of type 'Telerik.Blazor.Components.TelerikEditor'.
The error was: Arg_InvalidCastException
System.InvalidOperationException ... ---> System.InvalidCastException: Arg_InvalidCastException
   at Microsoft.AspNetCore.Components.Reflection.PropertySetter.CallPropertySetter[TelerikEditor,EventCallback`1]
   ...
   at Telerik.Blazor.Components.TelerikEditor.SetParametersAsync(ParameterView parameters)
```
Blazor's `#blazor-error-ui` banner ("An unhandled error has occurred. Reload") appears at the
bottom of the page. The Properties panel still renders and appears to work (Shape/Duration/
Position/Size sliders all show correct live values, editing them still works) — the crash looks
non-fatal to this component subtree, but it's still a real unhandled exception that should not be
happening, and a user seeing the red error banner reasonably reads it as something being broken.

**Root-cause hypothesis, narrowed by reading the code (not yet confirmed by fixing it):**
`CalloutEditor.razor:144` and `TextOverlayEditor.razor:27` both write:
```razor
<TelerikEditor @bind-Value="_richTextHtml" Tools="@_editorTools" Height="90px" OnChange="Apply" />
```
`@bind-Value` on a Telerik input generates its own paired `ValueChanged`/value-binding
infrastructure; explicitly also specifying `OnChange="Apply"` may be racing or colliding with
that generated code — `TelerikEditor.OnChange`'s actual parameter type may not be the plain
`EventCallback<string>` shape being supplied for it (per the exact wording "cannot cast" +
`EventCallback\`1` in the trace), possibly a Telerik-version-specific change to `OnChange`'s
signature since this code was originally written (item #16 phases 74/111/115/116). Both files use
the identical pattern, so a fix likely applies to both call sites the same way.

**Suggested fix, in order:**
1. Check the installed Telerik UI for Blazor version's `TelerikEditor.OnChange` parameter type
   signature directly (via decompilation/docs) and compare against what `Apply` (the method bound
   here) actually returns/accepts.
2. Try removing the explicit `OnChange="Apply"` and instead reacting to `@bind-Value`'s own
   `_richTextHtml`-changed path (a `ValueChanged` callback param, or an `OnBlur`, depending on
   what the installed Telerik version actually exposes) — `@bind-Value` already re-invokes on
   every change, so `OnChange="Apply"` may be redundant/conflicting rather than necessary.
3. Confirm the fix by reproducing this exact repro cleanly (console free of the `crit` log line,
   no `#blazor-error-ui` banner) in both `CalloutEditor` and `TextOverlayEditor`.

**How to verify a fix:** repro steps above; before a fix, `read_console_messages` shows the `crit`
`WebAssemblyRenderer` log line every time a Callout/TextOverlay is first selected in a session;
after a fix, selecting either should produce no console errors and no error banner.

> Found 2026-08-12 during item #57 phase 125/P1 live verification. Lives in the separate
> Ben.Video.Editor repo (Github-BenVideo remote), `Ben.Video.Editor/Components/CalloutEditor.razor`
> and `TextOverlayEditor.razor`.

---

## 59. "Insert (Make Room)" leaves clip `Order` chronologically inverted (✅ fixed 2026-08-13, phase 140)

**Fixed — and two corrections to what's written below.**

1. **The hypothesis below was wrong about the mechanism.** It guessed the ripple path was "missing
   a `RenumberItems` call". It isn't — `InsertClipWithRipple` already calls it. The real cause:
   `InsertClipRippleCommand.Execute()` *appended* the new clip (highest `Order`) even though
   ripple-insert lands it chronologically **first**, and `RenumberItems` derives `Order` from list
   index, so it faithfully preserved the inversion. Fixed by inserting at the correct index.
2. **The severity below was understated.** It says this is "a pure **rendering** desync, not
   silent data corruption". Not true: `TimelineTrack.VideoClips` sorts by `Order` and
   `ExportService.RunPipelineAsync` consumes that ordering directly, so this **exported the video
   with the clips concatenated in the wrong order**.

**A second, worse bug was found underneath it:** `RenumberItems` did
`Items[i] = Items[i] with { Order = i }`, replacing every entry with a *copy* and silently
orphaning the object references every `IEditorCommand` holds to undo itself — so
`InsertClipRippleCommand.Undo()` un-shifted detached objects while the clips actually on the track
kept their shifted positions permanently. Record value-equality kept `Items.Remove` working, so
the visible half of undo still worked, which is why it hid; the pre-existing undo test passed only
because it asserted on the caller's own orphaned reference rather than through the track. Fixed by
mutating `Order` in place (root fix across all 13 `RenumberItems` call sites).

3 new regression tests, all confirmed failing pre-fix; 1525/1525 passing. Live-verified with the
repro below. Full writeup: `README-phase-140.md`.

> **Better repro path:** the Server-tab double-import below is unreliable (hits the tracked
> Playground import flakiness). The **Video tab's "Add to timeline" button** reaches the identical
> collision/prompt code path with no re-import needed.

Original entry (kept for history):

Found incidentally while live-verifying item #57's phase 129/T1 (timeline transition overlap
model) — **not caused by that work**: reproduces with plain video clips and no transitions
involved at all, confirmed by reproducing the exact same layout corruption in a scenario with
zero transitions present.

**Repro (fresh session, minimal, reproduced cleanly twice):**
1. Load `/demo/full`, click Initialize, wait for ffmpeg.wasm to load.
2. Server tab → Import "test-video.mp4" to editor (lands as a single ~13.8s clip at
   TimelinePosition 0).
3. Import "test-video.mp4" again (same file, a second time). A "Not Enough Room" dialog appears:
   *"'test-video.mp4' would land on top of an existing clip. Shift the clips after this point
   later to make room (Insert), or trim/replace what's underneath instead (Overwrite)?"*
4. Click **Insert (Make Room)**.

**Observed:** `Clips.TotalDuration` correctly reports the true total (e.g. `0:27.7` for two 13.8s
clips) — the *underlying data* is fine. But the **rendered timeline layout is corrupted**: the
originally-imported clip (still `Order = 0`, so it renders *first* in the sequential flex-flow
loop) visually renders with a large leading gap and appears to occupy the *second* time slot, while
the newly-inserted clip (`Order = 1`, renders *second*) appears to render *after* it — even though
the newly-inserted clip's real `TimelinePosition` should be the *earlier* one (it's the one that
"made room" by going first). Concretely, in one captured repro: original clip rendered with
`margin-left: 1206px` (≈ one full clip-width of leading gap it should not have) and the new clip
rendered immediately after it with zero gap — i.e., the two clips render **in reverse chronological
order**, with a phantom gap where clip 1 should visually start. The ruler/"Fit"/total-duration
number itself stays numerically correct throughout (confirmed via `Clips.TotalDuration`) — this is
a pure **rendering** desync, not silent data corruption — but the visible timeline is actively
misleading about where each clip actually starts and in what order they play.

**Root-cause hypothesis, narrowed by reading the code (not yet fixed):**
`VideoTimeline.razor`'s sequential clip-rendering loop (`orderedItems = track.Items.OrderBy(i =>
i.Order)...`) computes each chip's gap-margin from a **running total that assumes `Order` and
`TimelinePosition` are chronologically consistent** (`gapPx = max(0, (item.TimelinePosition -
runningEndSeconds) * PxPerSecond)`, then `runningEndSeconds = item.TimelinePosition +
ItemDuration(item)`). Whatever command backs "Insert (Make Room)" (likely
`InsertClipRippleCommand` in `Ben.Video.Editor/Models/IEditorCommand.cs`, given the label match)
appears to **shift the existing clip's `TimelinePosition` later without renumbering its `Order`
relative to the newly-inserted clip** — the new clip likely gets appended with a *higher* `Order`
value (e.g. `Order = track.Items.Count`) despite ending up *chronologically first*. Since the
render loop trusts `Order` for iteration sequence but `TimelinePosition` for gap math, an
Order/TimelinePosition mismatch like this produces exactly the observed corrupted-but-numerically-
correct layout.

**Suggested fix, in order:**
1. Confirm the hypothesis directly: log/inspect each item's `Order` and `TimelinePosition`
   immediately after "Insert (Make Room)" completes, to verify the mismatch predicted above.
2. Whatever command implements ripple-insert-at-a-colliding-position needs to renumber every
   affected track's `Order` values to match the new `TimelinePosition` ordering afterward — the
   codebase already has a `RenumberItems(track)` helper used elsewhere in `ClipStore.cs` for
   exactly this kind of post-mutation cleanup; check whether the ripple-insert path is simply
   missing a call to it (or an equivalent sort-then-renumber-by-TimelinePosition pass).
3. Consider whether `VideoTimeline.razor`'s render loop should defensively sort by
   `TimelinePosition` instead of (or in addition to) `Order` — `Order` existing as a *separate*
   field from `TimelinePosition` at all is presumably intentional (drag-and-drop reordering UX,
   ripple math elsewhere), so this may not be the right fix, but is worth ruling in/out.

**How to verify a fix:** repro steps above; before a fix, the second (colliding) clip's rendered
position visibly doesn't match its real chronological order; after a fix, both clips should render
left-to-right in true chronological (`TimelinePosition`) order with zero unexpected gap, matching
`Clips.TotalDuration`'s already-correct number.

> Found 2026-08-12 during item #57 phase 129/T1 live verification — a plain video-clip scenario
> (Server-tab import ×2 + "Insert (Make Room)") reproduced this with no transitions present at
> all, conclusively ruling out T1's own changes as the cause. Lives in the separate
> Ben.Video.Editor repo (Github-BenVideo remote), `Ben.Video.Editor/Components/VideoTimeline.razor`
> (the render loop) and likely `Ben.Video.Editor/Models/IEditorCommand.cs`
> (`InsertClipRippleCommand`).

## 60. Split audio out of a video track via right-click (✅ fixed 2026-08-13, phase 155)

Turned out the backend (`ClipStore.DetachAudio` + `DetachAudioCommand` undo/redo +
`VideoTimeline.SeparateAudioAsync`) was already fully built and already covered by 7 existing
`ClipStoreTests` — it was simply dead code, never called from anywhere in the UI. Added a
"Separate Audio" item to the clip right-click context menu, disabled via a guard mirroring
`SeparateAudioAsync`'s own precondition (`MemFsName is not null && !MuteAudio`) exactly.

This also settles the design question this item originally flagged as unscoped: the existing
implementation already creates a fully **independent** `AudioClip`, not a linked pair matching
item #52's J-cut/L-cut model — kept as-is (the user can always manually link it afterward via
item #52's own gesture if J-cut/L-cut behavior is wanted later).

Live-verified: imported a clip via the Server tab, right-clicked it, "Separate Audio" enabled
(correctly disabled for a stale/unlinked clip), clicked it — new Audio track appeared with an
independent, correctly-positioned `AudioClip` and a waveform. Ctrl+Z correctly undid it.

> Noted by the user 2026-08-12, mid-session while item #57 (Camtasia GUI arc) was in progress;
> fixed 2026-08-13 phase 155. See `README-phase-155.md` in Github-BenVideo for full detail.

## 61. Multi-point volume envelope on the timeline (✅ fixed 2026-08-13, phase 156)

Turned out this was already fully built: `VolumeAutomationLane.razor` + `volumeAutomationLane.js`
implement a complete draggable-keyframe SVG automation editor (add/drag/delete, each with full
undo/redo), already wired directly into every timeline clip row — not a separate panel, not
behind a toggle. It just wasn't visible: `.bv-clip-chip`'s CSS hardcoded `height: 40px; overflow:
hidden;` across icon+label+waveform+automation all squeezed into one horizontal row, clipping the
lane's requested 40px almost entirely into invisibility.

Fix (user confirmed scope first — "fix the clipping only," no marker-snapping): `.bv-clip-chip`
now wraps, the automation lane (last child, `flex-basis: 100%`) drops to its own full-width row
below the existing content instead of competing for horizontal space, and the chip grows via
`height: auto` to fit both rows. Overlay items (Callout/ClipArt/TextOverlay) unaffected — inline
`height: 40px` wins over the class, and they never render this child anyway (only `VideoClip`/
`AudioClip` implement `IHasVolumeAutomation`). Also fixed a ~4px stray SVG inline-spacing gap via
a new `VolumeAutomationLane.razor.css` (didn't exist before).

Live-verified the full cycle via dispatched pointer events now that the lane is actually
reachable: add a keyframe, drag it (position + volume both updated), delete via double-click,
Ctrl+Z correctly undoes through `ClipStore`'s real undo stack. No marker-snapping added (matches
the confirmed scope) — logged as a possible future enhancement if still wanted.

> Noted by the user 2026-08-12, mid-session while item #57 (Camtasia GUI arc) was in progress;
> fixed 2026-08-13 phase 156. See `README-phase-156.md` in Github-BenVideo for full detail.

## 62. blob: URL "failed to load resource" (✅ root-caused + fixed 2026-08-13, phase 170)

**It was never a lifetime bug.** The import row picks its thumbnail element by *file* type
(`@if (item.IsVideo) → <video>`), but `PreviewUrl` changes *kind* mid-import: it starts as an object
URL over the picked file (a real video), then is overwritten with `thumbs[0]` — an extracted
`image/webp` still frame. The markup still renders `<video>`, which can never decode a WebP, so the
browser logs exactly one failed `blob:` load per video import: no exception, no C#-side signal, no
visible symptom beyond an empty thumb box in a row that clears itself. That matches this item's
reported signature exactly — solo, inspector-only, "intermittent" (needs a video import *and* an
open inspector). The clincher: the failing URL was still `fetch`-able at the moment it 404'd, which
rules out every revoke/lifetime theory the previous four phases had assumed.

Fix: track what the URL *is*, not what the file is — `FileImportStatus.PreviewIsVideo`, with all
assignments routed through one `SetThumbnailPreviewAsync`. Measured 1 → 0 failed blob loads on the
same import.

Chasing the (wrong) lifetime hypothesis first still paid: it surfaced **three genuine latent
defects** in the thumbnail path, all fixed in the same phase — revoke-before-swap in
`LazyFillThumbnailsAsync`; shared URL *strings* across duplicated clips and the import row (both
duplication paths copy the list, not the strings) now guarded by a pure `ThumbnailRevokePlan`; and
a missing `@key` on `VideoTimeline`'s thumbstrip. Plus a per-video-import leak of the superseded
`fileObjectUrl` blob.

**Why it stayed unfound for four phases:** phase 144 built `BlobUrlLifecycle` precisely to catch
this class of bug, then only pointed it at preview URLs; phase 159 added sidecar thumbnails. wasm
thumbnails — the largest blob population in the app — were never registered, so the detector was
structurally blind. Now registered under `ClipBrowser.wasmThumbnail`. See `README-phase-170.md`.

<details><summary>Original report (2026-08-12)</summary>

Ben spotted another `Failed to load resource` error for a `blob:http://localhost:PORT/<guid>` URL
during item #57 P5 live testing — no visible on-screen symptom this time, only caught in the
Network/Console panel. Not the same as item #58's `TelerikEditor` crash.

This is the **same class of issue already tracked** in memory
`feedback_playground_ffmpeg_import_flakiness` (not a new bug): recurring `blob:` `ERR_FILE_NOT_FOUND`
errors during long single-tab Playground sessions, previously seen bundled with a fully stuck
Export ("Processing… 0%" forever) during item #9's testing pass, working theory a stale/dangling
blob URL retry loop, **root cause still not found** despite several recent phase READMEs
(130/131/132/133) calling it "already-documented benign" — that characterization was never
actually earned; nobody has root-caused it yet. This occurrence is useful new data: it shows the
error can surface with **no accompanying stuck/hung symptom** at all, just silently in the
inspector — narrows out "always causes a visible hang" as a necessary condition.

> Noted by the user 2026-08-12 during item #57 P5 live verification. User has already asked
> (recorded in the memory above) to properly root-cause this later rather than keep routing around
> it — not blocking P5.

</details>

## 63. Keyframe-branch canvas edits bypass undo/redo entirely (✅ fixed 2026-08-13, phase 154)

Once a Callout/ClipArt layer has a motion path, body-drag, HUD position/size type-in, and
arrow-key nudge all route through `MotionKeyframeService.EditLayer`'s keyframe branch, which
mutates the keyframe directly via `UpsertKeyframe` — a completely separate scoped service from
`ClipStore`, never wired into `ClipStore`'s own `_undoStack`/`_redoStack`. New generic
`CommitMotionKeyframeCommand` + `ClipStore.CommitMotionKeyframeEdit(description, apply, revert)`
wires it in: a keyframe edit and an ordinary `ClipStore` mutation now interleave correctly on ONE
shared undo history. Drag gestures still upsert a keyframe every frame during the drag (unchanged
— never individually committed); a new `_dragOrigKeyframe` snapshot, captured once at drag-start
(mirroring the pre-existing `_dragOrigX`/`_dragOrigY` pattern exactly), lets `OnPointerUp` commit
the whole gesture as one undo entry at drag-end, matching the static-field branch's own behavior.

`TextOverlay` deliberately excluded — its *static* branch has never had an undo tier either (a
separate, pre-existing gap), so fixing only its keyframe branch would have introduced a new,
backwards undo-only-when-animated asymmetry instead of removing one.

Live-verified the exact repro scenario from this item's own original write-up: nudged a keyframed
Callout 3× (X 10%→13%), one Ctrl+Z correctly reverted exactly the last nudge (→12%, not "nothing
happens" and not a full-history wipe), two more Ctrl+Z unwound to the original 10%, Ctrl+Shift+Z
(redo) correctly restored forward to 11%.

**Found along the way, NOT fixed here — became item #73**: the on-canvas resize-handle and
shape-control-point drag overlays (`CalloutControlPointOverlay`/`ClipArtControlPointOverlay`) don't
call `Motion.EditLayer` at all — a deeper, separate bug (silently no-ops on animated layers, not
merely non-undoable), this item's own original text incorrectly assumed they already routed
through the keyframe branch.

> Found live during item #57 P5 verification, 2026-08-12; fixed 2026-08-13 phase 154. See
> `README-phase-154.md` in Github-BenVideo for full detail.

## 64. TextOverlay Properties panel silently discards in-progress edits (✅ fixed 2026-08-13, phase 139)

**The original description of this item was wrong** — corrected here, since the real bug turned
out to be both broader and different from what was first assumed.

**What was originally written (incorrect):** that `TextOverlayEditor.razor`'s `TelerikEditor` had
no commit hook and its `Apply()` was "never called from anywhere," so rich-text edits were lost.
**Actually false** — this panel has an explicit **"Apply" button** wired to `OnClick="Apply"`,
unlike `CalloutEditor` which auto-commits per field. It's an edit-then-click-Apply design, so
`Apply()` being absent from the editor's own change event is by design, not a bug.

**The real bug (found while verifying the above, then fixed):** `RefreshFromSource()`
unconditionally overwrote **every** local editing field from the source overlay — text, font,
size, colour, alignment, offsets, position, duration, fades, shadow, everything — and it was
wired to *three* external event sources (`Clips.OnChange`, `Motion.OnChanged`,
`Playback.OnStateChanged`). Since this panel holds uncommitted edits until Apply is clicked, any
of those firing mid-edit silently reverted the user's in-progress work. `Clips.OnChange` fires on
*any* mutation anywhere in the project, and `Playback.OnStateChanged` fires **every frame during
playback** — so simply having the panel open while the video played would continuously wipe
whatever was being typed.

Same bug class as `TransitionEditor.OnParametersSet` (found+fixed in phase 132), but more severe:
three high-frequency triggers instead of one. Fixed the same way — `RefreshFromSource` now only
reloads the editable fields when a *different* overlay becomes selected (tracked by `Id`); slider
bounds still refresh live, since those can't discard an edit.

Verified by direct A/B live testing using the panel's plain-Blazor-bound "Background box"
checkbox (a real C#-level edit, unlike the Telerik inputs — see the testing note below): pre-fix,
an unrelated project mutation reverted the pending edit (`true`→`false`); post-fix it survives.
Also confirmed switching between two different overlays still correctly reloads the panel (no
over-caching).

> **Testing note for future Telerik work:** synthetic `element.value = x` + dispatched
> `input`/`change` events do **not** reach a `TelerikNumericTextBox`'s bound C# field — per
> Telerik's docs its `ValueChanged` "fires during typing" through Blazor's own handlers, so a
> JS-set value updates only the DOM and silently no-ops the binding. This produced a misleading
> first test result. Use a plain-Blazor-bound control (`<input @bind=...>`) or real typing when
> proving C#-level state changes.

> Found 2026-08-13 while fixing item #58 (`TelerikEditor.OnChange` crash) and reading
> `TextOverlayEditor.razor` for comparison — the backlog's original #58 entry assumed both
> `CalloutEditor.razor` and `TextOverlayEditor.razor` had the identical broken pattern; only
> Callout actually did.

## 65. Default the Media & Properties panel to minimized, docked right (✅ fixed 2026-08-13, phase 157)

`VideoEditor.razor`'s panel now starts `WindowState.Minimized` and docks right via
`Left: calc(100% - 328px)` instead of expanded/left-docked. The four default position/size values
were extracted into `const string` fields referenced by both the field initializers *and* item
#53/phase 114's two `OnAfterRenderAsync` self-heal blocks (which reassert these exact values to
fix a `TelerikWindow` `ContainmentSelector` clamp bug) — exactly the interaction this item's own
note warned about, now structurally impossible to drift out of sync rather than merely avoided by
careful editing.

Live-confirmed minimized isn't a dead end: `OnTimelineItemSelected` already restores
Minimized→Default whenever a clip is selected (pre-existing, unchanged), and the title bar's own
"Restore" button (Telerik's native `WindowAction`, unrelated to item #68's `WindowActions` bug)
works correctly. Also confirmed — the user asked directly — that dragging the panel by its title
bar still works normally from the new `calc()`-based default position (Telerik overwrites it with
a plain pixel value on drag, same as always).

> Noted by the user 2026-08-13; fixed 2026-08-13 phase 157. See `README-phase-157.md` in
> Github-BenVideo for full detail.

## 66. Intermittent "Downloading timed out" false-positive on server-library import (✅ CLOSED — mitigated phase 149, root cause structurally removed by item #70, 2026-08-13)

Server-tab imports (`ClipBrowser.ImportFromLibraryAsync`) occasionally fail with "Downloading:
`<file>` timed out." even though the underlying HTTP GET completes in well under 100 ms —
confirmed via `DemoMediaLibraryProvider`'s own `HttpClient` logging showing a 68.5 ms round trip
at the exact moment the UI reported a timeout.

Phase 145 (item #57/#59-#65 flakiness arc) first saw this as a one-off during rapid live-clicking
and hypothesized it was caused by interacting before ffmpeg's Initialize had fully settled;
undiagnosed further at the time since it didn't reproduce on a clean retry in that session.

**It reproduced again during phase 147's cross-browser verification** (see `README-phase-147.md`
in Github-BenVideo), this time on a completely fresh page load with a single deliberate click —
no rapid interaction, Initialize already fully "Ready" before the click. This rules out phase
145's working hypothesis. A second fresh-session attempt with the identical single click succeeded
normally (~19s). Genuinely intermittent, root cause unknown — worth instrumenting
`DemoMediaLibraryProvider.DownloadFileAsync` / the shared `HttpClient`'s timeout wiring
(`AddHttpClient<DemoMediaLibraryProvider>` in `Ben.Video.Playground/Program.cs`) to find why an
`OperationCanceledException` is thrown when the request itself plainly succeeded.

**Root cause, confirmed by live reproduction:** Blazor WASM's single main thread hosts both the
`HttpClient` call's own continuations *and* all the JS-interop marshaling for ffmpeg's own worker
traffic. Heavy concurrent ffmpeg activity — most commonly the editor's own debounced auto-preview
timeline re-render, which fires whenever the clip list changes and is entirely unrelated to the
import itself — can delay a normally-68ms download's completion callback long enough to trip the
old 30s `HttpClient.Timeout`. Deliberately reproduced by firing the Server-tab card's "preview
thumbnail" button (its own independent download + ffmpeg thumbnail extraction) at the same moment
as an import click: a request that takes 68ms idle took **31.2 seconds** under that contention —
just over the old timeout. This also explains phase 147's original no-double-click repro: the
Server tab automatically kicks off an eager background thumbnail prefetch for any image files the
instant the tab loads, so a single import click shortly after can race against that automatic
background work with no deliberate concurrency needed.

**Fix:** bumped `DemoMediaLibraryProvider`'s `HttpClient.Timeout` from 30s to 60s (Playground
fixture only — a real host's `HttpMediaLibraryProvider` timeout is that host's own config, out of
this repo's scope). Considered and rejected pausing the auto-preview render during an in-flight
download (mirroring `PauseBackgroundRenderDuringExport`'s existing pattern) — that only guards
against the ONE contention source proven here, not eager-thumbnail-prefetch or any other ffmpeg
activity, so it's a partial fix dressed as a full one; the timeout bump is honest about what it
actually does. Validated by repeating the identical deliberate-contention reproduction after the
fix: 48.6s of contention completed cleanly under the new 60s ceiling, with zero errors in the
ffmpeg diagnostics operation log.

> Fixed phase 149, 2026-08-13 — see `README-phase-149.md` in Github-BenVideo for the full
> reproduction and verification detail.

> Found during phase 147 cross-browser stress verification, 2026-08-13.

**Closed out 2026-08-13 by item #70's sidecar arc (phases 158–162).** Phase 149 fixed the symptom
by raising a timeout while explicitly recording that the real cause was main-thread contention:
ffmpeg's own JS-interop traffic delaying an unrelated HTTP download's completion callback by 30+
seconds (live-measured at 31.2s and 48.6s for a normally-68ms request). Item #70 removed that
contention structurally rather than tolerating it:

- **phase 159** moved import-time probe + thumbnail extraction off the main thread — precisely the
  operations that were contending *during an import*;
- **phase 161** moved the auto-preview assembly (the other heavy main-thread consumer) off it;
- **phase 162** moved export concat + audio mix, the heaviest of all.

The phase-149 timeout stays as a **guard, not a fix**. Note the honest boundary: this is closed
*when the sidecar is present*. A browser-only install still relies on the raised timeout, which is
correct — there is no other thread to move the work to. See `README-phase-162.md`.

## 67. Import rejected with misleading "Click Initialize" message while ffmpeg is merely busy (✅ fixed 2026-08-13, phase 151)

`ClipBrowser.ImportFromLibraryAsync`'s upfront gate (`ClipBrowser.razor:914`,
`if (Ffmpeg.State != FfmpegState.Ready)`) hard-rejects any import attempt with:

> ⚠ Click Initialize in the toolbar before importing.

whenever ffmpeg's state is `Processing` — including a perfectly healthy, legitimately slow
background operation (e.g. an auto-preview `concatClips` re-render, confirmed live to take 44+
seconds for two short clips). The message is actively misleading: it implies ffmpeg needs to be
re-initialized, when nothing is wrong — the worker is just busy and will be `Ready` again shortly.

This gate predates phase 142's worker-serialization mutex and was never updated to match phase
145's own recorded design decision ("import-during-busy queues on the mutex, busy card shows
'waiting for ffmpeg…'" — see the phase 141-147 plan). That queue-and-wait behavior was designed but
never actually wired into this call site.

Fix should replace the hard rejection with either: (a) queuing the import attempt until
`FfmpegState` returns to `Ready` (matching the original design decision), or at minimum (b) a
different, accurate message when `State == Processing` ("ffmpeg is busy — try again in a moment")
versus the current message, which should be reserved for `State == Error` or `Unloaded`.

> Found during phase 147 cross-browser stress verification, 2026-08-13, while testing "import
> during churn" — see `README-phase-147.md` in Github-BenVideo.

**Fix (option (a), queuing):** two separate fixes, since phase 150 (shipped the same day) had
already split the old combined download+import flow in two. `DownloadAndCacheAsync` — the
download step — turned out not to need ffmpeg at all anymore (it's HTTP GET + OPFS write only),
so its copy of this gate was simply dead weight and was removed outright: downloading now works
even before Initialize. `AddCachedFileToTimelineAsync` — the step that actually calls into ffmpeg
— got the real fix: a new `WaitForFfmpegReadyAsync` that waits out a legitimate `Processing` state
(shows the row's existing "Queued" stage, polls with a 60s ceiling and the phase-143 watchdog's
wedge detection as a safety net) instead of rejecting immediately. Only `Idle`/`LoadingCore`/
`Error` still fail immediately, each with its own accurate message now (not a blanket
"Click Initialize" for every non-Ready state).

New pure `FfmpegBusyPolicy` class encodes the state→message mapping (the actual crux of the bug —
`Processing` must NOT be in the immediate-failure set) so it's unit-tested independent of the
Blazor component. 6 new tests, 1632/1632 suite passing.

Live-verified the core scenario: added one cached file to the timeline (triggering a debounced
auto-preview re-render), then immediately clicked a second cached file — it showed "Queued" (not
the old rejection) and resolved to "Done ✓" automatically once ffmpeg cleared, no manual retry.

> Fixed phase 151, 2026-08-13 — see `README-phase-151.md` in Github-BenVideo.

## 68. Export dialog's action buttons (Export Now / Add to Queue) can render off-screen (✅ fixed 2026-08-13, phase 152)

Root cause confirmed live: `TelerikWindow`'s `WindowActions` footer renders as a **direct child of
`#app`** (the document root), in ordinary document flow with `position: static` — not anchored to
the window's own positioned box at all. Confirmed by walking the "Export Now" button's
`parentElement` chain: `BUTTON → DIV#app → BODY → HTML`. Setting an explicit `Height` on the
`TelerikWindow` correctly bounds the window's own content area but has zero effect on the footer,
since it was never inside that box. Fix: moved the action buttons into `WindowContent` as an
in-content flex footer instead of using `WindowActions` at all — matching the pattern
`ProjectListDialog` (no reported bug) already uses. `WindowContent` became a flex column with the
settings form scrolling internally (`flex: 1; overflow-y: auto`) and a non-scrolling footer pinned
below it.

Also fixed the 3 mojibake ellipses in `ExportService.cs` flagged when this item was found
(`"Concatenating segmentsâ€¦"` etc. → correct `…`) — same double-encoding pattern still unfixed in
`ClipStore.cs`.

Also fixed, found live during this phase's own verification: the CRF quality slider was stuck at
Kendo's 200px default width inside a much wider row, cramming all 11 tick labels together. The
`width: 100%` CSS rule silently never matched because `TelerikSlider`'s root div is rendered by a
child component and never receives the parent `.razor.css` file's Blazor CSS-isolation scope
attribute — needed `::deep` (same pattern already used correctly elsewhere in
`VideoPreview.razor.css`/`VideoTimeline.razor.css`). This is a concrete instance of item #30's own
root-cause note below — the identical bug is very likely present, unverified, in three other
editors' own sliders (`ClipEditor`, `AudioClipEditor`, `ImageClipEditor`).

> See `README-phase-152.md` in Github-BenVideo for full detail.

## 69. File → Save reports success but the project does not restore on reload (✅ fixed 2026-08-13, phase 148)

`File → Save` shows a "Project saved." success toast, but a subsequent page reload does **not**
restore the project: no clips on the timeline, no error message, no "restoring…" indicator,
nothing in the console — indistinguishable from a session that was never saved at all. Live-
reproduced twice with a clean, deliberate `File → Save` (not the export-completion "Save this
project so you can come back later?" prompt, which was tested and correctly honored a Skip choice
in an earlier, discarded test run). Confirmed the failure holds both immediately after reload and
after re-clicking Initialize (ruling out "restore only runs post-Initialize" as an explanation).

`ProjectPersistence` is one of the Full-Featured demo's explicitly enabled flags, and the File
menu draws a clear distinction between "Save" (presumably in-app/OPFS-backed persisted state) and
"Save to Device…" (presumably a download-to-disk action) — so this reads as a real, currently
broken feature, not a documentation or expectation mismatch. Root cause not investigated (found
during a no-production-code verification phase) — worth checking `ProjectStore`'s save path
actually persists to OPFS/IndexedDB versus silently no-oping, and whether the restore-on-load path
is wired up at all for this demo configuration.

This is the most significant finding from phase 147's cross-browser stress verification — a user
could reasonably believe their work is safe after seeing "Project saved." and lose it entirely on
the next reload.

**Root cause, confirmed via code research:** nothing anywhere ever called `ProjectStore.OpenAsync`
(or any restore method) automatically on startup, in either the Playground or the real app —
`VideoEditor.razor`'s startup only ever loaded the project *index* (for the Open dialog's list),
never any actual project content. `SaveAsync`/`OpenAsync` themselves round-tripped correctly; the
gap was purely "nobody ever calls restore." There was also no persisted "which project is active"
pointer for a restore call to have used even if one existed.

**Fix:** new `bv-proj-active` localStorage key (written on save/open, cleared on New/Delete-of-
active) plus `ProjectStore.RestoreLastActiveAsync()`, called from `VideoEditor.razor`'s startup
behind the `ProjectPersistence` flag. Live-verifying this turned up two more, smaller pre-existing
bugs in the OPFS-restore path that only manual File → Open had been exercising until now — both
fixed in the same phase: `RestoreOpfsFilesAsync` never called `OPFSService.EnsureInitAsync()`
before checking `IsAvailable` (so on a truly fresh reload, with no prior OPFS operation to set it
as a side effect, media never actually remounted — clips came back with correct metadata but a
permanent "media missing" warning), and it never notified `ClipStore` after clearing that warning
flag (so even once fixed, the icon stayed stale until an unrelated re-render happened to clear it).

New `ProjectStoreRestoreTests.cs` (`ProjectStore` had zero prior test coverage) simulates a reload
via two instances sharing one fake localStorage — 6 new tests, 1621/1621 suite passing. Live-
verified end to end twice: Save → hard reload → clip metadata AND media both restore correctly,
warning icon clears once ffmpeg initializes.

> Found during phase 147 cross-browser stress verification, 2026-08-13; fixed phase 148,
> 2026-08-13 — see `README-phase-147.md` and `README-phase-148.md` in Github-BenVideo.

## 70. Extend the native sidecar beyond export rendering to free the main thread generally (✅ COMPLETE — all 5 phases 158–162 shipped 2026-08-13)

The native sidecar (phases 121-124, `NativeSidecarBackend`/`RenderService`) currently only offloads
the *final export render* (`IRenderBackend.RenderAsync`) to the native process. Everything else that
runs heavy ffmpeg work — the auto-preview timeline re-render (`VideoEditor.RefreshWorkingWindowAsync`,
via `Ffmpeg.ConcatCopyAsync`/`ConcatClipsAsync`), import metadata/thumbnail extraction
(`Ffmpeg.GetMetadataAsync`/`ExtractThumbnailsAsync`), and general clip-art/effects processing — still
runs through the main `FfmpegService` (ffmpeg.wasm) on Blazor WASM's single main thread, competing
with everything else that thread does (HTTP calls, JS-interop marshaling, rendering).

This is the direct, confirmed mechanism behind item #66 (fixed phase 149, but only with a timeout
bump — see that item's writeup): the auto-preview render's own JS-interop traffic delayed an
unrelated HTTP download's completion callback by 30+ seconds. If the sidecar handled *general*
ffmpeg operations — not just export — that class of main-thread contention would be structurally
eliminated rather than just tolerated with a longer timeout.

Real, separate scope: would mean extending `IRenderBackend` (or a parallel abstraction) to cover
probe/thumbnail/concat operations, not just full-timeline export, and deciding how/when the
Playground and real hosts opt into it for non-export operations. Noted here as a future direction,
not scoped or started.

**Scoped 2026-08-13 into a 5-phase plan** (user chose the full multi-phase arc over a single
surgical slice). Plan lives at `~/.claude/plans/also-the-sidecar-must-generic-whisper.md`.

- **Phase 158 ✅ shipped** — protocol v3 foundation: `GET /v1/capabilities` handshake (so the
  browser asks what a given sidecar build can do rather than assuming), ffprobe as a second
  *optional, fails-soft* verified tool in `FfmpegLocator`, and the job model generalized beyond
  one kind. Also added `SidecarJsonOptions.LenientResponses` — responses from a newer sidecar must
  not be fatal to an older browser build (with strict `Disallow`, one additive field would throw
  and read as "no sidecar here", silently losing a working connection).
- **Phase 159 ✅ shipped** — the first real main-thread relief: import-time `ffprobe` metadata and
  thumbnail-strip extraction now run in the sidecar when paired. `POST /v1/probe` is deliberately
  *synchronous with its own concurrency limit* (a sub-second metadata read queued behind two long
  encodes would be strictly worse than the wasm path it replaces); thumbnails go through the job
  lifecycle with a manifest + per-file result endpoints, where the manifest doubles as the
  authorization list so traversal never reaches the filesystem. Thumbnail bytes never enter the
  WASM heap — `fetchAsBlobUrl` does fetch→Blob→objectURL entirely in JS. `SidecarMediaProbe`
  returns null on every failure mode so an import never fails because a companion process is
  unhealthy. 27 new tests incl. a fixture that reads the real `ffmpegInterop.js` to keep the
  thumbnail argv in lock-step with the JS. See `README-phase-158.md`/`README-phase-159.md`.
- **Phase 160 ✅ shipped** — remote segment retention ("dual residency") + a concat job kind.
  Segments still land in MEMFS exactly as before and the sidecar *additionally* retains a copy, so
  the wasm fallback survives the sidecar dying mid-session — losing retention costs a re-render,
  never correctness. Concat takes ids the sidecar already holds, so no bytes cross the loopback for
  inputs, and uses the *same* `ExportArgBuilders` method the browser calls (shared via
  `InternalsVisibleTo`, so it can't drift). Every concat input is pinned for the job's duration —
  without that the LRU could evict an input between the existence check and ffmpeg opening it.
  Missing inputs return 409 with the full missing list so the caller re-renders exactly those.
- **Phase 161 ✅ shipped** — auto-preview concat offload; the first phase a user can *feel*. When
  the gate opens, the assembled Working Window preview never crosses the WASM heap, never takes the
  worker mutex, and never touches MEMFS. `PreviewConcatGate` is pure/static and requires every
  condition (no transitions, all-`bgseg_`, sidecar advertising concat, every segment in the remote
  index); the conservative direction is always wasm, because a wrong "yes" drops footage while a
  wrong "no" only costs speed. `PreviewUrlRevoker` routes revokes by origin now that a preview URL
  can be JS-minted, defaulting unknown URLs to the worker route so pre-existing callers stay
  correct.
- **Phase 162 ✅ shipped** — export concat + audio mix offloaded as a single job, closing the arc.
  The plan's gating design question was resolved first by audit: all three overlay passes emit
  `-map 0:a? -c:a copy`, a pure stream copy, so moving the mix earlier is safe and the pre-planned
  concat-only scope cut wasn't needed. `BuildAmixArgs` was extracted from `ExportService` with its
  parity test transcribed from the inline code *before* the move. The audio `FilterChain` is
  allowlisted by character class (it can't be range-checked and is machine-generated).
  ⚠ **Audio sync of a sidecar-mixed export is untested** — deliberate, user-approved: this machine
  has no real ffmpeg so the A/B was impossible. Do that A/B before relying on the mix path in
  production (see `README-phase-162.md`).

**Fallback guarantee (user-confirmed requirement, 2026-08-13):** if the sidecar isn't installed,
everything falls back to the browser's ffmpeg. Enforced at four independent layers —
`GetConnectionAsync()` returns null, `HasCapability()` is false, the gates return an
unavailable/blocked decision, and every facade (`SidecarMediaProbe`, `SidecarPreviewAssembler`,
`NativeClipEncoder`) returns null on *any* failure so the caller runs the existing `Ffmpeg.*` path
in the same pass. The sidecar is opt-in and unpaired by default, so a user who never installs it
gets exactly the pre-arc behavior.

> Noted by the user 2026-08-13, while discussing item #66's fix and whether the sidecar already
> covered this (it didn't — sidecar was export-only before this arc).

## 71. Make the ffmpeg status badge's "busy" state more visually distinct (✅ fixed 2026-08-13, phase 163)

User observation while live-verifying phase 151 (item #67's fix): after clicking Initialize, the
toolbar's ffmpeg status badge already distinguished `Ready` from `Processing… N%` by color, but the
user's read of it in the moment was that it just "says ready" without a clear enough signal of
busy-vs-available. Requested an "info" color and stronger distinction.

**Turned out to be more than cosmetic.** The item itself flagged the question — "does every
operation that should count as busy actually flip `FfmpegState`?" — and the answer was no:
`FfmpegService.IsWorkerBusy` (built phase 142, accurate for every worker call including lighter
ones) was never read by the badge at all. `GetMetadataAsync`/`WriteFileAsync`/
`ExtractThumbnailsAsync` — all heavily used during import — hold the worker lock the same as a full
`exec` but never set `Processing`, so the badge showed "Ready" for the entire duration an import
was genuinely blocking the worker. Not low-contrast — wrong.

Checked scope against item #70 first (a sidecar user hits this less often now that probe/
thumbnails/preview/export can run off the main thread) — still worth doing, since it fires on any
sidecar fallback and fully applies to non-sidecar installs. New `FfmpegStatusPresentation` (pure,
unit-tested, `FfmpegBusyPolicy`'s pattern from item #67) computes the label/CSS-class/tooltip; both
busy shapes (real Processing, and the newly-caught gap) collapse to one shared "busy" visual
treatment — Telerik's own `--kendo-color-info` token, filled chip, the same pulse `LoadingCore`
already used. Live-verified by polling the badge during a real import and catching `"Busy…"` /
`bv-status--busy` during the exact window that used to read "Ready".

> Noted by the user 2026-08-13, while live-verifying phase 151; fixed 2026-08-13 phase 163. See
> `README-phase-163.md` in Github-BenVideo.

## 73. Resize-handle and shape-control-point drag bypass the keyframe branch entirely (✅ fixed 2026-08-13, phase 164)

While fixing item #63 (keyframe-branch canvas edits bypass undo/redo), traced every
`Motion.EditLayer` call site to find which ones needed the new undo wiring. Found that
`CalloutControlPointOverlay.razor`'s and `ClipArtControlPointOverlay.razor`'s on-canvas
**resize-handle drag** (the draggable corner/edge squares) and, for Callout, **shape
control-point drag** (Arrow/Line curve handle, Star outer/inner radius, Rectangle corner radius)
never call `Motion.EditLayer` at all — confirmed by reading both files end to end. They write
straight to the clip's static `X`/`Y`/`Width`/`Height`/`ControlPointValues` unconditionally via
`Clips.UpdateCallout`/`Clips.CommitCalloutUpdate` (and the ClipArt equivalents), with no
`Motion.HasPath` check anywhere in either file. Both overlays render unconditionally whenever the
layer is the active selection (`VideoPreview.razor:81-97`), regardless of whether it's animated.

**Real-world impact**: dragging a resize handle or a shape control point on a layer that already
has a motion path silently has **no visible effect** at any keyframed time —
`MotionKeyframeService.Evaluate()` overrides X/Y/Width/Height/control-points from the keyframe
data whenever a path exists, so the static-field write these overlays make is invisible during
playback at any time a keyframe governs. This is a missing-feature bug, not merely an undo gap —
worse than item #63 was, which is why it's a separate item rather than folded into that fix.
Item #63's own original text incorrectly assumed these two overlays already routed through the
keyframe branch and only lacked undo; they don't route through it at all.

**Scope for the real fix**: route both overlays' `OnPointerMove`/`OnPointerUp` through
`Motion.EditLayer` exactly like `CanvasSelectionOverlay`'s body-drag and `SetSize` already do
(writing to a keyframe's `ScaleX`/`ScaleY` — and, for Callout, `ControlPointValues` — relative to
the clip's own base size, per `CanvasSelectionOverlay.SetSize`'s existing "edit the effective,
keyframe-aware value" pattern), then apply item #63's same `CommitKeyframeEditIfAnimated`-shaped
undo wiring on top once that's in place.

**Fix**: both overlays now route every drag through `Motion.EditLayer`, mirroring
`CanvasSelectionOverlay`'s body-drag/`SetSize` exactly — resize converts effective Width/Height to
ScaleX/ScaleY relative to the clip's static base size; control points write `ControlPointValues[key]`
directly (confirmed via `ApplyMotionFrame`/`LerpControlPoints` that this needs no relative
conversion, the same way X/Y doesn't). The subtler half: a live drag upserts one keyframe per
frame, so a per-frame running state seeded from *effective* geometry is required — re-reading the
clip's static fields each frame would never accumulate the delta while animated, since only the
keyframe changes. The resize-handle/control-point positions and the shape outline were also
switched to read from that same effective/live source, or they'd still be drawn at the stale
static position on an animated shape even after the write-side fix.

Live-verified thoroughly: Callout resize on an animated layer showed `Scale X 2.92x`/`Scale Y
4.42x` in the keyframe editor (matching the drag ratio exactly) with undo cleanly reverting to
`1.0x`/`1.0x`; a second, unanimated Callout's resize still wrote to the static fields unchanged
(regression check); the Rectangle corner-radius control point on an animated layer went
`rx: 4.0 → 40.0 → 4.0` through drag+undo; ClipArt resize on an animated Star clip moved the
Properties panel's own "● Live" Width value and reverted correctly. No unit tests — matching item
#63's own precedent, this logic has no clean pure-C# extraction point and this codebase tests
Blazor-component drag logic live only.

> Found 2026-08-13 during phase 154 (item #63's fix); fixed 2026-08-13 phase 164. See
> `README-phase-164.md` in Github-BenVideo for full detail. Also see `README-phase-154.md` in
> Github-BenVideo.


---

## 74. Post-#70 code audit — findings #1–#7 (✅ ALL FIXED 2026-08-13, phases 165–169)

A read-only audit of Ben.Video.Editor requested after the item #70 arc closed, covering UI gaps and
programming gaps. All seven findings are fixed and merged.

| # | Finding | Phase |
|---|---|---|
| 2 | `DisposeAsync` revoked preview URLs through the ffmpeg-worker route even when sidecar-origin — a regression introduced by phase 161 itself | 165 |
| 1 | Export **Cancel** couldn't interrupt a running encode | 166 |
| 3 | The codebase's only `async void` (JS interop on a tear-down-able component ⇒ unobservable exceptions) | 167 |
| 5 | Project delete was instant, permanent and unconfirmed | 167 |
| 6 | `DeleteAsync`/`RenameAsync` swallowed failures while their `Save`/`Open` siblings rethrew | 167 |
| 7 | **Toolbar "Open" crashed the entire app** when the Media panel was closed | 167 |
| 4 | All 30 `eval` call sites replaced with typed interop modules | 169 |

**#7 is the one that mattered most, and the audit didn't find it — live verification did.** The
toolbar's Open button is the file picker; it ran `eval("getElementById('bv-file-input').click()")`,
and that input unmounts with the Media panel, so with the panel closed it null-dereferenced and
killed the circuit (yellow error bar, reload required). Phase 157's minimized default had made it
reachable on a fresh load. Fixed twice over: `?.click()` so it cannot crash, *and* reopening the
panel so the button actually works — a guard alone would only downgrade it to "does nothing".

**#1's shaping constraint, verified before designing:** ffmpeg.wasm commands cannot be aborted
mid-flight (no abort channel; only `terminate()`, which destroys the worker and every cached
segment). Per phase 143's standing rule — *never kill an in-flight export without consent* — no
force-stop was added. Cancel now stops at the next *command* boundary rather than the next *phase*,
and the UI says so instead of appearing broken.

**#4 was not a security hole** and shouldn't be remembered as one: every interpolated value was a
constant, a typed `Guid`, or a loop index, and user text was correctly double-JSON-encoded. The
real costs were the `unsafe-eval` CSP requirement and the #7 crash class. Fixing it also uncovered
a latent bug hiding inside an eval string — `ClipBrowser` read `naturalWidth` synchronously right
after setting `img.src`, so image dimensions were `[0,0]` essentially always.

**Known gap:** the #5/#6 delete-confirm and error-banner UI is *not* live-verified — reaching the
project dialog needs saved projects, and seeding them is what uncovered #7 instead.

> See `README-phase-165.md` … `README-phase-169.md` in Github-BenVideo.

---

## 75. Native sidecar wired into the Ben solution (✅ COMPLETE 2026-08-14, Ben.Video phase 173)

Item #70 finished the sidecar's *capability*; this covers making it usable from the Ben app rather
than only from the WASM Playground.

**The blocker, which was not obvious.** Everything sidecar-related had been built and verified
under Blazor **WebAssembly**. `Ben.Web.WebApp` is Blazor **Server**, and the sidecar binds
`127.0.0.1` on the *user's* machine. The JSON half of the protocol went out over a C# `HttpClient`,
which resolves that address wherever the Blazor code executes — the browser under WASM (correct by
accident), the **server** under Blazor Server. A server-side request also carries no `Origin`
header, and `SecurityMiddleware` 403s every endpoint except bare `/v1/health`.

The resulting symptom was worse than an outright failure: **health succeeded, so the toolbar chip
reported a sidecar had been found, and only pairing failed** — reading as a mistyped pairing code
rather than a request that never reached the user's machine. Measured against a real sidecar:
`/v1/status` + valid token with no Origin → 403; the same request with an allowlisted Origin → 200.

**Fixed** by routing every sidecar request through the browser (`SidecarTransport` →
`sidecarInterop.js`); 7 call sites converted and the named `HttpClient` removed. A guard test greps
the sidecar services for `IHttpClientFactory`/`CreateClient(` — nothing else catches a regression,
because a new C# call site still works fine in the WASM Playground.

**Also in this item:** `Ben.slnx` gained `Ben.Video.Sidecar` (+ `Core`, `RenderService`,
`Sidecar.FakeFfmpeg`) under *Media Projects*; `options.NativeSidecar = true` in
`Ben.Web.WebApp/Program.cs`; the Ben dev origins added to `SidecarOptions.AllowedOrigins`; and a
build break fixed where `BenMediaLibraryProvider` had never implemented the `IProgress<double>`
parameter `IMediaLibraryProvider.DownloadFileAsync` gained in Ben.Video phase 150.

**⚠ Before this works off localhost:** the production origin must be added to
`SidecarOptions.AllowedOrigins` (via appsettings). Nothing else is needed; without it every request
past `/v1/health` is rejected.

> See `README-phase-173.md` in Github-BenVideo.

---

## 76. A finished render can go to the server or the user's machine (✅ COMPLETE 2026-08-14, Ben.Video phase 176)

**The user's model, stated explicitly:** the *rendered video* goes either to the server as a final
video or to the user's machine. The *project* is a separate concern — JSON export to save, JSON
open to resume. Two different questions; they now have two different prompts.

**What existed before:** export → blob download to Downloads → the retained copy deleted.
Publishing meant going to the project list, clicking Publish, and **re-selecting the exported file
out of Downloads** through a hidden `<InputFile>`. Nothing connected the file the editor had just
produced to the file the user picked back up.

**Now:** `<VideoEditor OnPublishExport="…" />`. Supply a handler and "Export Now" ends with a
destination prompt — *Upload to server* / *Save to my machine* / *Discard*. Leave it unset and
export behaves exactly as before; the gate is `HasDelegate`, not a new options flag, because a host
with no handler has no second destination to offer. Queued exports still download directly — nobody
is present to answer a prompt for a job the user queued and walked away from.

Host side: `VideoExportPublisher` (`Ben.Web.Library/Services/`) holds the two-step all three editor
pages need. `POST /api/video-projects/{id}/publish` attaches to an *existing* project row and 404s
without one, so a render made without ever saving to the server would have nowhere to go — it saves
the project first in that case, then publishes to what it just created, and remembers the id so a
second export updates that project rather than adding a row per render.

**Two decisions that look like bugs and aren't:**
- The retained-export discard is deliberately **not** in a `finally`. A host that throws keeps its
  output, so the prompt can stay open and still offer "Save to my machine" instead of deleting the
  only copy of a render the user just waited through. Handlers must therefore **throw** to report
  failure — returning normally means "safely stored".
- The bytes are read from the **blob URL, not OPFS**. A retained export always has a blob URL,
  including on the OPFS-unavailable branch (Safari private browsing) where it is minted from MEMFS.
  Reading OPFS would have uploaded an empty body in exactly the browser nobody tests.

The old publish-from-Downloads flow stays, for files the user already has on disk.

> See `README-phase-176.md` in Github-BenVideo.

---

## 77. Full end-to-end validation pass — main Ben solution ✅ Complete (2026-08-14)

Comprehensive validation of the whole Ben solution (not Ben.Video, which had its own item #9 pass) — every user journey (public/anonymous, client, org staff, SuperAdmin, media), checking for bugs/gaps in the UI, plus a code-streamlining pass. Prompted by "let's start over" — a fresh, broader pass distinct from the earlier Ben.Video-only testing.

> **In progress.** Bugs found and fixed so far:
> - `bootstrap.bundle.min.js` was loaded on every page but never used — nothing in the codebase calls any Bootstrap JS API (no `data-bs-toggle`/dropdown/modal/etc.; Telerik owns all interactive components). The deferred script re-executing on Blazor's enhanced navigation threw an uncaught `Cannot redefine property: delegateTarget` on every soft page navigation. Removed the script tag; kept the Bootstrap CSS/icon links, which are genuinely used app-wide.
> - **The entire Ben.Video editor was crashing on load, everywhere it's embedded** (`/my-videos`, `/video-editor`, every case's video editor) — a Razor comment (`@* ... *@`) was placed inside `<ExportDestinationPrompt>`'s opening tag, between two attributes. Razor comments aren't valid inside a tag's own attribute list, so the compiler passed the raw comment text through as a bogus component parameter name, throwing `InvalidOperationException` on first render. Fixed in the Ben.Video.Editor repo (commit `abd3bfd`) by moving the comment above the tag.
> - **Accepting a client's investigation request into a case silently dropped the client's actual report** — `CaseController.AcceptClientRequest` copied every address field from the `ClientRequest` onto the new `Case` but never copied `Description`, so the client's submitted narrative (what they experienced, since when) vanished the moment staff accepted the request, with no UI path back to the original `ClientRequest` record. Fixed by copying `Description` across; verified live end-to-end (submitted a request with a marker string, accepted it, confirmed the new case's Overview tab shows a "Case Summary" panel with that text).
> - **22-page AuthReady race left hard-navigated pages rendering empty with no error** — `OnInitializedAsync`'s `WaitUntilAuthReadyAsync` guard (the established pattern from an earlier session's 25-page audit) only protects that lifecycle method's own body; it doesn't carry over to the separate `OnAfterRenderAsync(firstRender)` hook these pages use for their actual data fetch (a deliberate split, to avoid firing authenticated API calls during static SSR prerender). On a hard navigation the bearer token isn't guaranteed attached by the time that later hook's first render completes, so the fetch can go out unauthenticated and quietly render an empty list/grid. Reproduced live on `OrganizationList` (Organizations page showed "No records available" on hard nav, fine via in-app link clicks) and `OrgPendingRequests` (a real pending request briefly vanished the same way). Swept every other page with the identical shape and applied the same guard, in two passes: first pass — `OrganizationList`, `OrganizationView`, `OrganizationClientSettings`, `OrganizationMembershipQuestions`, `CaseDetail`, `OrgPendingRequests`, `OrgCmsEditor`, `OrgCmsPageEdit`, `AdminExperienceTaxonomy`, `AdminLookupTypes`, `AdminRoles`, `OrgScheduler`, `OrgMessages`; a corrected, more thorough sweep caught 9 more the first pass missed — `OrganizationCreateEdit`, `AdminAllCases`, `AdminAllInvestigations`, `AdminUserDetail`, `AdminUsers`, `CaseList`, `CaseNotes`, `CaseAudioMixPage`, `UploadFiles` — live-confirmed `AdminAllCases`/`AdminAllInvestigations` genuinely showed 0 records on hard nav before the fix, 9/9 and 2/2 real rows after. (`PublicCaseDiscovery`, `ClientRequestWizard`, `MainLayout`, and the non-routed child component `AudioFilePreview` excluded — false positives or not independently navigable.)
> - **SuperAdmin permanently lost "Administration" nav access after using "Return to SuperAdmin"** to exit impersonation, until logging out and back in. `StopImpersonating` tried to restore `IsSuperAdmin` by re-parsing the saved original access token with `JwtClaimsParser` — but the Identity API issues opaque, data-protected tokens, not JWTs (an existing code comment on `LoginAsync` already documents this), so the parse silently returned `false`. Traced from a missing nav button, through the layout, through the token store, to this parsing call — `LoginAsync` already had the correct fix (call `/api/me` server-side instead of parsing). Applied the same pattern to `StopImpersonating`, now async: `WebApiAuthService.cs`, `IWebApiAuthService.cs`, `IBenAdminClient.cs`, `BenAdminClientAdapter.cs`, `MainNavigationDrawer.razor`, plus 2 test files. Live-verified: impersonate → Return to SuperAdmin → Administration button present, `/admin/cases` no longer redirects to Home.
> - **Audit Log page (`/admin/audit-log`) threw `InvalidOperationException` on every single load**, including anonymous/prerender requests — a bare `return;` inside a markup `@if (!UserState.IsSuperAdmin) { ...; return; }` block left the outer `<div>` element frame unclosed in Razor's render tree ("A frame of type 'Element' was left unclosed"). Fired unconditionally because `IsSuperAdmin` defaults false during static SSR prerender, before the real logged-in user's role is known. Removed the markup-time guard, replaced with the same `OnInitializedAsync` redirect-guard pattern every other SuperAdmin page already uses. Live-verified: 36 real audit records render with working filters.
> - **File Types admin page**: a stray literal `\n` (two characters, not a real newline) had landed inside the "Extensions" grid button's markup text, rendering as `"\n Extensions"`. One-line fix.
> - **The evidence Image Editor never worked at all, for six independent, stacked reasons** — every "Edit" click on an image file either crashed outright or silently rendered a black canvas: (1) `<TelerikWindow Title="...">` — no such parameter, needs `<WindowTitle>`; (2) `<TelerikTextBox PlaceholderText="...">` — the real parameter is `Placeholder`; (3) `image-editor.js` contained its entire ~800-line source duplicated back-to-back, so `const _instances` was declared twice — a `SyntaxError` that failed the module import outright (kept the newer, more complete copy with the Evidence Tools/Layers features the component actually calls); (4) even once the script parsed, the image still wouldn't load — the `UploadFile` download endpoint is `[Authorize]`-protected, but the editor handed Fabric.js's `fromURL()` that URL directly for an unauthenticated browser-side fetch, which always 401'd (fixed by fetching bytes server-side via the authenticated `HttpClient` and passing a base64 `data:` URL instead, matching every other media preview in the app); (5) Fabric.js v6 (loaded via CDN) rewrote `fromURL()`/`loadFromJSON()` from v5's callback signature to Promise-based, so the old callback was silently never invoked; (6) Fabric v6 also renamed the object-stacking API (`sendToBack`/`bringForward`/`sendBackwards` → `Canvas.sendObjectToBack`/`bringObjectForward`/`sendObjectBackwards`) — also fixed `destroy()` leaking the old `<canvas>` DOM element on every re-open. All six root-caused live via `DetailedErrors: true` surfacing the real .NET exceptions in the browser console (the WebApp's own log file carries almost no runtime output — Serilog is configured to write only to a SQL `Logs` table at `Error` level, not console) plus direct reflection against the installed `Telerik.Blazor.dll` to find real parameter names instead of guessing. Live-verified end-to-end: image loads, renders at correct aspect ratio, rotate/tool interactions work, clean console on a fresh load.
>
> **Found, reproduced, NOT yet fixed** — needs deeper Telerik-specific investigation beyond this pass's scope:
> - **Both `TelerikDialog`-hosted forms with a Title-gated Save button are permanently stuck disabled, blocking two real workflows entirely**: the Calendar's "New Event" dialog (`OrgScheduler.razor`) and the case "Schedule Investigation" dialog (`InvestigationPanel.razor`). Both use the identical pattern `<TelerikTextBox @bind-Value="@_form.Title" />` for the input and `Enabled="@(!_isBusy && !string.IsNullOrWhiteSpace(_form.Title))"` on the Save button. Typing a real title (verified via direct DOM inspection — the underlying `<input class="k-input-inner">` genuinely holds the typed text, not a placeholder) never enables Save. Ruled out: automation-tool typing artifacts (per-key `key` presses reproduce it identically to bulk `type`; other `TelerikTextBox`/`TelerikWindow`-hosted forms elsewhere in the app — e.g. "Add a Person," "New Role" — work correctly with the same typing method); stale-render display only (toggling the unrelated "All Day" `TelerikCheckBox` in the same dialog *does* visibly update and *does* prove a fresh re-render occurred, yet Title still reads as empty in that same re-render, meaning the underlying `_form.Title` genuinely isn't being set — not just a UI staleness issue). Both affected dialogs share `<TelerikDialog>` specifically (as opposed to `<TelerikWindow>`, which works fine elsewhere) — the common factor is suspicious but unconfirmed as the actual root cause. **Currently a hard blocker**: there is no way to create a calendar event or schedule an investigation through the UI at all. Needs either a Telerik-version-specific fix or a workaround (e.g. switching these two dialogs from `@bind-Value` to explicit `Value`/`ValueChanged` with a manual `StateHasChanged()`, which was not attempted live in this pass to avoid guessing at a fix without being able to verify the root cause first).
> - **Media Library grid's lazy "click-to-preview" for large audio/video files never completes** — clicking "Preview" on the 7.1 MB test mp3 in `/media-library` left the card permanently stuck (no waveform, no spinner, no error banner), while the browser's SignalR WebSocket connection repeatedly disconnected and silently reconnected underneath it (multiple new circuit IDs within ~15 seconds). The *same* file, rendered eagerly inside `AdminUserDetail`'s Files-tab grid (a different embedding of the identical `AudioFilePreview`/`WaveSurferPlayer` component), loads its waveform correctly — isolating the bug to `MediaLibraryGrid.razor`'s deferred-render path specifically, not `AudioFilePreview` itself. This is very likely the fetch-storm/large-payload risk the original Media Library Phase 1 plan explicitly flagged and deferred ("`UserMediaPreview` fetches full file bytes as base64 for both images and video/audio — there's no thumbnail endpoint... a real thumbnail endpoint is explicitly deferred to Phase 1.5/2") — this is that deferred risk now confirmed as a real, user-facing failure for files in the several-MB range, not just a theoretical one. Needs either the deferred thumbnail/streaming endpoint, or at minimum investigation into why the same component fails only when mounted via the grid's on-demand `RenderFragment` trigger.
> - **Media Library shows no visual distinction between a file and its case-copy of the same name** — copy-on-attach (item #6 phase 2) creates a second `UploadFile` record with an identical filename/size but a different `Id`, owner, and `Allow*Comments` settings (linked via `CaseCopyOfUploadFileId`). The grid has no badge or indicator marking a card as a copy, so two visually-identical cards can sit side by side; clicking "Comments" on one and being told "You don't have permission" while the other (same name, "your" copy) allows it reads as a bug but is actually two different files behaving correctly. Not a functional defect — the permission check was verified correct via direct API calls — but a real UX clarity gap worth a small fix (e.g. a "Copy" badge, or grouping copies under the original).
>
> Confirmed working across a wide sweep: public org discovery + case voting, the full client request-submission wizard (address verification → org matching → submit → staff accept-into-case), My Cases/My Investigations/My Requests, case messaging, related-people management, Upload Files, Media Library (grid/list/voting/comments on owned files), org Members/Cases/Calendar-viewing/Messages/Files/CMS/Roles(list+create+delete)/Addresses/Settings tabs, dark/light theme toggle, full SuperAdmin journey (Users/impersonation round-trip/Roles/cross-org Cases+Investigations/File Types/Lookup Types/Experience Taxonomy/Audit Log/New Organization), image preview in Media Library, audio waveform preview in AdminUserDetail's Files tab, all case-detail tabs (Overview/Timeline/Investigations/Files/Transfers/Messages/Reports/Research/Notes/Video) including Notes create/edit/delete round-trip, Audio Mixer page, and the full Case creation flow (end-to-end: new case #2026-005 created successfully in BenCo).
>
> **Known remaining gap in the Image Editor fix above**: `OrganizationFiles.razor`'s embedding passes an `ImageUrl` override (`AdminClient.GetOrgFileDownloadUrl`) instead of `UploadFileId`, for editing org-scoped files — that URL is equally `[Authorize]`-protected and hits the identical unauthenticated-fetch failure, but there's no existing `GetOrgFileDataAsync`-style byte-fetch method to swap it for (only the URL-returning variant exists on `IBenAdminClient`). Out of scope for this pass; needs a new API endpoint + client method mirroring `GetFileDataAsync`, or an anonymous signed-URL scheme.
>
> **Phase 6 — code-streamlining pass.** Reviewed the full session diff (36 files) for cruft; almost all of it was already-minimal one-line `AuthReady` guards with nothing to trim. The one genuine finding: the build carried 4 pre-existing "unused field" (`CS0414`/`CS0649`) warnings, and every one of them turned out to be a real bug rather than dead code — `CaseVideoEditorPage._myUserId` was read in an ownership check but never assigned, so `row.CreatedByAppUserId == _myUserId` was always false and the Publish/Delete buttons never rendered for any video project, for any user (removed the check entirely — the list is already server-filtered to the caller's own projects, so it was redundant on top of broken); `CaseVideoEditorPage._publishing`/`MyVideosPage._publishing` were set during an in-flight publish but never read, leaving the Publish button clickable mid-upload with no double-submit guard (wired `Enabled="@(!_publishing)"`); `ImageEditorPlayer._saving` had the same shape around the two Save actions, fixed with a re-entrancy guard since `WindowAction` has no `Enabled` parameter to bind to. Build is now 0 warnings, 0 errors.
>
> Still to test: a code-streamlining pass over anything touched this session.

---

## 78. Group type expansion — UFO / cryptid / other paranormal (long-term, deferred 2026-08-15)

**Decision: the site stays ghost-hunting-only for now.** Scoped, not built — long-term scope.
Revisit when the paranormal feature set has settled and there is a real second kind of group asking
to join.

Groups today are differentiated only by geography (`OrganizationAreaOfOperation`) and the
accepting-clients flags. `OrganizationType` exists nowhere in the codebase.

**Decided with the user, and still standing:** many-to-many — a group can claim several types and
appears under each. Seed set would be Paranormal/Ghost, UFO/UAP, USO, Cryptid/Bigfoot, Other.

### What it would cost

*The lookup machinery is genuinely cheap.* A new family (`OrganizationLinkType` is the template)
costs 2 entity files, a `DbContext.Generated` registration + migration, an admin record, a one-line
AutoMapper profile, a 12-line controller inheriting `AdminEntityControllerBase`, a public read
controller, and **one line** in `_families` at `AdminLookupTypes.razor:179`. The catalog admin UI
comes free, and `Name/Description/IconClass/ColorClass/IsActive/IsPublic/SortOrder` is already the
right shape for a type badge.

Phases, roughly:

| | Work | Size |
|---|---|---|
| G1 | `OrganizationType` lookup family + seed | S |
| G2 | `OrganizationOrganizationType` join + multi-select in org settings + audit | S–M |
| G3 | Badges on the public org home, `/find` results, org list | S |
| G4 | Public browse-by-type endpoint + a `/find` mode that works without a location | **M — the real work** |
| G5 | Type matching in the client request wizard (optional) | S–M |

### Two findings worth keeping regardless of whether this is ever built

**`/find` cannot browse.** `OrgDiscovery.razor` gates its search button on a non-empty location
query, and the only public list endpoint is `GET /api/public/organizations/search?lat=&lon=` —
there is no "all organizations" endpoint. So the "Browse All Groups" button on the home hero lands
on a page that demands a location before it will show anything. That is a discovery gap in its own
right, independent of types, and it is most of G4's cost. **Long-term scope as well** — recorded
here so that whoever picks up discovery work later knows the gap exists and that it does not
actually depend on group types.

**Do not copy the ExperienceCategory approval workflow.** Its schema has `IsApproved`,
`ProposedByOrganizationId`, `ApprovedByAppUserId`, `DateApproved` — but every write path in
`ExperienceCategoryController` hardcodes `IsApproved = true`. No org can propose anything; there is
no propose UI and no approval queue. It is the write-only-feature pattern this codebase has
produced before. If group types ever become org-proposable, that flow has to be built for real, not
inherited. SuperAdmin-curated is the recommendation.

### Questions to settle before starting

1. Do types **restrict** who a client can send a request to, or only label and sort? Restricting
   puts logic in the search endpoint and the wizard; labelling drops G5 and shrinks G4.
2. Is browse-without-location in scope? If not, types are visible but not filterable.
3. Curated or org-proposable? (Recommend curated — see above.)
4. Could the **Experience taxonomy** do the matching job instead? Clients already tag what happened
   at wizard step 3. Group type answers a different question — what a group *does* — so keep them
   separate, but G5's matching could plausibly be built on experience tags already collected, with
   no new taxonomy at all.

---

## 79. Contact / support page for visitors ✅ Complete (2026-08-15)

A page where a visitor — signed in or not — can ask for help with the site or reach a member of
staff. Needs CAPTCHA and anti-spam. The site's contact details (PO box, phone, anything else)
should live in the database, not in markup, so they can be corrected without a deploy.

> **Shipped on `feature/support-tickets`.** Built as recommended below: the ticket is the record,
> and email is a notification on top that is allowed to fail — so the whole thing works today with
> SMTP unconfigured. Anti-spam is honeypot + data-protected form token + rate limits by email and
> IP; **no CAPTCHA**, per the ordering below. The one design addition beyond the sketch is the
> **tracking link**: an opaque token gives an anonymous sender their own thread to read and reply
> to, which is how a staff reply reaches somebody with no account and no working mail. Contact
> details live in `SiteSettingKeys.Seed` as predicted, with a `MultiLineKeys` set added so the
> postal address gets a textarea. Details in `README-support-tickets.md`.

### Recommendation: build the ticket, send the email as a notification on top

**Build the internal ticket store, not email-only.** Three concrete reasons, all specific to this
codebase rather than general principle:

1. **The inbox already exists.** `UserMessage`/`UserMessageTo` and the notification bell already
   deliver system messages to specific users, and item #78's taxonomy notice now uses that same
   path to reach every SuperAdmin and Admin. A contact ticket landing there is a small addition,
   not a new subsystem.
2. **Email-only loses the thing you most need.** With mail alone there is no record of what was
   asked, no state, no way to tell an answered question from a forgotten one, and no way for a
   second admin to see that the first already replied. For a site whose whole job is keeping
   careful records of what people report, "we'll just email it" is the wrong default.
3. **Email is not actually finished.** `SmtpEmailService` exists (built for the sub-client invite
   flow, item #4) but SMTP is unconfigured, and there is no `Smtp`/`Email` section in
   `Ben.Data.WebApi/appsettings.json`. Making the contact page depend on it means the page does
   not work until that is done — and when a message silently fails to send, nobody finds out. A
   stored ticket is durable whether or not mail is configured.

So: **the ticket is the record; email is a notification about it, and is allowed to fail.** Send
mail when SMTP is configured, log and carry on when it is not, and never let a send failure lose
the message.

### Sketch

- `SupportTicket` entity — from-name, from-email, subject, body, an enum topic
  (Website Help / Report a Problem / Contact Staff / Other), optional `AppUserId` when signed in,
  status (New / Open / Answered / Closed), assigned admin, timestamps, source IP.
- `SupportTicketReply` for the thread, so an answer lives with its question.
- `POST /api/public/support-tickets`, anonymous. On success, a `UserMessage` to every SuperAdmin
  and Admin — the exact pattern `OrgExperienceTypeController.NotifyAppAdministratorsAsync` now
  uses.
- Admin page under Administration: list, filter by status, reply, close.
- Contact details from the database (below) rendered on the same page.

### Anti-spam, in the order worth building

1. **Honeypot field** — a hidden input real people never fill. Free, catches most naive bots.
2. **Rate limit by IP and by email address** — e.g. 3 per hour, 10 per day, enforced server-side.
   .NET's built-in rate limiting middleware covers this without a dependency.
3. **Minimum fill time** — a form submitted under ~3 seconds after render was not typed by a
   human. Needs a signed timestamp so the check cannot be skipped client-side.
4. **CAPTCHA last.** Prefer a privacy-respecting one (hCaptcha or Cloudflare Turnstile) over
   reCAPTCHA, and note that it adds a third-party script and a server-side verify call — the first
   external runtime dependency the public site would carry. Worth adding only if 1–3 prove
   insufficient in practice, which for a site this size they may not.

Signed-in users should skip the CAPTCHA entirely — the account is the rate limit.

### Site contact details in the database

The existing `SiteSettingKeys.Seed` mechanism (`SiteSettingsService`, admin page at
`/admin/site-settings`) already does exactly this job: declare a key in code, edit the value in the
UI, no deploy. Add `contact.address`, `contact.phone`, `contact.email`, `contact.hours` there
rather than building a parallel table. A fake PO box can go in immediately and be corrected later
from the admin page.

Only caveat: site settings are single-valued strings, so a multi-line postal address wants either
one setting per line or a settings page that renders a textarea. Worth deciding when picked up.

---

## 80. CMS: preview, templates, publish-when-ready, and embedding cases/investigations safely (not started, requested 2026-08-15)

Ben's request, five parts. The first three are ordinary CMS maturity. The fourth is where the real
design work is, because it is the point where a group could publish somebody's home address.

### 1. Preview a page and its pending changes

Today `OrgCmsPageEdit.razor` edits sections in place and saves straight to the live row. There is no
way to see the page as a visitor would before committing. Two shapes, and they are not equal in
cost:

- **Preview the saved-but-unpublished page** — cheap. `OrganizationPage.IsPublished` already exists
  and the editor already exposes it. A preview route that renders the page through the public
  renderer while ignoring the publish flag (for members with `CmsPageAction.View`) is most of the
  value for very little work.
- **Preview *unsaved* edits** — expensive, because it needs draft storage. See part 3.

### 2. A template library

Both granularities Ben asked for. A section template (one `CmsSection` with pre-filled
`ContentJson`) and a whole-page template (an ordered set of sections). The six section types in
`CmsSectionType` — RichText, ImageBanner, FileGallery, ContactInfo, MemberRoster, CustomHtml — are
the vocabulary a template is assembled from, so this is mostly a seeding-and-cloning job rather than
new rendering.

Open question worth settling early: are templates **site-provided only**, or can a group save its
own page as a template and reuse it? The second is a small extra step (a "save as template" that
clones sections) but changes ownership and permissions.

### 3. Draft vs live

`IsPublished` exists, the editor exposes it, and `OrgCmsEditor` already shows the state per page.
What does *not* exist is a draft that differs from what is live — editing a published page edits the
live page immediately, which is the actual gap behind Ben's "make them live when they are ready".

The honest options:

| Approach | Cost | Notes |
|---|---|---|
| Publish flag only (today) | done | Editing a live page is still live-editing |
| Draft copy of the page + sections, promoted on publish | M | Real drafts; needs a clone + swap and a "discard draft" |
| Version history with a published-version pointer | L | Gives rollback too; the most work |

Recommend the middle one unless rollback is wanted, in which case go straight to versions rather
than building drafts twice.

### 4. Embedding cases and investigations — the part with teeth

Appending public cases and investigations to a page is straightforward. **Private investigations are
not**, and Ben's two safeguards are the right ones. Both must be enforced **server-side, before the
data leaves the WebApi** — his own stated requirement, and the codebase already has the pattern for
one half of it.

**4a. Address obfuscation.** Show a ~5-mile circle instead of a pin; show city and state and say the
address is redacted. The redaction happens in the projection, so the exact coordinates are never in
the response at all.

> **Finding, worth knowing before this is built:** `PublicCaseDiscoveryController` already has fields
> named `ApproxLatitude` / `ApproxLongitude` — and passes `c.Latitude` / `c.Longitude` straight
> through. The name promises an approximation the code does not perform. Any published case's exact
> coordinates are public today. That is a live exposure independent of this item, and it is the first
> thing to fix when this is picked up (or sooner).

Note also that a circle drawn *centred on the true point* still leaks the point — the centre is the
answer. Jitter the centre within the radius, or snap it to a grid cell, so the circle is honest about
what it hides.

**4b. Client identity.** Replace real names with the alias the client configured, again before the
response is built, leaving the stored case untouched. **This already exists for cases**:
`Ben.Data.WebApi/Controllers/Public/PublicClientName.cs` is the single place that decides a client's
public name, prefers the client's own `ClientDisplayAlias` over the org's `PublicPseudonym`, and
falls back to *no name* rather than the real one. CMS embedding must route through that same helper
rather than growing a second answer — the whole reason it exists on its own is that two endpoints
showing the same person must not drift.

What it does **not** yet cover: other people on a case (`CaseRelatedPerson`, witnesses,
investigators). If a CMS block can embed those, they need the same treatment, and there is currently
no alias concept for them at all.

**Warn before, not after.** Ben's flow — warn on adding a private investigation, then ask about
address, then ask about identities — is worth keeping in that order. The warning is what makes the
two questions land as decisions rather than as a settings screen.

### 5. Suggested phasing

| | Work | Size |
|---|---|---|
| — | **Fix `ApproxLatitude` to actually approximate** (do this regardless) | S |
| C1 | Preview route for saved-but-unpublished pages | S |
| C2 | Section + page templates, site-provided | M |
| C3 | Drafts (or versions) so publishing is a deliberate act | M–L |
| C4 | Embed public cases / investigations | M |
| C5 | Private investigations behind the warning + the two redaction switches | M — the careful one |

Both redactions belong in one place each, reusable by any surface, in the shape `PublicClientName`
already set.

---

## 81. Score the haunting vote instead of only counting it (not started, requested 2026-08-15)

Ben: *"For voting on haunting, we use 3 values. I want the indecisive to equal zero, then +1 for
haunted and -1 for not convinced."*

Today `EvidenceVoteType` has three members and every surface reports them as three independent
counts — `ConfirmsCount` / `DisputesCount` / `InconclusiveCount` in `PublicCaseVoteController`,
`PublicCaseDiscoveryController`, `InvestigationController` and `EvidenceVoteDetailPanel.razor`.
Three numbers is an accurate report and a poor summary: nothing says whether a case leans haunted,
and two cases with wildly different weight of opinion can look similar.

The change: a single signed score, `Confirms +1`, `Inconclusive 0`, `Disputes −1`.

**The trap to avoid.** The enum's stored int values are `Confirms = 0, Disputes = 1,
Inconclusive = 2`, and those numbers are already in the database. The score must come from a
mapping function, **not** from renumbering the enum — renumbering would silently re-interpret every
vote ever cast.

Worth settling when this is picked up:

- Does the score go alongside the three counts or replace them? (Alongside, probably — the counts
  are what make a score trustworthy.)
- Sum, or average? A sum rewards a busy case; an average says how strongly the people who looked
  actually felt. Inconclusive votes pull an average towards zero, which is arguably the point of
  casting one.
- One place computes it, reused by every surface — the same rule `PublicClientName` follows, and
  for the same reason: four endpoints each doing their own arithmetic is four answers.

Deferred by Ben — *"we can work on that later."*
