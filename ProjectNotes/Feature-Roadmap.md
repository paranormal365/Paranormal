# "Things to Add" Master Roadmap

*Planned 2026-08-14. Source: [Things to Add.md](Things%20to%20Add.md).*

## Context

`Things to Add.md` captures eight feature areas — notifications, profile images/client privacy, role-based help docs, a device-data JSON standard, audit-log paging, EVP detection in the audio editor, expanded group types, and a case-page/investigation overhaul. This roadmap turns that wish list into phased, buildable chunks with dependencies and priorities, informed by a full exploration pass over the existing code (what already exists vs. what's genuinely missing is called out per area).

Decisions made when this roadmap was written:
- **Deliverable**: one master roadmap; implementation happens area-by-area in later sessions.
- **Top priorities**: (1) Notifications & unread badges, (2) Audio EVP detection.
- **Documentation**: in-app help pages (per-role guides with an index), not standalone documents.
- **Data-provider standard**: spec document only for now; import features come later.
- **Client aliases**: public pages/reports only — org investigators always see the real name.
- **Group types**: many-to-many (a group can claim multiple types).
- **Private-photo-to-client sharing**: requires BOTH org policy AND individual opt-in.

## Areas

1. Notifications & message badges — TOP PRIORITY
2. Audio EVP detection & clip extraction — TOP PRIORITY
3. Audit log server-side filtering/paging (small — server side already exists; UI fix)
4. User images & client privacy (public/private photos, aliases, witness photos, richer occurrences)
5. Case page & investigations overhaul (original request display, permission-filtered timeline, investigator binders)
6. In-app help documentation (per-role guides)
7. Device data-provider JSON standard (spec document)
8. Group type expansion (UFO/Bigfoot/other paranormal — deliberately last)

Notable exploration findings that reshaped the plan:
- Audit-log server-side paging/filtering **already exists end-to-end** — only the grid wiring is broken (item shrinks to a UI fix).
- Investigation **RSVP already exists end-to-end** (member buttons, org attendee table) — that part of the doc is done; the binder is the real gap.
- Two **security holes** found in audio controllers (no source-file access check) — promoted to the first work item.
- The audio editor already has region-drag, save-as-clip with lineage, silence detection, and cached FFT — EVP detection is an increment, not a greenfield.

---

## Area 1 — Notifications & Unread Badges (TOP PRIORITY)

### ✅ SHIPPED 2026-08-14 — N1, N2, N3 (N4 still deferred)

Built as planned. What the plan didn't anticipate:

- **Two features turned out to be write-only, and this work made them readable.**
  `UserMessage`/`UserMessageTo` could be *sent* by the audit log's "send as message" but no
  recipient could ever read one — both existing controllers over those tables are SuperAdmin-only
  and return every row unfiltered. New `MyMessagesController` (`/api/me/messages`) is the
  recipient-scoped view that was missing. Separately, file-permission requests were submittable
  and reviewable by API but had no review UI anywhere; the notifications page now lists them with
  file/requester names joined server-side (`GET /api/me/permission-requests/pending`) and wires
  approve/deny to the existing review endpoint. The orphaned `_pendingRequestCount` in
  `MainLayout` is gone, replaced by the bell.
- **`NotificationState` lives in `Ben.Web.Library/Services`, not `Ben.Web.WebApp`** — the bell and
  the drawer both inject it, and library components can't reference the WebApp project.
  `IBenUserState` gained a `StateChanged` event so the service is self-contained (the concrete
  `WebApiTokenStore` already had one for `IWebApiTokenStore`).
- **No separate "Messages" nav entry.** A single "Notifications" entry pointing at the new
  `/notifications` page covers system messages and permission requests; org messages stay per-org
  and roll up under the existing "Organizations" item.
- **`ExecuteUpdateAsync` is unusable in this codebase's tests** — every controller test uses the EF
  InMemory provider, which doesn't support it. `MarkAllRead` loads and saves instead.
- Badge colour thresholds live in `NotificationBadge` (`Ben.Web.Library/Services`), shared by both
  badges. `DescribeAge` includes the word "ago" so a caller can't compose "just now ago" — which
  is exactly what the first version rendered live.

Commits: `c9e0c3e` (N1), `6827b13` (N2), `c05e35a` (N3). 45 new tests.

### What exists today (explored)

Three independent messaging systems, differing in read-state granularity:
- **`OrgMessage`** (internal org messaging, `Ben.Data.Source/Entities/BenDataModel.OrgMessage.cs`) — the only system with true per-user read state: `OrgMessageRecipient.DateRead` (null = unread). Channels: OrgBroadcast / DirectMessage / CaseTeam / PublicFeed. Mark-read happens only as a side effect of `GET /api/organizations/{orgId}/messages/{messageId}`. **No unread-count endpoint exists.** Notably, `/organizations/{orgId}/messages` has no nav-drawer entry today.
- **`CaseMessage`** (client↔org case dialogue) — read state is per-*side* (`IsReadByClient`/`IsReadByOrg` booleans on the row), not per-user. Org side has `GET .../messages/unread-count` per case; client side gets `UnreadMessageCount` inline on the case-detail response (already rendered as a red badge in `MyCaseDetail.razor`).
- **`UserMessage`/`UserMessageTo`** (legacy system messages; per-recipient `DateLastRead`) — written only by the Audit Log "send as message" feature; **has no UI anywhere**. A notification inbox is the natural first consumer.

Layout hooks: `MainLayout.razor` already has a fetch-on-auth-change pattern (`RefreshPendingRequestCountAsync` → `_pendingRequestCount`, currently **never rendered** — an orphaned counter to absorb into this work). `MainNavigationDrawer.razor` re-renders on every navigation (`LocationChanged` → `StateHasChanged`, `DrawerData` is a computed getter), so per-item counts refresh on nav for free; `NavItem` needs a `Count` field + a drawer `ItemTemplate`.

**No real-time infra exists** — no hubs, no timers, everything is fetch-on-load. WebApi is a separate process from the Blazor app, so a SignalR hub there needs `HubConnection` + bearer plumbing in the web app.

### Plan (4 phases)

**N1 — Aggregate unread-summary endpoint.** New `GET /api/me/notification-summary` on WebApi returning one DTO per bucket, each with count + oldest-unread timestamp (for age-based coloring):
- `OrgMessagesUnread` — per org: `OrgMessageRecipients` where `RecipientAppUserId == me && DateRead == null`
- `CaseMessagesUnreadAsOrg` — per org the user is an active member of: client-sent unread `CaseMessage`s (`!IsReadByOrg`) grouped by case
- `CaseMessagesUnreadAsClient` — the user's own cases with org-sent unread (`!IsReadByClient`)
- `SystemMessagesUnread` — `UserMessageTo` where `ToAppUserId == me && DateLastRead == null`
- `PendingPermissionRequests` — fold in the existing orphaned `_pendingRequestCount` source
Wire 4-layer client (`IBenAdminClient` → `BenAdminClientAdapter`). One round trip for the whole badge system.

**N2 — Circuit-scoped `NotificationState` service.** Lives in `Ben.Web.WebApp/Services`, registered scoped (per circuit, like `WebApiTokenStore`). Holds the latest summary, exposes `event Action Changed`. Refresh triggers: (a) `TokenStore.StateChanged` (login/logout/impersonation), (b) `NavManager.LocationChanged` (cheap, debounced — skip if last fetch < ~10s ago), (c) a `PeriodicTimer` poll every 60s while authenticated. Must await `AuthReady` before first fetch (established `WaitUntilAuthReadyAsync` pattern). SignalR push is deliberately deferred (phase N4).

**N3 — Badge UI.**
- Top bar: bell icon `AppBarSection` in `MainLayout.razor` next to user email, with total-unread badge; click opens a popover breakdown by bucket, each row navigating to the right page (org messages page, case Messages tab via the existing `?tab=` support, client case, etc.).
- Drawer: add `Count`/`Age` to `NavItem` + `ItemTemplate` on `TelerikDrawer` — badge on "My Cases" (client unread), "Organizations" (org + case unread rolled up), and a **new "Messages" nav item** for the org-messages page (fixing today's missing nav entry).
- Color by age of oldest unread: < 1 day = primary (blue), 1–3 days = warning (yellow), > 3 days = danger (red). Thresholds as constants in one place.
- New minimal inbox page for `UserMessage` system messages (its first-ever UI), linked from the bell breakdown.

**N4 (deferred) — Real-time push.** SignalR hub on WebApi + `HubConnection` in `NotificationState`, replacing the poll. Only worth it after N1–N3 prove the model; polling at 60s is fine for current user counts.

### Files (representative)
- New: `Ben.Data.WebApi/Controllers/NotificationSummaryController.cs` (or action on an existing "me" controller), `Ben.Service.Models/.../NotificationSummaryResponse.cs`, `Ben.Web.WebApp/Services/NotificationState.cs`, `Ben.Web.Library/Messaging/NotificationBell.razor`, system-message inbox page
- Modified: `IBenAdminClient.cs`, `BenAdminClientAdapter.cs`, `MainLayout.razor` (bell + remove orphaned counter), `MainNavigationDrawer.razor` (NavItem.Count + ItemTemplate + Messages entry)

---

## Area 2 — Audio EVP Detection & Clip Extraction (TOP PRIORITY)

### ✅ SHIPPED 2026-08-14 — E0 through E4 (complete)

Commits: `0b3ff72` (E0), `d1e7ea6` (E1a/b), `a3f21c8` (E1c), `f01578f` (detector + accuracy gate),
`eb9f89b` (E2a scan + MP3 fix), `64e313d` (label fix), `207fecb` (tolerance + review list),
`e029aca` (E3b adjust-bounds), `1f29480` (E4 clip). Suite 1408 passing.

**Where the plan was wrong, and what replaced it:**

- **The detector is C# server-side, not browser JS.** The plan chose JS because the AudioBuffer is
  already decoded there. But accuracy was the stated priority, and a detector that can only be
  eyeballed in a browser never gets tuned — one that runs against a fixture in milliseconds does.
  Server-side also matches how every other audio operation here already works. A 3m06s MP3 scans in
  **1.1s**, so an hour of tape is ~20s: comfortably synchronous, no background job needed.
- **Sensitivity is presets *plus* full fine-tuning**, per the user during the build. The three
  presets seed a complete `EvpDetectionOptions` (tolerance dB, shortest sound, merge gap, context
  padding, longest candidate) which is then adjustable; picking a preset reseeds everything so it
  stays a starting point rather than a separate mode. Ranges are enforced server-side too.
  Measured on real audio: 4dB→60 candidates, 6dB→21, 9dB→6, 12dB→5, 16dB→3.
- **Detection is manual and always was** — only the Scan button triggers it. Worth stating because
  it was queried directly.

**Two honest limits, both pinned by tests rather than left implicit:**

- A quiet event *underneath* louder speech in the same band cannot be separated out by an energy
  detector. The detector flags the stretch; a person listens. Isolating it needs spectral
  subtraction or source separation — a different tool.
- The accuracy fixture is synthetic (three-formant tones with a syllabic envelope). Passing proves
  the detector responds to voice-band energy above the floor and rejects the two things that most
  often flood this kind of detector — wideband transients and steady out-of-band tones. It is
  **not** proof against real investigation tape.

**The scoring genuinely discriminates**: a door slam at 10× the amplitude of a quiet utterance
scores 80.3 against its 93.7. Loudness alone would rank it top.

**⚠ Pre-existing bug found and fixed here, unrelated to EVP:** NAudio's `Mp3FileReader` defaults to
the ACM codec (`Msacm32.dll`, Windows-only), so **every server-side MP3 operation — audio edit,
clip, mix, and now scan — threw `DllNotFoundException` on macOS/Linux**. Confirmed pre-existing by
hitting the untouched audio-edit endpoint and getting the identical stack. It hid because WAV works
and WAV is what this app writes, so anything produced by an edit round-trips fine; only
originally-uploaded MP3s hit it. All four call sites now go through one `AudioSourceReader` using
NLayer's managed decoder.

**Deferred:** E5 (keyboard review shortcuts, FFT spectral-flatness refinement, batch-dismiss below
a score).

### What exists today (explored — unusually strong foundation)
- Region drag-to-create/drag/resize on the waveform (hand-rolled pointer handlers), right-click menu with **save-as-clip already built** (`POST /api/upload-files/{fileId}/clip` persists a child `UploadFile` with `RegionStart`/`RegionEnd` lineage), non-persisted clip preview endpoint, Region Explorer modal with notes.
- Client-side **silence detection** (RMS loop over the already-decoded AudioBuffer) — the exact inverse of a naive candidate detector; FFT frames already computed and cached client-side when the spectrogram is on; custom voice-band overlay exists.
- EVP **point** markers (`AudioMarker.TimeSeconds`, confidence levels Possible/Probable/Confirmed) with full CRUD; full non-destructive Web Audio enhancement chain (10-band EQ, HP/LP, compressor, gate); 8 destructive server-side NAudio edits.
- Gaps: no candidate detector; `AudioMarker` has no `EndSeconds` (spans have no home); one-user-region-at-a-time rule; `OnRegionUpdated` not subscribed (stale bounds after resize); no pre/post context padding on clip creation; **two security holes found — `UploadFileAudioEditController` and `AudioMarkerController` lack the `FileAudienceAccess` source-file check that the clip controller has** (any authenticated user can edit/mark any file by id).

### Design decisions (from dedicated design pass)

**Detection algorithm — client-side, time-domain, band-passed RMS with adaptive noise floor.** No FFT dependency (spectrogram frames only exist when that toggle is on; a detector shouldn't change behavior with a UI mode). Two passes over the already-decoded AudioBuffer, in `WaveSurferPlayer.razor.js`:
- *Pass 1 (chunked/async, 30 s slices with progress + cancel, no worker — in-place reads beat copying ~600 MB/h):* mono mixdown → Butterworth HPF 300 Hz + LPF 3400 Hz biquads (voice band, filter state carried across chunks) → 25 ms window / 10 ms hop → store `bandDb[]` + `fullDb[]` envelopes (~1.4 MB/hour each).
- *Pass 2 (instant, over envelopes):* adaptive floor = **20th percentile** of `bandDb` over a centered 10 s sliding window (histogram-based, O(1)/frame) — 20th, not median, so sustained speech doesn't drag the floor up, which is what lets barely-above-floor EVPs surface without flooding noisy files. Hysteresis gate: active at `floor + onsetDelta`, release 2 dB lower. **One exposed knob**: sensitivity High=4 dB / Medium=6 dB (default) / Low=9 dB. Runs → events: merge gaps < 0.35 s, pad ±0.05 s, discard < 0.15 s, events > 10 s kept but score-penalized (likely investigator speech — user decides), dedupe against existing Confirmed/Dismissed spans, cap 500 highest-scoring.
- *Honest scoring, never "EVP probability"*: signal score 0–100 = 0.5·prominence (dB above floor) + 0.3·band-ratio (voice-band vs full-band energy — separates voice from clicks/rumble) + 0.2·duration-fit (ideal 0.2–3 s). Deterministic: same buffer + sensitivity ⇒ identical candidates.

**Schema — extend `AudioMarker`, no new entity.** Additive migration: `EndSeconds double?` (null = legacy point markers), `IsAutoDetected`, `DetectionScore float?`, `ReviewStatus` (new enum Pending/Confirmed/Dismissed, **default Confirmed** so existing manual markers need zero backfill), `LinkedClipUploadFileId Guid?`. Confirming a candidate is a field update, not a cross-table copy — the entire existing 4-layer wiring, audit, panel UI, and tests are reused.

**Review UX — persisted candidates, tabbed panel.** Candidates bulk-POSTed after scan (review spans sessions; Dismissed must persist for dedupe). Re-scan deletes only that file's *Pending* auto-markers. Rendering: candidates are programmatic regions (never fight the one-user-region rule) — Pending amber with score-scaled opacity, Dismissed behind a toggle. New "Candidates" tab on the EVP panel: sort by time/score, per-row Play (span + 0.5 s context loop), Go-to (new `zoomToRange` JS helper), Confirm dialog (label/confidence/note + "create clip now"), Dismiss, and **Adjust-bounds** — promotes exactly one region to draggable via `region.setOptions({drag:true,resize:true})` (verified supported by the vendored plugin), edits flow through the newly-subscribed `OnRegionUpdated`, save PUTs bounds and demotes.

**Clip with context padding — client-side over the existing clip endpoint.** Pre/post-seconds inputs (default 1.0 s) bidirectionally synced with a live draggable padded region, so submitted bounds are exactly what's on screen. Preview plays through the existing Web Audio enhancement chain (satisfies "toggle/adjust enhancements" at listening time). Baking into the file: only **Normalize** (add `bool Normalize` + `Guid? SourceMarkerId` to `ClipAudioRequest`; peak-normalize −1 dBFS in `AudioClipper`; controller links the clip to the marker atomically). Baking EQ/gate server-side is explicitly deferred — no server EQ exists, and `ParentFileId`/`RegionStart`/`RegionEnd` lineage means clips can always be re-cut losslessly.

### Phases (each: build + xunit tests + live browser verify)
- **E0 — Security + correctness prerequisites ✅ SHIPPED 2026-08-14** (commit `0b3ff72`). `FileAudienceAccess.CanViewFileAsync` on every action of `UploadFileAudioEditController` and `AudioMarkerController`, plus author-or-file-owner moderation on marker update/delete; 11 new tests; verified live (owner 200 / unrelated 403 / public file still readable).
  **Discovery while verifying:** `OnRegionUpdated` was not the whole story — the container's capture-phase `pointerdown` handler in `WaveSurferPlayer.razor.js` claimed *every* drag and called `setPointerCapture`, so the regions plugin never saw handle drags. **Region move/resize were unreachable by mouse entirely** (grabbing an edge silently drew a new region), which is why stale bounds had never been reported. Fixed by skipping drags that begin on a region body/handle (via `composedPath`, since regions live in the waveform's shadow DOM). **This was a hard prerequisite for E3's Adjust-bounds, which assumed edge-dragging worked.**
  **Deferred, noted:** `UploadFileVoteController` (`api/upload-files/{fileId}/votes`) also has no `CanViewFileAsync` check, but it exposes only aggregate vote counts (no file content) and belongs to the orphaned `UploadFileVote` system — `UploadFileVoteBar.razor` is rendered by no live page; `EvidenceVote` is the live one. Worth a guard (or deletion) when that system is next touched.
- **E1 — Schema + API**: entity fields + enum + migration; DTOs (`EndSeconds`, `BulkCreateAudioCandidatesRequest`, `ReviewAudioMarkerRequest`); endpoints `POST .../audio-markers/candidates` (transactional replace-Pending, cap, validation, audited) + `PUT .../audio-markers/{id}/review`; 4-layer wiring; span-aware marker regions.
- **E2 — Detector + scan UI**: `detectEvpCandidates` JS + `zoomToRange`; Scan button + sensitivity dropdown + progress/cancel; bulk-save + overlay. **Accuracy gate**: seeded test fixture (room tone + 3 quiet speech snippets at known offsets + clap + hum) — snippets found within ±0.1 s at Medium, clap scores low, hum doesn't flood; 1 h file scans < ~10 s with responsive UI.
- **E3 — Review workflow**: Candidates tab with Play/Go-to/Confirm/Dismiss/Adjust; reload persistence; re-scan doesn't resurrect dismissed.
- **E4 — Clip from confirmed EVP**: padding dialog with live synced region, enhancement-chain preview, `Normalize`/`SourceMarkerId` on the clip request, clip chip on marker rows.
- **E5 (optional)**: keyboard review (n/p/c/d), FFT spectral-flatness score refinement, batch-dismiss below score.

Critical files: `WaveSurferPlayer.razor.js`, `AudioFilePreview.razor`, `AudioMarkerController.cs`, `AudioMarkerRecord.cs`, `UploadFileAudioClipController.cs`, `UploadFileAudioEditController.cs`.

---

## Area 3 — Audit Log Server-Side Filtering/Paging (SMALL — mostly already built)

### What exists today (explored)
The server side is **fully implemented already**: `AdminAuditLogController` (`GET /api/admin/audit-logs`) accepts `page`, `pageSize` (clamped 1–200), `entityType`, `action`, `userId`, `dateFrom`, `dateTo`; runs a filtered, ordered, `Skip/Take` query; returns items + total count. Client wiring exists end-to-end.

The **UI is what's broken** (`Ben.Web.Library/SuperAdmin/AdminAuditLog.razor`):
- Grid is bound `Data="@_items"` with `Pageable=true` but **no `OnRead` handler and no `TotalCount`** — Telerik sees 25 rows as the entire dataset; `_currentPage` is never changed by anything, so pages 2+ are unreachable.
- The grid's `FilterMode=FilterRow` and `Sortable` operate on the 25 in-memory rows only, silently contradicting the server filter bar.
- Excel/CSV export exports only the fetched page.
- Date filters are sent without UTC normalization (`TelerikDatePicker` gives Unspecified-kind local dates; `OccurredAt` is UTC) — off by the viewer's UTC offset.
- The `userId` server filter has no UI.

### ✅ SHIPPED 2026-08-14 (commit `1c20c4a`)
Live-verified: pager reads "1 - 25 of 37", page 2 returns "26 - 37 of 37" with different rows and a matching server query per page turn; User filter narrows to 2 records; date boundary measured against the API — a "To = Aug 11" filter returned **10** records before the fix and **31** after (21 silently dropped).

Two extra traps found while wiring it, both fixed:
- **The grid never issues its first `OnRead` during static SSR prerender, and does not re-issue on hydration** — it sat permanently empty ("0 - 0 of 0") despite rows existing. Needs an explicit `Rebind()` once interactive. Worth remembering for any future `OnRead` grid in this codebase.
- Applying a filter via `Rebind()` re-reads *the current page*, so narrowing a filter from page 2 could land past the end of the new result set. Must reset page via the grid's own state (`GetState`/`SetStateAsync`).

Also: `TelerikComboBox` silently dropped its selection and snapped back to placeholder with a `Guid?` value + `Guid` ValueField; `TelerikDropDownList` with the identical shape (as used by the two filters beside it) binds correctly. And `ClearButton` is not a ComboBox parameter — the real name is `ShowClearButton` (found by reflecting over `Telerik.Blazor.dll`, same technique as the Image Editor fix).

### Original plan (1 phase)
- Switch the grid to Telerik's `OnRead` pattern: map `GridReadEventArgs.Request.Page/PageSize` to the existing endpoint, set `args.Data` + `args.Total`. Remove the in-memory FilterRow (keep the purpose-built server filter bar) or translate `Request.Filters` — recommend **remove FilterRow, keep the filter bar** (simpler, no ambiguity).
- Add a sort parameter to the controller only if sorting matters; otherwise drop `Sortable` (ordering is already newest-first, which is what an audit log wants). Recommend drop.
- Normalize `dateFrom`/`dateTo` to UTC day boundaries before sending (`dateTo` → end-of-day).
- Add a user filter to the bar (reuse `UserNameLink`-style lookup or a simple email search) since the endpoint already supports it.
- Fix the "send as message" dialog's user search to not fetch the full user table per keystroke (debounce + cache one fetch, or a server search endpoint — cache is enough at current user counts).

Files: `AdminAuditLog.razor` (main), possibly `AdminAuditLogController.cs` (only if adding sort).

---

## Area 4 — User Images & Client Privacy

### What exists today (explored)
- **`AppUser` has no image fields at all**, and — bigger — **there is no self-service profile page of any kind**. A logged-in non-admin user can edit *nothing* about themselves after signup (everything lives behind SuperAdmin-only `/admin/users/{id}`; the top bar shows only email + Sign Out).
- **The storage layer is already ready**: `UploadFile` (disk-authoritative via `IFileStorageService`), `UploadFileShare` with `ShareTargetType.Person`, and `FileAudienceAccess.CanViewFileAsync` already express "private image visible to specific people." The **`OrganizationLogo` flow is the exact pattern to copy** — a thin join entity (`OrganizationId, UploadFileId, AltText, IsActive`) + CRUD controller + upload-or-pick dialog in the CMS editor; file type seeded in `UploadFileTypeSeeder` (a `"ProfilePhoto"` type is a one-line addition).
- `UserNameLink.razor` (the shared name-rendering component) receives `DisplayName` as a parameter — it does **no lookup** — and is only adopted in 4 call sites; many pages render names directly.
- **Co-clients are fully built** (`CaseClientAccess`, `CaseClientInvite` with email invites, `/invite/{token}` accept page).
- `CaseRelatedPerson` (witnesses): `Name/Age/Relationship/LivesAtProperty/Notes` — **no photo field, and no update (PUT) endpoint**.
- **Client anonymity**: only `Case.PublicPseudonym` exists — org-set, used only on public pages (the public API never emits a real client name; it emits pseudonym-or-null). No client-controlled alias, nothing for co-clients/witnesses, no anonymization on timeline-entry authors.
- **Occurrences**: `LogOccurrenceRequest` is just `(EventDateTime?, Title?, Body?)`; files (image/audio/video/pdf/doc) attach only *after* creation; no ExperienceType tagging from the client side (the join table exists, org side uses it); `Body` is stored/rendered as HTML but authored in a plain textarea.

### Plan (phases U1–U6)

**U1 — `AppUserPhoto` entity + self-service photo API.** Copy the `OrganizationLogo` shape: `{ Id, AppUserId, UploadFileId, AltText, IsPublic (public vs private slot), IsActive, audit }`. One active photo per (user, IsPublic) slot. Seed `"ProfilePhoto"` `UploadFileType` (image extension allowlist, like Logo). New `MyProfileController` (`/api/me/profile`, `/api/me/photos`) — upload/pick/activate/delete, following `OrganizationLogoController`. The uploaded `UploadFile` for the *public* photo gets `IsPublic = true`; the *private* photo stays private and is served through a resolution endpoint (U3), not direct file access.

**U2 — Self-service profile page (`/profile`).** The first user-facing profile editor: DisplayName edit, public photo, private photo, and a personal opt-in toggle "show my private photo to clients I work with". **DECIDED: client-sharing requires BOTH the org policy and the individual opt-in** — so org Settings also gains "allow members' private photos to be shown to clients" (org-level policy). Add a user menu to `MainLayout`'s top bar (avatar thumbnail + dropdown: Profile, Sign Out) — replacing the bare email text. This page is also where future self-service settings accumulate.

**U2-cam — Take a photo with the device camera (requested 2026-08-14, deferred).** The user wants
the private profile picture settable by taking a photo then and there, not only by uploading a file
someone already has. Applies to the private slot in particular — a candid taken on the spot is
exactly the kind of image you'd share with colleagues and not the public page. Notes for whoever
builds it:

- **DECIDED: use the native camera where the device offers it.** `<input type="file"
  accept="image/*" capture="user">` hands off to the phone's own camera app — no permissions
  plumbing, no JS interop, and the user gets the camera UI they already know. The in-page
  `getUserMedia` → `<video>` → `<canvas>` → `toBlob` route is only worth building if desktop
  capture turns out to matter, since on desktop the `capture` attribute is ignored and the control
  falls back to a plain file picker. Start native; treat getUserMedia as a later add, not phase one.
- `getUserMedia` is **secure-context only** — it works on `localhost` but silently fails over plain
  HTTP to a LAN address, so testing from a phone against the dev server needs HTTPS or a tunnel.
- Output is a `Blob`/`byte[]`, so it can go through the existing upload endpoint unchanged; the
  Profile Photo `UploadFileType` already allows `.jpg`/`.png`/`.webp`. No schema work.
- Verification will be awkward: the browser tooling used here cannot drive OS file pickers or
  synthesise a camera stream, so this needs either a fake-device browser flag or manual testing on
  a real phone. Budget for that rather than discovering it late.

**Co-clients are not private from each other (decided 2026-08-15).** Within a single case, the
client and their co-clients have no need for privacy between themselves. They were invited onto
the case as participants in the same events, they already read each other's occurrences and
messages, and treating them as strangers would be a fiction. So: no consent flags, no aliasing,
and no photo gating *between people on the same case*. This says nothing about the org boundary or
the public boundary, which keep their own rules.

⚠ **This is currently a gap, not just a plan.** `UserAvatarController.MaySeePrivatePhotoAsync`
has four routes — self, shared org membership, member→client (two keys), and client→org-member —
and none of them fire when both parties are clients on the same case with no org membership
between them. Two co-clients therefore see only each other's *public* photo today. Closing it is
one more route in that method (shared case via `ClientRequest.AppUserId` or `CaseClientAccess`),
plus tests; the method is already shaped as a flat list of independent routes for exactly this
kind of addition.

**U3 — Avatar resolution + rendering.** New endpoint `GET /api/users/{id}/avatar` that picks which photo the *viewer* may see: private photo if (viewer shares an active org membership with the subject) OR (viewer is a client with a case at an org where the subject is a member AND that org's policy allows it AND the subject opted in) — else public photo — else null (render initials fallback). New `UserAvatar.razor` component (initials fallback, size parameter) + integrate into `UserNameLink`. Because `UserNameLink` does no lookup, add a small circuit-scoped avatar-URL cache service rather than threading file ids through every DTO. Adopt in the 4 existing `UserNameLink` sites + message threads + member lists.

**U4 — Client private-photo sharing to case orgs.** Per the doc: clients share their private image with a group while they have a case with it; co-clients likewise. This is just an extra clause in U3's resolution logic (subject has `Case→ClientRequest.AppUserId` or `CaseClientAccess` row at viewer's org) — no new tables.

**U5 — Witness photos + related-person editing.** Add `UploadFileId?` to `CaseRelatedPerson` (+ migration), add the missing PUT endpoint, extend the client's "Add a Person" dialog with an optional photo upload (Evidence-type file, case-scoped storage) and edit support.

**U6 — Client anonymity aliases.** Client-controlled per-case alias replacing their name in case displays: new nullable `DisplayAlias` on `CaseClientAccess` (co-clients) + a `ClientDisplayAlias` on `Case` (primary client), settable by the client from `MyCaseDetail` with suggested conventions (Witness A/B…, "the father", "a neighbor", custom). **DECIDED: aliases apply to public pages, reports, and shared documents only — org investigators always see the real name.** So substitution happens only in the public endpoints (`PublicCaseController`/`PublicCaseDiscoveryController`) and in `ReportBuilder` output; org-side endpoints are untouched. `Case.PublicPseudonym` stays as the org-side fallback lever; the client's alias supersedes it when set. Also extend aliasing to `CaseRelatedPerson` (a per-person `PublicAlias`) so witnesses named in public timeline content get the same treatment.

**U-Occ (occurrence enrichment, small).** Attach files at creation time (single dialog), optional ExperienceType tags for client entries (join table already exists), optional witness (`CaseRelatedPerson`) linkage per occurrence. Keep the plain textarea unless the user wants rich text (sanitization story must be checked first — Body renders as raw `MarkupString` today).

---

## Area 5 — Case Page & Investigations Overhaul

### ✅ SHIPPED 2026-08-14 — C1 through C4 (complete)

Built as planned. What the plan didn't anticipate:

- **The timeline assumed one entry per moment, and that assumption was wrong.** Raised by the
  user: several people can report the same event, and unrelated events can share a minute. All
  six sort sites keyed on `EventDateTime ?? DateCreated` alone, leaving ties in unspecified
  provider order — a timeline that reshuffles between page loads can't be cited. Every site now
  breaks ties on `DateCreated` then `Id`, and the org timeline labels tied entries "N of M at
  this time", deliberately neutral about whether they are one event or a coincidence.
- **Telerik dropdowns silently discarded their selection**, under both `@bind-Value` and explicit
  `Value`/`ValueChanged`. The C3 binder type picker saved "Note" whatever you chose, and the C2
  visibility picker had the same defect. Both are plain `<select>` now. Only caught by inspecting
  what persisted — the control looked correct on screen.
- **Deleting an investigation would have taken its binder entries with it.** SQL Server rejects
  `SetNull` on that FK (error 1785, multiple cascade paths), so the FK is `NoAction` and
  `InvestigationController.Delete` detaches entries explicitly. Observations must outlive the
  calendar event that produced them.
- **C4's bucket can't be coloured by urgency.** `NotificationBadge` classifies on
  `now - timestamp`, so a scheduled date would read as negative age and stay Fresh forever. The
  bucket uses the invite's own date like every other bucket, and the invite row leads the popover
  instead of relying on colour to convey imminence.
- **A badge that doesn't clear when you act on it reads as a failed save.** `NotificationState`
  only refreshed on navigation or the 60s poll, so answering an RSVP left the count stale while
  the user watched. `MyInvestigations` now forces a refresh after saving. The server count was
  right the whole time — this was only visible live.

### What exists today (explored)
- **Original request**: `Case.ClientRequestId` FK exists, and `AcceptClientRequest` snapshots the request's Description/address onto the case — but the case Description is then freely editable, so it diverges. The Overview tab renders only the literal words "Client Request" as the Source — no link, no content. **No endpoint lets an org member read the originating `ClientRequest`** (`GET api/client-requests/{id}` is owner-or-SuperAdmin only), and `ClientRequestFile` attachments are never carried to the case.
- **Timeline**: `CaseTimelineEntry` has exactly one visibility flag — binary `IsPublic` (public case page or not). No org-vs-client tiering; any active org member sees every entry. Entry types: ClientReport / InvestigatorNote / Evidence / ResearchNote. Public read path already filters `IsPublic` at the query level.
- **Investigations / RSVP — already built end-to-end**: `InvestigationAttendee` carries `AssignedRole` (free text), `Rsvp` (Invited/Accepted/Declined/Tentative), `DidAttend`. Members RSVP from `MyInvestigations.razor` (three buttons → `PUT api/my-investigations/{attendeeId}/rsvp`); orgs manage attendees + attendance in `InvestigationPanel.razor`. Client date negotiation (`InvestigationScheduleProposal`) also exists.
- **Investigator binder — nothing exists.** No per-investigator notes/findings entity, no `InvestigationId` on `CaseTimelineEntry`, no file-attachment table on `Investigation` at all. Only `Investigation.Notes` — a single shared HTML blob for the whole team.
- Public/private for cases: two-gate model (`IsPublic` && status Public/Haunted), pseudonym-or-null client name — solid, no changes needed.

### Plan (phases C1–C4)

**C1 — Original request on the case page.** New org-scoped endpoint `GET /api/organizations/{orgId}/cases/{caseId}/client-request` (gated: active org member + case belongs to org + `ClientRequestId` set) returning a purpose-built DTO: submitted date, Description (HTML), city/state, gender/birth-year, and the request's attached files (ids + names, downloadable via existing file endpoints — display from the request rather than copying into `CaseFile`, avoiding duplication). Overview tab gets an "Original Request" card rendering it, clearly distinct from the editable case Description ("as submitted by the client on {date}"). 4-layer client wiring.

**C2 — Tiered timeline visibility.** Replace binary `IsPublic` with `Visibility` enum: `OrgOnly = 0`, `Client = 1`, `Public = 2` (each level visible to the previous audiences too; org members always see everything). Migration backfills `true→Public`, `false→OrgOnly`. Update: the add/edit entry form (dropdown replaces checkbox), the public query predicate, and — the new capability — the client's `MyCaseDetail` timeline now shows org-authored entries marked `Client` or `Public` (today clients only see their own occurrences). This is the doc's "filtered by what is public and private and what the viewer has permission to see."

**C3 — Investigator binder.** Reuse the timeline rather than inventing a parallel store: add nullable `InvestigationId` FK to `CaseTimelineEntry` (+ migration), and a new entry type `InstrumentReading = 4`. A binder is then *the set of timeline entries scoped to an investigation*, attributed by the existing `AuthorAppUserId`, with files via the existing `CaseTimelineEntryFile` join:
- **Binder page/panel** (from `InvestigationPanel` and from `MyInvestigations` cards): entries for that investigation grouped by investigator, with "My entries" filter; add-entry form (note / instrument reading / evidence upload) preset to the investigation.
- Case managers/investigation leads see everything automatically (org members already read all entries — no new permission work).
- Entries flow into the main case timeline for free, satisfying "timeline shows when investigations occurred"; timeline gets an investigation filter chip.
- Investigation-scoped requests to investigators ("verify this / get me a reading"): defer to the existing `OrgMessage` CaseTeam channel; note as a later enhancement rather than a new entity.
- The future device-data JSON standard (Area 7) lands as `InstrumentReading` entries when import is eventually built — the schema slot is ready.

**C4 — Notification tie-ins (depends on Area 1).** Pending investigation invites (`Rsvp == Invited`, future-dated) join the notification summary; badge on "My Investigations" drawer item. Optional: evidence-due-date reminders.

---

## Area 6 — In-App Help Documentation (per-role guides)

DECIDED: **in-app help pages**, not standalone documents.

### Plan
- New `/help` section in `Ben.Web.WebApp`: an index page routing by role (Client, Group Owner, Group Member, Investigator, SuperAdmin) + one guide page per role, each with a per-section table of contents. Static Razor content (versioned with the app, no CMS) — the existing `OrganizationPage` CMS is org-scoped and wrong for this.
- Screenshots captured from the live dev site (the browser tooling used for validation passes can produce these), stored under `wwwroot/help/…`, referenced with light/dark-appropriate styling.
- Contextual entry points: a "Help" item in the nav drawer + `?` links on complex pages (case detail, audio editor) deep-linking to the relevant guide anchor.
- Content is written per-role by walking each journey (the recently completed end-to-end validation pass is effectively the outline: public discovery → client request wizard → case lifecycle → org management → media tools → admin).
- Maintenance rule: any session that ships a user-visible feature updates the relevant guide section (add to project conventions).

---

## Area 7 — Device Data-Provider JSON Standard (spec document)

DECIDED: **spec document only**; import features come later.

### Plan
- A versioned spec document at `ProjectNotes/specs/DeviceDataFormat-v1.md` (later publishable): 
  - **Preamble block** (required): format version, device manufacturer, model, serial number, firmware; session block: device power-on time, session start time, battery level at start, operator-entered location tag (free text + optional structured property-area tag), and the **trigger model** — how readings originate: `interval` (period), `event` (what movement/threshold triggers a record, debounce), or `hybrid` (event + heartbeat interval).
  - **Reading records** (array): ISO-8601 timestamp with explicit precision field, then all-nullable measurement fields: value(s) with unit, GPS lat/lng/elevation, movement vector, heading, plus an extensible `measurements` map for device-specific channels (EMF µT, temperature, humidity, audio-file reference, etc.). Nulls always permitted — devices report what they can.
  - **Conventions**: UTC timestamps, snake_case keys, semver format version, forward-compatibility rule (consumers ignore unknown keys), and a minimal JSON Schema file for validation.
- Deliverable includes 2–3 worked examples (EM meter with audio, motion-triggered temperature logger) so third parties can adopt without guessing.
- Future (not planned now): upload/attach flow onto investigations, parsing into timeline readings.

---

## Area 8 — Group Type Expansion (UFO / Bigfoot / other paranormal)

### What exists today (explored)
No `OrganizationType` concept exists anywhere — orgs are differentiated only geographically (`OrganizationAreaOfOperation`) and by accepting-flags. Two ready-made patterns to ride on:
- The **Lookup Types system**: 11 identical-schema lookup tables driven by one generic admin page; a new family costs a new entity + two one-liner controllers + mapper + **one line** in the `_families` array in `AdminLookupTypes.razor`.
- The **Experience Taxonomy** pattern (`ExperienceCategory`): adds an org-proposable/SuperAdmin-approval workflow + icon/color, global to the platform.

### Plan (deferred until the paranormal feature set stabilizes, per the doc's own "when we nail down all aspects" framing)
- `OrganizationType` as a standard lookup family (Name/Description/IconClass/ColorClass/IsActive/IsPublic/SortOrder) — SuperAdmin-curated initially; upgrade to the approval-workflow shape only if orgs should propose niche types.
- **DECIDED: many-to-many** — `OrganizationOrganizationType` join; a group can be both "Paranormal" and "UFO" and appears under every type it claims.
- Org self-selects its types in org Settings; type badges (icon+color) on `OrgDiscovery` results and org public pages; a type facet filter on `/find` and (later) `PublicCaseDiscovery`.
- Seed: Paranormal/Ghost, UFO/UAP, USO (unidentified submerged objects), Cryptid/Bigfoot, Other.

---

## Sequencing & Dependencies

| Order | Work | Size | Depends on |
|---|---|---|---|
| ~~1~~ | ~~Audio security fixes (E0)~~ ✅ **shipped 2026-08-14** | XS→S | — |
| ~~2~~ | ~~Audit log grid fix (Area 3)~~ ✅ **shipped 2026-08-14** | XS→S | — |
| ~~3~~ | ~~Notifications N1–N3 (Area 1)~~ ✅ **shipped 2026-08-14** (N4 push still deferred) | M | — |
| ~~4~~ | ~~EVP detection E1–E4 (Area 2)~~ ✅ **shipped 2026-08-14** (E5 deferred) | L | E0 |
| 5 | Case page C1 (original request) | S | — |
| 6 | Timeline visibility C2 | M | — |
| 7 | Investigator binder C3 + C4 | M–L | C2 (visibility), Area 1 (badges) |
| 8 | Profile photos U1–U4 (Area 4) | M | — |
| 9 | Witness photos + aliases U5–U6, occurrence enrichment | M | U1–U3 |
| 10 | Device data spec (Area 7) | S (document) | — |
| 11 | Help docs (Area 6) | M (content-heavy) | after features settle |
| 12 | Group types (Area 8) | S–M | deliberately last |

Items 1–2 are quick wins that could open any working session. Items 5–6 can interleave with 3–4 freely (no shared files beyond DTO additions). Once approved, copy this roadmap into `ProjectNotes/` (house convention: planning docs live in the repo) and add a pointer from `Things to Add.md`.

## Verification approach (applies to every implementation session)

Per established house practice for this repo:
- `dotnet build Ben.slnx` clean (currently 0 warnings — keep it that way) + full `Ben.Web.Tests` suite after each phase.
- EF migrations: `dotnet ef migrations add <Name> --project Ben.Data.Source --startup-project Ben.Data.Source` then `database update`; verify against the dedicated SQL Server (192.168.1.71), never Docker.
- Live browser verification against seeded dev data (WebApi :5252, WebApp :5078; seeded users daniel.park@benco.dev / sarah.mitchell@benco.dev / haveben@msn.com) — hard-navigation checks included (AuthReady pattern), restart Kestrel by PID.
- New auth-gated pages/components follow the `WaitUntilAuthReadyAsync` pattern in **both** `OnInitializedAsync` and any `OnAfterRenderAsync` data fetch.
- Every new mutation endpoint gets `IAuditLogService` auditing + xunit controller tests (EF InMemory, 403 cases included).
- Area-specific gates: EVP detector accuracy fixture (E2), notification badge live-update check (two browser sessions, send a message in one, badge updates in the other within one poll interval).
