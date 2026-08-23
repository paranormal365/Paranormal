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

## 9. Thoroughly test the Ben.Video component (recurring practice, not a finishable item — both tracked bugs fixed)

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

## 54. Member photos (✅ Complete — delivered by Area 4, closed out 2026-08-17)

Let members add a profile photo, with a per-photo choice of visibility: public (visible to anyone,
e.g. on a public case page or org roster) or members-only (visible only to other active members of
the same organization).

> Raised 2026-08-11 alongside item #55 (equipment tracking) — Ben WebApi/WebApp, not Ben.Video.Editor.

**Delivered by the "Things to Add" roadmap's Area 4 (U1–U6), merged to `main` 2026-08-15** — which
went further than this entry asked, giving the product its first self-service account surface:

- `AppUserPhoto` with **two slots**, public and private, one active each via a filtered unique
  index, rather than one photo with a visibility flag. Two pictures serve two purposes; forcing one
  photo to be either meant choosing between a face colleagues recognise and one a stranger may see.
- `GET /api/users/{id}/avatar` — the caller names a **person**, never a photo, and the server
  decides which (if either) that viewer gets. `AvatarCache` is circuit-scoped for correctness, not
  memory: avatar resolution depends on who is asking.
- The **two-key consent rule** in `PrivatePhotoConsent.MayShowToClient`: showing a member's private
  photo to a client needs both the group's policy and the member's own opt-in.
- `/profile`, `UserMenu`, plus witness photos and `Case.ClientDisplayAlias`.

**Closed out 2026-08-17.** `UserNameLink.ShowAvatar` existed but no caller ever set it, so the whole
avatar-rendering path was unreachable in the UI — the same write-only shape this backlog has now hit
three times. Now wired on the organization member roster, investigation attendee lists, and file
comment threads.

**Still unverified:** the `InputFile` → upload chain has never been exercised by a real click (the
dev tooling cannot drive an OS file picker). Profile and witness photos were verified via the API.

> The public **CMS member roster** section (`CmsSectionType.MemberRoster`) is still a placeholder
> that renders "Member roster section." — that belongs to item #80's CMS phases, not here.

## 55. Equipment inventory & checkout tracking (BUILT through phase 6 — header was stale; see item 86 for what was deliberately left)

> **Header correction (2026-08-20):** this read "not started" long after the fact. Personal and
> organization equipment, sharing, checkouts, photos, FAQs, counters and loan feedback all shipped
> across phases 1–6; all four phase-6 branches are content-verified in develop. What remains is
> recorded honestly in item 86 (deliberate leftovers), plus one default decision and Ben's own
> click-test. The description below is kept as the original spec.

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

## 80. CMS: preview, templates, publish-when-ready, and embedding cases/investigations safely ✅ Complete (all parts built 2026-08-17)

Ben's request, five parts. The first three are ordinary CMS maturity. The fourth is where the real
design work is, because it is the point where a group could publish somebody's home address.

### 1. Preview a page and its pending changes

Today `OrgCmsPageEdit.razor` edits sections in place and saves straight to the live row. There is no
way to see the page as a visitor would before committing. Two shapes, and they are not equal in
cost:

- **Preview the saved-but-unpublished page** — ✅ **built 2026-08-17**, alongside the coordinate fix,
  because it needed no schema and no decision. `/organizations/{OrgId}/cms/pages/{PageId}/preview`
  and `CmsPagePreviewController` return exactly the public endpoint's shape, so `OrgPublicSection`
  draws it and a preview cannot drift from the real page. **Only the publish flag is relaxed** —
  which sections show and in what order stays the public rule, or the preview would be reassuring
  about a page that will not look like that. Gated on `CmsSection` Read, answering 404 rather than
  403 so an unpublished page is not confirmed to exist by the shape of the refusal. Both guards
  verified by deletion.
- **Preview *unsaved* edits** — still open, and still expensive, because it needs draft storage.
  See part 3.

> **Found while building it:** `OrgCmsPageEdit` already had a "Preview" button, but it renders a
> **second, hand-written approximation** of each section (`RenderSectionPreview`) in a half-width
> column — a duplicate of `OrgPublicSection` that will drift from it. It has a real use the new
> preview cannot serve (it follows what you are typing, before saving), so it was kept and renamed
> "Side-by-side", with "View as visitor" beside it for the real thing. **Collapsing the two
> renderers is worth doing when part 3 lands**, since a draft store would let the side-by-side panel
> use the public renderer too and the duplicate could go.

### 2. A template library — snippet insertion

Ben clarified this three times on 2026-08-17, and the third is the one that settles it:

1. *"The templates are things like build a card with a header and body or build a collapsable set of
   items. It is functionality and look helpers."*
2. *"The templates created by users are ones that put together the ones we create on the
   server-side. Like carrousel."*
3. *"They can pick from a list and it adds to their html editor where it places it in for them to
   fill in the parts."*

**So this is a snippet palette, not a page builder.** An author editing a rich-text or custom-HTML
section picks a block from a list; its markup is inserted at the cursor with the parts left blank,
and they type into it. A "user template" is whatever they end up with — a carousel assembled from
inserted pieces — saved as a block of their own to insert again later.

That is dramatically less machinery than the earlier readings on this entry (a pre-filled section
type, then a nested node tree with a visual composer). **Both of those overshot** — this needs no new
`CmsSectionType`, no nesting model, no tree in `ContentJson`, and no changes to the public renderer
at all, because the output is ordinary markup in the section types that already render markup.

**What it actually needs:**

- A **snippet catalogue** — name, description, and a markup template with obvious placeholders. Card
  with header and body, collapsible list, carousel, two- and three-column strips, call-to-action.
  Site-provided, seeded, and using the Bootstrap classes the public pages already load, so a snippet
  looks right without shipping new CSS.
- A **picker in `CmsSectionEditor`** that inserts at the cursor rather than replacing the field.
- **Group-owned saved snippets** for the user tier — a name plus a blob of markup. The same
  reasoning as group-owned equipment: it is the group's site, and a member leaving should not take
  its building blocks with them.

**✅ Built 2026-08-17 — the block palette half.** `CmsSnippets` is the catalogue (card, card with
header, collapsible list, carousel, two columns, three across, callout, button link) and
`CmsSectionEditor` gained an **Add a block** picker for the RichText and CustomHtml section types.
Ids are made unique per insertion, and a test proves it by failing when they are fixed. Blocks are
appended rather than inserted at the cursor: the cursor lives inside Telerik's rich-text engine and
is not reachable from C#, and appending is honest about that rather than putting things somewhere
surprising. Insertion goes through the same binding a typed edit takes — writing to the DOM would
update neither the editor nor the saved JSON, a trap this codebase has already hit.

**Two real gotchas, worth handling on the first pass rather than after somebody hits them:**

- **Unique ids per insertion.** Bootstrap collapsibles and carousels are wired by `id`/`data-bs-target`.
  Insert two carousels into one page from the same snippet and they will drive each other. The
  inserter must rewrite the ids to something unique at insertion time.
- **Sanitization is now load-bearing.** These sections already render as `MarkupString`, so the XSS
  surface pre-dates this — but a palette that *encourages* pasting structural markup makes it much
  more travelled. Sanitize on save, allow-listing the tags and attributes the snippets actually use
  (including the `data-bs-*` the components need), rather than trying to block what is dangerous.
  **Not done, and it is the open risk on this part.** The unique-id test asserts no snippet *we*
  ship carries `<script>` or `<style>`, which is a different and much weaker guarantee than what an
  author may type into the same box.

### 2b. Page templates — "Investigation Results" ✅ built 2026-08-17 (both halves)

> Ben, fourth clarification: *"They create templates like Investigation Results and it gives them a
> page to fill in or chose from their media and records in the case to add to the template."* And:
> *"So, they put together the pieces they need to fill in for it to complete."*

**This is a different feature from 2a, and much larger.** A block snippet is markup with blanks an
author types into. A **page template** is a named, structured document — *Investigation Results* — that
presents a set of **slots**, and an author fills each slot either by typing or by **choosing from the
case's own media and records**. Assembling which slots a template has is itself something the author
does: they put together the pieces the write-up needs.

The distinction that matters for design: 2a's output is markup and knows nothing about the domain.
2b's output is **bound to a case** — a slot holds "this photo from this case's media library", not a
copy of it — which is why this overlaps **part 4** almost entirely and should be built with it rather
than before it.

**What it needs, roughly in dependency order:**

- **A template definition**: an ordered list of slots, each with a kind — free text, a heading, one
  photo from the case, a set of photos, a timeline entry, an investigation summary, equipment used
  on a visit, a piece of evidence and its vote score. Group-owned, and composable by the author.
- **A fill-in surface**: a page listing the template's slots, each with a picker scoped to **this
  case**. This is where most of the work is, and most of the value.
- **Rendering**, honouring part 4's rules — which is why it must not be built first. Coordinates are
  already redacted (fixed 2026-08-17); client names already go through `PublicClientName`; **private
  investigations and non-public media do not yet have an equivalent**, and a template that can pull
  "any photo from the case" into a public page is precisely the hole part 4 exists to close.
- **Binding, not copying.** A slot referencing a photo that is later deleted, or a case that is later
  unpublished, must degrade to nothing rather than to a broken image or a leak. Copying the photo
  into the page at fill-in time would make the page immune to a later "unpublish this" — which is
  exactly the wrong behaviour.

**Sequencing recommendation:** part 4 first (the redaction rules and the safe projections for
embedding case data), then 2b on top of it. Building 2b first means writing the pickers twice, and
the first version would be able to publish things part 4 is meant to prevent.

> **✅ Part 4 built 2026-08-17.** See above. 2b is now unblocked.

#### 2b, first half — page layouts ✅ built 2026-08-17

**The storage already existed and nothing could reach it.** `CmsTemplateScope.Page` was defined,
saved, listed, updated, deleted and sanitized by `OrgCmsTemplateController` — and no screen or
endpoint ever created a page from one. The **sixth** write-only feature in this codebase, and the
quietest, because every individual layer worked.

So this half cost far less than the entry assumed:

- `CreateCmsPageRequest.FromTemplateId` copies a page template's sections onto the new page,
  **server-side**, so the sanitizer sees the markup on its way in. "Cleaned when the template was
  saved" is not "clean now", and a rule enforced only by the browser is not a rule.
- **Copied, not referenced**, matching the decision already recorded on the entity — proven by a test
  that edits the template afterwards and asserts the page is untouched.
- Another group's template, a section-scoped one, a deleted one and unparseable content all yield a
  **bare page** rather than a failed create: the page is what the caller asked for.
- UI both ways — **Save as a layout** on the page editor, and a layout picker when creating a page.
  A source-scan test asserts both halves exist, because either alone is useless.

#### 2b, second half — the case-bound slot ✅ built 2026-08-17

`CmsSectionType.CaseMedia`. The stored content is a case id, the chosen file ids in the author's
order, and a caption switch; the public endpoint replaces it with a projection built by re-asking
`CaseMediaPublication` on every request. Two independent gates, both re-checked at render: the case
must belong to this group, and each file must still be publishable.

- **Binding, not copying — proved, not asserted.** A test narrows a timeline entry's visibility
  *after* the section is saved and asserts the photo leaves the page. Another unpublishes the case.
  Neither edits the page. That was this entry's stated requirement and it is the one that would have
  been quietly lost by the far simpler copy-at-fill-in-time design.
- **No `IncludeNonPublic` escape hatch**, deliberately, and unlike part 4's embeds. For a record the
  group owns, an acknowledgement is a real decision somebody can make. For an investigator's working
  file, nobody has ever said it could be shown — so the one route stays the one the prerequisite
  describes, and the section offers no way around it.
- **Captions are off by default and absent from the payload when off.** The caption is the timeline
  entry's own title — the group's working description — so it is withheld at the server rather than
  hidden in the renderer.

> **Found while building it, and it would have shipped broken:** *nothing could serve the bytes.*
> The prerequisite decided which files may be published, but `/api/upload-files/{id}/download` gates
> anonymous callers through `FileAudienceAccess.CanViewFileAsync`, which grants only files flagged
> `IsPublic` or shared to a Public target. A photo on a Public timeline entry is neither. The rule
> said publishable; the pipe said 401. Every visitor would have seen broken frames — and **the
> author never would**, being logged in and therefore inside the audience union.
>
> `PublicCaseMediaController` (`/api/public/cases/{caseId}/media/{fileId}`) closes it by asking
> `CaseMediaPublication.MayPublishAsync` per request, 404 either way so a refusal does not confirm
> an id. The route carries the case because "may this file be published" is only answerable in the
> context of one.
>
> **The cheap fix was the dangerous one.** Setting `IsPublic` on the file when an author picks it is
> two lines. That flag is global and permanent: it would outlive the page, survive the entry being
> pulled back to private, and grant the file to every other endpoint at once. Publishing a photo on
> one page would have quietly handed it out everywhere, for good — the exact opposite of the
> binding-not-copying discipline the rest of this item is built on.
>
> The reachability test guards all three halves, the third being that the renderer uses the public
> media URL *by name*: pointing it at the ordinary download URL compiles, passes every resolution
> test, and looks right to whoever built it.

#### 2b prerequisite — which of a case's files may be published (✅ built 2026-08-17)

The gap this entry flagged — *"private investigations and non-public media do not yet have an
equivalent"* — was half true. Investigations got theirs in part 4; **media had nothing at all**, and a
slot offering "a photo from the case" would have been a way to publish the investigators' working
files.

`CaseMediaPublication` answers it, and deliberately **grants nothing new**: the rule is the one the
public case page already follows, restated in one place rather than invented. A file is publishable
when it hangs off a timeline entry that is `Public`, on a case that is itself public. A template can
publish what a visitor could already reach and not one file more.

- **Resolved at read**, like `CmsEmbed`. Narrowing an entry to `OrgOnly` next month withdraws its
  photo from pages published today; unpublishing the case withdraws everything at once. Both proven
  by tests, which is the binding-not-copying requirement this entry asks for.
- **`Client` visibility is not public.** Shared with the family is not shared with the world — the
  one a reader gets wrong by treating "shared" as "not internal".
- Both guards verified by breaking them.

> **⚠ A decision for Ben.** Files on the case's **general Files tab** (`CaseFile`) are treated as
> never publishable, because that table **has no visibility column at all** — there is no answer to
> "did anybody agree to this being public?", and defaulting would publish in bulk exactly the
> material nobody has reviewed. That matches today's behaviour exactly (the public case page has
> never shown them). If those should be publishable, `CaseFile` needs a visibility field and a person
> to set it, which is a product decision rather than a guess.

### 3. Draft vs live — ✅ built 2026-08-17

**Ben chose the draft copy** over version history, accepting no rollback for noticeably less work.

A draft is a whole `OrganizationPage` row with its own sections, pointing at the page it will
replace via `DraftOfOrganizationPageId` (unique, filtered). That shape is the point: every public
query already filters `IsPublished && IsPublic`, and a draft is created with both false, so **the
public read path needed no changes at all** and future queries cannot forget to exclude drafts.

- **Copy-on-write, published pages only.** Nobody can see an unpublished page, so editing one
  directly is already safe and a draft would be ceremony. `POST .../draft` is idempotent — two
  editors opening a page at once must not make two drafts, and the unique index would otherwise turn
  the second into a 500.
- **Publishing copies onto the live row and deletes the draft**, rather than swapping ids. The live
  page keeps its id, so links, permission rows and attached cases all survive.
- **`IsHome`, `IsPublished`, `IsPublic` and `CaseId` are deliberately not copied** — they are the
  page's place in the site, not content. A test asserts publishing a draft cannot make an
  unpublished page live, which would otherwise be a way to publish something by accident.
- The editor routes to the draft's own id, so from there it edits an ordinary page row and nothing
  else in that screen knows drafts exist.

Guards verified by breaking them: making the draft published/public, and copying the visibility
flags on publish, each fail their tests.

**Still open:** the side-by-side editor panel still has its own hand-written section renderer.
Collapsing it into `OrgPublicSection` is easier now drafts exist — the panel could preview the draft
through the real renderer — and is worth doing alongside part 2's new section types.

### 4. Embedding cases and investigations — ✅ built 2026-08-17

Two section types, `EmbeddedInvestigations` and `EmbeddedCases`. The design decision everything else
follows from: **references are stored, records are resolved.** The section holds ids and switches,
never a copy of the data — so redaction runs on every request against live rows, and a client who
withdraws their alias next month disappears from pages published today. A snapshot taken at embed
time would freeze whatever happened to be true that afternoon and outlive every later decision.

**The published shapes have no field for the dangerous values.** No exact latitude, no street
address, no real name anywhere in `EmbeddedInvestigation` or `EmbeddedCase` — absent, not nulled.
Reflection tests assert that, which is the cheapest guard here and the strongest: every other test
checks what the code currently puts in the payload, this checks what the payload is *able* to hold.

Rules, all enforced at read:
- **Ownership is re-checked when the page is rendered.** The picker offers only the group's own work,
  but a picker is a convenience and a request can say anything.
- **Work not already public needs a deliberate acknowledgement**, so a section saved by an older
  editor cannot publish something by omission.
- **Client names route through `PublicClientName`**, which has no branch that returns a real name.
- **Malformed settings publish nothing.** Elsewhere in the CMS an unparseable section renders an
  empty box; here it would be deciding whether an address goes out, so it fails closed.
- **Preview resolves identically** to the public endpoint — a preview that redacted differently would
  be reassuring about a page that will not look like that.

The editor keeps Ben's order: warn about non-public work, *then* ask about the address, *then* about
identities. The warning is what makes the two questions land as decisions rather than as settings.

**Found while testing:** the resolver emitted PascalCase while the renderer reads camelCase, so every
embedded card would have rendered blank on a real page. Caught by a test asserting the title *is*
published — not by any of the ones asserting an address is not. The positive tests earned their place
again.

**Found by the discrimination run:** breaking the location switch on the investigation branch alone
failed nothing, because every location test happened to use a case. Two branches resolve locations
and only one was covered. Both are now.

### 4-original. Embedding cases and investigations — the part with teeth

Appending public cases and investigations to a page is straightforward. **Private investigations are
not**, and Ben's two safeguards are the right ones. Both must be enforced **server-side, before the
data leaves the WebApi** — his own stated requirement, and the codebase already has the pattern for
one half of it.

**4a. Address obfuscation.** Show a ~5-mile circle instead of a pin; show city and state and say the
address is redacted. The redaction happens in the projection, so the exact coordinates are never in
the response at all.

> **✅ Fixed 2026-08-17, ahead of the rest of this item.** `PublicCaseDiscoveryController` had fields
> named `ApproxLatitude` / `ApproxLongitude` and passed `c.Latitude` / `c.Longitude` straight
> through — every published case's exact coordinates were public. A live exposure independent of this
> item, so it was fixed on its own rather than waiting for the CMS work.
>
> `PublicCoordinates.Approximate` now snaps to a grid cell (~7 miles of latitude, widened by
> 1/cos(latitude) so cells stay roughly square on the ground) and publishes the **cell centre**.
> **Snapped, not jittered** — a random offset per request would let anyone average many responses
> back to the true point; snapping is deterministic, so there is nothing to average, and every case
> in a cell publishes identically.
>
> Two things found while building it. A test asserting that neighbours publish identically caught a
> real flaw in the first version: the longitude step was derived from the caller's *true* latitude,
> which made the published longitude a continuous function of it, so two houses on one street landed
> metres apart and the snapping did nothing. It is now derived from the snapped latitude. And the
> existing test was named `GetAll_ReturnsStoredCoordinates_WithoutGeocoding` — it asserted the leak
> and pinned it in place.

Note that a circle drawn *centred on the true point* still leaks the point — the centre is the
answer. The grid centre published above has nothing to do with the property, and any circle a client
draws must be at least `PublicCoordinates.RadiusMiles` for it to honestly contain the true location.

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

## 81. Score the haunting vote instead of only counting it (✅ Complete — shipped 2026-08-17)

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

Deferred by Ben — *"we can work on that later."* **Picked up and shipped 2026-08-17.**

### What was built

`EvidenceVoteScore` in `Ben.Data.Common`, beside the enum it weights — `Weight`, `Score` (from
votes) and `FromCounts` (for the discovery list, which never holds individual votes). All four
surfaces reuse it: `PublicCaseVoteController`, `PublicCaseDiscoveryController`,
`InvestigationController` and the widgets.

The three open questions, settled:

- **Alongside the counts, not replacing them.** The counts are what make a score trustworthy, and
  the entry's own leaning was right.
- **A sum, not an average** — the literal reading of what Ben asked for. `TotalVotes` already
  travels with it everywhere, which is what stops a sum being read without its weight. An average is
  this sum over that count if it is ever wanted, and needs no new storage.
- **One place computes it**, as planned.

`VoteScoreBadge` renders it identically on every surface: **+n** green, **−n** red, **0** amber
rather than grey (an even split is a real answer, not a missing one), with the vote count in the
tooltip.

### The trap, held

The enum's stored values are untouched. Two tests guard it from opposite directions: one pins
`Confirms = 0, Disputes = 1, Inconclusive = 2`, and one asserts every weight *differs* from its own
stored value — so an implementation that quietly cast the stored number could not pass. The
controller test was verified by removing the wiring and watching it fail.

---

## 82. Two P3 gaps left open: attendee findings, and the roster on case-less visits (both closed 2026-08-15)

Found auditing Area 9's permission phase after it shipped. Neither is a bug in what was built —
both are places where something was built halfway and the missing half is a feature, not a fix.

### 82a. `CanCompleteMyFindings` has nothing to complete ✅ built 2026-08-15

`InvestigationAccess.ComputeFlagsAsync` computes it (attendance-based: you were there, so you have
something to say), `OrgInvestigationRow` carries it across the wire, and the mirror in
`IBenAdminClient` documents it. **No screen reads it, because there is no per-attendee findings
feature to gate.** The nearest thing is the investigation's own `Summary`, which is one field for
the whole visit and manage-gated — the opposite of what this flag describes.

**Built.** Ben chose the feature over deleting the flag. `InvestigationFinding` — one account per
person per visit, written only by that person, revisable, withdrawable. No manager override, and
that is the point: whether somebody turned up is a fact another person can attest to, and what they
experienced is not. The grid shows a **Your write-up** badge on past visits you attended and have
not written up, which is the flag finally driving something.

Left for later: findings do not feed the case timeline, and there is no way to attach a file to one.

### 82b. The roster is only on case-bound investigations ✅ fixed 2026-08-15

**Fixed** by putting the roster where the visits already are: every row of the group's
Investigations grid has a **Team** button that expands the roster inline, gated on the same
`CanEditRecord` verdict the Edit button uses. One row open at a time, because each roster runs its
own poll timer. No new page and no detail view — the grid was already the place a case-less visit
lives.

Live-verified on "Cave return visit", a visit with no case: the team panel opens under the row.

---

## 83. Explain and list available permissions to assign to roles individually in an organization (closed 2026-08-17, pending click-test)

Ben, raised mid-planning-session for item #55: *"Explain and list available permissions to assign
to roles individually in an organization — each one has their own."*

Previously `OrgRoleEditor.razor`'s Permissions section rendered one create/read/update/delete toggle
row per `PermissionSection` (a hardcoded `DisplayName` + `OrganizationSecurityTable` pair), and the
`DisplayName` was the only explanation a role-builder got — "Files", "Membership Applications",
"Investigations." What each toggle actually *grants* only existed as an XML doc comment on the
corresponding `OrganizationSecurityTable` enum value, invisible from the UI.

**Built**: `PermissionSection` now carries a fourth field, `Description`, rendered as always-visible
small muted text under each row's `DisplayName` — chosen over a hover tooltip so the explanation
doesn't require discovering it. All 22 rows in the current `Sections` list have one. For the eight
values that already carried an XML doc comment (`OrganizationSettings`,
`OrganizationAddressMemberAccess`, `OrganizationAddressSearch`, `MembershipRequests`,
`OrganizationFiles`, `Investigation`, `Equipment`, `EquipmentCheckout`), the description is adapted
from that comment; the other fourteen (plain CRUD tables with no prior doc comment, e.g.
`OrganizationAddress`, `OrganizationNoteType`) got a new one-line description written for this pass.
Text lives only in `OrgRoleEditor.razor` — it is not read from the enum's XML doc comments at
runtime (Blazor Server doesn't ship the doc XML, and reflecting it in would be real machinery for
static strings), so a future doc-comment edit does **not** automatically update the row; whoever
touches the enum doc comment should update the matching `Description` in the same commit.

Not built: the "possibly explain C/R/U/D individually where a table's actions aren't uniform CRUD"
stretch goal — skipped, the per-row description already answers what a role-builder needs and no
current table has meaningfully non-uniform CRUD semantics.

**Guarded by a source scan** (`RolePermissionCoverageTests`). The `Sections` list is hand-written, and
a permission missing from it is *invisible* rather than broken — the enum value exists, the server
enforces it, and no role can ever be granted it. Three assertions: every organization-scoped value has
a row, no row names a value that does not exist, and every row carries a description. Verified by
removing a row and watching it fail.

> **Found by that scan on its first run:** `OrganizationSecurityTable.AppUser` (= 13) is **referenced
> nowhere in the codebase** — no controller checks it, no screen assigns it, and it has no row in the
> role editor. It is excluded from the scan as user-scoped rather than given a row, deliberately: a
> toggle that grants nothing is worse than no toggle, because it tells a role-builder something
> untrue. **Worth deleting**, but renumbering a persisted enum is not free — the stored
> `OrganizationAccessGrant.TableName` values would need migrating — so it is recorded here rather
> than done in passing.

**⚠ Not click-tested.** `Ben.Web.Tests.Services.GrantablePermissionTests` passes (unaffected — it
source-scans for `OrganizationSecurityTable.X` references, and the new code only adds plain string
literals) and the solution builds clean, but the actual rendered rows are behind Entra login, which
this environment cannot pass. Ben should open an org's role editor once and confirm the description
text reads well under real Telerik table layout before considering this fully done.

---

## 84. Organization subscription lapse, and what happens to their clients (CLOSED 2026-08-22)

**Shipped (84a):** `CaseStatus.Paused` (= 8, appended; all 55 branch points audited — public
surfaces were safe by allowlist construction, stats/avatars/filters fixed); `Case.StatusBeforePause`
makes the pause a lossless round trip (Active resumes Active); `SubscriptionLapseJob` runs the
whole clock — two-week warning to the billing people, one-week tell-your-clients prompt, then
lapse + pause open cases + message each case's clients directly, all date-keyed idempotent so a
renewal re-arms the warnings with no clearing code; `WhyReadOnlyAsync` on the guard makes a lapsed
group read-only across the five capped creates plus timeline entries, case files (upload AND
attach), notes, and org case messages — the client's own MyCase surface deliberately stays open;
reactivation restores exactly the cases the lapse paused, via `PeriodOpener.RestorePausedCasesAsync`.
Client banner on the paused case; help docs updated. 7 job tests, regressed two ways.

**Shipped (84b, same day):** `CaseTransferLog` gains `ProposedByClient` + the two consent flags;
`POST/GET/DELETE /api/my-cases/{caseId}/reassign` (paused cases only, one pending move at a time,
the case STAYS Paused while pending so rejection leaves nothing to clean up); consent enforced at
acceptance — withheld history re-scoped to a new `CaseTimelineVisibility.ClientOnly` (org queries
exclude it, the client-side `>= Client` filter admits it by construction; Public entries stay
Public), withheld investigations DETACH and remain the original group's flat records, shared ones
stay attached while still org-owned = dual visibility for dual ownership, no copies, no deletes.
Found and fixed in passing: the receiving side of ANY transfer had no surface at all (the per-case
list requires the case to already be yours) — new org-level `incoming-transfers` endpoint + an
Incoming cases card on the Cases page with the consent summary. The client hears the answer either
way; the move flow lives in the paused banner itself. 7 consent tests, regressed both directions
(consent inverted; detach replaced with delete).

Ben's policy, worked out while sizing monetisation (see item #85 for the billing model itself).
Deferred with the rest of monetisation until the site's functionality and help documents are
complete — this entry exists so the design is not re-derived later.

### The group's own wind-down

"Closed" does not mean gone. Billing stops at the end of the cycle and **everything stays available
until the paid period ends** — so a group that closes is simply active with a known end date, not a
new state. They keep every paid-for ability in the meantime, and can re-enable billing or upgrade
before the date arrives.

At the billing date:

- They **stop being able to add records**. No new scheduling, no new entries dated past the billing
  date.
- **Everything already there remains**: a scheduled investigation stays as a record, along with the
  history collection, visits and client interaction done up to that date.
- Read access continues until they are disabled.

### Notifications, escalating

| When | Who | What |
|---|---|---|
| ~2 weeks out | Owner, Administrators, and the group's **treasurer** if it has one | Billing is ending |
| 1 week out | The group | **They must notify existing clients** their cases will need reassigning |
| Date passes | Clients | Their case is paused; they may choose a new organization |

The owner should be able to **nominate who receives billing notices** rather than the system
guessing from roles — a group's treasurer is not necessarily an Administrator.

### What happens to the clients

Cases the organization worked go into a **paused** state, and the client may select a new
organization to investigate — if they wish to at all.

If they pick a new one, that organization can see the client's existing records, and **the client
chooses what carries over**, per category:

- whether to share the **history collected** by the original group, and
- whether to share the **investigations and investigation records** of the original group.

**Findings remain the original group's — and also the client's.** Dual ownership is the rule, which
is why the client can share them onward without the original group's permission.

This applies to every way a client relationship can end, not only a lapse: the group drops the
client, the client drops the group, or payment lapses. In all three, the information becomes
shareable at the client's discretion.

Groups are to be told all of this **when they join**, not when it happens.

### Notes for whoever builds it

- The per-category share choice is the same **two-key consent** shape already used for private
  member photos — one side offers, the other opts in — rather than a new mechanism.
- "Paused" is a real case state distinct from Closed and Transferred; check `CaseStatus` and the
  places that branch on it (`CaseOrgAccess`, the client-facing `MyCaseController`, the discovery
  endpoints) before adding it.
- The existing `CaseTransferLog` and its Pending/Accepted/Rejected/Cancelled flow is the closest
  prior art for moving a case between organizations — extend it rather than inventing a parallel
  path.
- Dual ownership means deletion by the original group must not remove what the client can still
  share. The equipment work's retire-instead-of-delete rule is the same instinct.

---

## 85. Monetisation: subscriptions, and paid rental (BUILT 2026-08-22 — header was stale like item 55's; the whole arc shipped: domain, admin screens, pricing page, contracts, notices, enforcement, lapse wind-down. Paid RENTAL remains unbuilt and undecided.)

Deferred by Ben until the site's functionality and help documents are complete. Recorded so the
thinking is not repeated. Payment provider undecided — **Square or PayPal** are the candidates.

### The model Ben has converged on

**The platform bills organizations, tiered by member count** — e.g. 1–3 members free, 4–10 at
around $15/month. One merchant, money flowing inward only. Deliberately **not** collecting money the
platform has not earned: a group may run its own member billing, but the platform does not handle
it.

That choice matters more than it looks. Collecting dues on a group's behalf, or taking a cut of
equipment rental, would make the platform a payment facilitator — payouts to each group, identity
verification per group, and the regulatory weight of holding other people's money. Billing
organizations directly avoids all of it and is a fraction of the work (weeks, against months).

Wind-down when a subscription ends is item #84.

### Sizing, if paid rental is ever revisited

| | What | Cost |
|---|---|---|
| Tier 1 | Record the agreed price and deposit; money changes hands outside the app | ~2–4 days |
| Tier 2 | Borrower pays through the app, settled with lenders manually | ~2–3 weeks, plus the question of holding funds |
| Tier 3 | Real marketplace with automatic payouts | ~6–10 weeks, plus terms, tax reporting and likely legal review |

Most of the rental *domain* already exists: the loan lifecycle, one-item-one-holder, due dates and
overdue, condition photos at both ends, ratings, and audit on every transition. Money attaches to
transitions that are already there; it does not reshape them.

### Monetising rental without handling money

**Sell the tooling, not the transaction.** A paid tier for the rental features — deposit tracking,
late fees, and a printable or e-signable rental agreement with the condition photos embedded as
evidence — is worth paying for precisely because the condition-photo and history work already
exists. The platform never touches a payment.

Secondary: promoted listings in the catalog, with the interest counters (phase 6b) as the evidence
that promotion works.

### Design consequences to raise when this is built, not before

- Member count becomes a **billing input**, so add/remove-member and active/inactive transitions
  become financially meaningful, and the tier boundary creates an incentive to under-report. Count
  actives at a defined moment rather than continuously.
- Financially relevant records must not be destroyable. The equipment work's
  retire-instead-of-delete rule is the pattern to follow.

---

## 86. Equipment: what phase 6 deliberately left (recorded 2026-08-17)

Item #55 closed across six phases. These were considered and deferred with a reason, so they are
here rather than lost in a branch README.

**SuperAdmin cross-group equipment browse.** There is no single screen listing every group's gear.
A SuperAdmin passes each group's own permission check anyway, so nothing is inaccessible — it is
purely a convenience, and building it invites treating the whole estate as one inventory when it
belongs to separate groups.

**Folding the org maintenance page into the unified item page.** Phase 6b's `/equipment/{id}` now
serves every audience, and `/organizations/{orgId}/equipment/{itemId}` overlaps it substantially.
The two were left side by side rather than merged mid-phase; the merge is small and safe once
somebody has used both for a while and can say which surface's habits should win.

**Model-page review and FAQ pagination.** Both aggregates cap at 20 with no paging. Fine while no
model has more than a handful; revisit when one does, together with caching, since a model page is
public and cheap to hammer.

**Time-series interest counters.** `ViewCount` and `LinkClickCount` are lifetime totals. "Views this
month" needs a separate table; nobody has asked, and the totals answer the question that prompted
them ("is anyone looking at this?").

**Future-dated borrowing / reservations.** Explicitly deferred with Ben during phase 6a: the request
queue plus visible current-holder covers the need. Revisit only if people start asking for gear
weeks ahead and the queue stops being enough.

**Video and audio metadata stripping.** Phase 6a strips images by re-encoding through SkiaSharp.
A/V needs an ffmpeg remux (`-map_metadata -1`) and ffmpeg is reachable only from the sidecar, not
the WebApi — a hosting decision, not a code change. Metadata is already *extracted* from A/V, so the
Admin view is complete; only the stripping half waits.

---

## 87. Open events — public investigations and open meetings (CLOSED 2026-08-20 — the reminder and its scheduler shipped in phase 6)

Ben: *"An open investigation can be open to the public and if someone wants to attend and let them
know they are coming they have to be a site user. The information to attend and information about
the investigation will be public on the site. These will benefit the organizations because it is
also an introduction to them by people attending. The organization can have open meetings as well...
We have to let them advertise by giving them the opportunity for people to attend. So, giving them
the ability to create open events might benefit us as well by increasing their numbers."*

**This is an acquisition channel, not a scheduling feature**, and that is the reason to build it.
Today the platform is a records system for groups that already exist. A public event listing is the
first thing on it that brings *strangers* in — someone finds a ghost walk at a local landmark,
signs up to attend, and meets a group. That is how groups grow, and it is also how the platform
grows, which puts it squarely alongside item #85's monetization thinking.

### Most of it already exists, and one flag is doing nothing

`OrgCalendarEvent` already has **`IsPublic`**, `MeetingUrl`, an optional `CaseId`, an
`OrganizationAddressId`, and an `OrgCalendarEventAttendee` table with `RsvpStatus`. `Investigation`
already carries an optional `OrgCalendarEventId`.

**But `OrgCalendarEvent.IsPublic` is written and never read.** There is no public endpoint serving
calendar events at all — the only `IsPublic` filter in `OrgCalendarController` is on `UserEmails`.
An organization can tick "public" today and nothing whatsoever happens. That is the **fifth**
write-only feature this backlog has recorded, and it means the substrate for this item is half
built already.

### The shape

**One concept, not two.** An open investigation and an open meeting are the same thing — *a public
event an organization hosts that a site user can say they are coming to*. `OrgCalendarEvent` is
already that, with an investigation optionally attached. Bolting "open" onto `Investigation` and
then adding a second mechanism for meetings would be two half-features that drift.

- **Public read**: `GET /api/public/organizations/{urlName}/events` and a per-event page. Anonymous.
  Title, description, when, where, and how to come — plus the organization, prominently, because
  the introduction is the point.
- **Self-RSVP** needs a **site account**, per Ben — that is the line between browsing and attending,
  and it is what makes an attendee reachable. Its own endpoint, refusing anything not public, and
  creating the `OrgCalendarEventAttendee` row the org currently has to create by hand.
- **Discovery**: an events list across organizations is what actually makes this an acquisition
  channel rather than a page nobody finds. Worth building with it, not after.
- **Interest counters**, reusing the equipment pattern (#55 phase 6b): views and RSVP conversions
  per event, visible to the organization. If this is being sold as advertising, they need to see it
  working.

### The constraint that is not negotiable

**A public event must not be at a private residence.** `InvestigationVisibility.Public` is already
refused there, deliberately: publishing what happens inside somebody's home is theirs to agree to,
and there is no mechanism for asking. A *public event listing with an address and a date* is a far
sharper version of the same problem — it is an invitation to strangers to come to a client's house.

So: open events are for landmarks, public sites, an organization's own address, and case-less
visits. Enforce it server-side on create, not in the UI. The coordinate redaction built for the
case-discovery leak (`PublicCoordinates`) is the right tool where an approximate location should
still be shown before someone commits to attending.

### The evidence bargain, from Ben's earlier message

*"All collected evidence and data is public and cannot be made private for an open investigation.
The location can be scrubbed and hidden to the public not attending, but evidence is not."*

Right, and cleaner now the event is public anyway: if anyone may come, nobody may afterwards decide
what the group saw is theirs to withhold. Two things follow that must be **enforced, not defaulted**:

- Visibility locks once the event is public and anyone has RSVP'd. Otherwise somebody flips it later
  and the people who turned up lose the deal they came under.
- Openness itself is not revocable once anyone has joined.

### 87a. "Near me" — filtering the public calendar by the visitor's location

Ben, same day: *"people who allow us to read their GPS coordinates should filter the public calendar
by their location."*

This is the half that makes the acquisition channel actually work. A national list of events is a
directory; a list of events **near you this weekend** is a reason to come back. Kept as its own
sub-part because it is only meaningful once public events exist, and it should not delay them.

**Consent, and what we do with the answer:**

- Browser geolocation is permission-gated and must stay opt-in — a **"Near me" button**, not a prompt
  on page load. Somebody who declines gets the list they already had, sorted by date.
- **Do not store it.** A visitor's position is needed for the length of one query. Storing it turns a
  convenience into a location history, which is a different product and a much heavier promise.
- **Round it before it leaves the browser.** `PublicCoordinates` already snaps a *case* to a grid
  cell; the same trick applied to the *visitor* means the server sees roughly where somebody is
  rather than exactly, and a coarse "within N miles" filter is unaffected by the loss of precision.
  Pleasing symmetry: the same function protects the people being listed and the people looking.
- **Say the distance, not the direction.** "About 12 miles away" is what a reader needs. Rendering a
  line from their house to the venue is not.

**Also worth having regardless of geolocation:** a town or postcode box. It works for somebody
planning a trip, for anyone who declines the permission, and on a desktop where the browser's guess
is often wrong by a county.

### 87b. Hidden locations, and how somebody without an account attends

Ben: *"If it is made hidden, they cannot see the actual location unless they choose to attend and
they ask to attend. Then they can see the actual address — assuming they have contact information if
they do not have an account yet. They can create one or maybe we allow the temporary with contact
info. I am not sure how this is usually handled."*

**The hidden-location pattern is well established** — Airbnb shows a circle until a booking is
confirmed, Eventbrite hides the address until a ticket is issued, and recovery meetings and private
supper clubs have done the same thing for decades. So the shape is not in doubt; the question is
what unlocks it.

**Two reveal modes, chosen per event**, because Ben described both "choose to attend" and "ask to
attend":

- **Open** — the address appears as soon as somebody confirms they are coming.
- **By request** — the organization approves first, and the address appears on approval. The
  equipment loan lifecycle is already exactly this shape (requested → approved → …) and is the
  pattern to copy rather than reinvent.

#### The account question — recommendation: a magic link that leaves a real account behind

The three options in the wild are guest RSVP with an email (Eventbrite), a required account
(Meetup), and a **magic link that creates a lightweight passwordless account on first click**
(Luma, Partiful). **The third is right here**, for three reasons that are specific to this product
rather than to taste:

1. **The reveal has to be gated on a *verified* email, not a typed one.** If anyone can type an
   address into a box and be shown where a group is meeting, the hidden location is theatre. A
   click-through link is the cheapest gate that actually verifies anything.
2. **Ben's stated purpose is acquisition** — *"an introduction to them by people attending."* A guest
   RSVP leaves nothing behind. A magic-link RSVP leaves the organization a contactable person and
   the platform a user, which is the entire point of the feature.
3. **Strangers are meeting at a location, often at night.** A persistent identity is the minimum for
   an organization to notice somebody who no-showed twice, or to remove them.

It also reconciles Ben's two statements — *"they have to be a site user"* and *"maybe we allow the
temporary with contact info"*. They **are** a site user; they simply never had to invent a password.
Setting one later is an upgrade, not a requirement.

**The infrastructure is already here.** `IEmailService` exists, and `CaseClientInvite` is already an
email + token + expiry + `AcceptedByAppUserId` flow built for item #4 — the same shape, pointed at
an event instead of a case.

**Worth getting right:**
- **Expire the link and single-use it.** A forwarded email should not hand the address to a mailing
  list.
- **The address lives behind the endpoint, not in the page.** Reveal means the server checks the
  RSVP and returns the address; it does not mean shipping it to the browser with `display:none`.
- **Cancelling revokes the reveal** for future loads. It cannot un-tell somebody, and pretending
  otherwise would be dishonest — but it should stop the page serving it again.
- **Say what happens before they commit.** "The exact address is shared with people who are coming"
  on the listing, so nobody feels tricked into identifying themselves.

#### The radius filter, and the leak it would otherwise create

A distance dropdown (**5 / 10 / 25 / 50 / 100 miles**) over the calendar and the map, per Ben.

**Filter and plot against the *redacted* coordinate, never the true one.** This is the part that is
easy to get wrong: if the radius query runs against the real position, somebody can narrow the
radius step by step around a guessed point and binary-search their way to the exact location —
the filter becomes an oracle that gives away precisely what the hidden address was protecting.
Running it against the `PublicCoordinates` cell centre caps the resolution of any such attack at the
cell size, by construction.

Same rule for the map pin, and show **"about 12 miles away"** rather than a precise figure or a line
drawn from the visitor to the venue.

### 87c. Seeing it on your own calendar, and confirming a non-user is coming

Ben, 2026-08-17: *"On the org calendar, we should make this event a different color and have a
different icon to represent public investigations. If someone chooses and is accepted or is
auto-accepted to attend an investigation, they should see it on their calendar. If not a member, we
should at least let them know when they are expected to attend an investigation. I base this off my
experience. Someone may give some information, but we need enough to be able to show them they have
elected to attend if not already users of our site."*

**Calendar styling — ✅ built 2026-08-17.** Public events now render with their own marker on the
organization's calendar. Worth noting what this uncovered: `OrgCalendarEventType` has carried
`ColorClass` and `IconClass` since it was built, and **only the type-manager screen ever read them**.
The calendar — the one place the distinction matters — rendered every appointment identically, so a
month with thirty events told an organizer nothing until they clicked one. Both now show.

**Attendees seeing it — ✅ built 2026-08-17.** `GET /api/public/events/mine` and an "Events you're
going to" section on `/my-investigations`. Put there rather than on a page of its own because from
the person's point of view "things I am going to" is one list, and the difference between being on a
team and having signed up to a public walk is a distinction only the database cares about.

Recently-finished events stay listed for thirty days: somebody asking *"what was that place
called?"* the morning after has nowhere else to look. And the page's empty state now checks **both**
lists — it would otherwise have told somebody they had nothing while hiding the event they signed up
to that morning, which is the same bug in a new place.

The original gap, for the record: `/my-investigations` exists, but it is
fed by `InvestigationAttendee`. An RSVP to a public event creates an `OrgCalendarEventAttendee`, a
different table, so **somebody who signs up to a public event sees it nowhere afterwards**. There is
also no personal calendar surface at all — only a list. Two options:

- Surface RSVP'd events on `/my-investigations` alongside assigned investigations. Cheap, and
  probably right: from the attendee's point of view "things I am going to" is one list.
- A proper personal calendar. More work, and worth it only if there is enough on it to be worth
  opening.

**Non-users — ✅ built 2026-08-17.** An email box on the public event page, a single-use link good
for a fortnight, and confirming creates a passwordless account and records the attendance in one
save. `EventAttendanceInvite` is modelled on `CaseClientInvite`, the same shape pointed at an event.

Three things worth knowing about how it behaves:
- **Asking always answers the same way**, whether or not that address already has an account, and
  whether or not the mail actually went. Anything else makes the endpoint a way of testing which
  emails are registered here.
- **Nothing happens until the link is clicked** — no attendee row, no account. A typed address is a
  claim, not a confirmation, and the page confirms on a button press rather than on load so a mail
  scanner prefetching the link cannot sign somebody up.
- **Capacity is re-checked when the link is used**, not only when it was sent. A fortnight is long
  enough for an event to fill.

Both guarantees verified by breaking them: leaving the token usable, and creating the attendee at
request time, each fail their tests.

**Still to do here:** a reminder before the date. Ben's *"I base this off my experience"* is the
argument — somebody who signed up three weeks ago needs telling again, and a stranger who does not
turn up is worse for the organization than one who never signed up.

**The original decision, for the record —** *"we need enough to be able to show them
they have elected to attend if not already users of our site"* settles the open question in favour of
the magic-link approach: collect an email, send a link that both confirms the address and creates a
lightweight passwordless account, and that link is thereafter **their view of the event** — proof
they are coming, the exact address, and a way to cancel. They are a site user; they simply never had
to invent a password.

A reminder before the date belongs here too. Ben's *"I base this off my experience"* is the argument
for it: somebody who signed up three weeks ago needs telling again, and a stranger who does not turn
up is worse for the organization than one who never signed up.

### The reminder, and the scheduler under it — ✅ built 2026-08-20 (phase 6)

Anyone whose RSVP is **Accepted** is emailed roughly a day before the event: time, place, the link
to the event page, and a way to say they can no longer come while the place can still be offered to
somebody else. Not the merely invited and not the tentative — an invitation nobody answered is not a
commitment, and mail about a thing somebody never agreed to is mail they did not ask for. Widening
that is one enum value, and should be a decision rather than a drift.

**This is the platform's first background worker.** `ScheduledWorkService` is a `BackgroundService`
that wakes every five minutes and runs each registered `IScheduledJob` in its own scope and its own
try/catch. **No Hangfire, no Quartz** — the work is a handful of jobs on a timer with no cron
expressions, no backoff, no dashboard and no persisted queue, and the one guarantee that matters is
provided by a unique index rather than by anything a job framework would supply. Adding a job is one
`AddScoped<IScheduledJob, …>` line.

Three decisions in it worth keeping:

- **The first pass waits 30 seconds.** Jobs that fire the instant the process starts run while
  migrations may still be applying, and turn a crash-restart loop into a job loop.
- **Resolution happens inside the guard, not before it.** An exception escaping `ExecuteAsync` stops
  the entire host by default, so a job whose constructor threw would have turned "reminders are
  broken" into "the API is down". Caught while writing the tests, not by them.
- **The marker is written after the send, never before.** Writing it first would make a failed send
  permanent silence; writing it after means the worst case is a duplicate — much the better of the
  two for somebody who is expected somewhere tomorrow.

`EventReminderSent`'s unique index across (event, user) **is** the idempotency, not a tidiness
constraint: the loop would otherwise find the same event on every pass and send the same person the
same email a dozen times before the evening. The in-memory provider the tests use does not enforce
unique indexes, so the tests exercise the query — the layer that operates on every normal pass — and
assert the index structurally, with the reasoning next to the assertion.

Turning **events off sitewide stops the mail**, not just the pages. A disabled section that carries
on writing to people is worse than one that merely hides itself.

### Smaller things to settle when it is picked up

- **Who sees the attendee list?** These are strangers. Default to the organization seeing names and
  attendees seeing only a count; make anything wider a deliberate choice.
- **Capacity and a cut-off.** A real site has a limit, and there is a point after which turning up
  is not useful. Cheap now, awkward later.
- **The organization needs to be able to remove somebody**, and an attendee to cancel.
- **Real-world safety.** This arranges strangers meeting at a location, often at night. Not a
  blocker, but the listing should carry what to bring and what to expect, and the organization
  should be named — an anonymous invitation to a dark building is not something to ship.

---

## 88. Local discovery — "what's near me" across groups, events and places (built 2026-08-17)

Ben: *"Like if a person wants to see what is local... group events or actually local groups etc."*

The natural companion to item #87. A visitor picks a distance and sees what is around them —
**groups, public events, and places worth visiting** — on a list and a map. This is the front door
for somebody who has never heard of any of these organizations, which makes it the same acquisition
argument as #87 and probably the same piece of work.

### What already exists, which is more than it looks

**Two different "nearby" questions are already implemented, and they are not the same question.**

- **"Which groups serve my area?"** — `PublicOrganizationSearchController.Search`, **live and wired**
  into `HomeHero`, `OrgDiscovery` and the client request wizard. Matches the visitor's point against
  each organization's declared `AreaOfOperation` circle and filters on `IsAcceptingClients`. The
  radius here belongs to the **organization** — it is their service range — and the endpoint
  deliberately **never returns an org's coordinates**, only a label, a distance, and whether the
  point falls inside their range.
- **"What is within N miles of me?"** — `SearchController.Nearby` at `/api/public/search/nearby`,
  which honours each `OrganizationAddress`'s `IsSearchable`, `SearchVisibility` and
  `SearchRadiusMiles`, takes a caller-supplied radius, and **nothing anywhere calls it**. Ben's
  distance dropdown is, almost exactly, a parameter that already exists and was never exposed.

So the second implementation is dead code that happens to be most of the feature being asked for.
Worth deciding on sight whether to wire it up or fold it into the live one, rather than writing a
third.

**What does not exist at all:** local *events*. `OrgCalendarEvent.IsPublic` is written and never
read (item #87) — so there is nothing to plot even once the map exists.

### The rule that must not be applied uniformly

Three kinds of thing would appear on one map, and **they do not share a privacy rule**. Getting this
wrong in either direction breaks something:

| What | Location shown | Why |
|---|---|---|
| **Organizations** | as precisely as they chose | A group ticking "searchable" *wants* to be found. It is a business listing. Grid-snapping it would defeat the feature. |
| **Public events** | approximate until attending | Item #87b — an invitation with an address is a different thing from a listing. |
| **Public cases** | always approximate | Somebody's home. `PublicCoordinates`, already enforced. |

The temptation, having just built coordinate redaction, is to apply it everywhere. **Do not.** An
organization that cannot be found has been broken, not protected.

### ✅ Server side built 2026-08-17

**The dead endpoint was extended, not replaced** — the decision this entry asked for. `SearchController.Nearby`
already honoured `IsSearchable`, `SearchVisibility`, `SearchRadiusMiles` and each address's
`PublicDisplayMode`; it now also returns **upcoming public events**, and answers with
`NearbyResults(Organizations, Events)` — two lists precisely because the two obey different rules.

- **Events reuse `PublicEventController.VisibleEvents`**, now `internal`, rather than restating the
  predicate. An event hidden on the events pages cannot surface here, including one at a private
  residence.
- **The published distance is measured to the *snapped* point, not the real one.** A true distance
  beside an approximate position hands back the position: query from three points and trilaterate.
  Everything reported derives from the grid cell, so there is nothing to solve for. The cost is that
  an event within a mile or two of the radius edge may fall on the wrong side, which does not matter
  for browsing.
- `NearbyEventResult` has **no field for a street address**, asserted by a test.
- The asymmetry itself is tested: the group is asserted to appear at its *real* coordinates and the
  event at *not* its real ones, in the same file, so a later uniform "redact everything" pass fails
  loudly.

> **Not "untested", just uncalled.** `SearchControllerTests` existed and covered the organizations
> half all along; what never existed was a caller. Changing the response shape broke those seven
> tests, which is exactly what they were for — they now unwrap `.Organizations`.

**Still to do:** the visitor-facing screen — a distance dropdown, a list and a map. The server now
answers the question; nothing asks it yet. Public *cases* are also not in the response (they are
already discoverable via `PublicCaseDiscoveryController`, with coordinates approximated); folding
them in is a third list obeying the same rule as events.

### The decision this forces

The live org search **deliberately withholds organization coordinates** — it returns a distance and
a label, never a point. That is a good decision for "who serves my postcode" and an impossible one
for "show me a map", which needs somewhere to draw a pin.

So plotting groups needs one of:
- Use `OrganizationAddress` (where `IsSearchable` and the public display mode already permit it) as
  the map identity, leaving `AreaOfOperation` for the service-area question. **Recommended** — the
  two questions stay separate, and the address already carries per-address visibility controls
  built for exactly this.
- Or let `AreaOfOperation` return its centre when the org opts in, which conflates a service area
  with a location and means a group "is" wherever the middle of its patch happens to be.

### Shape

- One **"Near me"** surface with a distance dropdown (5 / 10 / 25 / 50 / 100), a list and a map, and
  toggles for **groups / events / places**.
- Location by opt-in geolocation **or** a typed town or postcode — see 87a; the typed box is not a
  fallback, it is what most desktop visitors will actually use.
- Reuses `PublicCaseDiscovery`'s existing map rather than adding a second one.
- Empty states that do something: "no groups within 25 miles — try 50" beats a blank list, and is
  the difference between a visitor leaving and widening the search.

---

### ✅ UI built 2026-08-17

`NearbyDiscovery.razor`, mounted on the home page between `HomeHero` and `PublicCaseDiscovery`. On
load it asks the browser for the visitor's location; declining or lacking geolocation falls back to
the same typed-place-name search `HomeHero` already offers (`SearchGeocodingAsync`), so the feature
degrades rather than disappears. A distance dropdown (10/25/50/100 mi) re-queries on change.

**Deliberately list-only, no map.** `PublicCaseDiscovery` already carries a Telerik Map plus its own
colocated JS for marker clustering; duplicating that machinery for a first version was not worth the
risk in an environment where Telerik rendering cannot be visually verified. A map is additive later
— the two result lists this renders do not need to change shape to gain one.

The privacy asymmetry the server enforces is rendered as-is, not re-decided: a group's card links
straight to `/o/{org}`, an event's card says "approximate" and gives no address at all. Guarded by
`ReachableComponentTests.Nearby_search_is_called_by_a_screen` — the whole reason this item existed
was a fully-built, fully-correct endpoint with zero callers, and adding a UI without a test asserting
the UI is real would have been the same mistake with better production values.

**Dev seed data added, because the panel was correct and empty.** No seeded organization address set
`IsSearchable` (it defaults to false) and nothing seeded an `OrgCalendarEvent` at all, so a fresh dev
database rendered "Nothing found" — indistinguishable from broken. `SeedLocalDiscoveryAsync` now
marks *one* group findable (not both: a panel where everything is findable cannot show that the flag
is what does the work) and creates two public events.

The two events are placed deliberately. Bell Witch Cave is **33.4 miles** from the Nashville seed
point, so at the panel's default 25-mile radius it is out of range; the second event sits at the
other group's own Nashville address. A fresh database therefore shows one event immediately, and
widening the dropdown to 50 visibly adds the second — the control does something observable rather
than being taken on trust.

That second event also uses `OrganizationAddressId` with **no** `PlaceId`, which `VisibleEvents`
permits and which the nearby projection falls back to for coordinates. **That fallback had no test**
until the seed data started depending on it — a line of code nothing exercised, relied upon by data
whose breakage only a running app would have revealed. Now covered, including the point that such an
event is *still* snapped even though the organization's listing drawn from the very same address is
shown precisely: same coordinates, two answers, decided by what the row means.

### Playwright coverage — and the bug it found

`NearbyDiscoveryTests` (category `Nearby`), run against the live stack, **7 passing**. Geolocation is
granted and pinned to the Nashville seed point in one fixture; a second fixture withholds it to
exercise the declined-permission path.

**It found a real bug that nothing else could have.** The component was one long `if / else if`
chain: `Asking` → `Denied` → `_searching` → results. So whenever geolocation was declined, the
`Denied` branch matched and **shadowed every results branch** — a visitor who typed a place and
searched had their data load correctly and saw nothing at all. Unit tests pass (the endpoint is
right), the source scan passes (a screen does call it), and the feature is silently broken for
everybody who says no to the browser prompt. Fixed by splitting into two independent chains, which
is also correct behaviourally: the manual box has to stay on screen, since it is how somebody
searches a different place.

Two smaller things worth recording:
- **`HomeHero` carries the identical placeholder text**, so a placeholder-based Playwright selector
  fills *its* box and then clicks this panel's button — which stays correctly disabled. That looks
  exactly like a component bug and is not one; it cost a wrong diagnosis before the id was added.
- The manual input is a **plain `<input>` with `@oninput`**, not `TelerikTextBox`, matching the
  convention already used in the equipment and timeline editors: a Telerik text box commits on blur,
  so a button gated on its value stays disabled for anyone who types and clicks straight through.
- `[SetUp]` waits for the panel, not just `NetworkIdle` — a Blazor Server circuit loads its data
  after the network goes quiet, and the first run against a freshly restarted app failed on that
  race while warm runs passed.

## 89. Readable URLs — the scheme, settled 2026-08-17 (closed 2026-08-17)

Ben: *"we use the GUID for many of the IDs. That is not human readable... I was thinking we need
'/c' for cases and '/i' for investigations."* Then, on single letters: *"how can we provide a
concrete link to equipment... '/e' it is going to get crazy eventually."*

**He is right, and it settles the question.** `/e` is events or equipment. `/c` is cases or catalog.
Single letters do not survive the fourth entity type, and the app already has more than four.

### Two decisions

**1. Full words, not letters.** `/o/ghost-squad/events/2026-08-17-ghost-walk`, not `/o/ghost-squad/e/…`.

- They scale without collisions, and without anybody memorising a lookup table.
- They are self-documenting in a link, a log, or a support ticket.
- **SEO**, which matters specifically because item #87's purpose is discovery by strangers. A search
  engine reads `/events/` as a keyword; `/e/` is noise.
- The cost is that an organization cannot have a CMS page called `events` — bounded, and arguably
  correct, since a page about their events *is* that page.

**2. Two roots, decided by ownership — not everything belongs under `/o/`.**

Equipment is the case that proves it. The make/model catalog is deliberately **cross-organization**:
a Zoom H1n is not owned by one group, and pooling every owner's photos and links onto one page is
the entire point of what shipped in #55 phase 6b. Forcing it under `/o/{org}/` would be wrong.

| Root | For | Examples |
|---|---|---|
| `/o/{org}/…` | belongs to one organization | pages, cases, investigations, events |
| `/{type}/…` | platform-wide | equipment catalog and model pages, places, the public case map |

**The app already does this correctly** — `/equipment-catalog`, `/equipment-models/{id}` and
`/equipment/{id}` are top-level and org-agnostic. They do not need moving; they need slugs instead
of GUIDs.

### The shape

```
/o/{org}                              organization home
/o/{org}/{page}                       CMS pages — reserved words enforced
/o/{org}/cases/{case-slug}
/o/{org}/investigations/{slug}        flat, NOT nested under a case — see below
/o/{org}/events/{slug}                ✅ built 2026-08-17

/equipment/{brand}/{model}            the pooled make/model page
/places/{place-slug}
/cases                                the public discovery map
/events                               cross-organization "what's on" (item #88)
```

**Investigations are flat, and that is structural rather than aesthetic.** Ben asked whether an
investigation needs `/o/{org}/cases/{case}/investigations/{inv}`. It must not: `Investigation.CaseId`
is **nullable** — a group can investigate a landmark with no client case, and then a `PlaceId` is
required instead. A URL that assumed the case would have no form for those at all. The general rule
falls out of it: **URL depth follows what the model actually requires, not what is usually true.**
The organization is required on everything (verified 2026-08-17), so one level under `/o/{org}` is
always enough.

### Not everything gets a slug

A slug is a **public name**. An individual piece of equipment — somebody's specific recorder, called
"My backup H1n" — has no business having one: it would publish the owner's private naming in a URL,
and most items are not public at all. Keep the GUID there.

The rule: **a readable URL for things meant to be found and shared; an opaque id for things reached
from inside the app.**

### Still to do

- **The reserved-word bug — ✅ fixed 2026-08-17.** `CmsReservedSlugs` refuses a routed word on both
  create and rename, with a message naming the word and suggesting a way round it. Existing pages
  saved before the check are flagged **Unreachable** in the CMS list, because nothing else about
  them looks wrong — they sit in the list like any other page and only fail when somebody follows
  the link.

  The part that makes it stay fixed is a **source scan**: a test reads every `@page "/o/{...}/x"`
  route in the app and fails if `x` is not reserved. Refusing today's words was the easy half; the
  failure mode is somebody adding `/o/{org}/team` in six months with nobody remembering the list
  exists, and an organization losing a page silently. Verified by adding a route for an unreserved
  word and watching the test name both the word and the file.

  A few extra words are held back for routes the site will want. Reserving one costs an
  organization nothing today; taking it back after they have built a page there breaks their link.
- **Cases — ✅ built 2026-08-17.** `/o/{org}/cases/{slug}`, generated from the case **title** the
  first time it is published and then left alone. Derived rather than typed on purpose: the title is
  already on the public case page, so the URL exposes nothing the page does not, which is not true
  of free text.

  **A case is somebody's home**, and a URL outlives the page — it sits in browser histories,
  referrer headers and pasted links long after anyone thinks about it. So a title that reads like a
  street address is **refused at publish** rather than quietly slugged, which would have handed back
  what redacting the coordinates was built to protect. The check is deliberately narrow: it refuses
  a title an organization typed, and a rule that fired on "The 1892 Foundry" would teach people to
  work around it rather than to name things carefully.

  The old `#2026-042` reference still resolves, because it is what an organization says out loud to
  a client. The endpoint's old "expected format 2026-042" 400 became wrong the moment the same
  segment could carry a slug, and is now a plain 404.

  *Worth recording:* the street-address regex shipped broken in its first draft — `\b` was eaten
  during editing and became a literal backspace, so it matched nothing at all. Only the test caught
  it. A guard that silently never fires is the worst kind.

- **Investigations — ✅ built 2026-08-17.** `/o/{org}/investigations/{date}-{title}`, and a public
  page to go with it: a published write-up could previously only be reached through the page of the
  place it happened at, which is a fine way to browse and a poor way to share.

  Date first, then the title. The date makes a list sort by name alone and says something useful
  when a title does not; the title stops a date-only address being **walkable**, which would let
  anybody step through the calendar and enumerate a group's visits.

  **Flat under the organization**, as decided — `CaseId` is nullable, so a nested address has no
  form for a landmark visit. Visibility runs through the **shared** `InvestigationVisibilityFilter`
  rather than a second `Visibility == Public`, so a group-only investigation is unreachable here for
  the same reason it is unreachable on a place page. The location is approximate: a write-up says a
  group was somewhere, not which door they knocked on. The same street-address refusal as cases
  applies to the title.

  The place page's rows now link to it — they carried no slug, which is the third time in this
  session a list has shipped unable to open its own contents.

- **Equipment model slugs — ✅ built 2026-08-17.** `/equipment/{make}/{model}` — the last page in
  this work still wearing a GUID, and the one Ben raised first.

  **This slug is regenerated on rename**, which is the opposite of every other slug here and
  deliberate. A case, an event and an organization freeze theirs because somebody chose and shared
  it. The catalog is the site's own vocabulary and its rename path exists specifically to correct
  mistakes — a page for a make fixed from "Sansung" to "Samsung" that still answered only to
  `/equipment/sansung` would preserve the error in the most visible place there is. The cost is that
  a catalog link shared before a correction dies; accepted because these addresses are brand new, so
  nothing has been shared yet.

  Model slugs are unique **within the make**, matching how the names are: two manufacturers may both
  make an "X1", and neither should be forced to take a suffix. Existing rows are backfilled by the
  seeder in C# rather than by SQL in the migration, so there is one definition of how a name becomes
  a slug — a SQL approximation would quietly disagree with `UrlSlug` on accents and length.

  The GUID route stays and **redirects to the readable address**, because every list in the app
  still links by id. Without that the readable route would exist and nothing would ever reach it.

- **Alias-and-redirect for changed slugs — ✅ built 2026-08-17**, and the investigation found three
  faults rather than the one that was expected.

  **The expected one:** renaming an organization broke every link ever shared, silently. Old
  addresses are now kept as aliases and still resolve, and the public page moves the browser to the
  current address so what gets copied onward is the one that will still be right tomorrow. Aliases
  are **never reassigned** — pointing a saved link at a different group is worse than the link being
  dead, because a dead link says "gone" while a captured one says something false.

  **Two that were not:**
  - **`Organization.UrlName` had no unique index and the rename path never checked.** Create checked;
    rename did not. Two groups could hold one address, and all seventeen lookup sites are first-match
    queries — so a group could rename onto another group's address and take their public traffic.
  - **Nothing validated the characters.** Both write paths trimmed and lowercased and stopped there,
    so `ghost squad`, `a/b` and `../admin` were all storable.

  There turned out to be **three** creation paths, not two: the admin endpoint, the org endpoint, and
  `RegisterOrganizationAsync` in the repository layer, which knew about none of this. They now share
  one helper, in `Ben.Data.Source` because that is the only project all three can see. Two endpoints
  writing one column with different rules is exactly how the original collation bug happened.

  Cases, investigations and events need no aliases: all three generate a slug once and return early
  if one exists. **If any of them ever becomes editable, it needs this on the same day.**

---

## 90. Taxonomy typos and staleness — the Sansung problem (closed 2026-08-17)

Ben: *"if I make a piece of equipment from a manufacturer Sansung and I make a typo Samsung, and
delete the item before someone else makes a Samsung product, what happens when I delete my Sansung
product or what happens when I try to change Samsung to Sansung?"*

### What happened before, which was worse than the question assumed

- **Deleting the item left the typo behind.** The model and brand stayed in the shared catalog for
  ever, unapproved and unreferenced, and **the member who created them could not remove them** —
  rejecting taxonomy is a SuperAdmin action. Everybody adding a Samsung recorder afterwards was
  offered two manufacturers, and the wrong one looked exactly as real as the right one.
- **Renaming was impossible.** There is no rename endpoint for brands or models at all — only
  approve and delete, and delete is refused while anything references them. So "change Samsung to
  Sansung" could not be done, and its opposite could not be undone.
- **Dedup was collation-dependent.** Proposing matched on `Name == name`, so whether "samsung" and
  "Samsung" were one brand or two depended on the database's collation rather than on anything in
  the code — the same fault as the slug lookups.

### Fixed

- **Near-duplicate detection at the moment of typing**, which is the only cheap moment. Proposing
  "Sansung" when "Samsung" exists returns the suggestions instead of creating it; the person either
  picks the real one or confirms theirs is genuinely different. The pattern already existed for
  places, where `FindPlaceCandidatesAsync` asks "did you mean this?" before a duplicate exists.
- **Case-insensitive dedup**, explicitly, rather than by collation.
- **Orphan cleanup.** Deleting the last item that used an *unapproved* brand or model takes them
  with it — model first, then the brand that existed only to hold it. **Approved entries are never
  swept**: the catalog describes what exists in the world, not what somebody happens to own this
  week, and a Zoom H1n is still a real recorder on the day the last owner here sells theirs. That is
  the answer to "how long until a name goes stale" — an unapproved one goes the moment nothing uses
  it; an approved one never does.

### Two defects the positive tests found, which the negative ones would not have

Ben's point that *"proving a single negative doesn't necessarily mean proving it positive"* was
immediately borne out:

- **"Olympsu" for "Olympus" was not caught.** A transposition is the commonest typo there is, and
  plain Levenshtein charges two for it, putting it past the threshold. Now Damerau-Levenshtein,
  where a swap costs one.
- **"Ring" and "Ping" were flagged as the same name**, as were "Zoom" and "Boom". One letter is the
  entire difference between two real companies at that length. Short names are now left alone
  entirely — a check that cried wolf would train people to click past it, which is worse than not
  having one.

### Still to do

- **Rename-as-merge — ✅ built 2026-08-17.** Brands and models can be renamed at last, and a
  collision is **offered as a merge rather than performed**: two manufacturers becoming one changes
  what make somebody's equipment is, which is far too large a thing to happen because a name was
  typed. The 409 carries the id it collided with, so the caller chooses deliberately.

  Three rules worth knowing:
  - **Merging an approved brand into an unapproved one is refused.** Somebody correcting a typo has
    the two the wrong way round more often than not, and the result would be a catalog where the
    endorsed name vanished and the typo survived.
  - **A model name on both sides is folded, not moved.** Two "X1" rows under one brand is exactly
    what the unique index forbids, so the duplicate's items move to the survivor — the same merge
    one level down. Handling it here rather than failing is what makes the tool usable on real data.
  - **Models under different makes are not merged**; that silently changes what somebody owns, and
    it is the brand merge's decision rather than this one's.
- **The same treatment for other user-grown taxonomies — ✅ done 2026-08-17.** The guess was half
  right, and the wrong half is the more interesting one.

  **Experience types were worse off than equipment ever was**, because a group cannot delete a type
  it proposed — the only delete lives behind an app-administrator screen — so a mistyping was
  permanent. Five distinct gaps, all now closed:
  - No typo detection at all when a group proposed a type. Now checked against **reviewed** types in
    the same category, with the same confirm-it-is-different escape.
  - **The administrator's own create path never deduped**, so it could quietly make the second
    "Knocking" that every group was being stopped from making.
  - **Renaming onto a taken name silently produced twins** in one category — the exact mess the
    rename was trying to clear up, now indistinguishable. Now offered as a merge.
  - **No merge existed.** Now folds taggings onto the survivor, refusing to lose a review or to
    cross a category, since moving a tagging from Visual to Auditory rewrites what somebody recorded
    about their own night.
  - **No orphan sweep**, on either untagging or deleting the occurrence. Both now sweep, on the
    equipment rule: group-proposed and unreviewed only.

  Two things worth remembering from the build:
  - **"Reviewed" is `IsApproved && ApprovedByAppUserId != null`, not `IsApproved`.** An org-proposed
    type goes live immediately with the approver left null, and that null is the entire marker.
    Testing `IsApproved` alone would sweep away words an administrator had deliberately endorsed.
  - **The join's primary key is the pair (entry, type)**, so a merge cannot repoint a tagging — EF
    refuses to modify a key property on a tracked entity. Rows are deleted and re-added instead.
    Caught by reading the model config, not by a test.

  **The table had no unique index and no length cap at all** — `nvarchar(max)`, with the advertised
  100-character limit enforced nowhere. Both added, with a dedupe pre-step in the migration so it
  can apply to a database that already has twins. Verified against the real dev SQL Server, not only
  in memory.

  **Places turned out to be fine.** `PlaceMatcher` already had a genuine dedup rule — same address
  *and* within a tenth of a mile — and already normalised case, punctuation and a leading "the". The
  one gap was a mistyped landmark name, now tolerated, which is safe here in a way it would not be
  elsewhere: candidates are only *offered*, and proximity has already been checked, so a wrong
  suggestion costs a glance while a missed one costs a duplicate somebody must merge later.

### The typo check was unreachable in the UI the whole time — ⚠ found and fixed 2026-08-17

The most useful finding of the session, and it was **my own earlier work**. The server answers a
probable typo with a 409 listing the names it might have been. Both callers — the equipment editor
and the case timeline — threw it away and rendered "could not be added".

So the person could not take the suggested name, could not insist on their own, and simply could not
add any name resembling an existing one. **The check made the feature strictly worse than not having
it**: before it, the word at least got created.

Every unit test passed, on both sides. They asserted the server returns suggestions, which it did.
Nothing asserted a person ever sees them — the same shape as the platform-messages and
permission-requests findings, now the fifth instance.

Both screens now show a "did you mean" prompt: each suggestion is one click, and *"no, mine is
different"* creates it as typed. Two source-scan tests hold the line — one that the suggestions
reach a screen at all, one that **every** screen showing them can also overrule them, since a
prettier dead end is still a dead end.

---

## 91. Video editor — scope the Server media tab (CLOSED 2026-08-20 — phase 7)

The editor's **Server** tab lists every media file the signed-in person can reach, in one flat
list. That is fine with four demo clips and unusable with four hundred: a real investigation
produces dozens of files per visit, and the tab is the only way to get any of them into a project.

**The ask.** A scope selector above the list:

- **All** — everything they may see, as today.
- **Personal** — only files they own.
- **By case** — pick a case, see that case's media; then optionally narrow to a single
  **investigation** within it.

Permissions decide what each scope can return, not the selector: someone with access to all media
sees a case's whole set, while someone with narrower rights sees only their own share of it. The
selector filters what they are already entitled to — it must never widen it.

**Why it matters beyond convenience.** Importing is a two-click operation per file (download, then
add), so the cost of finding the right file dominates. Scoping by case and investigation also puts
the editor in the same mental model as the rest of the site, where work is organised by case first.

**Notes for whoever builds it.** The list comes from `GET /api/media-library/files`, which already
aggregates across ownership, org membership, shares and case links —
`MediaLibraryController` composes those sets, so the scoping belongs there rather than in the
editor, which should send a scope and an optional case/investigation id. `BenMediaLibraryProvider`
is the client side. Keep the content-type filter as-is.

### How it shipped (2026-08-20)

A selector above the Server tab: **All media / My files / By case**, with a case list appearing for
the third and a visit list appearing under that when the chosen case has more than one.

**The scope narrows and cannot widen**, and that is a property of where the filtering happens
rather than a promise. `MediaLibraryController` computes the full audience union first, exactly as
it always did, and applies a scope as an *intersection* over the result — so naming a case you have
no part in returns nothing rather than its contents. A test asserts precisely that, and fails if
the intersection is ever "optimised" into the union.

**The editor still does not know what a case is.** A second, optional interface —
`IMediaLibraryScopeSource` — hands it groups with labels and ids; it renders them and sends an id
back. A host that registers none simply gets All and Personal. Optional in the real sense: it is
resolved through the service provider rather than with `[Inject]`, because `[Inject]` calls
`GetRequiredService` and a nullable property does not make it optional.

**Two things this turned up:**

1. **The two hosts were listing different things.** The WASM host's Server tab called
   `/api/upload-files`, which is *owner-only*, while the Blazor Server site called
   `/api/media-library/files`, which aggregates. So the same tab in the same editor showed a
   narrower list on one host than the other, and the WASM host silently omitted images as well.
   Both now use the aggregating endpoint and both show images; anyone who wants the old behaviour
   picks **My files**.
2. **A stale-response race** — found by a test that changed scope twice quickly, which is exactly
   what somebody hunting for a file does. The first fetch was still in flight when the second
   scope was chosen, and whichever landed last won: the list would show the previous scope's files
   under a selector describing the new one. A generation counter now discards superseded results.
   The failing test went from a 17-second timeout to two seconds once it was fixed, which is how
   the race announced itself.

---

## 92. Home map renders into a sliver (CLOSED 2026-08-19)

Reported with a screenshot: on the home page, the "Public Investigations" map draws its tiles into
a narrow strip down the left of its container and leaves the rest black. The zoom and recentre
controls sit inside the strip, so the map believes it is that width.

**The guess in this entry was wrong twice over**, and is kept because the shape of the mistake is
worth remembering: it named Leaflet and `invalidateSize()`, and these maps are Telerik's. The
reasoning about *when* — measured once at mount, never re-measured — was right; the library was
not. Diagnosing from a symptom's resemblance to a library you have used before will do that.

**Where to look.** The home map component and its JS interop, plus anything that resizes the page
after first render. Worth checking whether it reproduces on a hard reload versus a soft navigation,
and whether collapsing the sidebar fixes or worsens it — both distinguish "never measured" from
"measured too early".

Not reproduced in the capture harness, which screenshots the same page at a fixed 1440×900 viewport
and gets a full-width map — so it likely depends on window size or on the sidebar state at load.

**Suspect the template migration first.** The map predates the move from the Telerik-based layout to
the current template, and the new layout owns the page's widths and the collapsible sidebar. A map
that measured itself correctly under the old chrome would fail exactly this way under new chrome
that sizes its container later or differently.

### What it was (fixed in 8798656)

Both public maps re-measured themselves by reaching for a global `kendo` object —
`kendo.widgetInstance(el).resize(true)`, behind a `typeof kendo !== 'undefined'` guard. Telerik UI
for Blazor ships no jQuery and defines no such global, so the guard was false every time and every
call did nothing. The map kept whatever width it measured at mount: load narrow, widen, and the
tiles stayed in a strip.

Both components now hold a `TelerikMap` ref and call `Refresh()`; the JS keeps only the debounced
resize event. The same dead path drove `setMapCenter`, so recentring the home map on a searched
location had quietly done nothing since it was written.

Re-checked live 2026-08-19 after the toolbar work, since the entry had been left open: tiles cover
100% of the map box at 1280, at 1600, at 1024 on a fresh load, after collapsing the sidebar, and in
dark mode. Worth knowing for the next map bug — a screenshot taken too early shows a blank white
map, because Kendo creates each tile with `visibility: hidden` and reveals it on load. That is not
a defect, and it briefly looked like one here.

---

## 93. Editor toolbar — overflow items need labels, and undo/redo need checking (CLOSED 2026-08-19)

Reported against the WASM host, but the toolbar is shared, so both apply to the site too.

**Labels in the overflow.** The toolbar's "More tools" dropdown shows its items as bare icons. In
the toolbar itself an icon with a tooltip is fine — the row is a known set and space is tight — but
a dropdown is a list of choices being read one after another, and there icons alone make people
guess. Give the overflow items their text. The same goes for the cloud icon, whose meaning
(save/publish to the server) is not something anyone should have to hover to learn.

**Undo and redo appear unclickable.** Both are legitimately disabled when there is nothing to undo —
their tooltips say "Nothing to undo" / "Nothing to redo" — so the first thing to establish is
whether they stay disabled *after* an edit. If they do, that is a real defect in the undo stack's
wiring; if they don't, the defect is that a disabled control gives no hint why, which the tooltip
only fixes for people who hover.

### What it turned out to be

**Undo and redo were never broken.** Measured before and after an edit: with an empty project the
button reports `Nothing to undo` and disabled; after adding a marker it reports
`Undo: Add marker "0:00.0"` and enabled. The undo stack was wired correctly the whole time. What
was true is the second half of the report — a disabled control explains itself only to whoever
hovers it, and in a dropdown nobody hovers.

**Labels.** Only three buttons could ever reach the "…" menu: Undo, Redo and Save to server. Every
other icon button in the bar sets `Overflow="Never"` and stays put, and `ToolBarTemplateItem`
behaves as `Never` regardless. Those three now carry child content, which the menu renders as the
row's text; a scoped rule hides that text while the button is in the bar, so the bar is unchanged.
The rule needs `::deep` — the span holding the text is Telerik's, so it never receives this
component's isolation attribute and a plain descendant selector matches nothing at all.

**And the reason nobody had complained the menu was useless.** It was covered. Kendo puts popups at
z-index 10002 and windows at 11500, and the Media & Properties window docks to the right, directly
beneath the "…" button. Hit-testing the menu's four items returned the window's title bar and tab
strip: not a cosmetic overlap but an unclickable menu, and at narrow widths it is the only route to
Preview, Export, Undo and Redo. The popup is now raised above the window layer. An earlier attempt
at 10050 looked plausible and changed nothing, because the number to beat was never the 10003 the
panel happened to be reporting at the time.

Verified end to end at 900px: added a marker, opened the menu, clicked **Undo** in it, and the
state went to `Nothing to undo` / `Redo: Add marker "0:00.0"`. Two guards added in
`ToolbarOverflowLabelTests` — one fails if a button that can overflow has no label, one fails if the
popup is not raised past 11500 — both confirmed to fail against the unfixed code.

## 94. Background render stalls at "Processing… 0%" after an overlay is added (CLOSED 2026-08-19)

Hit while automating the editor for the help screenshots, in the site host. Sequence: import two
clips, select a clip, add a text overlay, add a callout. The ffmpeg status chip then went to
`Processing… 0%` and stayed there for the full two minutes the capture waited, which also leaves
Export disabled (it requires `FfmpegState.Ready`).

Reproduced three times while capturing screenshots. It is a genuine stall, not slowness: the first
run sat at `Processing… 0%`, the next two reached `Processing… 47%` and stayed there for four and a
half minutes without moving. Export is unusable for as long as it lasts.

This may be the same class as the clip-art background-render stall hardened in phase 138, whose
exact trigger was never confirmed live. This one has a reproducible-looking recipe, which that one
lacked, so it is worth trying to reproduce by hand before assuming they are the same.

Reproduce with the seeded demo footage: `/my-videos`, Initialize, import `porch-camera.mp4` and
`hallway-camera.mp4` from the Server tab, then **click a clip on the timeline**. That last step is
the trigger; importing alone renders fine (`exec 4.6s`, `concatClips 3.9s`, both ✓).

### Investigated 2026-08-19 — one cause found and fixed, one still open

**Fixed: a source with no audio stream produced an invalid command.**
`BuildBackgroundRenderVideoArgs` decided whether to map audio from the export settings and the
clip's mute flag alone, never from whether the file actually has an audio stream. For a video-only
source it emitted `-map 0:a`, which ffmpeg refuses outright — verified against the real binary:

```
Stream map '' matches no streams.  To ignore this, add a trailing '?' to the map.
```

The probe now reports `hasAudio`, `VideoClip.HasAudio` carries it (defaulting true, so projects
saved before this are not silenced), and the builder attaches the silent `anullsrc` track it
already had for muted clips. Three tests, one of which fails against the old code. This moved the
stall from 47% to 64% — it did not cure it.

**Still open: what actually freezes.** With a clip selected, the status chip sits at
`Processing… 64%` for four minutes and more. The evidence says a command is hung in the worker
rather than the state machine being wrong:

- the operation trace records entries *on completion*, and shows **nothing** after the selection;
- `FfmpegState` is `Processing`, and the paths that fail cleanly set `Error` instead — covered by
  three new tests in `FfmpegServiceRecoveryTests` asserting a failed exec never leaves the service
  pinned at Processing;
- the ffmpeg log tail ends in five × `Aborted()`, which is how ffmpeg.wasm reports an internal
  abort;
- **the `WorkerWatchdog` never declared it wedged**, so the chip never offered its "⚠ Stuck —
  Reset?" affordance and the user has no way out short of reloading.

### Root cause found and fixed 2026-08-19 — nothing was ever stuck

**The render was never hung. The toolbar had stopped repainting.**

The decisive measurement: with the chip reading `Processing… 33%`, opening the diagnostics panel in
the same second reported `State | Ready`. Two components, one service, two different answers — so
the disagreement was in the rendering, not in ffmpeg.

`Toolbar.razor` reads `Ffmpeg.State` straight from the injected service — the status chip, its
progress bar, and the `Enabled` of Initialize, Open, Preview and Export all depend on it — and
subscribed to nothing, relying on its parent re-rendering. Blazor skips a child whose parameters
have not changed, and going from Processing back to Ready changes none of the toolbar's parameters.
So the toolbar kept painting whatever was true at its last render, indefinitely, and Export stayed
greyed out behind a progress bar that had stopped.

The percentage differing run to run (33, 47, 50, 64, 65) was the giveaway in hindsight: it was
whatever the last painted value happened to be.

`Ffmpeg.OnStateChanged` is now subscribed — and the guard test written alongside found the same
defect in three more components: `MediaLibraryPicker` (whose Import button would stay disabled
after ffmpeg became ready), `ClipBrowser`, and `DiagnosticsPanel` itself. All four fixed.

The watchdog was right not to fire: nothing was wedged.

Two real bugs were found on the way there and are worth keeping separately in mind — the two audio
mapping defects above — but neither was the stall.


---

## 95. Editor toolbar — reclaim the space (CLOSED 2026-08-20 — was already done)

Three changes to the same row, all about making room for the buttons that matter:

- **Drop the "Ben.Video" wordmark** and put the ghost logo at the far right of the bar instead, as
  a small image that does not grow the bar's height. Light and dark versions, chosen by the active
  theme like everything else on the page.
- **Hide the Initialize button once ffmpeg has loaded.** It is a one-shot action with a visible
  result — the status chip already says "Ready" — so keeping it costs permanent width for a button
  nobody presses twice.
- Use the space that frees up to show undo/redo (and friends) directly, rather than pushing them
  into the overflow where they need text labels (item 93).

**Closed 2026-08-20 on inspection: all three were already shipped**, as part of item 93's toolbar
work, and only this header was left stale — the same trap items 9, 55, 92 and 96 set. Verified in
the running editor rather than by reading: the Initialize button is rendered only in the Idle and
Error states, undo and redo are direct toolbar buttons rather than overflow entries, and the mark
sits at the end of the bar.

One detail worth recording because it is better than what was asked for. The mark is **not** two
images chosen by theme — it is a single mask painted with `--bv-text-muted`, so it follows the
theme by construction and there is no second asset that can drift out of step with the first.

## 96. Diagnostics and logs are visible to everyone (CLOSED 2026-08-20 — phase 7)

The editor's ffmpeg diagnostics panel and its error log are on the toolbar for every user. They are
operator tools: memory use, worker state, ffmpeg command output, internal errors. A client editing
their own footage has no use for them, and the output names internals they should not be reading.

Show them only to platform and group administrators. The editor itself has no notion of roles — it
is a component library — so the flag belongs in `VideoEditorOptions`, set by each host from the
identity it already has: the site from its user state, the WASM host from the signed-in account.

**Status: closed 2026-08-20.** The site host gates all three editor pages on
`IsSuperAdmin || IsAdmin`. The WASM host now asks `GET /api/me` after sign-in and sets
`ShowDiagnostics` from the answer — it has no claims to read, because sign-in there goes through
`MapIdentityApi` and yields tokens rather than a principal. It re-asks when the signed-in account
changes, which matters most in the sign-out direction.

The default stays **off**, and every failure resolves to off: signed out, no API configured, or the
call throwing. This is a display decision and not a security boundary — every endpoint those tools
reach authorises itself, so somebody who forced the answer would reveal a panel to themselves and
gain nothing. That is written next to the code, because the next reader will wonder.

Both directions are tested against the live WASM host. A test that only checked the panel was
hidden would have passed against the previous behaviour, which hid it from everybody — including
the people it exists for.

---

## 97. Expanded sidebar is clipped by the editor (CLOSED 2026-08-19)

With the site's sidebar minimised, hovering it expands a flyout over the page. On `/my-videos` the
flyout is cut off at the editor's left edge: the menu's own tooltip and the right-hand part of the
panel disappear behind the editor's chrome, so the labels are unreadable exactly where the flyout
is supposed to be doing its job.

A stacking problem, not a layout one — the menu is drawn, then something in the editor paints over
it. The editor's root establishes its own stacking context (it positions its panels, the preview
and the timeline against each other), so a flyout that relies on sitting above ordinary page
content has nothing to sit above once it crosses that boundary.

Worth checking against the template migration generally: the old Telerik-based chrome and the
current template do not necessarily agree about which layer the expanded sidebar belongs to, and
this is the first page where a full-viewport component sits beside it.

### Fixed — a z-index tie, not a clip

Reproduced with the sidebar minified and hovered open over `/my-videos`. Nothing was clipping it:
the editor's own horizontal rules were drawing *over* it. Measured, three `.bv-divider` elements at
**z-index 10, left: 90** — well inside the 252px the expanded sidebar occupies — against a sidebar
that shipped at **z-index 10** itself. Equal layer, later in the document, so the page won.

The shell now sits above page content: `.app-sidebar` at 1000 and `.app-header` at 1001, kept below
the popup layer `telerik-night.css` pins at 1090 so dropdowns and pickers still open above the
navigation. Fixing it at the shell rather than at the divider settles the whole class — a page is
entitled to stack its own content without knowing what the chrome uses.

Verified by hit-testing rather than by eye: at each divider's midpoint inside the sidebar's width,
the topmost element is now a sidebar link.

---

## 98. Charts — the template already styles ApexCharts, we just never shipped it (CLOSED 2026-08-20 — phase 2)

Ben likes the look of the ApexCharts in the SmartAdmin demos and asked whether they come with the
template. They effectively do: `smartapp.min.css` carries **190 `.apexcharts-*` rules** — the whole
theming layer, tooltips, legends, grid lines, the lot — so a chart dropped in would already match
the site. What is missing is only the library: `wwwroot/plugins/` holds `bootstrap` and `waves` and
nothing else, and no page references ApexCharts.

So this is not "build a charting story from scratch"; it is "add the script and use it".

**Where charts would actually earn their place**, rather than decorating a page:

- a group's own dashboard — cases opened/closed over time, investigations per month, equipment out
  on loan;
- the site administration screens, which currently report counts as bare numbers;
- a case's evidence over time, which is the one chart a client would care about.

Ben's reference for the full set of template functionality:
<https://getwebora.com/smartadmin/demo/dashboard-project-management.html> — worth walking through
before choosing, since the template ships more patterns than we have adopted.

**Peity** is the other half, and Ben flagged it separately: tiny inline charts — sparklines, mini
bars, mini pies — for places too small for a real chart. Think a row in a list, a stat tile, a
count beside a heading. Apex for the panel, Peity for the cell; picking one for both jobs is how
dashboards end up with either unreadable thumbnails or oversized decorations.

**Check first** whether the vendored Night skin's palette reaches the Apex variables the way it
reaches Kendo's; if not, the same bridge trick used for the video editor applies.

### How it shipped (2026-08-20)

**The licence turned out to be the real decision.** ApexCharts went dual-licensed at v5: free only
under $2M annual revenue, payable above it. For a site with monetisation on the roadmap that is a
dependency whose terms change exactly when it succeeds, so the vendored build is **4.7.0, the last
MIT release** — MIT cannot be revoked from a version already published. Recorded in
`wwwroot/plugins/apexcharts/VENDORED.md`, including the warning to read a future version's LICENSE
file rather than npm's `license` field, which said "SEE LICENSE IN LICENSE" for precisely the
releases where the terms changed.

**The check this item asked for, answered:** `smartapp.min.css` carries all 190 `.apexcharts-*`
rules and every one is light-theme; `themes/night.min.css` carries none. So `ben-charts.css` bridges
the dark half off `--bs-*` properties — but only for what the library renders as real DOM. Series
and axis-label colours are drawn from config, not CSS, so the JS module reads the palette at build
time and re-reads it on a theme change.

**Peity is not vendored.** It needs jQuery, which this site does not load; ApexCharts' sparkline
mode covers the same job. One library for both roles — recorded because Ben asked for Peity by name.

Built: `ApexChart.razor(.js)` (module-level Map keyed by container id — the multi-instance pattern
a dashboard requires) and `StatCard.razor`, generalised from the sidecar page's three hand-rolled
tiles, which were the only stat cards in the app and about to be copied. That page is the first
consumer: its "Installations by version" badge row was a bar chart with the bars left out, and is
now a bar chart.

One thing looking at it caught that no assertion would have: with a single category ApexCharts
stretches a bar across most of the panel, reading as a filled progress bar. Column width now scales
with category count.

Guards: three Playwright tests — renders with real geometry, one canvas per container (the
multi-instance regression), and the canvas paints no background of its own in dark mode. The
sidecar admin page also gained help documentation, which it had never had — found because the
orphaned-screenshot guard refused the new image.

---

## 99. Profile page — adopt the template's layout (CLOSED 2026-08-20 — phase 4)

Ben likes the SmartAdmin profile demo and wants our profile page to read like it:
<https://getwebora.com/smartadmin/demo/profile.html>

The catch is that ours carries considerably more than the demo does — two photos with their
two-key consent, contact details with their own visibility rules, email confirmation, addresses,
phones, links, and the investigation map. So this is a layout adoption, not a page swap: take the
demo's structure (the header band with the avatar and identity, the tabbed/carded body, the
consistent section rhythm) and fit our sections into it, rather than dropping our controls into a
page built for fewer of them.

Worth deciding up front which sections are prominent — most people come to this page to change one
thing — and whether the investigation map belongs on the profile at all or behind its own tab.

---

## 100. Internal messages — adopt the template's mail layout (CLOSED 2026-08-20 — phase 5)

Ben likes the SmartAdmin system-mail demo for our internal messaging:
<https://getwebora.com/smartadmin/demo/systemmail.html>

Three surfaces could share it, and they should look like one thing rather than three:

- the **case message board** (client ↔ group), today a plain list;
- **platform messages** from site administrators;
- the **notifications** page, which is closer to a feed but shares the read/unread rhythm.

The demo's useful parts are the list/detail split, the unread treatment, and the sender block —
all of which we already have data for. What it does not cover is who may see a given message, which
is the part our version cannot borrow: case messages carry visibility rules and platform messages
carry an audience, so the layout has to leave room to say so on each row.

Sequence this after item 99 — they are the same kind of work and share the card and header idioms.

**Closed 2026-08-20.** The group's Messages tab is now the template's mail idiom: a folder rail
(Inbox / Sent / Broadcasts / Direct / Case teams / Public, each with its unread count), a list of
`MailRow`s inside the template's own `<ul class="notification">`, and a reading pane under the list
in place of the modal. Platform messages on the notifications page use the same rows and the same
reading pane. The case thread stays a chat — bubbles are right for it — but its body now renders
through the shared `MessageBody`, and its received bubble no longer uses a fixed light fill that
turned white in night mode.

`MessageBody` is the point of the exercise beyond appearance: three surfaces each rendered a
message body their own way, and @mention/#hashtag linkification (phase 8) needs one place to land.
`MessageList.razor` is gone; nothing else consumed it.

**Deviation from the plan:** compose stayed a dialog rather than becoming a route. A composer with
bold/italic/lists is the "small formatting" exception to the pages-over-modals rule, and every mail
client in the world overlays it. The *read* view did move out of its modal, which is the half that
mattered.

**Three bugs found while building it, all invisible to an owner account:**

1. **Ordinary members could not open their own group at all** (fixed here). `GET
   /api/organizations/{id}` required Read access through the org security service, which returns
   true for Owners and Administrators and otherwise falls through to explicit grants and named
   roles. A plain Member had none, so the hub — whose first call this is — said "Organization not
   found or you do not have access" about a group they belong to and can post messages in. Three of
   BenCo's four seeded members were locked out. Active membership is now sufficient to read the
   organisation's own record; the check sits in the controller, not in `HasAccessAsync`, because
   members are emphatically not entitled to read every table.
2. **The recipient picker used an org-admin-only endpoint** (fixed here).
   `GetOrganizationMembersAsync` goes through the security service and throws for anyone who is not
   an org admin — that is, for exactly the person most likely to be sending a direct message. The
   catch around it turned the refusal into "this group has no other active members to write to."
   Now `GetOrgUserDirectoryAsync`, which asks only that the caller be an active member.
3. **The channel dropdown never called its own change handler** (fixed here). `ChannelChangedAsync`
   was written, correct, and unreferenced: the `BenSelect` had `@bind-Value` but no `OnChange`, so
   the member fetch never ran and the picker sat on "Loading members…" indefinitely.

The direct-message fix this item was partly about — compose sent `RecipientUserIds: []` while
offering Direct Message and Case Team — is also done, and is covered by a test that reads the
message as the recipient rather than trusting the composer.

All six tests in the new `Messaging` Playwright category sign in as **James, an ordinary member**,
not as Sarah, who owns BenCo. All three bugs above were owner-invisible. That is the lesson worth
keeping from this phase, and it generalises past messaging: a suite that only ever authenticates as
the most privileged account cannot see the product most people use.

---

## 101. Administrator dashboard — the template's stat-card bar (CLOSED 2026-08-20 — phase 3)

For administrators and site administrators, Ben wants the row of cards the template's project
dashboard opens with:
<https://getwebora.com/smartadmin/demo/dashboard-project-management.html>

Sensible things to put in them: sign-ins, new members, cases opened, investigations scheduled,
equipment out on loan, support tickets waiting. Each card wants a number, a period-on-period
change, and — per item 98 — a Peity sparkline rather than a full chart, which is exactly the size
the template's cards are built for.

**One of those needs data we do not keep.** The audit log records entity changes (create, update,
delete); nothing records a *sign-in*, so "logins" cannot be charted today without first recording
them. That is a deliberate decision to make rather than an oversight to fix quietly: sign-in
records are personal data with their own retention question, and a chart is a poor reason to start
keeping them indefinitely. Decide the retention window first, then record.

Everything else on the list is already in the database and only needs aggregating —
`AppUser.DateCreated` for new members, cases and investigations by their own dates, checkouts by
status, support tickets by state.

Gate the whole bar on the same administrator check the diagnostics panel now uses (item 96), so
one rule decides who sees operator-facing surfaces.

---

## 102. AudioFilePreview's toast has no element — its errors are shown to nobody (CLOSED 2026-08-19)

`AudioFilePreview.razor` declares `TelerikNotification? _toast` and routes three messages through
it, but no `<TelerikNotification @ref="_toast" />` exists in the markup, so `_toast` is null
forever and `Notify()` no-ops. Two of the three are save confirmations; the third is the one that
matters: **"Save failed — only WAV and MP3 sources can be clipped."** A user clipping from an
unsupported source sees nothing happen, which reads as a broken button.

Found by the compiler (CS0649), which is the same disguise the item #77 phase-6 bugs wore. Fix is
one line of markup; while there, check whether the site's shared toast pattern should be used
instead of a per-component TelerikNotification.

## 103. Six public components read auth state that may not have resolved yet (CLOSED 2026-08-19)

`PublicCaseDiscovery`, `HomeHero`, `CaseVoteWidget`, `EvidenceVoteWidget`, `UploadFileVoteBar` and
`FileCommentThread` read `UserState.IsAuthenticated` without awaiting `AuthReady` and without
subscribing to a state-changed event. Five are markup-only and merely render the signed-out
variant until something re-renders them; `PublicCaseDiscovery` is worse — `LoadVoteSummariesAsync`
bails on `!IsAuthenticated` **during initial load**, so on a hard navigation a signed-in user's
own votes never appear on the home page's case cards until they page or re-sort.

This is the same family as the Safari base-href bug and the editor toolbar repaint: state read
once, never followed. The site-wide pattern (await `AuthReady` in pages; components either receive
auth as a parameter or subscribe) should be applied, and the existing AuthReady guard test extended
to components that inject `IBenUserState`.

## 104. ImageEditorPlayer's opacity slider is fire-and-forget (CLOSED 2026-08-19)

The layer-opacity `<input @oninput>` calls `async Task SetLayerOpacity(...)` without awaiting it
(CS4014). Failures vanish as unobserved tasks, and a fast drag can interleave
`setLayerOpacity`/`RefreshLayersAsync` pairs out of order. Make the lambda async and await, or
funnel through a small debounce like the editor's own sliders use.

## 105. One flaky e2e test: RequestList_AnonymousRedirectsToLogin (CLOSED 2026-08-19)

The only failure in a 265-test run, and the product is fine — verified live, anonymous
`/my-requests` lands on `/login` with the sign-in form. The test asserts on `Page.Url` immediately
after `GotoAsync`, but the redirect is client-side after the circuit connects, so the assert races
it. Wait for the URL change (`WaitForURLAsync`) the way the login helper already does. NetworkIdle
proves nothing here — that lesson is already written down.

## 106. The editor pages don't link their own help doc (CLOSED 2026-08-19)

`using-the-video-editor.md` shipped with ten screenshots, and no screen links to it:
`MyVideosPage`, `CaseVideoEditorPage` and `VideoEditorPage` carry no `HelpLink`. The house rule is
docs + HelpLink in the same branch; the doc half landed alone. `getting-started` and
`requesting-an-investigation` are also unlinked but reachable from the help index, which may be
fine — decide deliberately.

## 107. Nineteen entity controllers are exposed surface with no caller (CLOSED 2026-08-19 — decided: they stay, documented)

The plain row controllers — `organization-addresses/emails/phones/links/notes/pages`,
`user-addresses/emails/phones/links/notes`, `user-messages`, `user-message-tos` and friends — have
zero client references. Their functions are served by the aggregate `MyContactInfoController`
(`api/me/*`) and the `api/admin/*` proxies; the lookup-*type* tables go through the generic
route-string client and are used. The rows are auth-filtered since security phase A, so this is
not a hole — it is dead surface that will rot and confuse. Decide: delete them, or mark them as
the deliberate raw-CRUD tier and say so in the controller docs.

**Decided:** they stay. They are SuperAdmin-locked since Phase A and enumerated by
`EntityReadControllerBaseAuthorizationTests`, so the surface is closed and guarded; deleting
thirteen controllers the night before a UAT deploy buys tidiness and risk. The decision and the
routing map (aggregates for users, admin proxies for operators, these as the raw tier) are
written on `EntityReadControllerBase` itself, where the next investigator will look first.

**How the rest closed (2026-08-19):** #102 routes through `BenToastService` — the documented
TelerikNotification replacement the component predated — and the dead field is gone. #103 applies
`WaitUntilAuthReadyAsync` to all six components (the three vote widgets reload, since their
summaries are viewer-specific; the comment thread and hero repaint; the discovery grid waits
inside `LoadVoteSummariesAsync`), plus a seventh nobody flagged: the notification bell, which was
already correct through `EnsureStartedAsync`. A new source-scan guard —
`Every_reader_of_auth_state_follows_its_resolution` — fails the build if a reader stops
following; it was verified to discriminate. An e2e hard-nav test was added too, with an honest
note: it passes against the un-fixed code on this machine (the race resolves in auth's favour
locally), so the source scan is the enforcing barrier, not it. #104 awaits the call. #105 rewrites
both redirect tests to wait on the URL change — the twin test shared the race and had merely been
lucky. #106 links the doc from all three editor pages, the standalone page from its signed-out
guard text, the two others from their headings.

---

## 108. Sitewide feature switches (CLOSED 2026-08-20 — phase 1 of the nine-phase plan)

Ben asked for SuperAdmin switches "for most logical sections of the site" while the two new
features (public feed, publications) were being flagged anyway. Ten switches now exist:
video editor, equipment, events, discovery, CMS public pages, media library, group messaging,
voting, plus the two unbuilt features.

**The rule the design turns on:** switching a section off takes its **URLs** down, not just its
navigation links. `FeatureGate` runs during the server render and shows the ordinary page-not-found
body, so a bookmark or a shared link reaches the same dead end as the menu does. The navigation and
the gate read one provider, so they cannot disagree — which is the failure mode this codebase keeps
re-learning, most recently as "a refusal the UI discards is worse than no rule".

**Shape.** Keys are declared in `SiteSettingKeys` with their defaults in one list
(`FeatureDefaults`) — established sections default ON so adding a switch never removes a working
feature, and the two unbuilt ones default OFF so they cannot appear early. No rows are seeded; a
key with no row reads its declared default. A new `[AllowAnonymous] GET /api/public/site-features`
returns the resolved bools, narrow in the same way `PublicSiteContactController` is narrow: it
walks the declared feature list, so a non-feature setting can never leak onto it. The website holds
a singleton `SiteFeaturesProvider` (30s snapshot, `RateLimitSettingsProvider` shape) whose fallback
is the declared defaults — an unreachable API leaves the site looking normal rather than stripped.

**Two bugs found while building it, both mine, both caught before commit:** `BooleanKeys` was a
static field initialised before the list it read, so every request touching the class died in a
`TypeInitializationException`; and the provider was a singleton holding a scoped client — a captive
dependency the container refuses outright.

**Also fixed in passing:** the admin settings page had no boolean editor, so the one bool-shaped
setting carried "Accepts true or false" in its description — an instruction that existed only
because the control was a text box. Switches now render as switches and save on toggle, and saving
invalidates the provider so the administrator who threw the switch sees it immediately instead of
up to thirty seconds later.

Guards: five xUnit tests (key parity across the two projects, default parity, admin-page coverage,
boolean rendering, and that the unbuilt features stay off) — the parity test verified to fail
against a deliberately drifted key. One Playwright test throws the switch through the real admin
UI and asserts the URL dies and returns.

### How it shipped (2026-08-20)

**Sign-ins get their own table, not audit rows.** `SignInEvent` is `AppUserId?`, `Utc`,
`Succeeded`, `Method` and nothing else — the dashboard's question is a `GROUP BY` over an indexed
date, and answering it from `AuditLogs` would mean string-matching action names across a mixed
free-text stream forever. A unit test pins the column set, because the temptation later is "just
an IP address", and that turns a counting table into a tracking one.

**Where the hook goes.** `/login` is mapped by `MapIdentityApi`, so there is no action of ours to
add a line to; recording lives in a `SignInManager` subclass, which every password check funnels
through. Writing the row is wrapped in a swallow-and-log: losing a data point beats locking people
out of the site. Verified live — three attempts produced two rows, the third being an address
matching no account, which never reaches a password check and has no user to attribute.

**Ben's chart ideas, costed and built:** busiest groups, largest groups, cases by status,
sign-ins and registrations over time, and the three geographic cuts (people, cases, investigations
by state) — all from `State` columns already on the entities. The registered-in-a-group funnel is a
stat card with the percentage stated, since the raw pair invites the reader to divide it wrong.
**Not built: anonymous visitor counts.** Nothing records people who are not signed in, so "new vs
returning" would mean building page-view tracking — a privacy and retention decision, not a chart.
The dashboard and its help doc both say so out loud rather than leaving the absence to be noticed.

**Two bugs found by looking at the page, not by tests passing.** Every chart rendered *twice*:
`create` is async, its library-load is a yield point, and two calls both cleared the container
before either registered anything to clear — ApexCharts appends, so the dashboard stacked two
complete charts per card. Serialised per container now, and the regression test counts canvases
against containers: it reports "8 containers produced 16 charts" against the unfixed code. Second,
a sparkline drew 300px wide inside a 258px card and hung 119px into its neighbour, because
ApexCharts overwrites the inline width of the element it owns; the width is measured from the DOM
now, with a wrapper the library cannot touch as backstop.

Two pre-existing e2e locators broke on the new org stats panel — `GetByText("Cases")` had always
meant "any text saying Cases" and only now had competition. Tightened to `GetByRole(Tab)`, which is
what they meant.

### How it shipped (2026-08-20)

Hero band (photo, name, sign-in address, chips that state something true about the account) over
three tabs: **About** (name, both photos, the two-key consent switch), **Contact** (the four
detail cards, two columns), **Where you've been** (the map, in its own tab as Ben chose). Every
mechanism moved unchanged — the plain-input-not-Telerik name field, the optimistic consent toggle
that reverts on failure, the data-URI photo pipeline that exists because an `<img>` sends no
bearer token.

**The find: `/api/my-investigations/attended` was returning 500 for every caller.** It ordered by a
property of the record it was projecting into, which EF cannot translate and reports at runtime,
not at compile time — the same shape as the two query bugs phase 3 hit. Both callers wrap it in a
catch that falls back to an empty list, so a total endpoint failure surfaced as the reassuring
sentence "you haven't attended an investigation yet", and the investigation map has been silently
empty for everyone since it was written. Fixed by ordering on the entity before the projection;
Sarah now has two attended investigations and a pin near Adams, Tennessee.

That is the third time a swallowed exception has hidden a working-looking failure in this codebase.
The catch is right — a history map must not take down someone's account settings — but a catch that
distinguishes "nothing to show" from "the call failed" would have said so.

**Cleanup the change forced:** eight e2e locators across five files said `GetByText("Cases")` when
they meant the Cases *tab*. They only broke once the org stats panel gave the word competition, and
Playwright's strict mode failed on the ambiguity rather than silently clicking the wrong element —
the good outcome. All now `GetByRole(AriaRole.Tab)`.

---

## 109. The test suite only ever signs in as privileged accounts (CLOSED 2026-08-22)

**Done:** `MemberSurfaceWalkTests` walks all eight member-facing org-hub tabs as James and asserts
content-not-refusal on each, plus the mirror check that the six admin-only tabs are ABSENT for
him. It caught a real bug on its first run: the Members tab rendered the admin-gated
membership-requests widget for everyone, so an ordinary member saw "Couldn't load this — 403" on
their own group's roster. Now gated on CanEdit. The API-level probe of 13 member endpoints found
the three 403s that exist are all deliberate (billing, CMS editing, addresses-with-access-controls).
The four seats were already in `BenTestBase` from the earlier phase; this closes the walking half.

### Original text

Phase 5 found three separate faults in group messaging that were **completely invisible to an owner
account** and total for everyone else: the organisation page refusing ordinary members outright, the
recipient list being fetched from an org-admin-only endpoint, and that fetch never being triggered
at all. See item 100 for the detail.

None of them were subtle. All three were caught within minutes of signing in as James — an ordinary
BenCo member — instead of Sarah, who owns it. The rest of the Playwright suite uses `UserEmail`,
which is Sarah, or the SuperAdmin. So the whole product is currently exercised from the two most
privileged seats in it.

BenCo's seed gives us four members at three levels (owner, active member, and Daniel, who is not an
active member and is refused by design), which is enough to test this properly without new fixtures.

What to do:

- Give `BenTestBase` a named ordinary-member account alongside `UserEmail`, so reaching for it is
  the easy path rather than a thing each test invents. `MessagingTests` defines its own today.
- Walk the surfaces an ordinary member is supposed to reach — the org hub's tabs, cases,
  investigations, calendar, files, equipment — as that member, and record what breaks. Expect more
  of the same shape: `HasAccessAsync` returns false for a plain Member on *every* table, so any
  surface gated on it that members are meant to use is broken right now.
- Where a surface genuinely is admin-only, the failure should say so rather than claiming the thing
  does not exist. "Organization not found or you do not have access" for a group you belong to is
  the wrong sentence even when the refusal is right.

This is adjacent to the standing "a server guard needs a UI path" rule, but the failure mode is the
mirror image: there the server refused and the UI discarded the refusal; here the server refuses
and the UI reports it faithfully, and nobody ever looked because nobody ever signed in as the
person it happens to.

---

## 110. Merge two groups into one (raised 2026-08-20 by Ben — low priority, logic still to work out)

An **admin-level** function: take two organisations and end up with one.

Ben's framing, which is the starting point rather than a spec:

- Someone has to choose **which group is the base** and which is merged into it. The distinction
  matters because everything that cannot be duplicated — the URL name, the settings, the identity —
  comes from the base.
- Someone has to choose **the name after the merge**. It is not necessarily either group's current
  name, so it is a decision, not a consequence of picking the base.
- It is **low priority**, and **the logic needs working through with Ben** before anything is
  built. Do not design this alone.

Things that will need answering when it comes up, noted now so the conversation starts further
along — none of these are decisions, just the questions the schema will ask:

- **Members.** Someone in both groups has two memberships with two roles; the merged group can only
  give them one. Higher role wins, base group's role wins, or ask?
- **The URL name that goes away.** Item 89 established that a released URL name can capture another
  group's traffic. A merged-away group's URL name should almost certainly become a permanent alias
  pointing at the survivor rather than being freed.
- **Cases, investigations, places, equipment, files, messages.** These reparent, but each carries
  its own visibility and ownership rules, and case visibility in particular is set per case with the
  original group as the audience.
- **Clients.** A client of the merged-away group did not agree to work with the survivor. Whether
  that needs telling them, or their consent, is a product question and not a data one.
- **Reversibility.** A merge that cannot be undone is a destructive admin action on other people's
  records, which argues for either a dry-run preview or a soft merge that can be unwound.

Sequence: after the current nine-phase plan. Nothing depends on it.

---

## 111. Evidence at a public investigation — who may add it, and is it all public? (BUILT 2026-08-22; publicity sub-questions still open)

**Shipped:** `EventEvidenceSubmission` + `EvidenceSubmissionStatus`; submit / mine / accepted /
queue / review endpoints plus an anonymous bytes endpoint gated purely on acceptance-and-public;
attendance proven by a confirmed `EventAttendanceInvite` OR org membership (members use the same
door, so the record of who offered what stays uniform); acceptance flips `UploadFile.IsPublic` and
messages the submitter; declining requires a reason. UI: submit panel + "your submissions" status
on the public event page (the public-record sentence sits ABOVE the button), accepted list on the
same page, review queue card on the group's Calendar tab that renders nothing when empty. Seed
adds a past public event with Daniel — who belongs to no group — as a confirmed attendee, which is
what makes the e2e deterministic. 9 controller tests (3 gates regressed) + 2 Playwright tests
covering the whole journey including a real file chooser and the signed-out read.

**Still open — the publicity sub-questions Ben did not decide:** a visitor's own recording of
themselves, other attendees appearing in someone's footage (the two-key consent question with
thirty strangers), and whether written documentation is as locked-open as raw evidence. The build
decides none of these; it states item 87's existing bargain and stops.

**Ben's decision:** attendees may SUBMIT, a member must ACCEPT — the queue shape, copying the
file-permission-request precedent. The publicity sub-questions (a visitor's own recording of
themselves, other attendees in someone's footage, documentation vs evidence) were NOT decided and
remain open below; the build tells submitters plainly that evidence accepted into a public
investigation's record is public, per item 87's recorded bargain, and decides nothing beyond that.

### Original text

Ben, while the accounts work was in flight: *"When we complete this, we need to address who and how
people who attend a group's public investigations are able to add evidence and if public events have
only public evidence and documentation."*

Two questions, and they are not the same one.

### Who may add evidence, and how

A public event brings **strangers** — that is the whole point of item 87, and it is what makes this
hard. The people who turn up are not group members, have no role, and in some cases have a
passwordless account created by clicking a link in an email. Today only the group's own members can
attach anything to an investigation.

The shapes worth weighing when we get here:

- **Nobody but members.** Simplest, and wastes the fact that thirty people were there with phones.
- **Attendees may submit, a member must accept.** A queue, like the file-permission requests already
  built. Keeps the group's record theirs while letting visitors contribute.
- **Attendees may add directly.** Fastest, and makes the group's evidence trail something outsiders
  can write to — which is a lot to hand somebody who signed up with an email address a fortnight ago.

The middle one is almost certainly right, and it has a precedent in this codebase to copy rather
than invent.

### Is everything at a public event necessarily public?

Item 87 already recorded the bargain: *"All collected evidence and data is public and cannot be made
private for an open investigation. The location can be scrubbed and hidden to the public not
attending, but evidence is not."* That settles the **group's own** findings.

What it does not settle, and what needs deciding:

- **A visitor's own recording of themselves or their friends.** Publishing it because they attended
  a public event is a different promise from the group publishing its own findings.
- **Other attendees appearing in someone's footage.** Thirty strangers in a dark building, and any
  of them may be identifiable. There is already a two-key consent rule for member photos; this is
  the same question with more people and less warning.
- **Documentation** — reports, notes, timelines — as distinct from raw evidence. The quoted rule
  says "evidence"; whether a group's written write-up is equally locked open is not stated.

Sequence: after the feed and publications. It depends on nothing in them, but it is a policy
decision as much as a build, and it should not be made in the middle of something else.

---

## 112. The 2FA enrolment panel hangs on "Starting…" (CLOSED 2026-08-20)

Pressing **Turn on two-step sign-in** on the profile's Security tab leaves the button reading
"Starting…" indefinitely. The QR never appears and no error is shown.

**What is and is not broken.** The API underneath is complete and verified end to end against a
live server with real TOTP codes computed from the secret it issues: setup, enable, sign-in with an
app code, sign-in with a recovery code, single-use enforcement on recovery codes, and disable. It
also rendered correctly through the browser once, early in the same session, before the panel was
finished. It is the **panel** that hangs, not two-factor authentication.

**What the evidence rules out.** A twenty-second `CancellationTokenSource` around the call does not
surface either — no timeout message, no error, no re-render — and `finally` sets `_busy = false` and
calls `StateHasChanged`. So the await is not simply slow: **the circuit stops re-rendering
altogether**. That also rules out the HTTP call itself, and swapping `PostAsync` for
`SendExpectingReasonAsync` (the helper every other POST on this page uses successfully) changed
nothing. `GET /api/me/2fa` on the same page works — the panel renders "Off" from it.

**Where to look next**, roughly in order of suspicion:

- Something in the chain doing sync-over-async and deadlocking the circuit's synchronisation
  context. A blocked circuit fits every symptom, including cancellation appearing to do nothing.
- `TelerikQRCode`'s first render — it is the one component on this page never used anywhere else in
  the product, and it is what the successful branch renders.
- The interaction between the panel's `StateHasChanged` and Telerik's masked textboxes.

**Reproduce it** by signing in as any account with 2FA off, opening `/profile` → Security, and
pressing the button. `AccountTests.EnrollingWithARealCodeTurnsItOn` is written, is currently
`Assert.Ignore`d pointing at this item, and will pass once the panel does — it should not be
deleted.

### The cause (found 2026-08-20)

**`TelerikMaskedTextBox` does not splat unmatched attributes — it throws.**

```
System.InvalidOperationException: Object of type
'Telerik.Blazor.Components.TelerikMaskedTextBox' does not have a property
matching the name 'aria-label'.
```

The `aria-label` had been added to that component the day before, as the fix for a *different*
finding: `LabelAssociationTests` had caught a `<label for>` pointing at nothing, because Telerik
renders no `id` on its inner input — only a `data-id` GUID. The attribute was added on the
assumption that Telerik splats what it does not recognise. It does not.

The exception is thrown **during render**, not during the call, which is why every symptom pointed
away from the truth: the API had already answered, the `finally` never took effect, the
cancellation token never fired, and clicking a different tab did nothing either. **The circuit was
dead.** It froze displaying the last frame it had successfully rendered — the one with the button
reading "Starting…".

What actually found it: reading the **browser console** through Playwright. Nothing server-side
showed it. The diagnostic that split the problem in two — a `Console.WriteLine` at the top of the
API action, proving the request arrived, was resolved and answered in milliseconds — is what
justified looking at the client at all.

### The fix

Both code boxes — the enrolment panel and the sign-in page — are now **plain inputs** rather than
`TelerikMaskedTextBox`. This is not a retreat from the house preference for Telerik components; the
component genuinely cannot do what is needed here:

- no `id`, so no `<label for>` can ever name it, and it has no accessible name at all;
- it throws on an unmatched attribute, **during render**, killing the circuit;
- no `inputmode="numeric"` and no `autocomplete="one-time-code"`, so a phone offers neither a
  numeric keypad nor the code it has just received.

A plain input gives all three, and a real label. A test asserts the accessible name comes from a
label pointing at a real id.

### Guarded against recurrence

`TelerikAttributeSplattingTests` scans every `.razor` file in the site, the library, the editor and
the WASM host, and fails on any Telerik tag carrying a plain HTML attribute. Verified by
reintroducing the bug: it reports the offending file, tag and attribute by name.

The next person will make the same assumption — that Telerik splats what it does not recognise — and
this is how they find out in a second rather than an afternoon.

### Two things fixed alongside

- **`LockedOut` was being reported as "invalid email or password".** Found because a run of probes
  locked the SuperAdmin account and the page said the password was wrong — sending somebody to
  reset a password that was right, when only waiting helps. The sign-in page now distinguishes five
  refusals.
- **`SigningInWithTwoStepAsksForTheCodeAndAcceptsIt` was lying twice.** It shared the fixture's
  account, so its result depended on what the previous test left behind; and the sign-in page
  **pre-fills developer credentials in Development**, so a submit landing before the test's own
  values reached the server model signed in as the developer, navigated to the home page, and
  looked exactly like a two-step account being let through without a code. It now creates its own
  throwaway account, and waits for the pre-fill to appear — which is itself proof the circuit is
  live — before replacing it.

### The misdiagnosis, recorded because it cost the most

Several failing tests were read as a slow cold start, and timeouts were raised to 60, then 90, then
120 seconds. **Measured, the page is interactive about 450ms after navigation on a cold host**, and
server render is 9ms. The real fault: a character typed before the circuit connects is not merely
ignored — the first interactive render overwrites the input from the server's empty value, so the
keystroke is *erased*. The cure is to type again, not to wait longer. Those tests now run in about
two seconds; they were taking ninety.

A generous timeout on a fast page buys nothing and hides the next real regression behind a minute
and a half of silence. Ben spotted it: *"It was almost instantaneous before these changes."*


---

## 113. Accounts: sign-up, @names, email confirmation and two-step sign-in (2026-08-20 — mostly shipped)

Four things Ben asked for in one stretch, all of them account identity and all of them prerequisites
for the public feed rather than part of it.

### @names — shipped

Ben: *"Lets let people choose a unique name to use for the @name when they create their account. We
verify it is not already taken."* and *"For now, we will not let them change their @name but in the
future we might... but super low priority."*

`AppUser.Handle`, unique, lower-cased, 3–30 characters of letters, digits and underscores, starting
with a letter. Reserved words are refused — route words that would make a profile URL read like a
section of the site, and names somebody would trust in a mention (`support`, `admin`, `ishaunted`).
Checked live as it is typed on the sign-up page; the unique index is what actually decides, and
registration reports a collision that lands between the check and the insert.

**Why a handle at all**, rather than matching display names: names here are neither unique nor free
of spaces, so `@sarahmitchell` could only be matched by stripping punctuation and hoping exactly one
account came back. Two people called Sarah Mitchell would then have meant notifying both or neither
— and the answer would change as accounts were added, so a post's meaning would depend on who else
had signed up since.

Every account has one. `UserHandleBackfillService` gives one to anything created before the column
existed and does nothing on every start after that; the other creation paths (Entra, event magic
links, the seeders, an administrator) get one derived from the display name or email and uniquified.

**Follow-up, explicitly low priority per Ben:** letting somebody change their @name later. It is not
free — the handle appears in other people's posts — but the mention tables already store the
account's id rather than the text, so old mentions would keep pointing at the right person.

### Sign-up and email confirmation — shipped

There was **no self-service sign-up at all** before this: accounts arrived through Entra, an invite,
or an administrator. `/signup` now creates one, and `/confirm-email` is where the link lands.

`MapIdentityApi`'s own `/register` could not be used — it takes an email and a password and nothing
else, so an account made through it has no display name and no @name, and a handle cannot be added
afterwards without letting people change it. Registration is therefore our own endpoint, generating
the same token type and pointing at the same confirmation flow.

Two decisions worth keeping:

- **The answer is identical whether or not the address is already registered.** An endpoint that
  says "that email is taken" is a way of testing who has an account here — worth more care on a site
  about people's homes than a precise error is worth. The real account holder gets an email saying
  somebody tried, which is the only party entitled to know. The @name is reported precisely, because
  it is public by nature.
- **Confirming happens on a button press, not on page load.** Mail scanners and security gateways
  fetch every link in a message; a confirmation that happened on load is one they can complete on
  somebody's behalf, which proves nothing about the address reaching a person.

### Two-step sign-in — API shipped, panel blocked

Standard TOTP, so **Duo Mobile and Okta Verify both work**, along with Google Authenticator,
Microsoft Authenticator and 1Password — they scan the same code. (Duo's push approval and Okta as a
single-sign-on provider are separate integrations and are not this. Okta as an identity provider
would sit beside the existing Entra OIDC path.)

**Opt-in, per account, and never required** — Ben: *"Let the end user determine if they want 2FA or
not. It is not an administrator-related setting."* The administrator screen previously had a
`TwoFactorEnabled` checkbox with no enrolment behind it, which would have switched on a second
factor nobody could satisfy and locked that person out of their own account. The control is gone and
the field is no longer written there; it shows as read-only status, because it is worth knowing when
somebody writes in unable to sign in.

Sign-in needed no new endpoint: `MapIdentityApi`'s `/login` already takes `twoFactorCode` and
`twoFactorRecoveryCode` and answers `RequiresTwoFactor`. Reading that detail also fixed a separate
long-standing lie — an unconfirmed account was being told "invalid email or password", which sends
somebody off to reset a password that was always right. Four refusals now say four different things.

**Telerik 14.1.0 has no OTP input component**, so the code boxes are `TelerikMaskedTextBox` with a
`000000` mask. It does have `TelerikQRCode`, which is what renders the enrolment code.

**The panel hangs — see item 112.** The API is complete and verified end to end with real TOTP
codes. Do not read item 112 as "2FA does not work"; read it as "the enrolment page does not".

### Found along the way

- A **captive dependency** — a hosted service holding a scoped service — which the container refuses
  to build, at startup, before anything else runs. Second instance of that class this month.
- **`TelerikMaskedTextBox` renders no `id`**, only a `data-id` GUID, so a label pointing at the
  component's `Id` names nothing: clicking it does nothing and a screen reader announces an
  unlabelled box. `LabelAssociationTests` caught it; the accessible name comes from `aria-label`
  instead, and a test now asserts the attribute reaches the input.
- **Blazor Server pages are server-rendered long before their circuit connects**, so a test that
  types as soon as an input appears triggers no handler at all and then waits out its timeout. It
  passes or fails depending on how warm the host is, which reads as flakiness. The account tests
  wait on the page's own echo to prove interactivity first.

---

## 114. Every page waits on a CDN for fabric.js (CLOSED 2026-08-21)

`App.razor` loads Fabric from an external CDN on **every page of the site**:

```html
<script src="https://cdn.jsdelivr.net/npm/fabric@6/dist/index.min.js" defer></script>
```

It is only needed by the image editor, and it is pre-existing — it came in with the original
SmartAdmin shell — but it is paid for by every visitor on every page, including the sign-in page and
the public microsite.

**How it surfaced.** The first navigation of a Playwright run times out on
`waiting until "load"` at 30 seconds, intermittently. Measured with a warm connection the fetch is
352ms; cold, with DNS and a TLS handshake to an external host, it is the slowest thing on the page
by a wide margin, and `load` does not fire until it finishes. Every test context is fresh, so there
is no browser cache to help.

**Why it matters beyond the tests:**

- **A visitor's first page view pays for it too**, and they have no warm connection either.
- **It is a third party on the critical path.** If jsdelivr is slow, blocked by a corporate proxy,
  or unreachable — which is the normal state of an air-gapped or restricted network — every page on
  the site waits, on a script only the image editor uses.
- It is a privacy leak of sorts: every page view tells a CDN a visitor was here.

**The fix is one already used here.** ApexCharts was vendored under
`wwwroot/plugins/apexcharts/` with its licence and a `VENDORED.md` recording where it came from and
why. Fabric should be vendored the same way — and, better, loaded **only by the image editor**
rather than from the shell, since nothing else touches it.

Small, self-contained, and it removes an external dependency from every page load.


---

## 115. The public feed (SHIPPED 2026-08-20 — phase 8)

Short-form public posts: anyone signed in can post, follow people, mention them with `@name` and tag
posts with `#tag`. **Off by default** behind `features.public-feed`, and the API 404s wholesale when
it is off — not 403, because a disabled feature should not be discoverable by the shape of its
refusal.

### What it is

`/feed` with two modes (everybody, or the people you follow), `/feed/tags/{tag}`, `/feed/{postId}`
for a thread, and `/feed/people/{userId}` for somebody's feed presence. Posts are plain text, at
most 1,000 characters — short-form is the point, and a wall of text belongs in a publication.

Storage reuses **`OrgMessage` with `ChannelType.PublicFeed`**. That table already had a nullable
`OrganizationId` and parent-based threading, which is exactly a feed post and its replies; a second
near-identical table would have meant two places to fix every time the way a message is written
changes.

### Decisions worth keeping

- **Anyone signed in may post**, Ben's call — which is what makes moderation part of the feature
  rather than an optional extra.
- **Reports hide nothing, and no number of them does.** There is no threshold, deliberately: an
  automatic one removes whatever is least popular rather than whatever breaks the rules, and the
  people worst served by that are the ones with unusual things to say — which is most of this
  site's subject matter. Hiding is a person's decision, recorded against their name.
- **Hidden, not deleted.** A deleted post takes its replies, its reports and the record of the
  decision with it. One decision resolves *every* pending report against that post, because five
  people reporting one post is one decision.
- **The moderation queue is not behind the feature flag**, unlike every reader-facing page.
  Switching the feed off does not un-report anything, and stranding those complaints behind the
  switch would leave the only record of them unreachable.
- **A mention is read when the post carrying it is opened** — reusing `OrgMessageView` rather than
  inventing a second read marker. A post scrolling past in the feed does not count: "you were
  mentioned" is the notification somebody would most resent losing unseen.

### Two things this fixed on the way

**Mentions now resolve on the `@name`, exactly.** They were written before handles existed and
matched a normalised display name, which had to refuse whenever two accounts normalised alike —
and worse, its answer could *change* as accounts were added, so a mention that resolved today would
stop resolving the day a second Sarah signed up. It is also one indexed lookup now instead of
reading every account into memory.

**`FeedTextSegmenter` is separate from `FeedTextParser`, and that is not duplication.** The parser
answers "which names does this post contain", returning each once — so a renderer driven by it would
linkify the first mention of somebody and leave the second as plain text. The segmenter answers
"where are they". Both call the parser to decide what a token *is*, including the rule that earns
its keep most: an email address is not a mention.

### Deliberately not built

Likes, reposts, images, and any ranking beyond chronological. Following is one-directional and
unacknowledged — it changes what you are shown, not what you may see, and a mutual-consent model
would imply a privacy guarantee the feed does not make. No report-reason prompt yet: the endpoint
takes one, and a dialog for it is worth adding once there is evidence administrators need more than
"somebody objected". No per-person feed endpoint — a profile filters a page of the feed, which is
honest for recent activity and thin for an old account; that is the fix if profiles turn out to be
somewhere people browse.

Authors are told nothing when a post is hidden. Whether they should be is a decision about wording
more than mechanism, and worth making deliberately rather than adding quietly.

### Tests

16 on the controller, 22 on the segmenter, 25 on the parser, 6 in the browser. The three properties
worth naming each have their own test — the feed 404s wholesale when off, a hidden post disappears
from every read path, and a report never hides anything by itself — and each was verified by
breaking the code it names.

The browser tests turn the flag on and put it back as they found it, and check the switched-off case
by **navigating repeatedly rather than waiting**: the website reads its flags from a snapshot
refreshed on a timer, so a page that has already rendered will never change its mind. Polling the
DOM of one page waits for something that cannot happen.

---

## 116. Publications (SHIPPED 2026-08-20 — phase 9)

The last phase of the nine-phase plan. Long-form writing by groups, readable by anybody.

### What it is

A **publication** is a group's own title — *Field Notes*, *The Ridgeway Case*. Inside it are
**posts**: case write-ups, research, notes worth more room than the feed gives them. Readers need
no account. Subscribing needs one, because a subscriber is somebody the group can reach.

Three tables — `Publication`, `PublicationPost`, `PublicationSubscription` — rather than reuse of
`OrganizationPage`. A CMS page carries site structure; a post is chronological and subscribable.

Behind `features.publications`, **default off**.

### Decisions worth keeping

**A draft is a post with no `PublishedUtc`.** Not a second status column: two fields that must
agree eventually disagree. Creating a post never publishes it, whatever the author intended.

**Two gates, both required.** A post is public only if it is published *and* its publication is
public. Independent on purpose — a group can get several pieces ready before anyone knows the
publication exists.

**The authoring and public controllers are separate classes**, not one set of queries with a
flag. One forgotten argument on a shared path is how a draft reaches the world, and there is no
forgetting an argument that does not exist.

**Slugs are derived once and never regenerated** — item 89's lesson, applied before it could be
repeated. Renaming changes the heading, not the link.

**Bodies are sanitised on save, not on render.** The stored markup is the safe markup, so no
future read path can resurrect what the author sent, and a change to the sanitiser cannot quietly
alter a thousand published articles.

**`RequiredTier` is written by nothing and withheld anyway.** Building the withholding path now,
against a column that is always null, costs nothing; retrofitting it later means changing what is
already being read. The body is withheld by the server — a paywall implemented in CSS is not a
paywall. This is the whole of what item 85 gets for now.

**Unsubscribing marks rather than deletes.** Unlike a feed follow — deleted outright, because a
soft-deleted follow is a record of who once read whom — a subscription is what a payment would
attach to, so a cancelled one stays answerable for what it covered. Re-subscribing revives the
same row.

### The anonymous path is the product

A publication nobody can read without an account is a newsletter with no readers. The public
controller is `[AllowAnonymous]`, the client's public calls use `GetAnonymousAsync` — which sends
no bearer token *even when the reader is signed in* — and **the tests hold no principal at all**.

That last part is the point. Sign a test in and read a public page and the feature passes its
tests while being broken for every real visitor: the author always sees what the visitor cannot.
The two help screenshots for readers were likewise captured signed out.

### Deliberately not built

Billing of any kind. Email digests when a post goes up — the scheduler from phase 6 is the right
home for it and it is a separate piece of work. Comments. Cross-posting to the feed.

Deleting a whole publication was left out here and **added straight afterwards** — see item 118.

### Tests

16 on the controllers. Six were run against deliberately broken code first — draft filter
removed, public gate removed, tier check disabled, listing bodies included, subscription revive
disabled — and each failed as it should before being trusted.

---

## 117. The sidebar, grouped by subject (SHIPPED 2026-08-20)

Eighteen top-level rows signed in with everything on, which is what prompted it. Now eight:
Notifications and Organizations stay put — checked constantly rather than navigated to — and the
rest fold into **My Work**, **Equipment**, **Media** and **Community**. Signed out it stays flat;
four entries folded into two groups costs a click each to save two rows nobody was struggling to
read.

### What grouping broke, and had to be fixed with it

**Only a leaf rendered a badge.** Folding a badged item into a group hid it — turning the change
meant to make the sidebar readable into a way of losing the one signal it exists to carry. Groups
now sum everything beneath them recursively, and take urgency from the oldest unread item in the
subtree. The parent badge shows only while closed.

**The filter matched one level deep.** Administration's tools are two levels down, so filtering
for one of them found nothing — a pre-existing bug that grouping would have spread to most of the
menu. Matching is recursive, matched groups are pruned to their matching children, and everything
left is expanded: a filter that reports a match and then hides it is worse than no filter.

**A group of one is worse than the item alone** — same row, plus a click to reach what the row
already named. Switch the media library and video editor off and Media held only Upload Files.
A one-child group now renders as its child.

### Found on the way: the bell under-explained itself

Checking whether the bell would cover a badge inside a collapsed group turned up that it would
not have covered all of it. `TotalCount` sums every bucket; the dropdown was a hand-written list
of seven; `FeedMentions` was in the first and not the second. A mention made the bell read "3
items waiting" and then account for two.

Fixed, and guarded by a test that reads the bucket list off `NotificationSummaryResponse` itself
rather than a list kept in step by hand — so the next bucket added cannot repeat it. Verified by
deleting the new row and watching it fail.

`My Checkouts` also now carries the equipment bucket it always had available and never showed.

---

## 118. Deleting a publication (SHIPPED 2026-08-20)

Shipped with 116 leaving no way to remove a publication at all — only its posts. A group that
created one by mistake was stuck with it, and the address never moves on rename, so "created by
mistake" mostly means "wrong title".

### The rule

**A group administrator may delete a completely empty publication. A SuperAdmin may delete any**,
taking its posts and subscriptions with it.

Two tiers because deleting a publication is two different acts. Removing one with nothing in it
costs nobody anything. Removing one people have written in and subscribed to destroys work and
breaks every link somebody shared — not a thing to do by clicking twice.

**A cancelled subscription still blocks the group.** "Empty" is meant to mean nothing ever
happened here, and somebody having subscribed and left is something happening.

### The one refusal a real user can reach, and why it matters

The group's own listing counts *live* subscribers; the rule counts *every* subscription ever made.
So a publication with one cancelled subscription looks empty on screen, the button is offered, and
the server refuses.

That is deliberate, and it is why the refusal is a sentence rather than a status. The alternative
— hiding the button on a guess the client cannot make correctly — leaves somebody with no way to
find out why. Five instances of "a server guard the UI discards" are already on record; this one
is guarded by a test that asserts the message names the blocker, and **the advice follows the
blocker**: telling somebody to delete the posts first when a subscriber stopped them sends them to
stare at an empty list.

Verified live from the org administrator's seat — not the SuperAdmin's, which bypasses the rule
and would have shown nothing.

### Tests

Six, four of them load-bearing and each verified against deliberately broken code: the rule
disabled, cancelled subscriptions excluded from the count, and the cascade removed so posts would
have been orphaned.

---

## 119. Ordinary-member seats in the test suite, and the two bugs the first walk found (SHIPPED 2026-08-20 — closes item 109)

Item 109 predicted that walking the member-facing surfaces as an actual member would find more of
the phase-5 shape — `HasAccessAsync` false on every table for a plain Member, refusals rendered as
empty lists. The very first walk found two.

### The seats

`BenTestBase` now names four: SuperAdmin, `UserEmail` (Sarah — administrator, the default and the
trap), **`MemberEmail` (James — plain Member, no grants, no named role)**, and `ClientEmail`
(Daniel — account, no memberships). Member/Client were previously re-declared in seven fixtures;
all local copies are gone, and the six hardcoded Daniel logins in RequestStatusProgressionTests
point at the seat. CoClientAccessTests' "stranger" is documented as the member seat wearing a
different hat.

### The fixture

`OrdinaryMemberSurfaceTests` (Category=OrdinaryMember) walks every tab the hub shows a member —
and asserts **real content, never a page merely loading**, because the failure it hunts renders as
"No records available", not as an error. It also asserts the admin tabs are absent, so "fix the
member" cannot be satisfied by giving members everything.

### Bug 1: the Files tab

`GET /api/organizations/{id}/files` required OrganizationFiles/Read through the security service —
false for every plain member. The tab rendered the 403 as an empty grid; a member with a group
handbook on the server was told the group had no files. Fixed: reading the list needs active
membership; the writes keep their permission gates. Same fix the org record itself got in phase 5.

### Bug 2: the Members tab

The roster was read from `/security/users` — the *manage-access* endpoint, Owner/Administrator
only. Details said "Members: 3"; the Members tab told James there were none. Fixed with a new
member-readable `GET /api/organizations/{id}/roster` (same shape, no contact details — display
name and role only, matching the user-directory precedent); the manage endpoint keeps its gate.

### What made both invisible

136 sites in the client adapter turn any non-2xx into `[]` — a refusal and an empty group are the
same value on screen. Too systemic to fix inline; raised as item 120.

### Verified

The fixture ran against the unfixed code first: Files and Members failed exactly as predicted
(Cases/Investigations failures were my locators, corrected and noted in the test). Green after the
fixes. Tabs confirmed working from James's seat live: Details, Members, Cases, Investigations,
Calendar, Messages, Files, Equipment.

---

## 120. The client adapter cannot tell "refused" from "empty" (CLOSED 2026-08-22 — all 120 converted; render debt tracked as item 141)

`WebApiClient.GetAsync` returns `default` on any non-2xx, and 136 call sites in
`BenAdminClientAdapter.*` follow it with `?? []`. Every one of them renders a 403 — or a 500 —
as "No records available". This is the mechanism that hid both item-119 bugs and the phase-5
messaging faults: the server refuses correctly, the page reports an empty world, nobody sees an
error anywhere.

Worth designing once rather than patching per-site: likely a `GetExpectingReasonAsync` sibling
(the Delete/Send variants exist) plus a component-level convention for "couldn't load" vs
"nothing here". Until then, any new list surface should assert real content in its tests, per
the OrdinaryMemberSurfaceTests pattern.

### Progress

| Slice | Date | Converted | Ratchet |
|---|---|---|---|
| Organization | 2026-08-21 | 19 | 120 → 101 |
| **Case** | **2026-08-22** | **20** | **101 → 81** |
| **Platform** | **2026-08-22** | **14** | **81 → 67** |
| **Equipment** | **2026-08-22** | **22** | **67 → 45** |
| **User** | **2026-08-22** | **13** | **45 → 34** |
| **Investigation** | **2026-08-22** | **8** | **34 → 26** |
| **Cms, Places, Media, Publications, Membership, Feed, Account** | **2026-08-22** | **26** | **26 → 0** |

**Case slice (branch `feature/loadresult-case-area`).** All 20 swallowing methods in
`BenAdminClientAdapter.Case.cs`, their declarations across `IBenCaseClient` / `IBenPlatformClient` /
`IBenMediaClient`, and 19 consumers. `CaseMessageThread` — shared by the client and org sides — now
takes a `LoadResult` delegate, so a refused thread stops telling a client nobody has written to
them. Six adapter tests were **inverted**: they asserted that a refusal "returns empty", which made
them green tests defending this very bug.

New `LoadResultRenderedGuardTests` requires any `.razor` calling a converted method to mention
`BenListState` or read `.Failed`, with an allowlist for genuine decorations that records the reason.
It stops the likely half-conversion — silencing the compile error with `.Items` and leaving the page
as wrong as before while the ratchet records progress. (A bUnit-style render test was the plan;
there is no bUnit in this solution, so this follows the existing source-scan convention instead.)

**None left.** All 120 are converted, and `SwallowedFailureRatchetTests` is now a **ban** rather
than a count — it scans the whole `Ben.Web.Services/WebApi` folder, not just the adapter, with one
principled exclusion: `LoadResult.Items => _items ?? []`, which is the mechanism that makes the
rule enforceable rather than an instance of breaking it. Verified to discriminate.

The final pass reached past the adapter again and converted **15** swallows inside `WebApiClient`
itself, which the old ratchet never counted.

**Three mutations were found wearing the same defect** and fixed differently, because "did this
happen?" is not "is this list real?": `SetMyEquipmentSharesAsync` (a refused save closed its dialog
reporting success), `SetInvestigationLeadAsync` (an empty roster would have wiped everyone off the
screen as though it had worked), and `ScanAudioForEvpAsync` — where an empty list does not mean the
scan came back clean, it means the scan never ran, and on this site "no EVP detected" is a finding
somebody acts on.

**Platform slice** brought the internal messaging surfaces over, which item 120 named from the
start. Two of the scheduler's call sites were correctness bugs rather than display ones: the
attendee dedup set and the invitee prefill both read a refusal as "nobody is invited", which would
have re-invited the whole list.

**Equipment slice** was the one where the guard paid for itself. The area uses `[.. await …]`
spreads, so 27 sites were a one-line mechanical change each — the whole slice compiled green while
every page still reported a refusal as empty. Registering the new method names in
`LoadResultRenderedGuardTests` turned it into a worklist of 14 files, which is how the twelve real
surfaces were separated from the two genuine picker feeds.

It also turned up a **mutation** with the same defect: `SetMyEquipmentSharesAsync` is a PUT whose
refusal became `?? []`, and `EquipmentShareEditor` discarded the result, closed its dialog and
reported success — somebody believed their equipment was shared when nothing had been saved. It now
returns `(Shares, Error)` and the editor shows the reason.

**User slice** reached past the adapter for the first time. `GetAllUsersAsync` and
`GetOrgUserDirectoryAsync` delegate to dedicated methods on `WebApiClient` itself rather than the
generic `GetAsync`, so their `?? []` sat outside the ratchet's scan — the ratchet counts
`BenAdminClientAdapter.*.cs` only. Both are converted; worth remembering that the ratchet measures
one file pattern, not the whole client.

It also turned up a defect in **item 133's own work**. An adapter that reshapes a response —
`Ok(result.Items.Select(…))` — silently drops `SessionExpired`, and both places doing that by hand
had dropped it: a signed-out roster told the reader to "try again" instead of to sign in.
`LoadResult.Map` now carries the whole outcome across and changes only the shape, and the
organization roster uses it.

**Three dead methods found so far**, declared and implemented but called by nothing:
`GetPublishedInvestigationsAsync`, `GetEquipmentItemCheckoutsAsync` and `GetMyPhotosAsync`.

**For whoever takes the next slice:** a list that is mutated in place — `Insert`, `Add`,
`RemoveAll` — must **not** be wrapped in `BenListState`. The wrapper keeps rendering the load's own
emptiness, so the first item added never appears. Branch on `.Failed` beside the existing empty
check instead.

---

## 121. No seeded org Owner who is not also SuperAdmin (CLOSED 2026-08-20 — see below)

Both seeded groups are owned by the SuperAdmin account: `DevelopmentDataSeeder` takes `owner`
from `SeedData:SuperAdmin:Email`, and BenCo's roster is AverageBen (Owner), Sarah
(Administrator), James and Emma (Members). So the **Owner role tier cannot be exercised
separately from SuperAdmin** — every check an Owner should pass by membership role, the seeded
owner passes earlier by app role, which is precisely the masking item 109 was about, one tier up.

Item 109's fixture covers Member vs Administrator; Owner vs Administrator differences (the org-id
display rule in OrganizationView already distinguishes them, and anything else that keys on
`OrganizationMemberRole.Owner`) are invisible until a seeded org has a plain-account owner.

Cheap fix when wanted: seed a third small group owned by Emma, or promote Sarah to Owner of BenCo
in the seeder (she is its Administrator today; MessagingTests' comment already believes she owns
it, which is how stale that assumption is).

### Closed same day, by DevelopmentRosterSeeder

A new seeder (`SeedData/DevelopmentRosterSeeder.cs`, running after DevelopmentDataSeeder, same
`DevData:Enabled` flag) widens the world rather than patching the one gap:

**Eleven new accounts.** Investigators Marcus Webb, Olivia Chen, Tyler Brooks, Rachel Kim, David
Okafor, Priya Sharma, Nathan Cole, Grace Delgado (@benco.dev); clients Linda Maxwell, Robert
Hayes, Karen Foster (@example.com — clients arrive from anywhere, so their addresses look like
it). Passwords follow the existing seed pattern (e.g. `M@rcus!Webb2026`); handles arrive via the
normal backfill service.

**The rosters.** TGH grows to eight (Rachel is a second Administrator, so "the admin" stops being
one person); NPS gains Priya and Nathan. **Music City Spirit Seekers** (`mcss`) is the third
group, **owned by Emma** — the first Owner who is not SuperAdmin, which is what closes this item.
Grace administers it; Olivia and Nathan belong to two groups each, so cross-group membership stops
being hypothetical.

**Three client stories, one per state that matters.** Linda → accepted, Active at TGH (manager
Rachel). Robert → accepted, Summarized at MCSS (manager Emma). Karen → Submitted, still sitting
in TGH's Requests queue, so decline/resubmit has something real to act on by hand.

**Three investigations with full rosters** — two completed (32 and 60 days back, so the dashboard
time charts have shape), one scheduled 9 days out (so reminder surfaces have a subject). RSVP
states are mixed on purpose: a roster where everyone accepted exercises none of the RSVP
rendering.

**Three real brands** (Panasonic, K-II Enterprises, Tascam) with era-appropriate models, and
items owned by Marcus, Olivia and Priya — including one shared into two groups and one
deliberately unloanable and unlisted.

Verified live from Emma's seat: the full owner strip (Settings, Roles, Requests, Edit) renders
through membership role alone. The first run crashed on Investigation's direct `OrganizationId`
FK (an investigation can exist without a case, so the org is its own required column); the retry
after the fix found everything the crashed run had created and duplicated nothing, which is the
idempotency doing its job.

---

## 122. The standalone Members page rendered before auth (SHIPPED 2026-08-20)

`OrganizationMembers.razor` called the API in `OnParametersSetAsync` with no
`WaitUntilAuthReadyAsync`. It has its own route, so a hard navigation to
`/organizations/{id}/members` rendered it before the circuit existed and before any bearer token
did — both its calls came back unauthorised, the client's `?? []` turned that into empty results,
and the page told a **SuperAdmin** the group had no members while printing the raw GUID as its
heading (the org lookup came back empty too, so `_orgName` fell through to `OrgId.ToString()`).

Embedded in the hub it was always fine: `OrganizationView` awaits AuthReady before rendering any
tab, so the component only failed at its own address — which nothing exercised.

**It was caught by a help screenshot.** The capture navigates to the standalone page, and the
re-run after the seed expansion published a grid reading "No records available. 0 – 0 of 0 items"
into the group-administration document. Worth recording: the screenshots are now load-bearing as
tests, because they are the only thing that visits some of these addresses cold.

### Guarded

`AuthReadyOnRoutablePagesTests` scans every `.razor` with a `@page` route that awaits
`AdminClient`/`Client` in a lifecycle method and requires `WaitUntilAuthReadyAsync`, with a named
exemption list for pages that genuinely work signed out (each entry carries its reason).

On its first run it found two more: `EquipmentModelPage` and `EventAttendanceConfirm`. Both were
checked rather than assumed — the catalogue endpoint answers 200 with no token and the help states
anyone may browse it, and `/attending/{Token}` is an emailed link whose token is the credential —
so both are exemptions, not bugs. Verified to discriminate by removing the fix and watching the
test name the file.

### The foundation, shipped 2026-08-20

Not the 136-site rewrite — a way to tell the truth, plus adoption on the two surfaces that
actually carried the bug.

**`LoadResult<T>`** (`Ben.Web.Services/WebApi/LoadResult.cs`) — a readonly struct carrying
`Items`, `Failed` and an optional `Reason`. `Items` is safe to enumerate in **every** state
including `default`, which is what makes adoption non-breaking: a call site that ignores `Failed`
behaves exactly as it does today.

**`IWebApiClient.GetListAsync<T>`** derives the distinction from the status code, treats an
unreachable API as a failure rather than an empty list, and carries the server's sentence through
when it is prose (a ProblemDetails blob or HTML page is dropped — same rule as
`SendExpectingReasonAsync`).

**`BenListState`** (`Kit/`) renders the three states a list actually has: loading, could not load,
nothing here. The failure state deliberately does **not** say "you do not have permission" — the
client cannot know that, and a 500 and a dropped connection arrive identically; guessing would be
the page's second untruth.

**Adopted** on the org Files tab and the Members roster — the two surfaces where refusals were
found rendering as empty lists (item 119). Everything else keeps the old path until touched.

15 tests on the type and client, including a theory that 403/401/404/500 are all failures.

### Two more bugs found while doing it

**`OrganizationFiles` had the same auth race as `OrganizationMembers`** (item 122). The guard
written for that missed it, because its lifecycle method is `=> await ReloadAsync()` and the scan
only read lifecycle *bodies* — one level of indirection. Found by loading the page by hand and
seeing the new "Couldn't load this" state appear when the API was up. The guard now treats any
routable page with a load-time lifecycle method that calls the API anywhere as in scope; it
over-triggers on API calls in button handlers, which costs one unnecessary await against a live
bug for the miss. Two more pages surfaced and both check out as genuinely anonymous
(`OrgDiscovery`, `SupportTicketTrackingPage`).

**A Razor paren bug in the Files grid's Source column.** `@((OrganizationFileRecord)context).X ? …`
ends the expression at the cast, so the cell rendered the record's `ToString()` —
`OrganizationFileRecord { Id = … }` — and the ternary as text. It compiled. Nobody saw it because
**a populated row in that grid was unreachable**: ordinary members were refused, and the page
loaded before auth. Fixing two access bugs is what made the third visible. One instance in the
codebase; grepped for the pattern.

---

## 123. Images fetched the whole upload to draw a thumbnail (SHIPPED 2026-08-20, Ben's request)

Every `<img>` on the site pointed at `/api/upload-files/{id}/download`, which serves the original
bytes. A group logo drawn in a 40px box pulled the entire upload down the wire at whatever size it
was uploaded, and the browser discarded nearly all of it. `/find` lists every group, so that was
one full-size image per card — on the page a first-time visitor is most likely to open, quite
possibly on a phone.

Invisible in development: the seeded logos are a few kilobytes and the API is on localhost. It
would have shown up as "the site is slow" once real groups uploaded real photographs.

### What shipped

`GET /api/upload-files/{id}/thumbnail`, beside the download route, reusing the thumbnail pipeline
that already existed for equipment photos and video assets (`MediaIngestService`, 400px long
edge, generated on first request so nothing needs backfilling).

**The access check is literally the same call as the download's.** A thumbnail is still the
picture; making it cheaper to fetch than the file it shrinks would be a way around the audience
rules. Verified: anonymous gets 401 on both, a signed-in non-viewer gets 403 on both.

**Non-images fall through to the real file** rather than 404 — the sanitiser returns nothing for a
PDF, and the equipment route had already settled this question the same way.

Six components moved over — org cards, the two public page headers, the CMS preview, the home
hero, and the user menu avatar. All were 36–120px boxes.

Measured on the seeded site photo: **960×540 / 14,709 bytes → 400×225 / 4,265 bytes**, a 71%
reduction on a file small enough to be a rounding error. On a phone photograph it is the
difference between a page and a download.

Guarded by `ImagesUseThumbnailsTests`, which fails on any `src=` bound to `GetFileDownloadUrl` —
and, in a second test, on the thumbnail helper falling out of use entirely.

`<a href>` download links are untouched: that is somebody asking for the file.

### Closed 2026-08-21 — and the image editor turned out to be dead

Fabric is vendored at `Ben.Web.Website/wwwroot/plugins/fabric/` with its licence and a VENDORED.md,
the CDN tag is gone from `App.razor`, and **the shell now loads nothing from a third party at all**
(verified in a browser: `externalScripts: []` on the sign-in page).

**The premise was wrong in our favour, then worse.** The item said Fabric was "only needed by the
image editor". In fact nothing in the live solution referenced it — the only other match in the
repo was the word *fabricated* in a comment. The reason: `image-editor.js` had only ever lived at
`Ben.Web.WebApp/wwwroot/js/image-editor.js`, and commit `1762dfc` deleted that project. The
component was ported to the new site; **its JavaScript module was left behind.**

So `ImageEditorPlayer.razor` — still rendered by `OrganizationFiles`, `CaseTimeline` and
`AdminUserDetail` — has been importing a 404 ever since. Confirmed live: `/js/image-editor.js`
returned 404 while its sibling `/js/geolocation.js` returned 200. The image editor was not slow or
partly broken; it could not start.

Recovered the module from `1762dfc^` into `Ben.Web.Website/wwwroot/js/`, and fixed a second latent
bug in it while there: every filter went through `fabric.Image.filters.*`, the **v5** path, which
is undefined in v6 and v7 alike — 7 call sites that would have thrown. Filters live at
`fabric.filters.*`.

**Updated to the latest, 7.4.0, at Ben's request.** The API surface was checked in a real browser
rather than by grepping a minified bundle (an earlier grep gave a confident false negative on every
class): all nine classes the module uses are present, and all six filter classes resolve under
`fabric.filters`. The old tag was `fabric@6` — a *floating* major that could change under the site
without a commit; the vendored copy is pinned and sha-verified.

**Loading moved to where it is used.** `image-editor.js` injects the script itself, once, on first
`init()`, with the in-flight promise cached so two editors opening together share one fetch.
Measured end to end: module imports in 6ms, `window.fabric` is `undefined` until `init`, then
7.4.0 loads and a canvas is created in 32ms, with filters reachable.

Guarded by `NoExternalAssetsInShellTests`, verified to fail when the CDN tag is put back. Google
Fonts is an explicit, documented exception.

**Not in the WASM host.** Checked at Ben's request: `Ben.Wasm.Video` never referenced Fabric, and
its `index.html` loads no external assets either. Nothing to change there.

---

## 124. The fonts were the last third party on the critical path (SHIPPED 2026-08-21)

Vendoring Fabric (item 114) did **not** stop the intermittent Playwright timeouts. The full suite
still lost 3 of 319 to the exact symptom item 114 had blamed on Fabric:
`navigating to /login, waiting until "load"` at 30s. All 3 passed in isolation and in the previous
run, so they were flakes — but flakes with a cause.

Two `@import url(https://fonts.googleapis.com/...)` statements were left, buried **inside**
stylesheets rather than in the shell:

- `css/smartapp.min.css` → Public Sans (body font, 6 faces)
- `app.css` → Irish Grover (logo face)

Confirmed live rather than assumed: `performance.getEntriesByType('resource')` showed exactly two
external requests on `/login` and the browser's own `renderBlockingStatus` reported **both as
`blocking`**. Worse than a tag in the head, because the browser cannot discover an `@import` until
it has already fetched and parsed the stylesheet containing it — a serial chain on every
navigation, in a fresh context with no cache, ~319 times a run.

### What shipped

All 7 woff2 files self-hosted under `wwwroot/fonts/` with `fonts.css` carrying Google's own
`@font-face` blocks verbatim, URLs rewritten to local paths. Every unicode subset kept — dropping
`latin-ext` or `vietnamese` would silently break accented characters rather than fail loudly.
`font-display: swap` preserved, so a slow font can never block content. 148 KB total.

**No CSS rule was altered** — only the two `@import` lines. The `font-family:"Public Sans"` rule on
`:root` is present exactly once before and after, and the page renders identically.

Measured on `/login`: **2 external requests → 0**, and the load event **282ms → 59ms**.

### The guard was sheltering the bug

`NoExternalAssetsInShellTests` originally allow-listed `fonts.googleapis.com` on the reasoning that
self-hosting fonts is a separate decision. That allowance was hiding the actual remaining cause.
The list is now empty, and a second test scans every stylesheet under `wwwroot/` for `@import`
from another host — the place the fonts were actually hiding, which a scan of `App.razor` could
never have found.

That second test strips CSS comments first: `fonts.css` documents the very `@import` it replaced,
and a guard that cannot tell a doc comment from a fetch flags the fix as the bug. It did exactly
that on first run. Both guards verified to fail against a reintroduced import.

---

## 125. Neither app honoured reverse-proxy headers (SHIPPED 2026-08-21)

Found while planning a Cloudflare Tunnel for the UAT deploy, before it was set up rather than
after.

Both `Program.cs` files called `UseHttpsRedirection()` and neither called `UseForwardedHeaders`.
Behind any reverse proxy — a tunnel now, Azure App Service later — TLS terminates at the proxy and
the request reaches the app over plain HTTP. The app would have seen `IsHttps == false`, answered
`307 → https://`, the proxy would have fetched that, and the request would have looped. IIS healthy,
app healthy, site unreachable, nothing in any log to explain it.

### What shipped

`UseForwardedHeaders` in both apps, honouring `XForwardedProto | XForwardedFor`, registered
immediately after `builder.Build()` — **before** anything that reads the scheme.

`KnownProxies`/`KnownNetworks` are deliberately left at their defaults, which trust forwarded
headers **only from loopback**. `cloudflared` runs on the same host and connects to `localhost`, so
the immediate peer genuinely is loopback. Widening it would let any caller claim to have arrived
over HTTPS from any address — the absence of that configuration is the secure state, not an
oversight.

`XForwardedFor` also restores the real client IP. Without it the audit log would have recorded the
proxy for every request — worse on the API, which is where security decisions are logged.

### Guarded on order, not presence

`ForwardedHeadersTests` asserts `UseForwardedHeaders` appears **before** `UseHttpsRedirection` in
both files. Registered after, it compiles, starts, serves every local request correctly, and still
loops behind a proxy — so presence alone proves nothing. Verified by moving the call after the
redirect and watching it fail.

The guard flagged its own documentation on first run: the comment above the call names
`UseHttpsRedirection`, so a naive `IndexOf` found the prose first. It strips comments now — the
same mistake the stylesheet guard made a day earlier, which suggests any source-scanning guard
should strip comments as a matter of course.

### Not verified locally, and honestly so

The local run cannot reproduce the loop: with no HTTPS port configured, `UseHttpsRedirection` is
inert, so both the with- and without-header cases return 200. What is proven locally is
registration and ordering. The behavioural proof has to come from the deployed site behind the
tunnel.

---

## 126. SuperAdmin Site Settings and Dashboard render empty on the server (CLOSED 2026-08-21)

Ben reports both `/admin/site-settings` and `/admin/dashboard` are blank for him as SuperAdmin.

**They are not blank locally.** Verified the same day against the dev stack: Site Settings renders
every setting card including the ten feature switches, and the Dashboard renders its four stat
cards, the sign-ins/registrations chart, the cases-by-status donut and the group tables. So this is
a **deployment** fault, not a page fault — which narrows it a great deal.

### The likely cause, and why it presents as "empty" rather than "error"

Both pages are pure API consumers, and both go through the adapter's
`GetAsync(...) ?? []` path — **item 120's bug class**. A failed call there is indistinguishable
from a successful empty one, so an API that is refusing, unreachable, or answering on the wrong
path renders as a page with nothing on it and no error anywhere. That is precisely the symptom
described.

Candidates, in order:

1. **API base path.** The server deployment serves the API under `/webapi`, and
   `ApiBasePathHandler` was added on 2026-08-21 (commit c82a7c9) for exactly this. If a call is
   built without the base path it 404s, and 404 → `?? []` → empty page.
2. **Auth.** SuperAdmin-only endpoints answering 401/403 to a token the site is not sending — same
   silent-empty outcome.
3. **CORS**, if the site and API are not same-origin in that deployment.

### How to tell them apart in one step

The browser's network tab on the deployed site, filtered to `/api/`: the status codes on
`/api/admin/site-settings` and `/api/admin/stats/summary` name the cause immediately — 404 is the
base path, 401/403 is auth, a CORS error is the third.

### Worth doing regardless

Adopting `LoadResult`/`BenListState` on these two pages would have made this self-diagnosing: the
page would have said "Couldn't load this" instead of silently claiming there are no settings. They
are good candidates for the next slice of item 120 adoption, precisely because they are
admin-only pages where a silent empty state is most misleading.

### 2026-08-21 — made self-diagnosing, not yet diagnosed

**The cause is still unknown.** Both pages render fully against the dev stack, so it is a
deployment fault, and nothing here identifies which one. What changed is that the pages will now
*say* what happened instead of rendering blank.

- **Site Settings** reads through `LoadSiteSettingsAsync`, so a refusal is a failure rather than an
  empty list. Its existing `catch` never fired because nothing ever threw.
- **Dashboard** treats a null summary or charts as failure — those endpoints always return an
  object when they answer at all — and names what to check.
- **`GetListAsync` now reports the status** when the body is not prose: "The server answered 404
  (Not Found)." A blank page says nothing; 404 says the path is wrong and 403 says the path is
  right and the caller was refused. That is the whole question.

**A simulation that was NOT faithful, recorded so nobody repeats it.** Pointing `WebApi:BaseUrl` at
`/wrongpath` reproduced blank-and-silent, but that path exists nowhere — whereas on the deployment
`/webapi` does exist and `ApiBasePathHandler` restores it. Ben caught this: it risks re-fixing
something the deploy already solved. The base path is not implicated by that test.

**What the symptom does suggest:** sign-in works on the deployment and the admin menu renders, so
the API is reachable and the token is accepted. That points away from the base path and toward the
SuperAdmin *role* not being honoured on those endpoints — `/api/me` computes `isSuperAdmin` from
the database, while `[Authorize(Roles = SuperAdmin)]` reads the token's role claim, and those two
can disagree. Unconfirmed.

**Next step is one look at the deployed browser's network tab**, filtered to `/api/admin/` — or
simply reloading those pages once this ships, since they will now print the status themselves.

---

## 127. Sign-in blamed the password when the API was unreachable (SHIPPED 2026-08-21)

Found while reproducing 126, and real independently of it.

`LoginFailure` had five cases and no way to say "the endpoint was never reached", so a 404 or a 5xx
fell through to the catch-all and the page said **"Invalid email or password."** The credentials
were correct and had never been examined. That sends somebody to reset a password that was fine,
and the reset cannot help — the same mistake the rate-limit case was fixed for, on the page where
it is most costly.

`LoginAttempt` already carried the status code, so this is a sixth case rather than new plumbing:
`WasUnreachable` (status 0, 404, or 5xx) maps to `LoginFailure.Unreachable`, and the page says the
problem is with the site rather than the password.

### 2026-08-21 — cause found: a stale WebApi, not a code fault

Ben refreshed the WebApi on the server and both pages came up. **There was never a bug in the
code** — the deployed `C:\Ben\WebApi` predated the controllers those two pages call, so the routes
did not exist and returned 404.

The dates were the tell, and they are worth keeping as a diagnostic habit:

| Controller | Added | Deployed page |
|---|---|---|
| `MeController` | 07-16 | worked |
| `AdminSiteSettingController` | **08-15** | blank |
| `AdminStatsController` | **08-20** | blank |

Everything failing was recent; everything working was old. Two other facts narrowed it before the
check: production and development share one database, so the SuperAdmin rows behind the failing
session were the same rows that work locally — which killed the role-claim theory — and the
notification badge rendered, proving the token reached the API and was accepted.

**The lesson is about deployment, not code:** the website and the WebApi are published separately,
so they can drift, and the symptom of drift is a *recent* feature failing while everything older
works. Worth republishing both together, or stamping a build version the site can compare.

The reporting added earlier in the day is what made this a five-minute diagnosis instead of a
guess, so it stays.

---

## 128. Admin dashboard: axis defects (SHIPPED 2026-08-21)

Found in Ben's screenshot of the working dashboard.

- **Day-first dates on the chart axis.** `DayLabels` formatted with a hardcoded `"d MMM"`, giving
  "23 Jul" — day-first, on a site that is month-first everywhere, and written *at the call site*,
  which is the exact thing `DateTimeViewerExtensions` exists to prevent. Now `ChartDayPattern`
  (`MMM d`) with `DateTime` and `DateOnly` overloads, pinned by two tests.
- **Fractional counts on the y-axis.** Left to itself ApexCharts picks a "nice" scale, so "People
  by state" — tallest bar 1 — drew an axis reading 0, 0.2, 0.4, 0.6, 0.8, 1. Fractional people.
  Every number this dashboard draws is a count, so the axis is integer-only, floored at zero, with
  no more ticks than the largest value can fill.
- **Ninety rotated labels stacked on each other.** Thinned to about eight ticks; the tooltip still
  names every day. Rotation then had to go too — angled labels were clipped by the panel edge,
  rendering "Jul 23" as "l 23".

---

## 129. Admin dashboard: readability and dead ends (SHIPPED 2026-08-21)

Ben asked what would make the dashboard prettier and more functional. Four changes, plus two bugs
the work uncovered.

- **The stat cards are links.** "97 people" that cannot be clicked is a dead end — the number
  raises a question and the list answers it. People → users, In a group → groups, Cases → cases.
  *Signed in this week* deliberately stays inert: no page lists recent sign-ins, and a card
  linking somewhere approximate is worse than one that stays put.
- **Three "by state" panels became one with a toggle.** They filled a whole row to show one bar
  each, which with a single state in the data is a row of decoration.
- **Group charts are horizontal.** A vertical bar gives its label only as much width as the bar,
  which is why "Tennessee Ghost Hunters" was rendering as a rotated "…essee Ghost Hunters".
- **The donut legend carries counts.** Every number on it used to be behind a hover, which is no
  answer for someone reading the page rather than pointing at it.

### Two bugs found while verifying, both pre-existing

**Charts never re-themed.** `RethemeAsync` was exported with a "call this from whoever owns the
toggle" contract and had **no callers anywhere** — so every chart on the site kept the palette it
was born with, and in light mode that meant near-white axis labels on a white card. The module now
watches `data-bs-theme` on `<html>` itself with a MutationObserver. A contract nobody can forget
beats a contract everybody forgot; the unused method is gone rather than left as a trap.

**The re-theme then ate the axis config.** Apex's `updateOptions` *replaces* a nested object
instead of merging into it, so sending `yaxis: { labels: { style } }` silently dropped the label
formatter and `maxWidth` — group names truncated again the instant anyone touched the toggle, and
the integer axis would have gone with them. `retheme` now derives its options from `baseOptions`
and the stored spec, so a created chart and a re-themed one cannot drift apart.

### Not done, and why

- **The sign-in spike.** One seeded day of ~2,700 flattens the other 29 to the floor. It is seed
  data, but any real burst does the same; a rolling-average toggle is the cheap insurance. Left
  for Ben to decide whether it is worth a control.
- **Nobody has an address.** "People by state" counts 1 of 97 because **no seeder writes
  `UserAddress` rows** — that bar is Ben's own record. The panel is honest and useless until
  either the seeder populates addresses or real users do. A data decision, not a chart fix.

---

### 2026-08-21 — the organization area, and a ratchet so it cannot regrow

**The decision: replace, do not parallel.** The three methods converted on 08-20 were added
*beside* their originals as `LoadXAsync`, leaving `GetXAsync` in place. A day later two of those
originals — `GetOrgFilesAsync` and `GetSiteSettingsAsync` — had **zero callers**: the parallel
approach had produced dead code within a day, while doubling the interface and leaving every old
method sitting there as the trap it already was. Converting the return type instead means the
compiler names every consumer, and there is one way to call each thing. Nothing has shipped, so
this is the cheapest it will ever be.

**A ratchet, because a ban is unmergeable.** 120 swallow sites, each needing its consumers changed
with it, cannot land as one change. `SwallowedFailureRatchetTests` asserts the count is both at
most and exactly the ceiling: it can only ever fall, and leaving the ceiling slack is itself a
failure. Verified to discriminate by adding one and watching both assertions fail. **120 → 101.**

**Converted:** the whole organization area — 17 methods and 32 files. `GetAnonymousListAsync` was
added so public endpoints report failure too; a visitor refused a public list has no account, no
error and no reason to try again, which makes anonymous surfaces the ones that need this most, not
least.

**Where the difference is now on screen** — rather than only in the type: the groups list, all
cases, all investigations, site roles, group roles, public events, org discovery, and the front
page's own search. Supporting fetches — dropdown options, name lookups, permission maps — take
`.Items`, because a page makes no "nothing here" claim about them.

**A test that was defending the bug.** `SearchOrganizationsAsync_WhenApiReturnsNull_ReturnsEmpty`
asserted exactly the behaviour item 120 exists to end, and passed. It now asserts the opposite.
Worth remembering that a green suite was part of how this survived.

**Found and NOT fixed, deliberately:** `OrganizationView` decides `_isMember` and `_canEdit` from
whether the org appears in the list it fetched, so a *failed* fetch silently demotes a member to a
non-member and hides what they may do. Unchanged by this pass — an empty list did the same — but
it is the same bug wearing a permissions hat, and it should be fixed with the rest of that page's
conversion rather than bolted on mid-verification.

**Still open: 101 sites** across equipment, case, platform, user, investigation, cms, places,
media, publications, membership and feed. Same recipe each time; the ratchet stops the number
growing while the work continues.

---

## 130. Dates were day-first in 74 places a constant could not reach (SHIPPED 2026-08-21)

Ben reported British dates on "Date Created" columns. **This was the fourth time he had reported
it**, and he was right every time.

**Why three previous fixes did not hold.** Each one corrected `DateTimeViewerExtensions` and
whichever screen was in front of us, and the constants then looked authoritative. A constant
governs only what refers to it — and the places that were wrong *could not* refer to it. A Telerik
picker takes `Format="dd/MM/yyyy"` as a string attribute; a grid column takes
`DisplayFormat="{0:dd/MM/yyyy}"`. Neither can hold a C# constant without being written to.

**The audit found 74 day-first patterns across 28 files**, including the WebApi — so emails to
borrowers and event attendees carried them too, not just the UI. `DisplayDateFormatTests` passed
throughout, because it only ever asserted the constants.

Fixed by making the constants reachable — added `MediumDatePattern`, `GridDateFormat`,
`GridDateTimeFormat` — and rewriting every call site to reference them instead of carrying a
pattern. ISO `yyyy-MM-dd` was deliberately left alone: `<input type="date">`, log lines, sort keys
and generated filenames all need it, and it is not ambiguous to anybody.

**`DateFormatSourceGuardTests`** now scans every `.cs` and `.razor` across six projects and fails
the build on any day-first literal, in each spelling that has actually turned up. Verified to
discriminate: reintroducing one `Format="dd/MM/yyyy"` fails it. Comments are stripped first — the
fifth guard in this codebase that would otherwise fire on its own explanatory prose.

**The lesson is bigger than dates.** When something is reported wrong repeatedly *after* being
fixed, the fix is landing somewhere the broken code never consults. Go and find the call sites, and
add a source scan rather than another assertion on the thing that was already right.

---

## 131. Signed-out, the "Request an Investigation" page's button does nothing (CLOSED 2026-08-22)

Ben: *"the sign up button if you try to create a case and are not logged in, does nothing."*

`/my-requests/new` (`ClientRequestWizard.razor`) renders this to an anonymous visitor:

```razor
@if (!UserState.IsAuthenticated)
{
    <p class="lead">You must be signed in to submit a request.</p>
    <button type="button" class="btn btn-primary"
            @onclick="@(() => NavManager.NavigateTo("/login"))">Sign In</button>
    return;
}
```

**Why it does nothing.** `@onclick` needs a live SignalR circuit. This page is reached by a
signed-out visitor — often the very first page they open — and it is prerendered long before the
circuit connects, so a click in that window is dropped on the floor with no feedback. Navigation
needs no circuit at all: a plain `<a href="/login" class="btn btn-primary">` works in the
prerender, works with JS disabled, and is right-clickable. See the standing note on the Blazor
Server interactivity race.

**It is also the wrong destination.** Somebody who has never used the site and wants to report
activity has no account yet. The dead end offers only Sign In; it should offer **Create an
account** (`/signup`) as the primary action with Sign In secondary, and both should carry
`?returnUrl=/my-requests/new` so they land back on the request they were trying to start rather
than on a dashboard. `Login.razor` already supports `ReturnUrl` (built for the case-invite flow,
item 4).

**Same defect, same page family — fix together:**

- `ClientRequests.razor:14` and `:32` — "New Request" buttons, also `@onclick` navigation
- `HomeHero.razor:57` already gets this right (`<a href="/login">`), which is the model

**Worth a guard.** A `@onclick` handler whose whole body is `NavManager.NavigateTo(<literal>)` is
always better as an anchor, and a source scan can say so. That would also have caught this one.

### Verified live, and the first diagnosis was only half right

Reproduced in a browser rather than left as a reading of the source, which changed the answer.

**The wizard's button.** The `@onclick` mechanism is real but small: measured on localhost, the
button is painted 5ms after navigation starts and the circuit's negotiate completes at 55ms — a
**~50ms dead window**. Wide enough to swallow a fast click, and much wider over a real network,
but not enough to explain a button that reliably does nothing. Fixed anyway, because an anchor is
strictly better: `<a href="/login" class="btn btn-primary">` needs no circuit, survives a slow
connection, works with scripting off and can be opened in a new tab. Confirmed by `curl` — the
anchor is in the server's HTML with no JavaScript involved at all.

**Ben's clarification settled the destination question.** *"Clicking the sign in button can
redirect to the sign in page. There is a link there for signing up."* So the wizard keeps its
single Sign In action; no create-an-account button was added, and the `returnUrl` idea was dropped.

**What was actually dead was `/signup`.** Following the path Ben described — wizard → Sign In →
login → "Create an account" → `/signup` — the Create account button ships **`disabled`**, and
stays disabled until the @name availability check returns. Every other field is `[Required]` with
a `ValidationMessage`; `Handle` had **neither**, and was enforced only by that silent `disabled`.

Reproduced exactly: fill every field, leave @name alone, click Create account →
`button_disabled: true`, **zero validation messages anywhere on the page**, nothing happens. And
the @name box's grey placeholder read as a filled-in value (see item 134), so skipping it is the
natural thing to do.

### Fixed

- `Handle` gets `[Required]` and a length rule, plus a `ValidationMessage` beside the field, so it
  behaves like every other field. The same click now says *"Choose an @name — it's how people
  mention you."*
- The button is disabled only while the request is in flight. **A disabled control cannot explain
  itself**, so it is the wrong way to enforce a rule the person can still fix — the same lesson as
  a server guard the UI discards.
- `SubmitAsync` checks the handle itself, so somebody who types a name and clicks inside the 400ms
  debounce is no longer blocked by a race; the server remains the authority on uniqueness.
- The three navigation buttons became anchors: `ClientRequestWizard` and `ClientRequests` (×2).
- **At Ben's request**, the `@` prefix now reports the check: grey at rest, blue while in flight,
  green when free, red when taken. See item 135 for what that took.

### The other ten — checked one at a time, seven converted (2026-08-22)

Ben asked for these to be done **only after verifying they are not working as one would expect**,
which was the right instruction: checking turned ten into seven, and produced a sharper rule than
"convert them all".

**What decides it is whether the button exists during the prerender.** A handler in markup that
renders unconditionally is emitted by the server before the circuit exists, so it is on screen,
looks pressable, and swallows clicks until SignalR connects. A handler inside a branch that cannot
render until after the circuit — an auth check, or a field that is null until loaded — has no such
window, because the button is not there yet.

Verified by fetching each page **anonymously with curl**, no JavaScript involved, and looking for
the button in the raw HTML:

| Site | Renders in prerender? | Verdict |
|---|---|---|
| `OrganizationCreateEdit` "Back to Organizations" / "Cancel" | yes | converted |
| `OrgCmsEditor` "Back to Organizations" (`@if (!EmbeddedMode)`, default false) | yes | converted |
| `AdminUsers` "New User" | yes | converted |
| `AdminUserCreate` "Back to Users" / "Cancel" | yes | converted |
| `ClientRequestDetail` "My Requests" | yes | converted |
| `OrganizationList` (inside `@if (UserState.IsSuperAdmin …)`) | **no** | left alone |
| `AdminUserDetail` (inside `else` of `@if (_detail is null)`) | **no** | left alone |
| `ClientRequestWizard` (signed-in branch, `@if (_step == 5)`) | **no** | left alone |

**The dead window is bigger than the first measurement suggested.** The signed-out request page
measured ~50ms; `/admin/users` measured **298ms** — and that is localhost with a warm server and no
network. Wide enough for a real person to lose a real click.

An incidental confirmation of why these could not be clicked directly: an anonymous visit to
`/admin/users` renders the full page chrome and then **redirects to home once the circuit connects
and auth resolves** — the markup ships to anyone, the authorisation happens a third of a second
later.

`NavigationIsAnAnchorTests` now bans the pattern, with the three verified exceptions listed
alongside the branch that protects each. Verified to discriminate: turning the `AdminUsers` anchor
back into a button fails it.

---

---

## 132. Dark mode: fixed-light Bootstrap utilities make white cards with near-white text (CLOSED 2026-08-22)

Ben: *"even when you are in dark mode and on the audit log page, when you expand the log record,
the row that opens has a white background card with near-white text."*

`AdminAuditLog.razor`'s `<DetailTemplate>` used `bg-light` for the panel and `bg-white` for the
JSON block. Both are pinned to a literal colour, so in dark mode the panel stayed white while the
text inside kept the theme's light-on-dark foreground.

### What was actually wrong — and two things worth correcting

Reading the compiled `smartapp.min.css` instead of reasoning from class names changed the scope
twice, both times **downward**. The dark block redefines `--bs-tertiary-bg-rgb`,
`--bs-secondary-bg-rgb`, `--bs-body-bg-rgb`, `--bs-emphasis-color` and `--bs-secondary-color`, but
**not** `--bs-light-rgb`, `--bs-white-rgb` or `--bs-dark-rgb`:

| Class | Resolves through | Redefined in dark? | Verdict |
|---|---|---|---|
| `bg-light` | `--bs-light-rgb` | no | **broken** — 22 uses |
| `bg-white` | `--bs-white-rgb` | no | **broken** — 3 uses |
| `text-bg-light` | `--bs-light-rgb` + literal `#000` | no | **broken** — 1 use |
| `text-dark` | `--bs-dark-rgb` | no | broken **only** off a fixed background — 10 of 76 |
| `alert-light` | `--bs-light-bg-subtle` / `-text-emphasis` / `-border-subtle` | **yes, all three** | fine — leave alone |

1. **`alert-light` was never broken.** The first version of this item said to sweep it. Wrong: the
   theme redefines all three variables it reads (#343a40, #f8f9fa, #495057), so the ~15
   `alert alert-light` empty states were always theme-aware.
2. **65 of the 76 `text-dark` uses were fine.** They sit on `bg-warning` or `bg-info`, neither of
   which changes between themes, so black text on them is correct in both. Only the 10 paired with
   `bg-light`/`bg-white` needed anything, and they were fixed with their background.

Guessing from the class name would have "fixed" 80 working things.

### What shipped

- 23 replacements: `bg-light` → `bg-body-tertiary` (surfaces) or `bg-body-secondary` (chips),
  `bg-white` → `bg-body`, `text-bg-light` and paired `text-dark` → `text-body-emphasis`
- **One deliberate exception**, annotated in place: the 2FA QR-code container keeps `bg-white`.
  A QR code is read by a camera, not a person, and scanners need the light modules light in
  either theme.
- **`ThemeSafeColorExtensions`** — `ColorClass` is a *stored* value (an org picks a colour for a
  calendar event type; `AdminLookupTypes` lets a SuperAdmin type any class into a free-text box),
  so fixing the dropdown alone would only help the next choice. Stored values are translated at
  render across 8 sites, and the picker's "Black" (`text-dark`, which vanished into the page in
  dark mode) became "Contrast" (`text-body-emphasis` — black on light, white on dark).
- **`FixedLightUtilityGuardTests`** bans all three utilities in `.razor`, and bans `text-dark`
  except on a background that never changes; allowlist entries must still be real. Verified to
  discriminate — reintroducing the audit log's `bg-light`, and adding a `text-dark` to a
  theme-following surface, each fail it.

### Measured in the running app

| | background | text | contrast |
|---|---|---|---|
| Audit-log panel, before | `#ffffff` | `#dee2e6` | **1.30:1** |
| JSON block, before | `#ffffff` | `#dee2e6` | **1.30:1** |
| Panel, after | `#2b3035` | `#dee2e6` | **10.23:1** |
| JSON block, after | `#212529` | `#dee2e6` | **11.85:1** |

WCAG AA wants 4.5:1; at 1.30:1 the text was effectively invisible. Every replacement token was
measured in both themes and changes value; all three banned ones were measured and do not. The home
page renders zero banned backgrounds, and its two remaining `text-dark` are both
`badge bg-warning text-dark` — black on yellow-ochre, correct in both themes.

Ben confirmed the page himself: *"Audit log fix looks great."*

The custom stylesheets needed nothing — `app.css`'s `#fff` uses sit inside
`:root[data-bs-theme="dark"]` blocks, mixing outline-button colours *toward* white on purpose.

---

## 133. An expired or lost token reads as a raw 401, and one page shows the error and a spinner together (CLOSED 2026-08-22)

Ben, on two SuperAdmin pages: *"site settings page gives a couldn't load site settings, the server
answered 401 (Unauthorized). then below it the word 'Loading…'"* and *"Support tickets page say
Could not load tickets."*

**First, the likely cause of that particular sighting.** The website host was restarted mid-session
while Ben was signed in. `IWebApiTokenStore` is registered **scoped** (`Program.cs:83`), which under
Blazor Server means per-circuit: restarting the host destroys every circuit and the access token
with it. The browser reconnects into a fresh scope holding no token, `WebApiClient` sends no
`Authorization` header, and the API answers 401 to everything. Signing in again clears it. So these
two reports are probably not standing product defects — but they expose two that are.

### 133a. A dead session is reported as an HTTP status code

The page said *"the server answered 401 (Unauthorized)"*. That sentence is `LoadResult.Reason`
being rendered faithfully — item 120 working as designed — but 401 is the one status where the
generic treatment is wrong. It does not mean "something went wrong fetching this list"; it means
**this person is no longer signed in**, and the only useful thing to say is so, with a way back.

This is not a dev-only artefact. It happens in production whenever a token expires, an app pool
recycles, or a deployment restarts the host — and the reader will be told their site settings could
not be loaded rather than that they need to sign in again.

**Fix.** Handle 401 distinctly from other failures in the client — a flag on `LoadResult` or a
dedicated reason — and have `BenListState` (and the ad-hoc error banners) render "Your session has
ended. Sign in again" with a link carrying `returnUrl`. Consider re-authenticating silently where a
refresh token exists. Related to item 131, which is also a dead end offering no way forward.

**Worth deciding separately:** whether the token should survive a reconnect at all. It is scoped
today, and `EntraTokenPersister` only bridges the prerender-to-circuit handoff, not a host restart.

### 133b. AdminSiteSettings renders its error and "Loading…" at the same time

Real, and independent of the 401. In `AdminSiteSettings.razor`, `LoadAsync` sets `_error` and
returns on failure — leaving `_settings` null. The template's only test is:

```razor
@if (_settings is null) { <p class="text-secondary">Loading…</p> }
```

So a failed load shows the red banner *and* a spinner that will never resolve, which reads as "it
failed, but it is also still trying". The page needs the third state the rest of the site now has:
this is a surface that should be using `BenListState`, which distinguishes loading from
couldn't-load from empty. `AdminSupportTickets` gets this right (`_loading && _page is null`) and
is the model.

Cheap, and worth doing with the next item-120 slice rather than alone.

### Fixed (2026-08-22)

**133a — a dead session says so.** `LoadResult<T>` gains `SessionExpired`, and
`WebApiClient.SendListAsync` maps 401 to it before any other handling. It is deliberately a
*subset* of `Failed`, so every existing call site that only checks `Failed` is unaffected.

**403 is deliberately excluded.** Forbidden means the session is fine and this particular thing is
not theirs to see; telling that person to sign in again sends them round a loop back to the same
refusal. Only 401 means "you are not signed in any more".

`BenListState` grew a fourth state — loading, **signed out**, couldn't-load, empty — rendering
*"You've been signed out"* with a **Sign in again** link carrying `returnUrl` so they come back to
the page they were on. It is an anchor (item 131's rule) and offers no Try again button, because a
retry on a dead session is a control that cannot work. `Login.razor` already had the `ReturnUrl`
parameter and its open-redirect guard, so the link is relative.

**The design reaches surfaces that never adopted `BenListState`, for free.** `SessionEnded()`
carries **no** `Reason`, and four places render `Reason ?? "their own sentence"` — so a null falls
through to their own wording instead of quoting HTTP at somebody. The two that a signed-in person
can actually hit (`InvestigationPanel`'s binder, `ClientRequests`' organization picker) now name the
state outright. The other two run anonymous searches, where a 401 cannot arise.

**133b — the error and the spinner.** `AdminSiteSettings` now renders through `BenListState` with a
real `_loading` flag cleared in a `finally`. The old template's only test was `_settings is null`,
which stayed true after a failure, so the red banner and a spinner appeared together and read as
"it failed, but it is also still trying". That is now structurally impossible rather than merely
fixed: the loading branch and the failure branch are the same component's mutually exclusive arms.

Four tests, verified to discriminate — removing the 401 mapping fails the two that assert it, while
the 403 and unreachable-server tests stay green, which is what makes them worth having.

### Still open, deliberately

- **Only the list path is covered.** `GetAsync`, `PostAsync` and friends still answer a 401 with
  `default`, so a single-record fetch or a save that hits a dead session is as silent as ever. That
  is the same shape as item 120 and belongs with it.
- **`AdminSupportTickets`** ("Could not load tickets") is not on `LoadResult` yet — it is in the
  Platform slice, 14 swallows. Its message is at least generic rather than a status code, so Ben's
  specific complaint does not apply there, but it cannot say "you've been signed out" either.
- **Whether the token should survive a host restart at all.** `IWebApiTokenStore` is scoped, so a
  restart takes every circuit's token with it. `EntraTokenPersister` only bridges prerender to
  circuit, not a restart. Worth deciding separately — this item makes the symptom honest, it does
  not remove the cause.

---

## 134. Placeholders looked like filled-in text in dark mode (CLOSED 2026-08-22)

Ben: *"if the placeholder in dark needs to look like a placeholder and not filled in text… Real
text in dark mode looks way too close 'colorwise' to placeholder text."*

The template renders `.form-control::placeholder` as `--bs-secondary-color`, which in night mode is
`rgba(222, 226, 230, .75)` — **the same hue as body text** (`#dee2e6`) at 75% opacity. Measured
against real input text in the running app, the ratio was **1.00:1**: indistinguishable.

This was not cosmetic. It is why item 131 happened: the @name box showed a grey `sarahmitchell`
placeholder that read as a value, so the field got skipped, and the only thing standing between the
person and their account was a button that refused in silence.

Fixed with a `--ben-placeholder-color` token defined per theme in `app.css`, so each value is
chosen against its own background rather than one value being asked to work on both:

| | placeholder vs real text | placeholder vs input background |
|---|---|---|
| Dark, before | **1.00:1** | — |
| Dark, after | **4.01:1** | 2.96:1 |
| Light, after | 2.96:1 | 2.72:1 |

Legible, and unmistakably not a value.

---

## 135. The @name availability indicator, and what the template does to colours (CLOSED 2026-08-22)

Ben, on the signup page: *"I liked the @ being blue and then turning green if the name was
available after checking or red if the name was unavailable… I liked it being gray and turning blue
while checking."*

Built as four states on the `@` prefix — grey at rest, blue in flight, green free, red taken —
which needed a new `_handleChecking` flag, since the 400ms debounce plus a round trip is long
enough that the field otherwise just sits there.

**Two things had to be beaten, and both are worth knowing for any future work on this template.**

1. **`.input-group:focus-within .input-group-text` sets `color: var(--bs-white) !important`.** So
   while the field has focus — exactly when somebody is typing a name — the prefix was forced to
   white and none of the states showed. Bootstrap's own `.text-success` and friends lose that
   fight: one class against three, even with `!important`. The prefix now carries its own class
   with matching specificity and keeps a neutral, theme-following chip background in every state.
2. **The semantic colours are theme-independent, but the chip background is not.** Raw
   `--bs-primary` on the night chip measured **1.70:1** — the "checking" state, whose entire
   purpose is to be noticed, was effectively invisible. `--bs-danger` was 2.78:1.

The state colours are therefore mixed per theme, the same technique `app.css` already uses for
outline buttons in dark mode. Measured after:

| state | dark | light |
|---|---|---|
| idle (grey) | 6.49:1 | 3.69:1 |
| checking (blue) | **4.60:1** (was 1.70) | 7.44:1 |
| free (green) | 4.54:1 | 4.51:1 |
| taken (red) | 4.50:1 | 4.54:1 |

**The general lesson:** a semantic colour that does not change between themes still needs checking
against a background that does. Item 132 established that reading the compiled CSS beats guessing
from class names; this is the same point one level down.

---

## 136. First and last names get their initial capital (CLOSED 2026-08-22)

Ben: *"can we capitalize first and last names when blurring the text box. Display name can be
whatever."*

Done on `/signup` via `@bind-Value:after`, which for a text input runs on change — i.e. on blur.

**Only words typed in all lower case are touched.** Anyone who wrote "McTest", "van der Berg" or
"d'Eath" meant it, and a blanket title-case would quietly correct somebody's own name to something
wrong — the one field where being clever is least welcome. Word boundaries include hyphens and
apostrophes, so `mctest-o'brien` becomes `McTest-O'Brien` while an existing `McTest` is left alone.
Verified live: `testy` → `Testy`, `McTest-o'brien` → `McTest-O'Brien`, display name untouched.

Only the signup form does this today. If the same treatment is wanted on profile editing and the
admin user screens, that is a small follow-up — the helper is currently local to `SignUp.razor` and
would move to `Ben.Web.Services` first.

---

## 137. UAT dashboard: "Couldn't load the dashboard figures" after a republish (CLOSED 2026-08-22)

Ben, after publishing to ishaunted.com: *"when I go to dashboard after logging in, I get: Couldn't
load the dashboard figures — the server refused the request or could not be reached."* That sentence
is `AdminDashboard`'s own catch, which cannot tell the three causes apart.

### Established by probing the live site anonymously

| Probe | Result | What it rules out |
|---|---|---|
| `GET /webapi/api/admin/stats/summary` | **401** | The route **is deployed**. Not a stale API package, not a wrong path *on the server side*. An `[Authorize]` route answers 401 anonymously; a missing one answers 404. |
| `GET /api/admin/stats/summary` (no prefix) | **404** | Confirms the prefix matters — a website calling the un-prefixed path would see exactly the 404 the message mentions. |
| `GET /webapi/api/public/cases` | **200** | The API is up, reachable, and serving. |
| `GET /webapi/api/public/organizations/search` | **200** | Anonymous API paths work end to end. |

So the API is healthy and the endpoint exists. The failure is on the website→API leg, and it is one
of two things.

### The two candidates, and the one check that separates them

**Read the failing request in the browser's network tab.** The full URL and status decide it:

- **URL is missing `/webapi`** → configuration. The website's `WebApi:BaseUrl` is wrong on the box.
  `ApiBasePathHandler` restores the base path for leading-slash calls, but it can only restore a
  path that is configured in the first place.
- **URL has `/webapi` and the status is 401 or 403** → authorization. `AdminStatsController` is
  `[Authorize(Roles = RoleNames.SuperAdmin)]`, so the signed-in account is not carrying the
  SuperAdmin role claim on that deployment.

Checked and **ruled out** already: `AddIdentityApiEndpoints` *does* call `.AddRoles<IdentityRole<Guid>>()`,
so role claims are populated in principle — this is not the "roles were never registered" bug.

### Two repo-level defects found while diagnosing, worth fixing regardless

1. **`scripts/publish-website.sh` writes `appsettings.Production.json`.** This is the exact pattern
   the API side abandoned: that file loads only when `ASPNETCORE_ENVIRONMENT` matches, and a value
   sitting unread in the package already cost a night on `FileStorage:RootPath`
   (see the UAT deployment notes). `uat-webapi-config.py` merges into `appsettings.json` for the API
   precisely because of it. The website was never given the same treatment.

2. **Run with no arguments, it writes the literal string `__SET_ME__` as the API base URL.**
   `API_URL="${1:-}"` then `"${API_URL:-__SET_ME__}"`. It prints a warning, but a warning in a build
   log is not a guard: the package ships, IIS serves it, and every API call fails at runtime with a
   message about the server being unreachable. `new Uri("__SET_ME__")` is not even a valid absolute
   URI. **The script should refuse to publish** rather than emit a package that cannot work.

Both are cheap and would make this class of failure impossible to ship silently. Neither turned out
to be the cause here, and both are still worth doing.

### Resolved — it was the authorization branch

Eight endpoints across seven controllers were gated on `[Authorize(Roles = RoleNames.SuperAdmin)]`
instead of the SuperAdmin policy. **A bare `Roles` attribute names no authentication scheme**, so
ASP.NET re-authenticates with the *default* scheme alone — the local Identity bearer handler. A
caller holding a valid Entra JWT is not refused for lacking the role; they come back
unauthenticated, and the endpoint answers **401 where a 403 was meant**. The role check never runs.

That is why the site let Ben onto the dashboard while the API refused him: the page's guard reads
`UserState.IsSuperAdmin`, which comes from `/api/me` — and `/api/me` resolves the role from the
**database**, not from a claim. Two sources of truth, agreeing right up until the scheme mismatch.

Fixed by moving those endpoints onto policies whose registration pins both schemes explicitly, with
`SuperAdminHandler` resolving the role by OID for Entra sessions.
`AdminAuthorizationIsAPolicyTests` ratchets it — no controller may use `[Authorize(Roles = ...)]` —
and was verified to discriminate.

**Confirmed live by Ben after republishing: "Dashboard works again."**

This also retro-closes the real cause of **item 126**, which was closed as "made self-diagnosing,
not yet diagnosed" — the same two pages, the same fault, finally named.

---

## 138. Grid filter-row dropdowns are unreadably narrow (CLOSED 2026-08-22)

Ben: *"when you pull up a grid like 'All Investigations' as SuperAdmin… there are dropdowns to
choose from like the status column. You cannot read what to choose from because the size of the
items to choose from is so narrow… I don't need the column to be wider, just the selection list."*

The screenshot shows the Status column's filter cell: the popup is clipped to roughly the width of
the little dropdown button, so the options read as `Se…`, `…posed`, `…pted`.

### Researched — the setting Ben was reaching for does exist

Reflected out of the installed **Telerik 14.1.0** assembly rather than taken from memory.
`DropDownListPopupSettings` derives from `DropdownPopupSettings`, which exposes:

`Width`, `MinWidth`, `MaxWidth`, `Height`, `MinHeight`, `MaxHeight`, `Class`, `AnimationDuration`

So `<DropDownListPopupSettings Width="auto" MinWidth="16rem" />` sizes the popup to its content and
leaves the column alone. The same properties exist on `ComboBoxPopupSettings`,
`MultiSelectPopupSettings` and `DropDownButtonPopupSettings`.

### The catch, and the two ways round it

Popup settings only reach dropdowns **we** render. The filter cell in
`FilterMode="GridFilterMode.FilterRow"` is built by the Grid, and there is no parameter path to its
popup. What the Grid does expose, on `BoundColumnBase`:

- **`FilterCellTemplate`** — supply our own editor for that column's filter, which *can* carry
  popup settings. For a Status column this is the better UI anyway: a real list of statuses beats
  an operator dropdown plus a text box.
- `ShowFilterCellButtons` — reclaims the space the operator and clear buttons eat in a narrow cell.
- `FilterOperators` — trims the operator list.

Only **three** grids use FilterRow: `/admin/cases`, `/admin/investigations`, `/upload-files`.

A `min-width` in CSS is the fallback for the Grid's own internal operator menu, which no parameter
reaches. Note `.k-animation-container` carries **no width in the stylesheet** — Telerik sets it
inline from the anchor's width — so a stylesheet `min-width` is what overrides it, not `width`.

### Fixed with the CSS floor, and verified

`FilterCellTemplate` was the tidier option on paper but only reaches the columns we rewrite; the
Grid's own operator menu has no parameter path, and that is the one in the screenshot. One rule in
`app.css` covers every list popup, present and future, scoped with `:has(.k-list-container)` so
date and colour pickers are untouched.

**Measured against real components**, filter row on a 130px Status column:

| | before | after |
|---|---|---|
| popup width | the anchor's width | **210px** |
| anchor (filter cell) | 174px | 174px |
| options | `Se…`, `…posed`, `…pted` | "Is equal to", "Is not equal to", "Does not contain" — all twelve in full |

---

## 139. File Types grid clips its own action buttons (CLOSED 2026-08-22)

The command column on `/admin/file-types` is too narrow for what it holds: **Edit** and
**Extensions** fit, and the **Delete** button is cut off at the right edge — the trash icon and a
sliver of its label are visible, the rest is outside the column.

A row action a person cannot click is not a smaller button, it is a missing feature — and unlike a
narrow text column, nothing about it invites the reader to widen anything.

Worth checking the other admin grids in the same pass, since the command column is usually written
by copying a neighbour: any grid whose `GridCommandColumn` has a fixed `Width` and three or more
buttons is a candidate. Options are a wider command column, dropping the button labels to icons
with tooltips, or moving Delete behind an overflow menu.

### Fixed at the mechanism, not just the column

Kendo sets `white-space: nowrap; overflow: hidden; text-overflow: ellipsis` on every grid cell.
That is right for a text column — a long name gets an ellipsis. On a command cell it means a button
past the width is **cut off**, and unlike text there is no ellipsis to hint that anything is
missing. A row action nobody can click is not a smaller button; it is an absent feature.

So command cells now allow wrapping, which makes a cramped column taller instead of hiding
something — self-correcting for the 18 command columns that exist and any written later. Three
that were genuinely too narrow were widened as well, so the common case stays on one line:
`AdminFileTypes` 220→300, `AdminUserDetail`'s addresses 220→320, `OrganizationFiles` 240→380.

**Verified against real components** with the cell constrained to 220px: the buttons wrap, the last
one is fully visible, and header and body columns still line up.

A first attempt also set `display: flex` on the cell. That turned out to be **redundant** —
`telerik-night.css` already makes command cells flex — and adding a second opinion about a `<td>`'s
formatting context, from a file that does not own that decision, is how column alignment breaks on
the first frozen or virtualised column. Dropped.

---

## 140. Do the inline `User.IsInRole` checks share the Entra blind spot? (CLOSED 2026-08-22 — 2 did, not 87)

Item 137's fix moved eight endpoints off `[Authorize(Roles = ...)]` onto the SuperAdmin policy,
because a bare Roles attribute pins no authentication scheme and so answers 401 to an Entra caller.
That is fixed and guarded. This item is about the layer underneath it.

**The API makes 87 inline `User.IsInRole(...)` calls across 37 controller files.** They decide
things like whether a SuperAdmin sees another group's CMS pages, whether a file share is visible,
and whether a message board is readable — mostly by widening what an ordinary user would get.

Those calls read role **claims**. An Entra JWT carries no Identity role claims of its own;
`EntraClaimsTransformation` adds them, and when it runs and finds a linked account it does the job
properly — it calls `GetRolesAsync` and adds a `ClaimTypes.Role` claim per role. So in the happy
path these checks are fine.

**The question is the unhappy path, and it comes from the fix's own documentation.**
`AppUserPrincipal.ResolveAsync` says the OID fallback exists *"because it does not always run"*. If
that is accurate, then every one of those 87 sites can silently evaluate `false` for a legitimate
SuperAdmin signed in with Microsoft — and unlike the endpoint attribute, **there is no status code
to notice**. The caller is quietly treated as an ordinary user: a filtered list, a missing button,
a `Forbid()` that looks like a permissions decision rather than a bug. That is the same failure
shape as item 120 — a wrong answer delivered in the voice of a correct one.

**What to do, in order:**

1. **Establish whether the premise is true.** Under what conditions does
   `EntraClaimsTransformation` not run? `IClaimsTransformation` runs inside
   `AuthenticationService.AuthenticateAsync`, which policy evaluation calls per scheme, so it may
   in fact always run and the fallback is belt-and-braces. Worth settling, because the answer
   decides whether this item is a real defect or a note.
2. If it can be skipped, give the inline checks one shared helper with the same two paths as
   `SuperAdminHandler` — claim first, database by OID second — rather than 87 copies of a claim
   read.
3. A guard, once the helper exists, so the 88th call site uses it.

Recorded rather than acted on because the premise is unverified, and because acting on it would
mean touching 37 files on the strength of a parenthetical.

### Answer: the premise was mostly wrong — 2 sites, not 87

**`options.DefaultPolicy` pins both schemes.** So on any action with `[Authorize]` — bare, or with
a policy — the authorization middleware authenticates Entra too, the claims transformation runs,
and `User` is replaced with the merged principal carrying its database roles. **79 of the 81 role
checks in the controllers sit on such actions and were always correct.** None of the `IsSuperAdmin()`
helper calls were affected either.

**The gap is `[AllowAnonymous]` actions**, where nothing does that. `UseAuthentication` populates
`User` from the *default* scheme alone, so a caller signed in with Microsoft arrives with no
principal at all — not lacking the role, unauthenticated — and the check silently says no. Two
endpoints:

- **`EquipmentCatalogController`** — an unapproved model 404s unless you are its proposer or a
  SuperAdmin. An Entra SuperAdmin was **404ed out of the very model they were there to review.**
- **`EquipmentItemDetailController`** — an Entra SuperAdmin saw the visitor's view of an item.

Both fail **closed** — an admin saw less, never more — which is why this was a visibility gap
rather than a security hole, and why nobody noticed.

Fixed with `BenControllerBase.CallerIsSuperAdminAsync()`: the local claim first, then the Entra
scheme authenticated explicitly. `AnonymousEndpointRoleChecksTests` bans `User.IsInRole` inside an
`[AllowAnonymous]` action, and was verified to discriminate.

**A bug in the first version of that fix, caught by the existing tests.**
`HttpContext.AuthenticateAsync("Entra")` **throws when the scheme is not registered**, and Entra is
registered only when configured — so the first attempt would have taken both anonymous endpoints
down in every environment where Entra is off, which is most of them. It now asks
`IAuthenticationSchemeProvider` whether the scheme exists first. Four `EquipmentItemDetailTests`
failed immediately, which is the only reason it was caught before the branch was pushed.

---

## 141. Pages that can see a refusal and still render it as "nothing here" (CLOSED 2026-08-22 — all 22)

Item 120 removed the client's ability to lie: every list method now returns `LoadResult<T>`, and a
ban stops the pattern returning. **This is the other half** — the pages that receive that result and
still only render two states.

**They are no worse than before.** Previously the adapter handed them a bare empty list; now it
hands them a result whose `.Items` is empty. The sentence on screen is identical. What changed is
that the truth is now available at the call site, and this item records where it is going unused.

Listed in `LoadResultRenderedGuardTests.AwaitingRenderPass` — deliberately **not** in
`Decorations`, which is for fetches where a refusal genuinely costs the reader nothing. Each entry
here has a list a person reads. A second ratchet holds the count at 22 and lets it only fall, so
the list cannot become a place to hide a new page.

The 22: `AdminFeedReports`, `AdminFileTypes`, `AudioFilePreview`, `CaseVideoEditorPage`,
`ClientRequestWizard`, `CmsSectionEditor`, `FeedThreadPage`, `FileCommentThread`,
`InvestigationRoster`, `MediaLibraryGrid`, `MyVideosPage`, `NewInvestigationWindow`,
`OrgAddressManager`, `OrgPublicationPosts`, `OrgRoleEditor`, `OrganizationMembershipQuestions`,
`OrganizationSecurity`, `OrganizationView`, `PlaceView`, `PublicationsDirectory`, `UploadFiles`,
`WsRegionExplorer`.

**Worth doing in the same slice-by-slice way**, and worth doing in this order — the ones where the
false "empty" is a claim somebody acts on: `ClientRequestWizard`'s organization search (choosing
who to send a case to), `OrgRoleEditor` and `OrganizationSecurity` (who has access),
`InvestigationRoster` (who attended), then the media and CMS surfaces.

### Done 2026-08-22 — 22 → 17

The five where the false "empty" ends an errand rather than merely misleading:

- **`ClientRequestWizard`** — the organization search. The one screen where "no groups near you"
  stops the whole thing: somebody reporting activity concludes nobody covers their area and leaves.
- **`OrgRoleEditor`** — "nobody holds this role" is what an administrator grants access on.
- **`InvestigationRoster`** — who attended is evidence; an empty roster over a refusal is a record
  saying nobody was there.
- **`OrganizationMembershipQuestions`** — a group shown no questions writes them again, and
  applicants then answer two sets.
- **`OrgAddressManager`** — the address list is what a group's area of operation is judged from.

Two more were cleared before the list existed: `OrganizationMembershipRequests` (people waiting to
join, shown as nobody waiting, is an application that never gets answered) and `OrgPublications`.

### Closed — all 22, same day

The remaining seventeen followed: the publications directory and posts, the place view, the feed
moderation queue, the feed thread, the media library grid, file comments, both video project lists,
the file-types and uploads grids, the region-note explorer, the CMS embed picker, audio markers,
the new-investigation place search, the org security page and the org view.

**Three of them were not list surfaces at all**, and are the ones worth remembering:

- **`NewInvestigationWindow`** — the place-candidate search is what stops a second copy of a place
  being created. Read as "no match exists" when the search was refused, it invites exactly the
  duplicate it prevents. It now says so before you save.
- **`OrganizationView`** — owner-ness is derived from the roster, so a refused roster read as "not
  an owner" and quietly removed the person's own controls. It fails *closed*, which is the safe
  direction, but silently — the page now says the role could not be confirmed rather than letting
  somebody conclude their access changed.
- **`AudioFilePreview`** — markers are somebody's analysis of a recording, and "no markers yet" over
  a refusal says the file was reviewed and found empty.

**The debt list is gone**, because it is empty. `LoadResultRenderedGuardTests` is unconditional
again — verified by regressing a page that had been on the list and watching it fail. The class
doc records that the list existed and what shape to use if one is ever needed again: something that
can only get shorter, never a silent exemption.

Item 120 and item 141 together mean the client cannot report a refusal as an empty list, and no
page can receive that answer and ignore it. Both halves are enforced by tests that were each
verified to discriminate.

Removing an entry means wrapping the list in `BenListState`, or branching on `.Failed` where the
list is mutated in place — the wrapper keeps rendering the load's own emptiness after the first
item is added.

---

## 145. Price Bands killed its circuit on production (CLOSED 2026-08-22, same day it was reported)

Ben, live: loading /admin/subscription-tiers terminated the Blazor circuit. The browser log showed
Telerik frames, which suggested a component bug — but those were the aftermath of the dead
connection. The cause: the tiling-validation endpoint returns `Ok(null)` when the price list is
HEALTHY, ASP.NET renders that as **204 with an empty body**, and `WebApiClient.GetAsync`'s
`ReadFromJsonAsync` throws on an empty stream — unhandled inside `OnInitializedAsync`, circuit
dead. It fired precisely in the common case, and never during development because the page was
verified by curl and never actually opened. Fixed in the client for the whole class (204 or
zero-length success reads as null, `GetAsync` and `GetAnonymousAsync` both), 3 regression tests
verified to fail without the guard, and the three Billing screens added to the Playwright admin
walk — the layer that would have caught this before it shipped.

---

## 142. Email-and-password sign-in fails on production; Entra works (CLOSED 2026-08-22)

Ben, on ishaunted.com: **Entra sign-in works, but filling in Email and password does not.**

That pairing is the useful half of the report. Entra and local sign-in share the return-URL
handling, the cookie, the circuit and the redirect — so whatever is broken is almost certainly on
the part they do *not* share: the Identity password check, the `SignInManager` call behind it, or
the endpoint that receives the form.

### What to rule out first, in order

1. **The rate limiter.** `feedback_signin_rate_limited` records that a 429 from this endpoint used
   to surface as "Invalid email or password", and Ben would have been retrying. Curl the endpoint
   and read the actual status before believing any on-screen message. This is the cheapest check
   and it has already fooled us once.
2. **Whether the request arrives at all.** The site is behind IIS at `/` with the API at `/webapi`;
   a sign-in POST that 404s or is swallowed by the reverse proxy looks identical to a wrong
   password from the browser.
3. **Password hash provenance.** Accounts created before a key or hashing-option change can fail
   to verify while the account itself is fine. Entra users never touch this path, which fits the
   symptom exactly.
4. **`SignInResult` other than `Succeeded`.** `IsLockedOut`, `IsNotAllowed` (unconfirmed email) and
   `RequiresTwoFactor` all end up rendering as a generic failure. Item 112 already records that the
   2FA panel hangs — if production accounts have 2FA on, `RequiresTwoFactor` is a strong candidate
   and the two items are the same bug wearing different clothes.

### The reporting defect underneath it

Whatever the cause turns out to be, the screen said something that did not distinguish four very
different situations. Same disease as item 120: one message for every failure. Fix the cause, then
make the four outcomes above say four different things — locked out, not confirmed, needs a second
factor, and genuinely wrong — because the next occurrence should be diagnosable from the screen.

**Do not test this by typing Ben's password into the form.** Probe with curl against a seeded
development account, or read the server log for the `SignInResult` that production is producing.

### Root cause — none of the four suspects; a product gap

Probing production with a fake account returned a clean 401 "Failed" from `/webapi/login`, and the
production sign-in page rendered "Invalid email or password" for it — transport, endpoint, rate
limiter and page all healthy. The truth: **an account created through Entra has no password**
(`EntraAuthController` calls `CreateAsync(user)` with none), and until today the product had **no
way to acquire one** — no forgot-password link, no reset page, no set-password panel. The Identity
endpoints and a real email sender existed, unreachable: the sixth write-only feature found by
building the UI for something that "already worked". Ben's production account is Entra-born;
"Invalid email or password" was technically true — there was no password to be wrong.

### Shipped

- `/forgot-password` + `/reset-password` pages riding Identity's own endpoints; the reset email
  now carries a finished link (it used to send a bare code with nowhere to paste it)
- `MyPasswordController` (`/api/me/password`): status, add-first-password (the session is the
  proof — an Entra-born account has no "current password" to ask for), change-password
- Profile → Security gains a Password panel beside two-step sign-in
- The "Invalid email or password" message now hints at the Entra-born case without disclosing
  whether an address has an account
- Help: "Forgot your password — or never had one" in getting-started

### Verified live (dev stack, seeded member account)

forgot → logged link (SMTP fallback) → reset page → new password → login 200; wrong password still
401; change-password demands the current one; the spent reset code refused with a sentence. The
one branch not exercised live is `AddPasswordAsync` for a hash-less account (needs an Entra
session to obtain a token) — it is the `else` of a live-verified `if`, and Ben's account will be
its first real test after the next deploy.

---

## 143. Monetization levers beyond tiers — the menu (OPEN, decisions pending — 2026-08-22)

Ben mid-build on item 85: tiers are not the only part to monetize — equipment limits, loan limits,
open-case limits, "keep in mind things we could do to monetize what we are building" — and then
asked for suggestions. The foundation now supports two mechanisms, and almost every idea fits one:

**Mechanism 1 — keyed limits (`SubscriptionTierLimit`, shipped with item 85).** A cap is a row
(band × `SubscriptionLimit` enum × max), no row = no cap, zero = feature off for that band. In the
enum already: OpenCases, EquipmentItems, ActiveEquipmentLoans, OpenInvestigations, PendingInvites,
StorageMegabytes, PublishedPages. Cheap additions when wanted: members-per-case, EVP scans/month,
video render minutes (the sidecar/RenderService makes these measurable).

**Mechanism 2 — feature gating (zero-means-off).** Candidates, roughly by leverage:
- Video editor tiers — the most differentiated asset. Basic trim free; overlays/keyframes/callouts/
  background rendering/native sidecar paid. Rough-vs-fine render quality is already a concept.
- EVP detection — basic scan free, adjustable-tolerance presets paid.
- CMS — basic public page free; custom layouts, case-bound media slots, publications paid.
- White-label / custom domain for a group's public site (needs real work, not just a gate).

**Explicitly deprioritised:** paid placement in local discovery (erodes trust in a community
product); marketplace shapes where clients pay groups through the platform (payouts, disputes,
tax — see the monetization-direction memory: platform-bills-orgs first).

**Enforcement rule when limits go live:** the check belongs server-side at the create/loan/open
endpoint, refusing with a sentence that names the cap and the band — and per the standing lesson,
every such refusal needs a UI path that renders it.

**Ben's launch shape (2026-08-22):** "to start, mostly the tiers will just be how many open cases
and how many members increasingly." Both levers already exist and need no code: member bands are
the tier boundaries, and Open cases is a cap row in the Price Bands editor, enforced at both
case-creation doors with closed cases never counting. Setting it up is admin data entry.

**The levers menu for later, ranked by Ben's own principle** (cap what scales with value received;
never what groups do to organise themselves):

1. **Storage** — the only lever that tracks a real cost to the platform, in a media-heavy product.
   Mechanism exists (`StorageMegabytes`); enforcement waits on one decision: whose storage a case
   file counts against.
2. **Video rendering** — real CPU burned per render. Shapes: background/server rendering as a paid
   feature (in-browser rough rendering free), fine two-pass quality paid, or metered render
   minutes. The editor is the product's moat; this monetises its most expensive part without
   locking the basic tool.
3. **Video editor depth** — basic trim/clip free; overlays, keyframes, callouts, text effects,
   clipart on paid bands. Zero-means-off already expresses it per feature.
4. **EVP detection** — basic scan free; adjustable-tolerance presets, or scans-per-month, paid.
   Server-side compute, so it also tracks cost.
5. **Public presence** — published pages (cap exists), custom page layouts, case-bound media
   slots, publications on paid bands; later, custom domain / white-label as a headline paid
   feature (high perceived value, low marginal cost, real build).
6. **Equipment lending** — active-loans cap (exists). Mild, but lending is coordination the
   platform does.
7. **Support priority** — cheap to offer, standard, zero code beyond a flag.

**Deliberately not recommended:** charging for data export (data hostage-taking erodes trust),
paid placement in local discovery (erodes the community product), and any cap on roles, naming,
taxonomy, or members-per-case (self-organisation, not scale).

**Ben's governing principle (his words, near enough):** maximise what we can earn *without turning
people off*. The useful test that falls out of it: cap the things that scale with the value a group
gets (storage, open cases, equipment, renders) and leave alone the things groups do to organise
themselves (roles, members-per-case, naming, taxonomy). Ben floated a custom-roles cap; the enum
value exists (`CustomRoles = 8`) so the option is real, with a note recommending it stay unset.

Nothing here is decided. This item is the menu; Ben picks.

---

## 144. Per-member pricing, possibly per-member contracts (OPEN — decision pending, 2026-08-22)

Ben, during the phase-B build: what if a tier charged **per member**, and **each member had their
own contract**? Answer given: doable, in two shapes with very different costs.

**Shape 1 — per-seat price, one group contract.** `PricingMode` on the tier (FlatPerPeriod vs
PerMember); the bill is seat price × `MemberCountAtPeriodStart`, which already exists and is
already frozen. Snapshot machinery, notices, admin UI all unchanged; one renewal date per group.
Cheap — roughly a column, a resolver branch, and pricing-page wording.

**Shape 2 — every seat its own contract.** Per-seat start dates, per-seat price-at-signing,
staggered renewals. The contract-snapshot machinery generalizes (one row per seat instead of one
per org-period), so it is structurally reachable — but it multiplies billing events, renewal
notices and the admin surface, and makes MANUAL billing painful: ten members means ten renewal
dates for a SuperAdmin to mark paid. Recommended only after a payment provider automates
collection.

**Ben's pick (2026-08-22): the overflow-seat model.** A group's band covers its member count as
today; a group can grow PAST its band by new members creating their own accounts and signing up,
and ishaunted.com bills the NEW MEMBER individually — under the group's contract agreement, at a
per-extra-member price. In Ben's words, "this is just another tier I can set up later with price
per extra member values at that time" — so nothing is built now. When he sets it up, the schema
addition is small and known: a per-extra-member price on the tier (a `SubscriptionTierPrice`-style
row keyed "per extra member per period", or a nullable `PricePerExtraMember`), a member-level
subscription record for the overflow seats (Shape 2's machinery, but scoped to overflow only —
the base group contract stays one row), and the join flow offering "this group is full at its
plan; join by subscribing yourself for $X/month". The resolver's band-tiling rule needs one
amendment when this lands: a band with a per-extra-member price is allowed to be outgrown.

**Referral links (Ben's follow-up, same day): SHIPPED in the minimal honest shape.** A referral
link is `/pricing?code=X` — the Coupons screen's codes panel has a per-code **Copy link** button.
The visitor lands with a banner, and signed-in group cards show the code quoted against their own
cadence (or the refusal sentence, learned there rather than at checkout). Attribution needs no new
machinery: which code was redeemed is already a `CouponRedemption` row, so a seller's results are
their code's redemption count. **Still future:** a commission/payout ledger — what the platform
OWES the seller per redemption — which belongs with the payment provider work, since payouts
without a money pipeline are a spreadsheet anyway. A generated batch of one single-use code per
seller, or one shared multi-use code per seller, both work today; the campaign budget caps
exposure either way.


---

## 146. The super-testing journey, and the three doors it found missing (CLOSED 2026-08-22)

Ben's ask: run the product as a brand-new person — sign up, create a group, take a tier, add a
member or two, open a case with an investigation. Built as `NewGroupJourneyTests` (Playwright,
category `Journey`): every account is created DURING the test, so nothing leans on the seeded
roster; email confirmation uses the dev fallback (the link lands in the API log, read via
`BEN_API_LOG`; the fixture skips without it).

**Writing the journey found three write-only features before it ever ran:**

1. **Nobody could APPLY to join a group.** The API accepted applications and the group's review
   panel existed — but no screen ever called `ApplyForMembershipAsync`. New `OrgApplyPanel` on the
   group's public page (`/o/{urlName}`): sign-in aware, shows pending state, renders the server's
   sentences ("not accepting applications", "already a member") verbatim.
2. **Nobody could FOUND a group.** The register endpoint has always let any signed-in user create
   a group and become its Owner — the billing model depends on self-serve founding — but the only
   create page was SuperAdmin-gated. New `/organizations/new` ("Start a group": name + web
   address, slug suggested from the name), a Start-a-Group button on /organizations for everyone,
   and the admin create kept beside it.
3. **The coupon line had no input box** on the manual payment screen — the request field existed
   and nothing sent it (the server-guard-needs-a-UI-path lesson wearing its input-field face).
   The Subscriptions modal now takes a code; the journey redeems LAUNCH25 on the new group's
   first period.

The journey passes end to end in ~24s: cold signup → confirm → found group → applications on →
SuperAdmin records the Small-group tier with LAUNCH25 → two more cold signups apply → founder
accepts both → roster of three → case opened → investigation scheduled.

**Also from this session's full-suite analysis (12 e2e failures, all resolved):** nine were
environmental (the WASM editor host wasn't running — it must be up for the full suite: :5180);
one was a stale test asserting the pre-item-131 disabled-button contract on signup; two were the
evidence tests, whose diagnosis found and fixed two REAL page bugs — `NearbyDiscovery` and
`PublicCaseDiscovery` threw `JSDisconnectedException` from `DisposeAsync` (marking every
navigation off the home page as an unhandled circuit exception), and the evidence submit handed a
`RemoteBrowserFileStream` straight to `StreamContent`, which could leave the page frozen and even
double-submit on circuit replay — it now buffers before the HTTP call. The tests themselves were
also wrong twice over (asserting the note where only the file name renders; navigating away while
the upload streamed).


---

## 147. Documentation audit — ten surfaces shipped with no help link (CLOSED 2026-08-22)

Ben asked whether documentation and help were up to date. Audited rather than assumed, and the
answer was **no** in three places:

1. **Ten new surfaces had no `HelpLink` at all** — pricing, start-a-group, the apply panel, the
   password panel, the evidence review queue, all three Billing screens, and both password pages.
   The prose existed for every one of them; the in-app door to it did not. This is the standing
   "docs + HelpLink in the same branch" rule failing quietly across a whole day's work, because
   nothing checks it.
2. **`your-profile.md` never mentioned the Security tab** — neither the new Password panel nor
   two-step sign-in, so the natural place to look said nothing. New section added.
3. **The product PDF was two days stale** (Aug 20), predating items 84, 85, 111 and 142.
   Regenerated: 77 pages, all of today's sections present.

**The guard.** `HelpLinkAnchorGuardTests` now scans every `.razor` in both web projects, resolves
each `HelpLink`'s slug and anchor against the actual headings in the help content, and fails on
any that points nowhere — a stale anchor is silent otherwise: it renders, it clicks, and it drops
the reader at the top of a page missing the section they were promised. 56 links checked, 0
broken; verified by renaming an anchor and watching it fail. It does not yet enforce that a NEW
page HAS a link — that needs a list of what counts as a page, which is a judgement call, so the
rule stays a habit backed by this audit.

## 148. True-data launch seed — every lookup table populated with real values (CLOSED 2026-08-22)

Ben will clean the shared database before launch and rebuild it from seeders, so every lookup
table needs launch-real data in a production-safe seeder — not just the dev roster. The survey
found most already were: experience taxonomy, contact/note types (org and user), file types,
message types, and subscription tiers all ship real values. Per-org calendar event types are not
a global lookup and were excluded from seeding — but see below. The genuine gap was the
**equipment catalog**: production seeding created only "Generic / Unbranded" plus one generic
model per category; every real-looking brand on the site came from the dev-only roster seeder
and would vanish on a clean rebuild.

`EquipmentTaxonomySeeder` now ships a real launch catalog: **29 genuine manufacturers** (K-II
Enterprises, DAS Distribution, Digital Dowsing, GhostStop, FLIR, Zoom, Tascam, Sony, Fluke, GQ
Electronics, …) and **40 genuine products** with model numbers and field-use descriptions,
mapped across the existing 17 categories (K-II EMF Meter, Mel-8704R, P-SB7 Rev4, REM-Pod,
Ovilus V, Zoom H1n, Sony PCM-A10, Panasonic RR-DR60, FLIR ONE Pro, SiOnyx Aurora Pro, Laser
Grid GS1, …). Name-matched idempotent, approved-on-arrival (curated data skips moderation),
slugs assigned, never touches existing rows including user-proposed same-name entries. Verified
live: first cold start added 40, second added 0.

Calendar event types got the launch treatment differently: they are per-organization, so a
seeder cannot reach them — instead `OrgCalendarDefaults.AddDefaultEventTypes` now stamps five
defaults (Investigation, Public Event, Meeting, Training, Fundraiser) onto every NEW group from
all three creation doors (self-service registration and both SuperAdmin creates), staged on the
same SaveChanges as the organization itself. A founder's calendar is usable from the first
moment; the owner can rename, recolour, or retire them. Covered by tests on the registration
service and the SuperAdmin controller door, both regressed against the un-wired code.

## 149. A click must always show something — case-less investigations were dead ends (CLOSED 2026-08-22)

Ben, as SuperAdmin, clicked the two seeded internal Bell Witch visits on /my-investigations and
nothing happened. Deliberate code, wrong design: a case-less investigation has no case page, so
three handlers (MyInvestigations ×2, MyProfile's map, and OrgInvestigations' own map pins) read
`if (CaseId is not { } …) return;` under an element styled `cursor:pointer`. Ben's rule, now
policy: **"A link should always show something... even if it is a message explaining why it
shows nothing... or where to find it."**

The fix gives case-less investigations a real destination: the group hub
(`/organizations/{id}`) now honors `?tab=` (every BenTab got a stable Id) and `?inv=`, which
lands on the Investigations tab with the row highlighted (`table-active`), scrolled into view,
and its Team roster already open. Clicks from the tab's own map pins focus the row in place.
Two structural bugs fell out along the way:

- **BenTabs deep-link race**: `OnParametersSet` applied `ActiveId` before any tab had
  registered, so a `?tab=` on first render silently kept index 0. `Register` now honors a
  pending ActiveId as the named tab arrives. (CaseDetail's ?tab= links had been winning this
  race by load-order luck.)
- **Both video Publish buttons were dead**: MyVideosPage and CaseVideoEditorPage import
  `/_content/Ben.Web.Website.Library/js/domInterop.js`, which did not exist — the only
  domInterop.js shipped in Ben.Video.Editor's assets. The Library now ships its own (with the
  scroll helper this fix needed).

Proof: three Playwright tests (CaselessInvestigationClickTests) — the specific click, the
deep-link surviving cold navigation, and an every-card-navigates sweep — plus
DeadEndClickGuardTests, a source scan banning the `CaseId is not { } … ) return;` idiom in
.razor files, regressed by reintroducing it and watching it name the file and line.

## 150. SuperAdmin was Forbidden by six case surfaces the case page itself allowed (CLOSED 2026-08-22)

Ben's report: production audio-mix page said "Couldn't load this case's audio files. This is a
problem reaching the server, not an empty case." Root cause reproduced locally as a **403**:
`CaseFileController.IsOrgMember` checked membership only, while the case endpoint honors
SuperAdmin — so half the page loaded and the other half was refused. (The error surface itself
worked exactly as designed — that message replacing a silent empty list is item 141 doing its
job.)

The sweep found the same membership-only helper in six controllers: CaseFile, CaseAudioMix,
ScheduleProposal, CaseReport, CaseResearch, EventEvidence. All six now check
`User.IsInRole(SuperAdmin)` first, the same shape as CaseNoteController / InvestigationController
/ OrgCalendarController. UploadFileShareV2 already handled SuperAdmin at its call sites. Each of
the six has a SuperAdmin-non-member regression test; the CaseFile one was watched failing
against the reverted code. Live-verified: the exact production request (org 50000001…, case
445ddf1d…) now answers 200 with an empty list — that case genuinely has no audio files yet, so
after the next deploy the mixer will say so instead of erroring.

## 151. The site-wide announcement was the seventh write-only feature (CLOSED 2026-08-22)

Ben set a site-wide announcement in Site Settings and it showed nowhere. `site.announcement` was
declared, seeded, editable — and read by nothing. It now rides the anonymous
`/api/public/site-features` response (named explicitly on `SiteFeaturesInfo`, preserving the
"only declared values can be published" property) into `SiteFeaturesProvider`, and MainLayout
renders it as an info banner above every page's body — every page, not just home, because its
declared purpose is maintenance windows and the people it warns are mid-task; anonymous
visitors included. Plain text, line breaks preserved, never markup.

Two rules from the pipe: only a response that names features may set OR clear the announcement
(a failed fetch cannot wipe a live notice), and the admin's Save now awaits `PrimeAsync()`
rather than firing `Invalidate()` — Invalidate schedules the refresh behind the NEXT reader, so
the admin's own next page could still show the old snapshot, which is exactly the "did it
save?" moment the call exists to prevent (found by this feature's e2e test).

Proof: four unit tests (endpoint publishes/omits, provider carries and clears, failed refresh
keeps the notice, layout wiring) — the endpoint one watched failing against the reverted
controller — plus a Playwright test that saves a notice, sees the banner on home AND another
page, clears it, sees it leave, and restores whatever announcement was set beforehand in a
finally, because the database is shared with the public site. Help doc updated
(site-administration → The site-wide announcement).

## 152. "Allow groups to self-register" was a switch that did nothing (CLOSED 2026-08-22)

Found by auditing every declared site setting after item 151. `org.allow-self-registration` was
declared, rendered as a switch, and described as *"When off, only a SuperAdmin can create one"* —
and **read by nothing anywhere in the codebase**. An administrator could switch it off, watch the
page report Off, and every signed-in visitor kept founding groups. Worse than item 151: this one
is a policy control, so its failure mode is believing you closed a door. Worse still, the
Start-a-Group founder door added for item 146 is exactly what it is meant to gate — the hole was
widened while the switch sat there looking authoritative.

Now enforced in `OrganizationMembershipController.RegisterOrganization` (403 with a sentence, not
a bare refusal) **and** given a UI path, because a server rule the UI never surfaces is the same
bug wearing a different coat: the "Start a Group" button is hidden and `/organizations/new`
explains itself and points at the contact form. SuperAdmins are exempt at both layers. Unset
reads as **on** — self-registration is how the product has always worked and the billing model
depends on it, so introducing the check must not close the door for a site that never set it.
Carried to the website on the existing anonymous site-features response, like the announcement.

Verified live end to end: switch off → member gets 403 and no button, SuperAdmin still gets 201.
Three unit tests plus a Playwright test that restores the switch in a `finally` (shared database).

**The guard that stops the next one:** `SiteSettingConsumerGuardTests` asserts every setting in
`SiteSettingKeys.Seed` is read somewhere outside its declaration and outside the admin page that
edits it — editing a setting is not consuming it, which is the exact failure. Regressed by
declaring an unread probe setting and watching the test name it.

## 153. Seven feature switches reported "Off" while their features were running (CLOSED 2026-08-22)

Caught by the item-152 Playwright test refusing to toggle a switch it believed was already off.
The admin page drew each switch from the **stored** value, but an unset flag's real behaviour is
its declared default — and the established sections default **on**. So Site Settings was telling
Ben that the video editor, events, discovery, group public pages, the media library, group
messaging and voting were all switched off, while every one of them was running.

`SiteSettingRecord` now carries `DefaultWhenUnset`, the admin page renders the effective state,
and a row with nothing stored is marked **(default)** beside On/Off so "nobody has set this" stays
distinguishable from "somebody set this". Same class as 151 and 152: a control that misreports the
state it controls is as bad as one that does nothing.

## 154. Four feature switches still gate nothing (OPEN — ratcheted, 2026-08-22)

The sweep behind 152 found the flags themselves half-built. `features.discovery`,
`features.cms-pages` and `features.voting` are read by **no code at all**, so switching them off
changes nothing. `features.events` is read only by `EventReminderJob`, which is worse than
untouched: switching it off silently stops the reminder emails while leaving calendars, event
pages and RSVPs working, so people sign up for events and are never reminded.

Not fixed here because closing them is a product decision per feature — whether
`features.cms-pages` also takes down `/o/{group}/cases`, for instance — across roughly two dozen
surfaces and an anonymous read path, and `SiteSettingKeys`'s own rule ("turning one off must kill
the URLs, not just the navigation links") means each one is real work rather than a one-line gate.

`FeatureFlagGatesSomethingTests` records the four as a shrinking list, the same shape as the
item-120 ratchet: an eleventh switch that gates nothing cannot ship, and fixing one requires
deleting its line. The test also documents what it cannot see — it measures whether a flag is read
*at all*, not whether the gate is complete, which is why `features.events` passes it while being
the most misleading of the four. **The help documentation now warns administrators off all four
rather than promising behaviour they do not have.**

## 155. No group created after item 148 could be deleted (CLOSED 2026-08-22)

Found while cleaning up a probe group: `DELETE /api/organizations/{id}` answered **500**. Every
foreign key onto `Organizations` is `NoAction` by convention, so deleting a group has never
cascaded — and item 148 gave every new group five default calendar event types **at birth**, which
turned that latent weakness into a certainty: from that commit on, no newly created group could
ever be deleted, and the failure surfaced as an unhandled server error.

Delete now removes the rows created *with* the organization — the founder's membership and the
default event types, neither of which is anyone's reason to keep it — and catches the remaining
`DbUpdateException` to answer **409 with a sentence** naming what to do. Refusing to delete a group
that still has cases, files or events is correct; doing it with a 500 was not. Live-verified by
deleting the probe group cleanly, and covered by a regression test that seeds both birth children.

## 156. Organization roles & permissions, tier-aware — the full plan (OPEN — planning with Ben, 2026-08-23)

Ben's request, near-verbatim, plus what the codebase already has so the plan builds on it
instead of beside it.

**The ask.** CRUD settings per role per organization. Users can belong to several organizations;
roles are per organization, and a person in two groups counts against BOTH groups' member-count
tiers (already true — memberships are per-org rows). All CRUD settings are shown when creating a
role, but where the organization's tier does not include a given capability, those toggles are
**grayed out with a note that upgrading the tier would make them apply**. Defaults are
**no permission**. Every new organization gets **several roles created for it** at birth. The
**owner always has access to everything**. Members may hold **one or more roles**. An accepted
member gets a **baseline read of the obvious parts** of the organization; beyond that, nothing
unless a role they hold grants it. Candidate roles: Equipment Manager, Case Manager, CMS Manager,
Client Manager, Content Manager, Historian, Secretary.

**Already built (do not rebuild):**
- `OrganizationRole` + `OrganizationRolePermission` (per-table CRUD bitmask,
  `OrganizationSecurityTable` × `OrganizationSecurityAction`) + `OrganizationRoleMembership`
  (a member may hold many roles; OR across them).
- The role editor (`OrgRolesManager`/`OrgRoleEditor`) with 22 permission sections, each with a
  plain-language description (item 83) and a coverage guard test.
- Enforcement: `OrganizationSecurityService.HasAccessAsync` — SuperAdmin, then Owner/Administrator
  bypass, then direct grants, then role permissions. Default deny, exactly Ben's "none unless
  covered". ~38 call sites; a further ~16 controllers use plain is-member/is-admin checks — the
  de-facto "baseline member read" today, implicit rather than declared.
- Tier machinery: keyed `SubscriptionLimit` rows incl. a dormant `CustomRoles` cap;
  `SubscriptionLimitGuard`; better-of contract rule.

**Genuinely new:**
1. **Default roles at organization creation** — same pattern as item 148's calendar event types
   (`OrgCalendarDefaults`, stamped from all three creation doors on the same SaveChanges).
   Backfill decision needed for existing orgs.
2. **Tier-gated capabilities in the role editor** — a mapping from (permission section × action)
   to "included in your tier?", grayed toggles + upgrade note when not. Nothing like this exists;
   the tier system caps counts, not capabilities. Shape needs Ben's answers below.
3. **Baseline member read made explicit** — either a seeded, protected "Member" role or a
   documented implicit baseline; today it is scattered across is-member checks.
4. **Role templates** for Ben's candidate list, as starting points a group can edit.

**Ben's decisions (2026-08-23), locked:**
- **D1 — Tier gating is per permission AREA.** Each tier carries a checklist of included areas
  (Equipment, Cases, CMS, …); the role editor grays out sections whose area the group's tier does
  not include, with an upgrade note. Managed as a checklist per tier in SuperAdmin.
- **D2 — Owner and org-designated Administrators keep the blanket bypass** within their own
  organization. Custom roles govern Manager, Member, and Viewer memberships. (Confirmed distinct
  from the application-wide SuperAdmin/Admin identity roles, which this plan never touches.)
- **D3 — Baseline read for any accepted member:** group details/profile, member list,
  calendar/events, group messages, shared-files list. Cases, investigations, equipment, CMS,
  clients, and settings require a role.
- **D4 — On downgrade/lapse, uncovered permissions stop applying at runtime** but remain stored;
  the editor shows them grayed-but-remembered and they resume on upgrade. Nothing is deleted.
- **D5 — Roles are strictly additive.** A role can only add permission on top of the
  no-permission default. There is no deny/revoke row and never will be: holding more roles can
  never reduce access. (The existing resolver is already OR-across-roles, so this is a stated
  invariant to guard, not a change.)

**Key code fact the plan turns on:** `OrganizationSecurityTable` (36 values, persisted numbers,
never renumber — append only) has **no Case, no ClientRequest, and no OrgCalendar value**. Case
access today is enforced purely by is-member checks, which is exactly the "implicit baseline"
that D3 replaces. So "Case Manager" and "Client Manager" roles require NEW enum values, new
editor sections, and controller migration — the largest genuinely new work in the plan.

---

### The plan — six phases, each independently shippable

**Phase A — Permission areas + tier inclusion model (zero behavior change).**
New `OrganizationPermissionArea` enum (explicit numbers, append-only): OrganizationProfile,
Membership, Cases, Investigations, Equipment, PublicPages, Files, Clients, Calendar. A total
static map `AreaFor(OrganizationSecurityTable)` — every org-scoped table maps to exactly one
area (user-scoped values and the dead `AppUser=13` are a documented exclusion list). Append new
enum values `Case=37`, `ClientRequest=38`, `OrgCalendar=39`. New entity
`SubscriptionTierPermissionArea` (TierId × Area, unique index) + migration. **Seed every
existing tier with ALL areas** so deploy changes nothing; Ben unchecks to differentiate later.
Resolution rides the effective tier the limits already use (better-of contract rule); a group
with no subscription resolves to the default tier's areas; no tiers configured at all reads as
all-areas — a billing hiccup must never lock a group out of its own data (same fail-open
philosophy as SubscriptionLimitGuard; deliberate downgrade is D4's job, not an outage's).
SuperAdmin UI: area checklist per tier on AdminSubscriptionTiers, audit-logged.
*Tests:* mapping guard (total, no orphans, every area non-empty); resolver units (no-sub /
contract better-of / lapsed); SuperAdmin-only endpoint auth; all regressed against un-fixed code.

**Phase B — The new permission surfaces, additively (still no tightening).**
Role-editor sections for Case, ClientRequest, OrgCalendar with plain-language descriptions
(`RolePermissionCoverageTests` forces completeness). Controllers gain role checks **in addition
to** what exists: operations that are admin-only today (e.g. calendar event Create, client
request accept) become "org-admin OR role grant" — so a Secretary or Client Manager role is
immediately useful, while member-wide reads stay untouched until Phase D.
*Tests:* per-endpoint — role grants the write to a non-admin member; no role still Forbids;
regressed.

**Phase C — Default roles at birth + backfill + grandfathering.**
`OrgRoleDefaults.AddDefaultRoles` stamped from all three creation doors on the same SaveChanges
(item 148 pattern), and **added to the org-delete birth-children list** (item 155's lesson —
role memberships, permissions, then roles). Idempotent name-matched backfill seeder for
existing groups. Starting grants (every group can edit or delete these afterwards):

| Default role | Grants |
|---|---|
| Case Manager Role | Cases CRUD, Investigations CRUD |
| Equipment Manager Role | Equipment CRUD, Checkouts CRUD |
| CMS Manager Role | Public pages CRUD, CMS sections CRUD |
| Client Manager Role | Client requests CRUD, Cases Read |
| Content Manager Role | Org files CRUD, CMS sections RU |
| Historian Role | Read on every area |
| Secretary Role | Calendar CRUD, Membership requests RU, Org profile RU |

Naming decided by Ben 2026-08-23: every permission role carries the **"… Role" suffix**, because
the member-title ladder (item 157) legitimately uses overlapping words — "Case Manager" the
title-adjacent word and "Case Manager Role" the permission set must never be confusable in the
UI. Titles are seniority; roles are permission sets; the suffix is what keeps that boundary
visible to a group owner reading a screen.

**Grandfathering (recommended, needs Ben's yes):** a one-time migration assigns an
"Investigator" role (Cases + Investigations Read) to every existing active non-admin member, so
the Phase D flip strips nobody mid-case; members joining after cutover start at baseline.
*Tests:* the three creation-door tests extended; delete test extended; backfill idempotence
(run twice, second adds zero); regressed.

**Phase D — The enforcement flip (the breaking phase).**
`HasAccessAsync` gains the area gate: role permissions AND direct grants in area X count only
while X is in the group's effective areas (D4). Baseline read becomes an explicit constant —
`OrganizationBaseline.ReadTables` — honored for any active membership (Viewer included), with a
source-scan guard listing exactly which controllers may still use bare is-member checks (the
baseline surfaces) versus which must call `HasAccessAsync`. Then the migration: the Cases
cluster (CaseFile, CaseNote, CaseReport, CaseResearch, CaseAudioMix, ScheduleProposal,
Investigation reads, CaseTransfer, EventEvidence review), equipment reads, and CMS drafts move
from is-member to `HasAccessAsync(table, Read)`. The UI mirrors the server the same day
(server-guard-needs-a-UI-path — five strikes already): OrganizationView's Cases /
Investigations / Equipment tabs and BenNav render from a new lightweight
"my effective permissions in this org" endpoint, and every refusal renders BenListState
(item 141), never "nothing here".
*Tests:* the full HasAccessAsync matrix — Owner/org-admin bypass, baseline × membership kinds,
area included/excluded, multi-role OR (D5), **multi-org isolation** (a role in group A grants
nothing in group B), inactive membership/role/permission rows, Viewer; effective-permissions
endpoint; every case regressed.

**Phase E — Tier-aware role editor + org-facing surfaces.**
Editor sections grouped by area; an ungated area's toggles are disabled but show their stored
values (D4 grayed-but-remembered) under a note — "These come with the {tier} plan — upgrade to
put them into effect," linking to /pricing. The server refuses *changes* to ungated sections
(400 with a sentence) while preserving stored rows on unrelated edits. Role list shows
"N permissions inactive on your tier." The public pricing page lists each tier's included role
areas (verified on the anonymous path — authors-see-what-visitors-cannot). Downgrade notices
name the areas that will stop applying, through the existing TierChangeNotice machinery.
*Tests:* editor server guard (refuses the change, preserves the rows) regressed; pricing
anonymous render.

**Phase F — End-to-end proof, help, and the verification pass.**
Playwright: (1) `RoleTierJourneyTests` — SuperAdmin unchecks an area → owner's editor grays with
the note → a role-holding member loses that access at runtime as a rendered refusal → re-check →
access resumes; everything restored in `finally` (shared DB). (2) `OrdinaryMemberBaselineTests`
— a role-less member sees exactly the D3 baseline; assign Case Manager → cases appear; unassign
→ gone (test-as-an-ordinary-member rule, seeded accounts). (3) Fresh group lists the seven
default roles, and can still be deleted. (4) Multi-org isolation. Help docs in the same branch:
organization-administration (roles rewrite: additive model, defaults, tier graying),
getting-started (what a new member sees), site-administration (the tier checklist), plus
HelpLinks and the PDF regen. Full unit + e2e suites, then a live click-test as owner, org-admin,
member, and viewer before merge.

**Flagged for Ben, not blocking:** (1) grandfathering yes/no — recommended yes, above;
(2) Viewer membership semantics — recommended: baseline read only, but roles remain assignable
to Viewers like anyone else; (3) whether Historian ships as a default role or only as an
add-from-template option.

**Sizing:** A, B, C, E, F ≈ one session each; D ≈ two (it touches ~16 controllers and every
member-visible tab). Order is load-bearing: A and B change nothing visible, C prepares the
safety net, and only then does D flip enforcement.

## 157. Member title ladder — seniority, not permissions (CLOSED 2026-08-23 — built as agreed)

Ben's concept, agreed after discussion: a per-organization ladder of member **titles** —
seniority within the group, deliberately and permanently distinct from permission roles (item
156) and from the membership security kinds (Owner/Administrator/Manager/Member/Viewer).
**Titles define the level a member is within the group; roles define sets of permissions.**
Titles grant nothing, ever.

Decisions locked:
- **Ladder ends at Lead Investigator.** Ben's default rungs: Probationary, Junior Investigator,
  Investigator, Senior Investigator, Lead Investigator. "Case Manager" is NOT a rung — it is a
  permission role (Case Manager Role, item 156) designating who actually manages cases.
- **Additive purity preserved:** "probationary" is a label, never a restriction. A group that
  wants a genuinely restricted newcomer already has the Viewer membership kind — probation-as-
  title plus Viewer-as-restriction covers both meanings without ever inventing a deny mechanism.
- **Per-org and editable**, same pattern as calendar event types (item 148): an
  `OrganizationMemberLevel` lookup (Name, SortOrder, IsActive) seeded with the five defaults at
  every creation door on the same SaveChanges — and added to the org-delete birth-children list
  (item 155's lesson). Idempotent backfill for existing groups.
- **A nullable `MemberLevelId` on `OrganizationUserMembership`** (no title is fine, and a
  deleted level nulls out rather than blocking). Displayed wherever the member displays: roster,
  member list, profile's group section, investigation team lists; public team page only if the
  group opts in.
- **Deferred on purpose:** any bridge from level to auto-assigned roles ("Investigator and above
  get Case Read"). Useful someday; reintroduces the title/permission entanglement today.

**Built 2026-08-23, one session, exactly as specified above.** `OrganizationMemberLevel` +
nullable `OrganizationUserMembership.MemberLevelId` (SetNull — deleting a rung clears, never
blocks), migration applied; `OrgMemberLevelDefaults` stamped at all three creation doors on the
same SaveChanges and added to the org-delete birth-children list; `MemberLevelSeeder` backfilled
every existing group (skips any group that has ANY levels, so an edited ladder is never
touched); CRUD + assign endpoints (members read, admins write, cross-org assignment refused
with a sentence — that guard watched failing); ladder manager in group Settings (add, rename,
reorder by swap, delete) and a per-row assignment dropdown on the Members tab (plain-Blazor
select — the Telerik synthetic-event trap bites exactly here), badge for non-admin readers.
Six controller unit tests + all three door tests and the delete test extended (ladder assertion
regressed); Playwright MemberTitleLadderTests passes ×4 including cleanup in finally. Help:
organization-administration → "Member titles — the ladder" + HelpLink. Verified live in the
browser and via API: TGH answers the five seeded rungs in order.

One operational relearning while verifying: BOTH hosts on :5252/:5078 were stale from a
previous session — `dotnet run` on an occupied port dies silently and the old binary keeps
answering, which produced a phantom 404 on the new endpoint. Kill by PID from
`lsof -sTCP:LISTEN` before trusting any live check (feedback_dotnet_run_stale_process_trap,
third occurrence).

## 158. Engagement assignments — investigation duties + case contacts (CLOSED 2026-08-23 — built as proposed)

The third people-concept, from Ben's scenario: ten people RSVP to a scheduled investigation —
who is lead investigator *for that visit*, who is in charge of equipment, who collects the
evidence when finished? And every case needs **at least one point of contact besides the case
manager**. Neither titles (seniority) nor roles (standing permissions): a duty for one specific
engagement.

**What already exists** (this feature is half-gestured-at in the schema):
`InvestigationAttendee.IsLead` (bool) and `InvestigationAttendee.AssignedRole` (**free text** —
no consistency, nothing to filter on, no way to see an unfilled duty), and
`Case.CaseManagerAppUserId` (real, assigned, tested). Missing: structured duties, and any case
contact besides the manager.

**Investigation duties:**
- Per-org `InvestigationDuty` lookup (calendar-event-types pattern), seeded: Lead Investigator,
  Equipment, Evidence Collection, Documentation — editable per group, birth-children rules apply.
- `InvestigationDutyAssignment`: attendee × duty. Lead Investigator is single-holder; others
  allow several. Migration: `IsLead = true` becomes a Lead assignment; existing `AssignedRole`
  strings migrate to a matching duty where the name matches, otherwise survive as a note field.
- Scheduling screen gets a "who's doing what" panel over RSVP'd attendees, **showing unfilled
  duties** — the organizer sees the gap before the night of, which is the point of structuring
  this at all.
- **Duties grant nothing** — coordination, not permission (additive rule stays pure) — with one
  scoped exception: the Lead duty feeds the existing InvestigationAccess manage-this-
  investigation logic, which already honors leads.

**Case point of contact:**
- Per-case contact assignment (one or more members), shown on the case header for investigators
  **and on the client's view** — the client finally has a named human. Client-message
  notifications route to contacts + case manager. With no explicit contact, the case manager IS
  the contact, so the client-facing surface never renders empty.

**Title-to-duty eligibility (Ben, 2026-08-23):** *"the higher the title, the more responsibility
they can take on during an investigation."* Built in as an optional per-duty **minimum title**:
each InvestigationDuty may name a minimum member level from the group's own ladder (item 157);
null = anyone. Comparison is by the ladder's SortOrder at assignment time, so it survives
renames and follows each group's own ordering. **Soft enforcement**: under-level attendees
render grayed with the reason, and whoever manages the visit may override with an explicit,
recorded confirm — the senior calls in sick, the capable junior steps up. Seeded duties ship
with **no minimums** (no surprise behavior; groups opt in). A deleted rung nulls the
requirement (SetNull) rather than blocking. This is the one sanctioned title→responsibility
bridge, and it is deliberately eligibility-not-permission: titles still grant no CRUD, ever —
the level→auto-role bridge stays deferred (item 157). Tests: eligibility rendering, the
override path and its audit trail, SetNull on rung deletion, SortOrder-not-name comparison.

**Naming note, accepted:** the title "Lead Investigator" (rank, item 157) and the duty "Lead
Investigator" (tonight's lead) share words on purpose — that is how groups talk, a junior can
lead a small visit, and context (profile vs. roster) disambiguates.

**Built 2026-08-23, one session.** `InvestigationDuty` (per-org, IsSingleHolder,
MinimumMemberLevelId SetNull) + `InvestigationDutyAssignment` (attendee × duty, unique,
EligibilityOverridden recorded) + `CaseContact`; migration applied. Defaults (Lead
Investigator solo / Equipment / Evidence Collection / Documentation, no minimums) stamped at
all three creation doors, in the delete birth-children list, backfilled for 14 existing groups;
the legacy structurer turned 7 IsLead/AssignedRole values into assignments idempotently, and
free text that matches no duty survives untouched. The Lead duty writes through to
`InvestigationAttendee.IsLead`, so InvestigationAccess and every lead badge keep one source of
truth. Duty board on the roster's Team panel (unfilled duties badged, per-duty assign picker,
soft-eligibility refusal with an Assign-anyway confirm, ⚠ on overridden holders); duty manager
in group Settings (solo flag + minimum-title select, cross-org level refused); case contacts
panel on the case Detail column and on the client's case view with the case-manager fallback
badge; client-message notification bucket routed contacts → manager → members (org admins
always see it). Two of the house guards caught the build mid-session — a `?? []` in the new
adapter method and a dropped LoadResult in the contacts panel — both fixed, not excused.

14 new unit tests (duty rules incl. eligibility regressed by disabling the gate; contact
fallback and gates) + door/delete tests extended; Playwright
InvestigationDutyAndContactTests green ×3 with shared-DB cleanup in finally. Two e2e lessons:
a retrying click on a TOGGLE alternately opens and closes the thing it waits for — click once
and wait; and when a mid-load re-render can bounce a clicked tab strip, arrive by the item-149
?tab= deep link instead — the deep link IS the state. Help: working-a-case (two sections) +
organization-administration (duties). Deliberately deferred to item 160 (Ben's matrix spec):
per-title duty ELIGIBILITY beyond the single minimum, capability semantics (PoC/invite/
schedule per duty), and the Case Lead position.

## 159. Impersonation-faithful bell + your-organizations links in the sidebar (CLOSED 2026-08-23)

Two navigation-shell asks from Ben, verbatim in substance:

1. **Impersonation fidelity for the bell.** When impersonating someone, the notification bell in
   the top bar must reflect exactly what the impersonated person sees — their counts, their
   buckets — not the SuperAdmin's own. (Audit which other shell surfaces read the real identity
   while impersonating; the bell is the named one, but the fix should sweep the header.)

2. **Sidebar links to your groups.** A member of one organization gets a link to that
   organization's page, with its name, directly below Home in the main navigation sidebar. A
   member of several gets the list. A member of MORE THAN FIVE gets a "Your Organizations"
   expandable group holding the links — same collapse behaviour as the existing grouped menu.
   (BenNav already renders grouped entries with chevrons and badge roll-ups; this reuses that
   machinery, fed from the person's memberships — and under impersonation it must show the
   impersonated person's groups, which is the same fidelity rule as the bell.)

**Built 2026-08-23.** What the investigation found: the bell itself was already faithful —
impersonation swaps the real bearer token and NotificationState refetches on the switch — but
THREE adjacent fidelity breaks made the whole view lie:
1. **The sidebar showed the signed-out menu while impersonating** (an explicit
   `|| IsImpersonating` in both nav branches) — the single biggest lie in the view; the
   impersonated person's real menus, groups, and badges never rendered. Removed; only the
   SuperAdmin's own Administration section stays hidden while impersonating.
2. **Impersonation did not survive a reload**: `PersistedAuthState` never carried
   IsImpersonating or the Original* session, so a refresh restored the impersonated token with
   no banner and no Return — the SuperAdmin was silently stuck as the other person until
   logout. The full quintet (flag + original access/refresh/id/email/display-name) now
   persists and restores.
3. **The avatar kept the impersonated person's initials after Return** — StopImpersonating
   restored email and id but not UserDisplayName. Restored with the rest.

The sidebar links: new membership-rows-only endpoint
(`/api/security/organizations/my-memberships`) and service method, deliberately distinct from
the SuperAdmin-sees-all list — the sidebar answers "YOUR groups" (a SuperAdmin sees the three
they belong to, not all fourteen; unit-tested contrast). One link per group under Home; six or
more fold into a "Your Organizations" expandable. One timing fix worth remembering: the nav's
fetch guard must read the TOKEN STORE, not the IsAuthenticated parameter — inside the store's
own StateChanged handler the parameter is a render behind, and the fetch silently skipped on
every soft sign-in. Verified live in the browser: sign-in shows the three links; impersonating
Sarah shows HER two groups, her badges (bell 73 vs 78, Notifications 4 vs 7), her menus; a hard
navigation keeps the banner and Return; returning restores everything including the avatar.
Help updated (getting-started nav, site-administration impersonation section). e2e coverage
deferred until Ben lifts the test hold.

## 160. Title-to-duty eligibility matrix, owner-configured per org (OPEN — Ben, 2026-08-23)

Ben's spec, given while item 158 was being built, in substance: a new organization tab where the
owner decides which investigation-level duties each TITLE is eligible for — a matrix, not just a
minimum. His worked example: a Junior Investigator may ASSIST with equipment; an Investigator
may RUN the equipment but may not be a point of contact for the investigation; a Lead
Investigator may run equipment AND be a point of contact AND send invites to members for the
investigation — but may not schedule or reschedule it. The CASE LEAD acts as the investigation's
administrator: always a point of contact, schedules investigations, assigns and re-assigns
duties from the member list (for the investigation and the case itself), provides history, and
assigns adequately-titled members to historical research — with "adequate" being the org admin's
determination via this matrix.

What this builds on (158, shipped): `InvestigationDuty.MinimumMemberLevelId` is the degenerate
single-threshold case of this matrix — the schema hook is already there. What is genuinely new:
- an **eligibility matrix** (title × duty) replacing/augmenting the single minimum, edited on a
  new org tab;
- **capability semantics attached to duties** — point-of-contact-for-the-visit, may-invite,
  may-schedule are capabilities a duty confers, which is a step beyond "duties grant nothing"
  and must be reconciled with that principle deliberately (they are per-visit capabilities, like
  the Lead's manage right — scoped, expiring, not standing CRUD);
- a **"Case Lead" duty/position** with defined powers (the investigation-admin bundle above),
  overlapping the existing case manager and item 158's case contacts — the design must say
  which of those three the client sees and which schedules;
- interaction with item 156's permission areas (scheduling is also a CRUD permission — the
  matrix and the role system must not give two different answers to "may Sarah schedule this").

Not started. Needs a design pass with Ben before building — the capability list per duty is
product surface, not plumbing.

## 161. Action-needed banners under the site-wide announcement (OPEN — Ben, 2026-08-23)

Ben's spec: when an investigation request is waiting, show an info alert **just below the
site-wide announcement banner** for anyone who can accept and review investigation requests;
likewise a waiting membership application shows the alert to anyone with permission to accept
members. Per-viewer, permission-aware banners in the MainLayout slot the announcement (item 151)
already owns — the same render position, driven by the caller's own pending-work counts (the
notification summary already carries some of these buckets; the banner is a louder surface for
the two decisions that block OTHER people: a client waiting on an answer, an applicant waiting
at the door). Design notes for the build: dismiss-per-item-or-session so it nags without
becoming wallpaper, link straight to the queue it names, and counts must be permission-scoped
server-side (the item-141 rule — never render a bucket the caller cannot open).

## 162. Default avatar is an upload, not a Guid box (CLOSED 2026-08-23)

Ben: *"Instead of having to provide a Guid for the missing user icon in the admin settings, I
would rather it be an upload like is currently used for uploading anyone's avatar"* — and
*"replacing it removes the old icon."* The Site Settings row for `avatar.default.upload-file-id`
now renders an **Upload image** control instead of a free-text Guid input: pick a JPEG/PNG/GIF/
WebP and it uploads through the same path profile photos use (public on purpose — the image
renders for signed-out visitors), points the setting at the new file, and refreshes the preview.
**Replacing deletes the previous image** best-effort after a successful save; a file that is
genuinely referenced elsewhere survives because its foreign keys make the hard delete refuse,
which is the right arbiter. Browser-file streams are buffered before upload (the
RemoteBrowserFileStream freeze). Seed description + help updated ("there is nothing to clean
up"). Upload click-through verification rides the held e2e pass — the file picker cannot be
driven by the sandboxed browser tool.

**Flagged in passing (spawn-task chip raised):** `UploadFileController.Delete` has NO ownership
check — any authenticated user can hard-delete anyone's file and its blob. Its sibling Update
endpoint has the owner-or-SuperAdmin gate; Delete needs the same, plus a regression test. Same
controller family as the previously flagged GetAll/Download gaps.

## 163. Three default avatars — man, woman, generic (CLOSED 2026-08-23)

Ben: *"break it into 3 kinds: known man, known woman, generic when we do not know."* Two new
settings (`avatar.default.man.upload-file-id`, `.woman.`) beside the generic, all three
rendering the item-162 upload control with per-key previews and replace-deletes-old.
`UserAvatarController` resolves: the person's own photo always first, then the man/woman image
when their profile declares it AND the image is configured, then the generic — an unset
specific image degrades to generic, never to a broken picture.

The chain was dead without one more piece the survey caught: **nothing anywhere wrote
`AppUser.Gender`** (every existing Gender reference was the ClientRequest entity) — the
settings would have passed the consumer guard while feeding off a field nobody could set,
the write-only bug one level down. So the profile gained an optional, self-declared **Sex**
select — per Ben's wording: blank by default, with Male / Female / Unspecified as the options —
null-means-untouched in the update request, blank and Unspecified both stored as null and both
selecting the generic icon (the two are indistinguishable on purpose; nothing else ever reads
the field). It is
used for exactly one thing and the profile says so. 5 avatar-resolution tests (regressed by
nulling the man branch) + a profile round-trip test; help updated on both pages. Upload
click-throughs ride the held e2e pass.

## 164. File deletion belonged to everyone; now it belongs to the owner (CLOSED 2026-08-23)

Found while building item 162: `UploadFileController.Delete` had **no ownership check at all** —
any authenticated user could hard-delete anyone's file AND its blob from disk. The destructive
sibling of this controller family's previously flagged GetAll/Download gaps, and strictly worse:
a read leaks, a delete destroys. The sibling Update endpoint had carried the correct
owner-or-SuperAdmin gate the whole time.

Ben's rule, set while fixing it: **only a file's owner can delete it. An organization can
exclude a file from its own collection, but never delete it from the person's account.** The
audit confirmed the org-side surfaces already obey: a case-file "delete" removes only the link
(the UploadFile survives — chain of custody), and an OrganizationFile is the org's own byte-copy,
so removing it never touches the source. The one path to a person's actual file now carries the
same gate as Update; SuperAdmin retains it for moderation, the one deliberate exception to
"owner only" — somebody has to be able to remove abuse, and that somebody is accountable.

Three tests: non-owner gets Forbid with the row AND the blob surviving (the blob check matters —
a Forbid that still deleted from disk would be the same hole in a different layer); owner
deletes both; SuperAdmin may. The non-owner test was watched failing against the ungated code.

**The full lifecycle, Ben's rules, verified 2026-08-23 as already-shipped behavior:** an
organization EXCLUDES a file from its collection (Files-Delete permission removes the org's own
copy; the person's original survives). The end user may RE-SUBMIT it by sharing the file with
the organization — the share is owner-controlled. Re-ADDING it to the collection then requires
someone with the org's Files-Create permission (`copy-from-user`, which also verifies the
source is public or actively shared with this org), and publishing it publicly requires
Files-Update on top. Every step is a real HasAccessAsync permission, not mere membership, so
the item-156 roles arc will govern these gates without further work.

## 165. The documentation refresh pass (CLOSED 2026-08-23)

Ben lifted the e2e hold with "do e2e and screenshot and help and any missing seeding and
anything else we missed." The pass:

- **Two event-evidence e2e tests had been silently skipping since the day after they were
  written**: the seeded past event's slug embeds the SEED date, and the tests recomputed
  "today minus 30" — drifting one day per day. They now ask the anonymous events API for the
  real slug (the same source the page uses). Both run and pass again.
- **New e2e coverage** that had waited on the hold: ImpersonationAndSidebarTests (the
  impersonated person's real menu, the reload surviving with its exit, membership-only sidebar
  links) and DefaultAvatarUploadTests (a real PNG through the file input — the one interaction
  the sandboxed browser pane cannot drive — including replace-over-replace and a shared-DB
  cleanup that clears only what the test set).
- **All 14 help-media generators re-run** (~40 dark-mode screenshots refreshed, including the
  new Site Settings upload rows, the sidebar group links, and the members-grid Title column) and
  the **product PDF rebuilt** (~10MB) via the headless-Chrome print step.
- **A stale-description bug the screenshot pass caught**: SiteSettingsService.GetAllAsync
  preferred the STORED description, fossilizing the wording of the day a row was first written —
  the generic avatar row still told administrators to "paste its file id here". Descriptions now
  always come from the current declaration.
- **Seeding review**: nothing missing — the aged-out past event was a test-side drift, not a
  seed defect; the three default-avatar images are deliberately unset for Ben to choose;
  duties/levels/catalog all backfilled on earlier items.

Final sheet: **322 e2e passed, 0 failed**, the only 15 skips being the capture generators
themselves; 5,255 unit tests; zero warnings. Baseline run had two 30-second GotoAsync timeouts
under full-machine load (both pass in 2s solo) — congestion, recorded here so the next reader
doesn't chase them.

### Item 156 Phase A — SHIPPED 2026-08-23

Permission areas exist end to end, gating nothing yet, exactly per the phase contract:
`OrganizationPermissionArea` (9 areas, numbered, append-only); the TOTAL table→area map in
`PermissionAreas` with its guard (every OrganizationSecurityTable value mapped or in the
declared user-scoped exclusion list — never neither, never both; every area non-empty; the tier
admin endpoint's SuperAdmin policy asserted by reflection); `Case=37 / ClientRequest=38 /
OrgCalendar=39` appended to the security-table enum, which the item-83 coverage guard
immediately caught until the role editor gained their three described sections (grants storable
now, becoming decisive as later phases land — the descriptions say so);
`SubscriptionTierPermissionArea` rows (unique tier×area) with cascade from the tier;
`IncludedAreasResolver` mirroring SubscriptionLimitGuard's tier resolution and failing OPEN in
every ambiguous case (no tiers, invalid list, zero rows) — only a checklist that SAYS so may
exclude; the all-areas seed placed BEFORE the tiers-exist early-return (first attempt sat after
it and never ran on an existing database — caught live, the zero-behavior-change promise almost
shipped as zero-rows-fail-open instead); whole-list-replace endpoint + save-on-toggle checklist
on Price Bands. 11 new unit tests (map totality, resolver incl. regressed checklist case,
endpoint replace semantics) + TierRoleAreasTests e2e round-trip ×2 with restore. Help updated.
Live-verified: 3 tiers × 9 areas seeded; an uncheck survives reload; restored.

Next: **Phase B** — role checks added ADDITIVELY to admin-only writes (calendar create, client
request accept) so Secretary/Client Manager roles become useful with zero tightening.
