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

## 4. Client-facing case log (timeline, sub-clients, evidence) — 🟡 Partially complete (2026-08-06)

Clients should be able to return to their submitted request/case after submission and:
- ✅ Add new timeline entries for new experiences or evidence. *(already existed — `LogOccurrence`/`UpdateOccurrence`/`DeleteOccurrence`.)*
- ✅ Add media (audio/photo/video) as evidence. *(already existed — `AttachFile`/`DetachFile` on occurrences.)*
- Add other people who have had experiences at the property as **sub-clients**:
  - ⬜ **Not built:** the primary client invites someone by email to create an account, which gets linked to the case as a sub-client. `AddCoClientAsync`/"Share Case Access" already exist but only work for people who **already** have an account — the invite-and-create-account flow itself (email sending, signup token, auto-link on registration) is a distinct onboarding feature, deferred as its own follow-up rather than folded in here.
  - ✅ **Shipped:** without requiring an account, add basic info about a person (name, age, relationship, whether they live there) so they can be referenced in notes/timeline entries. New `CaseRelatedPerson` entity + `api/my-cases/{caseId}/related-people` (primary-client-only) + a "People at the Property" section in `MyCaseDetail.razor`. Scrubbed-when-public is satisfied structurally — it's never returned by any public-facing endpoint — rather than by an explicit scrub toggle.
- ✅ Every client-side write to the case log should create an audit log entry capturing user info. Added `IAuditLogService` calls (the same mechanism used by ~45 other controllers) to every write in `MyCaseController`: occurrence create/update/delete, co-client add/remove, occurrence-file attach/detach, related-person add/delete. IP address specifically is captured only for occurrences, via `CaseTimelineEntry.IpAddress` (a pre-existing dedicated column) — the generic `AuditLog` table has no IP column at all, and adding one is a bigger cross-cutting schema change affecting every other controller that logs through it, so it was left out of this slice rather than done partially/inconsistently.

> **Shipped on `feature/client-case-log`.** Remaining for this item: the email-invite-to-create-account sub-client flow.

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

## 6. Universal media library component — 🟡 Phase 1 complete (2026-08-06)

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

---

## 8. Media & Properties panel — dedicated Preview tab + real thumbnails (not started)

In the video editor's floating "Media & Properties" window (`Ben.Video.Editor/Components/VideoEditor.razor`):
- Add a third tab and move the preview window into it, instead of the preview sitting below the editor page as it does today.
- `ClipBrowser.razor`'s Video/Audio/Image/Server tabs currently show icon-only placeholder rows (`SvgIcon.FileVideo`/`FileAudio`/`Image`) for every clip — replace with small thumbnails or a list view like the main website's media library (`MediaLibraryGrid.razor`, `Ben.Web.Library/Media/`) already does: eager-load thumbnails for images, lazy-load-on-click for video/audio (since there's no dedicated thumbnail endpoint yet — same constraint noted in item #6). **No voting UI** — unlike `MediaLibraryGrid`'s optional `ShowVoting`/`EvidenceVoteWidget`, this bin is media-only, no vote controls or vote results.

> Requested 2026-08-06, right after the panel's drag/resize/fill-height crash was fixed (see item #6's follow-up above) — the user wants this done once that panel is confirmed working correctly, which it now is.

---

## 7. Drag-to-scrub playhead on audio file records ✅ Complete (2026-08-06)

On audio file playback/preview (e.g. `AudioFilePreview.razor`/`WaveSurferPlayer.razor`), let the user click-and-drag the mouse on the waveform's playhead to scrub through the audio, not just click-to-seek.

> **Shipped on `feature/drag-scrub-playhead`, merged to `develop`.** Click-and-drag on the full-view waveform now has two runtime-switchable modes via a toolbar toggle (icon-only, tooltip "Create Region"/"Scrub Playhead", defaults to region mode): region mode draws a selection region as before; scrub mode moves the playhead live and plays audio audibly while dragging (starts playback immediately if paused, restores the prior paused/playing state on release). Implemented as one unified pointer-event system in `WaveSurferPlayer.razor.js` rather than a separate playhead-only hit-test, so the two gestures never conflict. Also added an "Explore Region" action to the region right-click menu that opens the existing `WsRegionExplorer` for a freshly-drawn region — reusing its already-built playback/save-as-clip/notes panel (`UploadFileRegionNote`) wholesale rather than building new note infrastructure, once investigation surfaced that component already did exactly what "add a note to a region" needed.
>
> Two unrelated pre-existing bugs were found and fixed on `develop` while verifying this feature: a hard-navigation auth redirect (pages showed the login form on refresh even when signed in) and a missing `.AddAdditionalAssemblies()` call that 404'd every `Ben.Web.Library`-hosted page on a fresh page load.
