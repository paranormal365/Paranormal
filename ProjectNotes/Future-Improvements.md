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
> Still to test: preview scrubbing, mixed video+image timeline preview/export, volume automation UI, text overlays, callouts, clip art, transitions, motion keyframes, export dialog/queue, project save/open (device + server), subtitle export, error log panel, asset browser, remaining keyboard shortcuts. Also noted but not yet investigated: `ImageClip.Width`/`Height` are never populated on import, so image clips always render at their native resolution in Preview/Export instead of being scaled/padded to match the project's output resolution — fine for a single image matching project aspect ratio, but will look wrong once a mismatched-aspect image is mixed into a real project.

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

## 16. Rich text properties for text overlays and callout text — 🟡 Slice A complete (2026-08-08)

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
- **Still open:** Font selection from Google Fonts or another common free font library — needs its own
  design: the SVG-as-image rasterization path can't reliably fetch external `@font-face` resources, so
  fonts would likely need embedding as base64 data-URIs inside the SVG or pre-registering via the
  `FontFace` API.
- **Still open:** Font weight/bold, underline, subscript, superscript — ideally applicable *inline* while
  typing (mixed formatting within one text block), not just as a single style for the whole block. Forces
  a runs/spans data model; SVG `<tspan>` supports per-run styling natively.
- **Still open:** Direct in-preview editing — click into the text on the canvas and type/format it there,
  not only through a side-panel form. Requires first building a live text/callout rendering layer in the
  preview — today their real appearance only exists in exported output, not the editor preview.

> Requested 2026-08-09. Slice A (phase 74) shipped 2026-08-08.

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

## 25. Timeline snapping — expand/verify coverage (not started)

`Snapping` already exists as a feature flag and has real implementation (`_snapGuidePx` in
`VideoTimeline.razor`, a visible snap-guide line) — not a from-scratch feature. Noted as worth a fresh look:
confirm snap points cover the cases users actually want (clip edges, playhead, markers, other clips'
start/end) during the various drag interactions (clip reposition, trim, marker drag), and consider whether
coverage should expand. No specific gap identified yet — flagged for future investigation rather than a
known bug.

> Noted by the user 2026-08-08, no specific complaint yet — revisit and scope precisely before starting.

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

## 28. Callout/text overlay: drag-to-extend start/end on the timeline (not started)

Callouts and text overlays should support the same click-and-drag start/end trim behavior video/image clips
already have on the timeline — dragging the start edge (when there's room before it) or the end edge to
extend/shrink the item's duration, with a resize cursor (e.g. east-west/left-right arrow) shown when
hovering over an extendable edge to signal the interaction is available.

> Requested by the user 2026-08-08. Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).

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

## 30. Slider tick-label numbers bunch up and become unreadable (not started)

In many of the editor's Telerik sliders (Properties panels — position/size/corner-radius/fade/etc.), the
tick-label numbers under the track render bunched together and overlapping, unreadable at typical panel
widths. Needs a pass over all editor sliders, not just one — and per the user 2026-08-09, not a uniform
fix: some sliders (e.g. the video preview's scrub bar, fixed during item #36 phase 84) should just hide
labels entirely; others (Properties-panel sliders like position/size) may still find the numbers useful,
so each needs its own judgment call rather than one blanket treatment.

**Real fix confirmed 2026-08-09** (found via reflection on `Telerik.Blazor.dll` while fixing the preview
scrub bar) — do NOT reach for CSS hacks first, matching [[feedback_prefer_telerik_native_components]]:
`TelerikSlider<T>` inherits `TickPosition` (`SliderTickPosition`: `Before`/`After`/`Both`/`None`) and
`LabelTemplate` (`RenderFragment<T>`) from `Telerik.Blazor.Components.Common.TelerikSliderBase<TValue>` —
neither is in the component's own IntelliSense-visible declared members (only on the base class), which
is likely why this wasn't found earlier. `TickPosition="SliderTickPosition.None"` removes labels/ticks
entirely (used for the scrub bar); `LabelTemplate` lets a slider keep labels but reformat/thin them out
(e.g. round to fewer decimals, or only render the template for values that are multiples of `LargeStep`)
for the sliders where the user said numbers are still useful. Also note: `TelerikSlider`'s own rendered
root doesn't carry the Blazor CSS-isolation scope attribute (same as `TelerikWindow`/`TelerikTabStrip`
elsewhere in this codebase) — if a CSS approach is ever needed for something `TickPosition`/`LabelTemplate`
can't do, it must be rooted via `::deep` at a real scoped ancestor, not the slider's own class name.

> Noted by the user 2026-08-08, refined 2026-08-09. Lives in the separate Ben.Video.Editor repo
> (Github-BenVideo remote).

---

## 31. Callout text-inside parity, or link a text overlay + callout as one unit (not started)

Callouts gained basic centered text-inside support in phase 74 (item #16 slice A), but the fuller ask:
either make text-inside a first-class equal of standalone text overlays (fonts, alignment within the
shape, wrapping, etc.), **or** support linking a text overlay and a callout together into one logical
unit — if one moves, the other moves; if one fades, the other fades; if one resizes, the other resizes,
and so on. Linking may be the more powerful model (any shape + any text placement) but needs design:
a link id between track items, grouped selection/drag on the timeline and canvas, and shared
motion-path/fade application.

> Requested by the user 2026-08-08. Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).

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

## 34. `VolumeAutomationLane` JS-interop crash under rapid UI interaction (not started)

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

---

## 35. Direct on-canvas motion-keyframe editing — bezier handles, resize, type-in values, drag-to-move (not started)

Motion keyframes (position/scale/rotation/alpha/shadow, etc.) are currently edited only through the
Properties-panel form (sliders/number fields, add-keyframe-at-playhead button). Wanted: a true
on-canvas editing experience, similar to the Pen tool in Photoshop/Illustrator —
- Click-drag a keyframe's bezier handles directly on the motion path to shape the interpolation curve
  between two points, not just pick an easing preset.
- Resize the object directly on-canvas (drag a corner/edge handle) and have that recorded as the
  keyframe's scale value at the current time.
- Type a value directly (e.g. click a position/size number and edit it inline) instead of only via a
  slider.
- Drag the object itself to a new position on-canvas, with that becoming the keyframe's stored
  position — building on the on-canvas drag support already shipped for the *current frame* (item #11,
  phase 67) but extending it to keyframe-authoring specifically, not just live preview interaction.

Depends on/extends item #11 (preview on-canvas interactivity, shipped phase 67) and the existing
`MotionKeyframeEditor`/`MotionKeyframeService` (linear + bezier interpolation, easing curves already
exist server-side — this is about giving them a direct-manipulation UI). Needs design: how bezier
handles render/hit-test on the timeline-relative motion path overlaid on the preview canvas, and how
that interacts with the existing Properties-panel form (likely both should stay in sync, not replace
one another).

> Requested by the user 2026-08-09. Lives in the separate Ben.Video.Editor repo (Github-BenVideo
> remote).

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

## 38. Long-form project memory budget — e.g. three 20-minute 1080p clips (not started)

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

## 41. Properties panel should show live per-frame interpolated values, not just keyframe values (not started)

As the playhead moves or the video plays, values shown in the Properties panel (and elsewhere) for
an animated layer should update on a per-frame basis to reflect the actual interpolated value at
that point in the show — e.g. if a layer's X position animates from keyframe A to keyframe B, and
the playhead sits partway between them, the panel should show the true in-between value at that
frame (like "X = A+2"), not just the value at the nearest keyframe. Applies to any item currently
shown in the Properties panel that has animatable/keyframed properties (position, size, color,
shadow, etc. via the motion-keyframe system).

> Requested by the user 2026-08-09. Lives in the separate Ben.Video.Editor repo (Github-BenVideo remote).
