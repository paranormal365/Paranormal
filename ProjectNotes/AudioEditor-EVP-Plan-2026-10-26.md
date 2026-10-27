# Audio Editor Page, EVP Scanning of Files and .ben Sessions

## Context

Ben asked (10/26/2026) for three things in one plan:

1. The **audio editor** reviewed visually and for gaps, its buttons and menus regrouped (most-used on the
   bar, the rest in logical dropdowns and settings), styled with the Signal theme, interactive and
   feature-rich, with much more visual analysis and every scan setting tweakable. **Bug fixes are in scope.**
2. **Scanning files for EVPs** — tunable, visual, batchable.
3. **Scanning committed `.ben` field sessions** for EVPs without modifying the `.ben`: markers, pulling audio
   out as a saved EVP, by the owner or an authorized person (e.g. an organizer for a session submitted to
   their event).

### Decisions Ben made in this planning session (don't re-ask)
- The editor becomes **its own page** with routes; compact players keep a quick-look popup.
- AAC decoding goes through **ffmpeg**: production already has it (`MediaTools:FfmpegPath`, set by
  `scripts/deploy-ishaunted.ps1:645`; the feed's video screening depends on it).
- **Who may scan a session:** the owner; the hosted event's team when the session was submitted to that
  event; active members of the group behind its investigation / calendar event; the **claimant of a public
  place** the session was published to. Public and share-link viewers never.
- **Separate copies:** each scanner (a person, or a group) works on their own copy of the clip with their own
  markers; nobody sees anyone else's. The `.ben` is never opened for writing.
- **A group's copy belongs to the group**: stored under the group's files, visible to active members and
  confirmed event staff, governed by the group's plan, not a person's 2 GB cap.
- Scans run as a **background job** with a non-blocking percent indicator in the editor and the waveform
  drawn **semi-transparent where not yet scanned, turning solid** as each stretch completes.
- Keyboard: **K keeps, no J/K/L shuttle** (arrows step).

## Verified starting point (code read 10/26/2026)

- Editor = `Ben.Web.Website.Library/Manage/Audio/AudioFilePreview.razor` (2594 lines, no code-behind): a
  compact 80 px player **and** a `<BenModal Size="fullscreen">` (:89–645) holding the whole editor plus every
  dialog. No route. Opened by right-click → "Open Full View" from CaseFiles, `/media-library`
  (`Media/MediaLibraryGrid.razor:415`), AdminUserDetail, RequestReview, BenContentPicker, `Kit/BenAudioTile`.
  `WaveSurferPlayer.razor(.js, 1722 lines)` wraps vendored WaveSurfer 7.12.11 (`wwwroot/js/wavesurfer/`),
  custom spectrogram workers, Web Audio listening chain. `WsRegionExplorer.razor` is the explore modal.
  EVP lab panel is Signal-styled; EQ/Edit/Clips panels are plain Bootstrap with inline styles. Icons are
  the Feather sprite via `Kit/BenIcon` (197 Library files); emoji remain in transport/edit/menus.
- Telerik 15.1.0; `TelerikToolBar` precedent in `Ben.Video.Editor/Components/Toolbar.razor:10`;
  `telerik-signal.css` has no `.k-toolbar/.k-dropdown-button/.k-split-button/.k-slider` rules yet.
  `.ben-editor-page` (`signal.css:364–367`) hides banner + rail; used by `CaseVideoEditorPage.razor`.
- Detector `Ben.Data.WebApi/Services/Audio/EvpDetector.cs` Version 1 (pinned scores test). Options
  `Ben.Service.Models/Entities/EvpDetectionOptions.cs` (5 fields; presets only change ThresholdDb).
  Scan = one sync `POST api/upload-files/{fileId}/audio-markers/scan` (`AudioMarkerController.cs:286`),
  `ReplaceCandidates` :199, learning writes `TryRecordScanAsync` :353 / `TryRecordRulingAsync` :417.
  `EvpScans/EvpScanCandidates/EvpRulings` are written, never read; no misses; no admin view.
- Decoder `AudioSourceReader.cs:71–89` reads MP3 (NLayer) and WAV only. Session audio is AAC `.m4a`
  (`audio/mp4`), and the phone writes `moov` after `mdat` (`MediaDuration.cs:20`) → ffmpeg needs a file path,
  not `pipe:0`. `AvMetadataStripper.cs:85–107` already copies to a temp file for the same reason.
- `.ben` = STORED zip, `BenBundleStore` indexes and range-serves members. Members have **no UploadFile**;
  every marker/scan table keys on `UploadFileId`; `FileAudienceAccess.CanViewFileAsync` has no field-session
  clause. Session read rule = `FieldSessionUploadController.MayReadAsync` (:1007) + private helpers
  `MayContributeAsync` (:1063, includes a public-investigation door), `IsEventTeamAsync`, `IsHostedTeamAsync`
  (`Attach.cs:116–137`). Place claimant = `OrganizationVenueProfile` (`BenDataModel.Venue.cs`) with
  `PlaceId` and `VerifiedUtc != null`. Public archive gate = `PublicFieldSessionController.Publishable` (:291)
  + `ArchiveMediaPublication` (needs `MediaReviewState == Approved`).
- `UploadFileAudioClipController.Clip` never calls `AccountStorageGuard.WhyCannotStoreAsync` (0 calls).
- Background patterns: `Services/Scheduling/ScheduledWorkService` (5-min `IScheduledJob`s, e.g.
  `WeightRefitJob`) and `StoreReservationExpiryService` (own `BackgroundService`). `ValidateOnBuild` is on.
- Admin charts use `ApexChart` (`AdminDashboard.razor:157`). Admin APIs `[Authorize(Policy = RoleNames.SuperAdmin)]`.
- Fixtures: `Ben.Web.Playwright/Fixtures/field-session-stage.ben` (real device bundle), `Fixtures/test-audio.mp3`.
- Tests reach the editor via `AudioEditorTests.ReadyInFullViewAsync` (/media-library → show-waveform →
  right-click → Open Full View → wait "Clear Regions"). Help: `Ben.Web.Services/Help/Content/using-the-audio-editor.md`,
  5 images, `HelpMediaCapture.Capture_UsingTheAudioEditor`, `docs/build-documentation-pdf.py`;
  `AudioEditorRelease.Version` is asserted against `Changelog/Content/audio-editor.md`.

## Shape of the result

```
/audio/{fileId}  ·  /field-sessions/{sessionId}/audio/{clipFileId}         (.ben-editor-page)
├ header: back · file name · Size/Type/Duration/Position · HelpLink · "Editor 2.0.0"
├ TelerikToolBar  [Play Stop Loop] | [Select/Scrub · Clear] | [Spectrogram · Scan for EVP ▾ · Markers(n)] | [− fit + sel] ···· View ▾ Listen ▾ Edit ▾ Clips(n) ?
├ stage ─────────────────────────────────────────────┬ inspector (320 px, collapsible)
│  candidate strip (6 px heat strip, whole file)     │ Markers | Clips | Listen | Display | Edit
│  minimap                                            │ (Markers = the EVP lab: scan, settings,
│  waveform  (scan mask overlay: dim = not scanned)   │  presets, candidates, your markers)
│  spectrogram (resizable, marker/candidate overlays) │
│  level lane · detector lane (floor, threshold, band)│
│  transport: ▶ ■ ⟳ · time · spectrum+peak meter · zoom · speed chip · A/B
└ dialogs unchanged (BenModal): marker, keep, make clip, explorer, label, shortcuts
```

Scanning: one engine (`EvpScanService`) behind a sync endpoint (short files), a job queue (long files, batches,
sessions), and a session scanner that first **copies the clip out of the `.ben`** into the scanner's own
(or their group's) files and then treats it like any other audio file.

## Phases (each merges on its own; order matters inside a track, tracks can interleave; 1b was added after phase 1 began)

Tracks: **E** editor · **S** scan engine · **F** field sessions. Migrations: M1 (phase 5), M2 (phase 6),
M3 (phase 8), M4 (phase 9, added 10/27: its tables were left out of M2), M5 (phase 9b), M6 (phase 11); Ben applies each on MonsterASP himself. If two phases deploy together, fold
their migrations into one.

Every phase: branch `feature/<slug>`, a `ProjectNotes/FeatureHistory/README-<slug>.md` describing the phase
(Ben's rule), `## Unreleased` lines in `Ben.Web.Services/Changelog/Content/audio-editor.md` (and `website.md`
for site-level entrances), American English, plain product wording, `scripts/run-e2e.sh` green before merging
to develop.

---

### Phase 0 (E) — Bug fixes in the editor as it stands · ½ day · no migration

All in `Ben.Web.Website.Library/Manage/Audio/` unless noted. Each row = one atomic change.

| # | Where | Fix | Verify |
|---|---|---|---|
| 1 | `AudioFilePreview.razor:708` marker "Ends at" | `max="@_modalDuration"`, `step="0.01"` (was `max="100"`, capped the end at 100 s) | Playwright `A_markers_end_can_pass_100_seconds` (end 150 on a 186 s file → row shows 2:30) |
| 2 | `:554–555` fade inputs | `form-control-sm`, `max="@_modalDuration"`, width 80 px | visual |
| 3 | `WaveSurferPlayer.razor:123` speed select | bind `value="@_rate"`; `OnRateChange` sets `_rate` (hard-coded `selected` on 1×) | reopen shows the chosen speed |
| 4 | `WaveSurferPlayer.razor.js:1408` `_drawSpectrogramLabels` | add `melScale` param; compute y with the same `freqToY` as `_drawVoiceBandOverlay`; pass it at :1222 and :1273 | labels move when Mel toggles |
| 5 | `js:1358–1402` `_drawSpectrogram` | delete (never called; references undefined `melScale/melMax/fMax/colormap`) | `grep _drawSpectrogram(` → 0 |
| 6 | `js:1452` voice band | `VOICE_BAND = [300, 3400]` (detector's band; was 3000) — becomes options-driven in phase 5 | overlay top edge at 3.4 kHz |
| 7 | `js:756–760`, `:128–129, 139–140, 180–184, 296–298`; `WaveSurferOptions.cs:421–442`; `WaveSurferPlayer.razor:155,180–181,443–448`; `AudioViewState.ToRequest` | remove dead envelope / spectrogram-windowed plugin flags and the unused `ExtraControls` (DB columns and API untouched) | build + `AudioEditorTests` green |
| 8 | `AudioFilePreview.razor:132–135` | move the "settings not saved" span to the hint line (:206) under its own comment | — |
| 9 | emoji: `AudioFilePreview.razor` region/spectrogram menus (:969, :992–1102), edit buttons (:535–543); `WsRegionExplorer.razor:378–384`; `WaveSurferPlayer.razor:80–88` | plain text labels + `<BenIcon>` (play/pause/square) | `grep -P "[\x{1F300}-\x{1FAFF}\x{2600}-\x{27BF}]"` on the three files → 0 |
| 10 | `AudioFilePreview.razor:118` Clear Regions | icon `x-square` (trash implied deletion of audio) | — |
| 11 | `WsRegionExplorer.razor:131` Save as WAV | `btn-primary` (was `btn-success`) | — |
| 12 | `AudioFilePreview.razor:168` toolbar "Silence" | rename to **Detect silence**; edit-panel button stays **Silence selection** | `Silence_detection_does_not_steal_the_region_you_drew` updated |

Changelog: one line per user-visible fix (1, 3, 4, 6, 9, 12).

---

### Phase 1 (E) — The editor gets its own page · 1–2 days · no migration

1. `Manage/Audio/IAudioEditorSource.cs`:
   ```csharp
   public interface IAudioEditorSource {
       string Url { get; }  string FileName { get; }  string ContentType { get; }  long FileSize { get; }
       Guid AnalysisFileId { get; }        // the UploadFile markers/scans/clips attach to
       string BackHref { get; }
       AudioEditorCapabilities Can { get; } // [Flags] Markers | Scan | Clips | Edit | SaveView
   }
   ```
   `UploadFileAudioSource` (wraps `IMediaUrlBuilder.Download(fileId)`, all capabilities when `CanViewFile`).
   Phase 9 adds `SessionClipAudioSource`.
2. Extract `Manage/Audio/AudioEditor.razor` + `AudioEditor.razor.cs` (partial class): markup :89–927 and
   every `@code` member from `_modalTime` onward move verbatim; `[Parameter] IAudioEditorSource Source`
   replaces FileId/FileName/ContentType/FileSize/SourceUrl; `OnModalReadyAsync` → `OnPlayerReadyAsync`;
   the modal-close reset block (:1640–1653) becomes `DisposeAsync` (saves the view). `AudioFilePreview.razor.css`
   → `AudioEditor.razor.css`. Root element gets `id="audio-editor"`.
3. **Safety commit:** `AudioFilePreview`'s fullscreen modal renders `<AudioEditor Source=… />` for one commit;
   `AudioEditorTests` must pass unchanged — proof the extraction lost nothing.
4. `Manage/Audio/AudioEditorPage.razor`: `@page "/audio/{FileId:guid}"` and
   `@page "/field-sessions/{SessionId:guid}/audio/{FileId:guid}"`; shell copied from
   `Organization/Cases/CaseVideoEditorPage.razor:16–70` (loader, `SignInLink.For` gate, `<div class="ben-editor-page">`,
   `FeatureGate SiteFeatures.MediaLibrary`); header row (back link from `?return=`, else `/media-library`;
   facts; `HelpLink using-the-audio-editor`; `AudioEditorRelease.Version`); `<PageTitle>` = file name;
   `await IBenUserState.AuthReady` (Ben's AuthReady rule). Session route shows "This recording can't be opened
   here yet" until phase 9 registers `IAudioEditorSourceResolver`.
5. `AudioFilePreview.razor` becomes compact player + **Quick look** (the modal at `Size="lg"`: 200 px player
   with controls + "Open in editor" button); context menu: Play / Pause / Rewind / Quick look / **Open in editor**
   (`NavigateTo($"/audio/{FileId}?return={escaped current path}")`). Delete the fullscreen modal and the nested
   per-clip recursion (:633). `Kit/BenAudioTile.razor` footer gets "Open in editor".
6. Tests: `AudioEditorTests.OpenEditorAsync` → click "Open in editor", `WaitForURLAsync("**/audio/*")`,
   `Editor = Page.Locator("#audio-editor")`; same change in `AudioScrubModeTests:118`, `AudioEditorWalkTests:149`,
   `LookAndTruthsTests:387`, `HelpMediaCapture` (`.btn-close` → `GoBackAsync`). New: `The_editor_has_its_own_address`,
   `Back_returns_to_where_you_came_from`, `Quick_look_still_plays`.

---

### Phase 1b (E) — WaveSurfer 8.0.3 and its own spectrogram · 2–3 days · no migration

Added 10/26/2026 at Ben's request, relayed from a review session. It sits after the page shell
(phase 1, which uses no WaveSurfer API) and before the toolbar rebuild, so everything that talks to
WaveSurfer (toolbar, spectrogram, overlays, scan mask) is written once, against v8. **The facts
below come from the review session's reading of wavesurfer.xyz, the GitHub releases and
`examples/spectrogram.js`; check each one against 8.0.3 itself before relying on it.**

1. **Re-vendor** 8.0.3 core and the plugins Ben listed (10/26/2026), each from its example page:
   - **regions** (https://wavesurfer.xyz/examples/?regions.js)
   - **timeline** (?timeline.js)
   - **minimap** (?minimap.js)
   - **envelope** (?envelope.js)
   - **spectrogram** (?spectrogram.js)
   - **hover** (?hover.js)

   Record the version and SHA in `VENDORED.md`, and delete `plugins/spectrogram-windowed.esm.js`,
   a deprecated shim. Deep imports need `.js`.
   - **Zoom is not on Ben's list, but it is vendored anyway.** Checked in the 8.0.3 source
     (`src/`): the core has no wheel handling, and only `src/plugins/zoom.ts` provides wheel zoom.
     Leaving it out would silently drop wheel zoom. Ben can overrule.
   - **Source checkouts** (Ben, 10/26/2026), in `/Users/ben/Documents/GitHub/`:
     - `wavesurfer.js` at 8.0.3 + 3 commits (cdef4ac, 2026-10-23, including a WebAudio
       ratechange/volume fix). Check the review session's v8 claims against this source, not the
       docs.
     - `NAudio` at 3.3.0+ (we reference 2.2.1). Scans don't need it, because phase 4 decodes .m4a
       through ffmpeg. It is there if a NAudio upgrade is ever weighed.
     - `FFMPEG` at 9.1-dev (ceb466cbcd, 2026-10-25). This is reference source, not a build: the Mac
       runs the prebuilt ffmpeg 9.0 in `Ben.Video.Sidecar/ffmpeg/osx-arm64/`, and production runs
       its own `MediaTools:FfmpegPath` binary. Use it to check how the AAC decoder and
       `-ss`/`-t` behave on moov-at-end files (phases 4, 6, 9) before trusting a guess.
   - **Envelope is on Ben's list.** Phase 0 removed our unused envelope wiring. Ben, 10/26/2026:
     include it if it earns its place. It does, and it is in phase 7 as a drawn volume curve, kept
     as a new file the way every other edit is.
2. **`WaveSurferPlayer.razor.js` onto the v8 model:**
   - `getState()` signals (`currentTime`, `isPlaying`, `volume`, `loadPhase`, `scrollPosition`)
     replace hand-written event bookkeeping. `getRenderer().getVisibleRange()` replaces the
     zoom/scroll listeners (js:241–245).
   - A `Scope` (`scope.add/listen/timeout/interval/raf`, torn down by `destroy()`) replaces the
     manual unsubscribe arrays (js:635–719).
   - **Breaking changes to handle:**
     - `destroy()` is final, so every open makes a new instance.
     - A superseded `load()` rejects with `AbortError` and fires no `error` event. Catch it, or
       `OnWsError` never hears about a replaced load.
     - `getRenderer()` is no longer an event emitter.
     - Keep the **MediaElement backend**: the listening chain hangs off the media element, and
       `getMediaElement()` returns null on WebAudio.
3. **Stock spectrogram replaces ours.** `SpectrogramPlugin.create({ rendering: 'windowed',
   progressiveLoading, useWebWorker, … })` replaces:
   - `toggleSpectrogram`;
   - `spectrogram-worker.js` and `spectrogram-draw-worker.js`;
   - the OffscreenCanvas pre-render and the 128 px canvas.

   Map the saved fields onto the new options so nobody's settings reset:
   - `fftSamples` → `fftSamples`;
   - colormap → `colorMap` (Jet / Viridis / Inferno passed as 256-entry arrays; `gray` stays);
   - mel → `scale: 'mel'`, otherwise `'linear'`;
   - labels → `labels`.

   Verify zoom always sets `minPxPerSec`; the example says a scrolling file misrenders without it.
4. **Overlays on top of the plugin.** A thin canvas over the spectrogram, placed from
   `getVisibleRange()`, draws the voice band (300–3400 Hz, the detector's band) and the
   candidate/marker spans, with frequency → y taken from the plugin's own scale. Small custom
   plugins go through `WaveSurfer.definePlugin`. Phase 6's scan mask and phase 7's spectrogram
   overlays use the same layer, which shrinks both phases.
5. **New dials** in the Display tab (phase 2) / View ▾:
   - scale (linear, log, mel, bark, erb);
   - frequency min/max;
   - window function;
   - `gainDB` / `rangeDB`;
   - pre-emphasis (keeps formants visible above 1 kHz);
   - split channels.

   They are persisted in the `view` part of `EditStateJson` (phase 3).
6. **Regions in v8:**
   - The `region-out`-at-a-region's-start misfire is fixed upstream. Our `PlayedFirst` learning
     signal comes from `OnModalRegionIn`, so neighboring candidates may have been marked "heard"
     by an in/out ping-pong.
   - Add `Playing_past_one_candidate_does_not_mark_its_neighbor_heard` (two adjacent candidates;
     play only the first). Run it on 7.12.11 first to learn whether we shipped the bug, and say
     so in the README.
   - v8 regions auto-scroll near the edge while dragging and enforce `minLength` / `maxLength` while
     drawing. Use these in the bounds-adjust flow.
7. **Tests:**
   - `Mel_labels_and_voice_band_line_up_with_the_picture`;
   - `Spectrogram_settings_survive_leaving_and_reopening`, including a pre-upgrade saved row;
   - `A_ninety_minute_recording_draws_windowed_without_freezing`;
   - the `PlayedFirst` test above;
   - the full `AudioEditor|AudioPlayer|CaseMixer` set on the new version.

---

### Phase 2 (E) — Toolbar regroup, inspector tabs, one speed · 2 days

1. `Ben.Web.Website.Library.csproj:14–17` comment: add "…and the editors' ToolBar / DropDownButton /
   SplitButton / Slider" (Ben's prefer-Telerik rule; `BenContextMenu` stays for right-click menus; never
   `TelerikDialog`). Confirm `Telerik.Blazor.Components` is in Library `_Imports.razor`.
2. Split `AudioEditor.razor` into children, each with its own `.razor.css`:
   - `AudioEditorToolbar.razor` — `TelerikToolBar OverflowMode="Menu"` (copy the shape of
     `Ben.Video.Editor/Components/Toolbar.razor`), state-less, `EventCallback`s only:

     | Group | Control | Component |
     |---|---|---|
     | Transport | Play/Pause · Stop · Loop | `ToolBarButton`×2, `ToolBarToggleButton` (`Overflow="Never"`) |
     | Selection | Select ⇄ Scrub (pressed = scrub) · Clear Regions | `ToolBarToggleButton`, `ToolBarButton` |
     | Analyze | Spectrogram (toggle) · **Scan for EVP ▾** (primary = run with current preset; items Low / Medium / High / Fine-tune…) · Markers (n) | `ToolBarToggleButton`, `TelerikSplitButton`, `ToolBarToggleButton` |
     | Zoom | − · Fit · + · To selection | `ToolBarButton`×4 |
     | spacer | | `ToolBarSpacer` |
     | View ▾ | Timeline · Spectrogram labels · Mel scale · Voice band · Minimap · Level lane · Detector lane · Detect silence · Display settings… | `TelerikDropDownButton`, check glyph `<BenIcon Name="check">` in item content |
     | Listen ▾ | Listening tools… · Speed 0.5/0.75/1/1.25/1.5/2 · Compare original | `TelerikDropDownButton` |
     | Edit ▾ | Make clip from selection… · Explore selection… · ─ · Cut selection · Silence selection · Normalize · Reverse · Gain… · Fade… · Change speed… · Change pitch… (each titled "(new file)") | `TelerikDropDownButton` |
     | Clips (n) · ? | | `ToolBarButton`×2 |
   - `AudioInspector.razor` — `BenTabs`: **Markers** (the EVP lab moved verbatim; keeps `#evp-markers-panel`,
     "Scan for EVP", "Add Marker at …", "Keep as an EVP marker" ids for tests/help) · **Clips** (saved clips as
     rows: small play + "Open in editor") · **Listen** (EQ, HP/LP, compressor, gate, `TelerikSlider` speed
     0.25–3, volume) · **Display** (resolution, colormap, labels, mel, voice band, silence threshold, lane
     toggles, spectrogram height) · **Edit** (the six destructive ops; opened by an Edit-menu item with that op focused).
   - `AudioTransport.razor` — Play/Stop/Loop, time, zoom `TelerikSlider`, speed chip (click → Listen menu),
     A/B placeholder, meters placeholder. `WaveSurferPlayer ShowControls=false` here (its own bar stays for
     compact/quick look).
3. Toggles bind `Selected` to the fields that drive the player (`_spectrogramEnabled`, `_regionDrawMode`,
   `_showVoiceBand`, `_melScale`, `_silenceDetectionEnabled`) → pressed states exist; labels toggle lives in View ▾.
4. One `_speed` in `AudioEditor`; Listen menu and Listen tab set it; `WsRegionExplorer` takes `Speed`/`SpeedChanged`
   and drops its own range (:91). Destructive speed is only "Change speed… (new file)".
5. Panels no longer stack between toolbar and player → audit finding U (empty bands) disappears structurally.
6. Tests: `Toolbar_menus_hold_the_lesser_used_controls`, `One_speed_control_drives_playback_and_the_explorer`,
   `Toggles_show_pressed` (`aria-pressed`), update `Silence_detection_does_not_steal_the_region_you_drew`
   (View ▾ → Detect silence); `HelpMediaCapture` steps retargeted.

---

### Phase 3 (E) — Signal styling + remembered view · 1–2 days · no migration

1. `Ben.Web.Website/wwwroot/theme/telerik-signal.css`: `.k-toolbar` (bg `--ben-surface`, border `--ben-line`,
   radius `--ben-radius`, 36 px buttons, `.k-selected` = `--ben-accent-soft`/`--ben-accent`), `.k-dropdown-button`
   popup, `.k-split-button`, `.k-slider` (track `--ben-line-strong`, thumb `--ben-accent`). Add toolbar + dropdown
   + split button + slider samples to `/styleguide` (the stated checkpoint for every Telerik restyle). Measure
   contrast in the browser (Ben's rule), light and dark.
2. `AudioEditor.razor.css` and the children: tokens only (`--ben-surface-raised`, `--ben-line`, `--ben-radius-lg`,
   `--ben-accent-grad` top edge like `.evp-lab`). Listen/Display/Edit tabs lose `bg-body-secondary` and inline
   styles; inputs become `BenTextBox` / `BenSelect` / `TelerikSlider`. Touch targets ≥ 38 px (root 14 px).
3. Colors out of JS: `resolveTelerikColors()` (js:69) → `resolveSignalColors()`; new tokens in
   `signal-tokens.css` (`--ben-audio-region`, `-clip`, `-candidate`, `-marker`, `-silence`, `-voice`, rgb triplets,
   light + dark) replace literals at js:405, 438, 1452, 1502 and razor :1265/:1346. Copy `signal-tokens.css`
   to both WASM hosts (`SignalCopiesStayInStepTests`).
4. Remembered view (no migration): `EditStateJson` → `{ "chain": {...}, "view": { voiceBand, dragScrub, zoom,
   speed, volume, silenceOn, lanes, minimap, inspectorTab, spectrogramHeight }, "scan": { sensitivity, options } }`;
   `AudioListeningChain.FromJson` accepts the old flat shape as `chain`; `AudioViewState` gains `View` + `Scan`;
   `RestoreSavedViewAsync` applies zoom/speed/volume after `OnPlayerReady`. (Phase 5 moves `scan` to its own column.)
5. Tests: `How_you_set_the_editor_up_survives_closing_it` extended (zoom, speed, voice band, sensitivity);
   `LookAndTruthsTests` AE-2 retargeted to `#audio-editor-toolbar`, light + dark screenshots.

---

### Phase 4 (S) — Decode .m4a anywhere; one scan recorder · 1–2 days · no migration

1. `Ben.Data.WebApi/Services/Audio/IAudioDecoder.cs`:
   `bool IsAvailable; bool CanDecode(contentType, fileName); IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMono16kAsync(Stream, contentType, fileName, CancellationToken)`
   (chunks of 2^16 samples) and `Task<byte[]> DecodeRangeToWavAsync(Stream, contentType, fileName, startSeconds, endSeconds, ct)`.
   - `NAudioDecoder` — MP3/WAV via `AudioSourceReader.ReadMonoAt` / `AudioClipper.Clip`; always available.
   - `FfmpegAudioDecoder` — `IsAvailable` = `MediaToolOptions.FfmpegPath` set and exists. Copies the source to
     `{temp}/ben-evp-{guid}{ext}` (moov-at-end), runs `ffmpeg -nostdin -hide_banner -loglevel error -i <in> -vn -map 0:a:0 -f s16le -ac 1 -ar 16000 pipe:1`,
     yields chunks from stdout, drains stderr, kills on `MediaToolOptions.DecodeTimeoutSeconds` (default 600),
     deletes the temp file in `finally`. Range: `-ss -t … -c:a pcm_s16le <out>.wav` then read the file (WAV on a
     pipe has a 0xFFFFFFFF length `WaveFileReader` rejects). Non-zero exit → `AudioDecodeFailedException(stderr tail)`.
   - `CompositeAudioDecoder` (registered as `IAudioDecoder`): NAudio for mp3/wav, ffmpeg for other `audio/*`;
     ffmpeg missing → `AudioDecoderUnavailableException("This server cannot read .m4a yet.")`.
   Register in `Program.cs` next to `AvMetadataStripper`.
2. `Services/Audio/EvpScanRecorder.cs` extracted from `AudioMarkerController.Scan/ReplaceCandidates/TryRecordScanAsync`
   (:199–411), identical semantics (delete Pending, insert in one transaction, `OverlapsEnough` vs reviewed, cap,
   `EvpScan` + candidates written after and allowed to fail). `ProposeAsync(db, fileId, analysis, sensitivity, options,
   createdBy, notePrefixFor)` → `(markers, scanId)`. Controller calls it. Existing `AudioMarkerControllerTests` stay green.
3. `AudioMarkerController.Scan` decodes through `IAudioDecoder` (m4a accepted; unavailable → 400
   `ScanRefusalRecord(Reason, Message)`, never a bare string).
4. `UploadFileAudioClipController.Clip`/`Probe`: decode through `IAudioDecoder`; `MediaDuration` for m4a; **add the
   missing `AccountStorageGuard.WhyCannotStoreAsync`** before `WriteAsync` (paid-org members skip, as the guard does).
5. `Controllers/Admin/AdminMediaToolsController` → `GET api/admin/media-tools` →
   `MediaToolsHealth(FfmpegConfigured, Path, Found, Version, Problem)`; `SuperAdmin/AdminDashboard.razor` card
   "Audio decoding: ffmpeg 6.1 at …" or "Not configured: .m4a scans are refused until MediaTools:FfmpegPath is set".
6. `MediaLibraryGrid.razor` / `AudioEditor`: m4a no longer filtered out of Scan / Make clip.
7. Fixture `Ben.Web.Tests/Fixtures/device-clip.m4a`: cut from a real device bundle's `media/audio-001.m4a` with
   `ffmpeg -t 3 -c copy` (keeps moov last; a test asserts the box order). Tests: `FfmpegAudioDecoderTests` (skip with
   a message when no ffmpeg on the machine; `ffprobe` guard on the fixture first), `CompositeAudioDecoderTests`
   (unavailable sentence), `AudioClipControllerTests` m4a via fake decoder + cap refusal. `scripts/run-e2e.sh`
   exports `MediaTools__FfmpegPath=$(command -v ffmpeg)` when present.

---

### Phase 4b (S+E) — Edits and clips saved without loss, at about half the size · ½ day · no migration

Ben, 10/26/2026, on WAV's size: "I would not recommend a format that loses information." So MP3 is
never offered: it discards the quietest sounds and what it judges masked, which is what an EVP
search looks for, and an .m4a converted to MP3 would lose twice.

1. **FLAC is the default for every saved edit and clip.** Lossless, so the decoded samples are
   bit-for-bit the WAV's, at roughly 50–60 % of its size for room recordings. Browsers play it
   (Chrome, Edge, Firefox, Safari 11+). Encoded by ffmpeg (`-c:a flac`) through `IAudioDecoder`'s
   sibling `IAudioEncoder`; with no ffmpeg the save is WAV, as now, and says so.
2. **The save shows both sizes.** The Make clip dialog and the Edit tab read "Saved as FLAC, 4.1 MB
   (WAV would be 9.8 MB). Nothing is lost: it plays back identically." A "Save as WAV instead"
   checkbox for anyone who needs WAV for another program.
3. `UploadFileAudioClipController` / `UploadFileAudioEditController` take `Format` (Flac default,
   Wav); `ClipAudioRequest` / `AudioEditRequest` gain it, defaulting to Flac. NAudio cannot read
   FLAC, so `AudioSourceReader.Reads` stays MP3/WAV and a FLAC source goes through ffmpeg like
   .m4a; a saved clip can itself be clipped, edited and scanned.
4. Tests: `A_flac_clip_decodes_to_the_same_samples_as_the_wav` (round-trip through ffmpeg, sample
   equality), `A_clip_is_smaller_as_flac`, `No_ffmpeg_saves_wav_and_says_so`, Playwright
   `The_save_says_how_big_each_format_would_be`.

---

### Phase 5 (S) — Every dial exposed, presets · 1–2 days · **migration M1 `EvpScanSettings`**

1. `EvpDetectionOptions` gains (defaults = today's constants so the default path is bit-identical):

   | Field | Default | Range | Rule |
   |---|---|---|---|
   | `BandLowHz` | 300 | 50–8000 | < BandHighHz |
   | `BandHighHz` | 3400 | 50–8000 | clamped to Nyquist−1 in the detector |
   | `FloorWindowSeconds` | 10 | 2–60 | |
   | `FloorPercentile` | 0.20 | 0.05–0.50 | fraction; UI shows % |
   | `ReleaseHysteresisDb` | 2 | 0–10 | < ThresholdDb |
   | `MaxCandidates` | 500 | 10–500 | server clamps to `MaxCandidatesPerScan` |
   | `MinScore` | 0 | 0–100 | |
   | `ExplorationShare` | 0.10 | 0–0.5 | item 253's "keep some lower-ranked" |
   | `ReviewedOverlapFraction` | 0.5 | 0.1–1 | replaces the private const |

   Not exposed, on purpose (say so in the record's remarks): frame window 25 ms and hop 10 ms — hop is the unit
   every stored feature is measured in; changing it per scan would make features from different scans incomparable.
   `FromSensitivity` returns full records; `PresetName(options)` → "Low"/"Medium"/"High"/null (= Custom).
2. `EvpDetector`: constants → option reads (`BandPass(input, rate, options)`, `SlidingFloor(bandDb, options)`,
   `GateRuns(…, ThresholdDb, ReleaseHysteresisDb)`). After scoring: drop `< MinScore`; `cap = min(maxResults, MaxCandidates)`,
   `reserve = floor(ExplorationShare × cap)`, top `cap − reserve` by score, then every k-th remaining by start time
   (deterministic, no RNG) flagged `Exploratory = true`. **Version stays 1**; the pinned-scores test is the proof.
3. M1: `EvpScans.OptionsJson nvarchar(max)`, `BandLowHz float`, `BandHighHz float`; `EvpScanCandidates.Exploratory bit`;
   `AudioMarkers.IsExploratory bit`; new `EvpScanPresets (Id, AppUserId FK cascade, Name nvarchar(60), OptionsJson,
   IsDefault, DateCreated, DateUpdated; unique (AppUserId, Name))`; `UploadFileAudioConfigs.EvpScanOptionsJson nvarchar(max) null`
   (last-used per file — `AudioListeningChain.ToJson()` rewrites `EditStateJson` from a fresh record, so unknown keys there are lost).
   Never `migrations remove`; EXEC for any `Sql()` touching new columns.
4. `AudioMarkerController.Scan`: validate, clamp, pass the overlap fraction, write `OptionsJson`/bands/`IsExploratory`,
   upsert `EvpScanOptionsJson`. New `EvpScanPresetController` `api/evp-scan-presets` (GET mine, POST, PUT, DELETE; owner-only;
   server runs `Validate()` on stored JSON). `AudioMarkerRecord.IsExploratory` ("included as a low-ranked sample").
   `IBenAdminClient`/`WebApiClient`/`IWebApiClient` methods.
5. UI `Manage/Audio/EvpScanSettingsEditor.razor` (`Value`/`ValueChanged`, preset dropdown Low/Medium/High/Custom/saved,
   "Save as preset…", "Set default"), in the Markers tab's Fine-tune drawer, grouped: *What counts as a sound* (threshold,
   hysteresis, shortest/longest, join gap) · *Where it listens* (band low/high — also drives the spectrogram's voice-band
   overlay) · *Room noise* (floor window, percentile) · *What reaches the queue* (max candidates, min score, exploration
   share, reviewed overlap). Seed: file's `EvpScanOptionsJson` → user's default preset → Medium. Scan ▾ split-button
   items reflect the same presets.
6. Tests (`EvpDetectorTests`): pinned scores unchanged; `A_narrower_band_changes_the_pinned_scores` (assert inequality
   of arrays; run it against the un-fixed code first — Ben's "verify a test discriminates"); `A_wider_floor_window_absorbs_a_step_more_slowly`;
   `Hysteresis_zero_fragments_a_wavering_utterance`; `Min_score_removes_below_it`; `Exploration_reserve_is_floor_of_share_times_cap`
   (cap 2 → 0 reserved, so `RespectsTheResultCap` stays); `Exploration_is_deterministic`. `EvpDetectionOptionsTests`
   every new range + `PresetName`. Controller: `Scan_RecordsTheFullOptions`, `Scan_HonorsTheOverlapFraction`,
   `Scan_ClampsMaxCandidates`, preset CRUD + other user's preset is 404.

---

### Phase 6 (S+E) — Background scan jobs with live progress in the editor · 2–3 days · **migration M2 `EvpScanJobs`**

**Engine.**
1. `EvpDetector.Analyze(IAsyncEnumerable<ReadOnlyMemory<float>> chunks, options, maxResults, IProgress<EvpScanProgress>?, ct)`:
   stateful biquads, framing with a `(window − hop)` sample carry, per-frame `bandDb/fullDb/zeroCrossings` into
   growable arrays (90 min ≈ 540 k frames); floor/gate/shape/score after the last chunk (unchanged code).
   `Measure` switches zero crossings to per-frame counts (count the crossing with the previous frame's last
   sample so totals match). `EvpScanProgress(ProcessedSeconds, TotalSeconds, SettledSeconds = Processed − FloorWindow/2)`
   per chunk; cancellation per chunk. The `float[]` overload wraps one chunk (tests unchanged).
   Test `Streaming_and_array_analysis_give_identical_candidates` (fixture cut into odd 7-sample chunks; verify it
   fails when the tail carry is dropped).
2. `Services/Audio/EvpScanService.RunAsync(fileId, userId, sensitivity, options, progress, ct) → EvpScanOutcome(ScanId, Found, Proposed, Error?)`:
   open file → `IAudioDecoder.DecodeMono16kAsync` → `Analyze` → `EvpScanRecorder.ProposeAsync` → save last-used
   options. The sync endpoint and the job call this one method.

**Data (M2).** New file `Ben.Data.Source/Entities/BenDataModel.EvpScanJob.cs`:
- `EvpScanJobs (Id, FieldSessionUploadId? FK cascade, RequestedByAppUserId FK NoAction, Scope? int, ScopeOrganizationId?,
  Sensitivity int, OptionsJson, PresetName?, Status int {Queued, Running, Done, Failed, Canceled}, Percent int,
  Error nvarchar(1000), CreatedUtc, StartedUtc?, FinishedUtc?, CancelRequestedUtc?)`; indexes
  `(RequestedByAppUserId, FinishedUtc)`, `(FieldSessionUploadId, FinishedUtc)`.
- `EvpScanJobSegments (Id, EvpScanJobId FK cascade, UploadFileId FK cascade, FieldSessionUploadFileId? no FK,
  RelativePath nvarchar(260), Order, Status, Percent, SettledSeconds float, TotalSeconds float, CandidateCount?,
  TopScore real?, EvpScanId? no FK, Error, StartedUtc?, FinishedUtc?)`; **unique filtered index `(UploadFileId) WHERE FinishedUtc IS NULL`**
  (one live scan per recording, enforced by the database).
- Phase 9's tables (`FieldSessionAnalysisCopies`, `UploadFiles.SourceFieldSessionUploadId/SourceClipPath`) go in
  **this same migration** so phase 9 adds none. Enums in `Ben.Data.Common/Enums/`: `EvpScanJobStatus`, `AnalysisScope`, `ScanRefusal`.

**Queue + worker.** `Services/Audio/Scanning/EvpScanQueue` (singleton `Channel<Guid>`, `Signal`, `Cancel` via
`ConcurrentDictionary<Guid, CancellationTokenSource>`); `EvpScanWorker : BackgroundService` (registered like
`StoreReservationExpiryService`; on start any `Running` → `Failed("The server restarted during this scan.")`;
takes ids from the channel or polls `Queued` every 30 s; `SemaphoreSlim(1)` per process); `EvpScanRunner.RunAsync(jobId, ct)`
(scoped): claim → per segment in `Order`: `EvpScanService.RunAsync` with a progress sink writing
`Percent/SettledSeconds` at most once per second → segment summary (`CandidateCount`, `TopScore`) → job percent =
duration-weighted mean. Decoder exceptions → segment `Failed` with the sentence, job continues; anything else →
job `Failed`, logged. Cancel = `CancelRequestedUtc` + CTS; a canceled job keeps finished segments and never touches
Pending markers of unfinished ones. `Services/Scheduling/EvpScanJobRetentionJob : IScheduledJob` deletes jobs
finished > 7 days ago (results live in `EvpScans`/markers).

**API** `Controllers/Entities/EvpScanJobController.cs` `[Authorize]`:
- `POST api/upload-files/{fileId}/scan-jobs {sensitivity, options?}` → `CanViewFileAsync`, `audio/*`, `CanDecode`
  (else 400 `ScanRefusalRecord`), per-user live cap 3 (429), `AudioProcessingPolicy`, unique-index clash → 409
  "A scan of this recording is already running." → 202 `EvpScanJobRecord`.
- `GET api/scan-jobs/{id}` → `EvpScanJobRecord(Id, Status, Percent, Error, Segments[{UploadFileId, RelativePath,
  Status, Percent, SettledSeconds, TotalSeconds, CandidateCount, TopScore, Error}], Created/Started/Finished)`,
  `ETag` over (Status, Percent, segment states) → 304 on `If-None-Match`.
- `DELETE api/scan-jobs/{id}` (requester) · `GET api/upload-files/{fileId}/scan-jobs/live` (live job or 204, so a
  reopened editor re-attaches). The sync `POST …/scan` stays for files ≤ 10 min; longer → enqueues and answers 202.
- Progress is **polling with ETag every 1.5 s** (no SignalR hub exists; the site reaches the API over HTTP from
  another host; a 304 per viewer per 1.5 s is the cheapest thing that also survives restarts).

**Editor (E).**
- `AudioEditor.RunScanAsync` → `StartScanJobAsync` then a `PeriodicTimer(1.5 s)` loop (CTS disposed with the component):
  diff segments, `OverlayMarkersAsync` for newly-Done ones, push ranges to JS, `_scanPercent`. Toolbar stays live;
  the Scan button reads "Scanning 42%" with a Cancel item; Markers tab shows `TelerikProgressBar` + the existing sweep.
- JS `setScanProgress(containerId, ranges)` in `WaveSurferPlayer.razor.js`: a `<canvas class="ws-scan-mask">`
  appended once to `ws.getWrapper()` (`position:absolute; inset:0; pointer-events:none`, z below regions), resized
  on zoom/redraw; ranges derived per segment: `[0, settled]` Done (clear), `[settled, processed]` Scanning (moving
  hatch; static under `prefers-reduced-motion`), rest Pending (`rgba(var(--ben-bg-rgb), .55)`); same on the minimap;
  cleared when the job ends. Candidates appear per clip when its segment finishes.
- Leaving mid-scan: the job runs on; on reopen `…/scan-jobs/live` resumes the loop.
- Tests: runner with a fake decoder (synthetic events from `EvpDetectorTests` generators) lands Pending markers +
  `EvpScans`; enqueue twice → 409; cap → 429; cancel leaves old Pending rows; ETag 304. Playwright
  `A_scan_shows_progress_and_the_waveform_fills_in` (percent increases; mask alpha > 0 while Running, 0 when Done),
  `Leaving_during_a_scan_does_not_lose_it`.

---

### Phase 7 (E) — Visual analysis A: spectrogram, minimap, zoom, loop, keyboard · 2 days · no migration

| Item | Build |
|---|---|
| Spectrogram taller + resizable | the stock plugin's `height` (phase 1b) driven by a `style="resize:vertical"` pane + `ResizeObserver` → `setOptions({height})`; height persisted in `view` |
| Marker/candidate overlays on the spectrogram | `setSpectrogramOverlays(containerId, [{start,end,kind}])`; `_redrawSpectrogramViewport` paints translucent rects + 1 px edges after labels, colors from the phase-3 tokens; called wherever `_markers` changes |
| Minimap | vendored `plugins/minimap.esm.js` is already wired behind `EnableMinimap`; editor config sets it on (`height 40`, token colors); View ▾ toggle; persisted |
| Zoom to selection / candidate / fit | `zoomToRange(containerId, start, end, pad=0.15)`: `pxPerSec = wrapperWidth / (span×(1+2pad))`, `ws.zoom`, `ws.setScrollTime`; toolbar buttons + candidate-row Zoom |
| Loop | `loopRange/stopLoop` via `timeupdate`; Loop toggle loops the selection or the candidate being auditioned |
| Keyboard | `Manage/Audio/audio-editor-keys.js` (`attach(dotnetRef)`/`detach()`; ignores inputs/contenteditable/open modals) → `[JSInvokable] OnShortcutAsync(key)`. Map: **Space** play/pause · **←/→** ±1 s (Shift ×5) · **[ ]** selection start/end at playhead · **K** keep · **D** / Delete dismiss · **N/P** next/previous candidate · **Z** zoom to selection · **F** fit · **L** loop · **B** compare original (phase 8) · **Esc** clear selection · **?** shortcuts dialog (BenModal) |

| Volume envelope (Ben, 10/26/2026) | the vendored `plugins/envelope.esm.js` over the waveform, off until Edit ▸ **Draw volume…** turns it on: drag points to pull down a bang or a door beside a faint sound, or bring a quiet stretch up. Live through the listening chain's gain while drawing; **Save as a new file** posts the points to `audio-edit` as a new `Envelope` operation (linear gain between points, clamped −40…+20 dB, the WAV/FLAC path like every edit). Points are not kept on the recording itself. |

Tests: `Envelope_points_reach_the_saved_file` (a −20 dB dip over a fixture tone measures ≈ −20 dB in the
output, and 0 dB outside it), `AudioEditorTests.Drawing_a_volume_curve_saves_a_new_file`,
`Keyboard_space_plays_and_bracket_keys_set_the_selection`, `K_keeps_and_D_dismisses_the_focused_candidate`,
`Zoom_to_selection_fills_the_window` (region ≥ 60 % of the wrapper), `A_candidate_can_be_looped`,
`Spectrogram_shows_the_candidates` (overlay pixels inside a candidate's bounds via `EvaluateAsync` + `getImageData`).

---

### Phase 8 (S+E) — Visual analysis B: the detector's own view · 2–3 days · **migration M3 `EvpScanTraces`**

1. `EvpTrace(BucketSeconds = 0.1, BandDb[], FullDb[], FloorDb[], GateRuns[(start,end)])` on `EvpAnalysis`: band/full =
   max over 10 frames, floor = mean; gate runs are the raw pre-shaping runs so the page can show "the gate opened here
   but shaping dropped it". Threshold line is `floor + ThresholdDb` client-side (the options are known) — moving the
   threshold slider redraws the line without a rescan.
2. M3: `EvpScans.TraceBlob varbinary(max) null`, `TraceBucketMs int null`. `Services/Audio/EvpTraceCodec` ("EVPT", v1,
   Int16 dB×100 arrays, Int32 run pairs; 90 min ≈ 324 KB), written inside `EvpScanRecorder` (same failure tolerance).
   Stored, not recomputed: re-decoding 90 minutes to redraw a chart is the cost the 2026-09-06 memory finding was about.
3. `GET api/upload-files/{fileId}/audio-markers/scan-trace?scanId=&from=&to=&maxPoints=4000` → `EvpTraceRecord`
   (slice + max/max/mean decimation); pre-M3 scan → 404 "This scan was made before traces were kept; rescan to see one."
4. `EvpDetector.Explain(features)` → three score terms (prominence ×50, band ratio ×30, duration ×20) + plain sentences
   ("rose 14 dB above the room", "72% of its energy in the voice band", "0.8 s long"); `Score` calls `Explain` so
   `Explain_terms_sum_to_the_score` holds. `GET …/audio-markers/{markerId}/detection` → `EvpCandidateDetailRecord(Score, Features, Terms)`.
5. Editor lanes `AudioLanes.razor` + `audio-lanes.js` (canvases in the waveform's scroll container, redrawn on zoom/scroll):
   - **Level lane** (client only): RMS dBFS per 10 ms from `ws.getDecodedData()` (reuse the `detectSilence` loop as
     `_frameRms`), silence threshold line when Detect silence is on.
   - **Detector lane**: `bandDb` line, `floorDb` line, `floor+threshold` dashed, shaded gate runs — the viewer sees why
     each candidate fired; windowed fetch when the visible span has < 4000 stored points.
   - **Candidate popover** (Markers tab): stacked score bar with the three sentences, the 7 (later 9) features as a
     definition list, spectrogram thumbnail cropped client-side from the spectrogram canvas.
   - **Live spectrum + peak meter** (`AudioMeters.razor`, transport row 160×36): `AnalyserNode` (fftSize 2048) after
     `gain`; rAF loop on play/pause; 64 log-spaced bars + peak hold.
   - **A/B original vs cleaned**: `setChainBypass(containerId, bypass)` reconnects `source` → `destination`; Listen ▾
     "Compare original", key B, transport chip reads "Original" while held.
   - **Candidate strip** (6 px canvas above the minimap, whole file): blocks tinted by score, confirmed solid; click seeks + selects.
   - Stereo channel views (Mixed/Left/Right/Split; spectrogram worker takes a channel) → **later**.
6. Tests: codec round-trip; `Trace_buckets_cover_the_whole_recording`; `Trace_gate_runs_contain_every_candidate`;
   `Decimation_keeps_the_peak_under_each_candidate`; `Explain_terms_sum_to_the_score`; controller trace/404 cases;
   Playwright `The_detector_lane_explains_a_candidate`, `Compare_original_bypasses_the_filters`. Help shot `detector-lane.png`.

---

### Phase 9 (F) — Scan a committed `.ben` session · 3–4 days · **migration M4 `FieldSessionAnalysisCopies`** (built 10/27)

1. **Policy.** Move `MayReadAsync/MayContributeAsync/IsEventTeamAsync/IsHostedTeamAsync` bodies unchanged into
   `Services/Access/FieldSessionAccess` (controller methods become forwards; existing tests prove it). New
   `Services/Access/FieldSessionAnalysisAccess`:
   ```csharp
   enum ScanRefusal { None, NotSignedIn, NoSuchSession, NotYours, PublicViewOnly, NoAudio }
   record ScanScope(AnalysisScope Scope, Guid? OrganizationId, string Label);
   record ScanDecision(ScanRefusal Refusal, AnalysisScope? Scope, Guid? OrganizationId, string? Message);
   Task<IReadOnlyList<ScanScope>> ScopesAsync(db, session, userId, ct);
   Task<ScanDecision> MayScanAsync(db, session, userId, AnalysisScope? requested, Guid? requestedOrgId, ct);
   string Describe(ScanRefusal);
   ```
   Scopes, in order: owner → `Person`; investigation → `MayContributeAsync` **narrowed to attendee-or-active-member**
   (the public-investigation door at :1085 grants reading, never scanning) → `Organization(investigation org)`;
   calendar event → `IsEventTeamAsync` → org; hosted event → `IsHostedTeamAsync` (active member or confirmed
   `HostedEventStaff`) → org; **place claimant** → session `Publishable` AND `MediaReviewState == Approved` AND
   `OrganizationVenueProfiles.Any(PlaceId == session.PlaceId && VerifiedUtc != null && active membership)` → `Organization(venue org)`.
   `MayScanAsync`: Guid.Empty → `NotSignedIn`; no scopes and Publishable → `PublicViewOnly` ("Only the person who
   recorded this, their group, or the location's venue can scan it."); else `NotYours`; no audio → `NoAudio`.
   `FileAudienceAccess.CanViewFileAsync` gains one clause: an Organization-scope analysis copy is viewable by anyone
   whose `ScopesAsync` includes that org (lets confirmed non-member staff open the group's copy).
2. **Copies.** `FieldSessionAnalysisCopies (Id, FieldSessionUploadId FK cascade, FieldSessionUploadFileId FK NoAction,
   CopyUploadFileId FK cascade unique, Scope, ScopeAppUserId?, ScopeOrganizationId?, SourceSha256, SourceRelativePath,
   MadeByAppUserId, DateCreated)`; check constraint on scope columns; unique filtered indexes per (file, person) and
   (file, org). `Services/FieldSessions/FieldSessionAnalysisCopies.EnsureAsync(db, session, file, scope, userId)`:
   storage check first (Person: `AccountStorageGuard.WhyCannotStoreAsync`; Organization: the org storage check
   `UploadFileController` applies to org uploads), then stream `IBenBundleStore.OpenEntryAsync` (or the legacy per-file
   UploadFile) into `UserFilePath(userId)` / `OrgFilePath(orgId)` while hashing; SHA mismatch vs `FieldSessionUploadFile.Sha256`
   → delete bytes, 409 "The copy did not match the recording on file." UploadFile row: `ContentType` from
   `FieldSessionFileGuard`, `FileName = "{StartedAt:yyyy-MM-dd} audio-002.m4a"`, `OwnerOrganizationId` for org scope
   (`AppUserId = null`), provenance `SourceFieldSessionUploadId`, `SourceClipPath`. The bundle is read only; nothing
   writes to its path. Bytes verbatim (≈30 MB/hour), not a WAV transcode.
3. **Session scan.** `Controllers/FieldSessionUploadController.Analysis.cs` (new partial):
   - `GET api/field-sessions/{id}/analysis` → `FieldSessionAnalysisView(MayScan, Refusal, Message, Scopes[],
     Clips[{FileId, RelativePath, DurationSeconds, StartMs, CopyUploadFileId?, PendingCount, ConfirmedCount, LastScanUtc}],
     LiveJob?, DecoderAvailable)` — the one call every entrance uses.
   - `POST api/field-sessions/{id}/clips/{fileId}/analysis-copy {scope?, organizationId?}` (idempotent).
   - `GET api/field-sessions/{id}/clips/by-path?path=` (matched against `RelativePath`, never used as a file path).
   - `POST api/field-sessions/{id}/scan-jobs {sensitivity, options?, scope?, organizationId?, clipFileIds?}` →
     `MayScanAsync` (403/404 `ScanRefusalRecord`) → timeline (`FieldSessionTimelineService`, policy Withheld,
     variant "analysis") → audio clips → `EnsureAsync` per clip → one `EvpScanJob` with N segments → 202.
   - **Question context** `Services/Audio/Scanning/EvpQuestionWindows`: from `TimelineMarker` kinds `evp_question`
     (Note = question) / `evp_wait_end` and `TimelineClip.StartMs` (rules M4/M5): window = question → first
     `evp_wait_end`, else next question, else +60 s; candidate absolute = `StartMs + s×1000`; inside → marker
     `Note = "Q: " + text` (Label stays "Detected signal"); unplaced clip → no context. Passed as `notePrefixFor`.
   - `GET api/field-sessions/{id}/evps` → clips with `SourceFieldSessionUploadId == id` for callers with any scope:
     `(FileId, FileName, RegionStart, RegionEnd, MadeBy, CanPlay)`.
4. **Extraction.** `UploadFileAudioClipController.Clip` copies `SourceFieldSessionUploadId/SourceClipPath` from the
   source onto the child (offset = `RegionStart`); `UploadFileRecord` gains both; file detail shows "From session
   {date}, audio-002 at 12:34" linking `/field-sessions/{id}`.
5. **Editor source.** `SessionClipAudioSource` + `IAudioEditorSourceResolver`: plays through the existing
   `/media/field-sessions/{s}/files/{f}?t=` proxy (no copy needed to listen); the first marker/scan calls
   `analysis-copy` and `AnalysisFileId` becomes the copy's id; `Can` = Markers|Scan|Clips (no destructive Edit,
   no SaveView on the bundle member). Header shows "Session {date} · audio-002 · your copy / {group}'s copy".
6. **Entrances** (each reads `…/analysis`, shows the server's sentence, hidden for `NotSignedIn`):
   `Kit/FieldKitEnginePlayer.razor` "Scan for EVPs" (`data-testid="session-scan"`; clip chooser when several;
   disabled with the message when `MayScan` false or `DecoderAvailable` false) + section "EVPs pulled from this session"
   (play/download when `CanPlay`, else "In {name}'s files"); `Kit/GroupFieldSessions.razor:74` row
   "Scan for EVPs" (`group-session-scan`); `Shared/PlaceView.razor:548` archive rows for the claimant (batch via
   `GET api/places/{placeId}/archive/scannable` if the list grows); `/media-library` m4a "Scan".
7. **Tests.** `FieldSessionAnalysisAccessTests` matrix: roles {anonymous, stranger, owner, investigation attendee,
   active member, confirmed non-member staff, verified venue member, share-link viewer} × sessions {personal
   unpublished, investigation (private / public variant), hosted event, published-to-public-place (Approved / Pending)}.
   `FieldSessionAnalysisCopiesTests` (bytes equal, SHA mismatch leaves no file, paths per scope, org ownership).
   Controller: question prefix from a document fixture; **the `.ben` is untouched** (hash before/after `EnsureAsync` +
   a full job; assert `IFileStorageService.WriteAsync` never got the bundle's path). Playwright
   `FieldSessionEvpScanTests` (category `FieldKit`; `Assert.Ignore` when `DecoderAvailable` is false): guest uploads
   `field-session-stage.ben` to the seeded hosted event; organizer scans from the event page, confirms a candidate,
   saves an EVP; the group's files hold it with "From session …"; the guest's library and session page are unchanged.
8. Follow-up recorded in `Future-Improvements.md`, not blocking: `AccountStorageGuard.UsedBytesAsync:112–124` joins
   through `FieldSessionUploadFiles.UploadFile`, so a published/event-linked personal **bundle** is still charged;
   add a subtraction over `FieldSessionUploads.DocumentUploadFile` where `IsBundle`. Close
   `README-event-scenario-gaps.md:67` with this phase.

---

### Phase 9b (S+E) — VoiceNet as the scan's second stage · 2 days · **migration M5 `EvpVoiceLikelihood`**

Ben, 10/26/2026 (relayed from the work-in-progress review session): wire VoiceNet into the EVP scan
as a second stage of the same scan, not a separate one. It replaces the "VoiceNet re-ranking" entry
that sat under *Later, gated*; the gate now governs only the default sort (step 6).

1. **Model.** `Services/Audio/OnnxVoiceScorer` (`IVoiceScorer`) mirrors `OnnxNsfwScreener`'s
   loading, but the model is **committed**, not fetched (decided 10/26/2026): it is ours (trained in
   `tools/EvpLearn` on LibriSpeech test-clean, CC BY 4.0, plus our synthetic noise) and 1,136,075
   bytes, where the screener's script exists only because that model is 87 MB and hosted
   elsewhere. Copy `tools/EvpLearn/runs/20260915-162340/voicenet.onnx` (the run c64354d2 reported)
   to `Ben.Data.WebApi/Models/voicenet-v1.onnx`, `CopyToOutputDirectory=PreserveNewest`, and check
   it lands in the single-file publish folder beside the exe (see the "single-file publish swallows
   content" lesson). The file name is the model version: `EvpScans.VoiceModel` stores
   `voicenet-v1`, and a retrain ships as `voicenet-v2` beside it. `tools/EvpLearn/runs/` stays
   ignored; `tools/EvpLearn/README` gains how a retrain is promoted (copy the run's onnx to
   `Models/voicenet-vN.onnx`, bump the configured name). A missing file (a mis-publish) still
   registers a `NullVoiceScorer`, WARNs at startup, and reports `IsAvailable` false.
   Input `"audio"` `[B, 16000]` (1 s, 16 kHz mono, peak-normalized per window); output `"voice"`
   `[B]` in 0–1. ONNX Runtime 1.29 is already referenced. In-process on IIS: ~121 ms per minute of
   audio.
2. **In the scan.** After `EvpDetector` finds and pads candidates (`EvpScanService.RunAsync`,
   phase 6), each padded span is scored in 1 s windows at a 0.25 s hop and the **maximum** is the
   candidate's `VoiceLikelihood`. Spans are re-read through `IAudioDecoder.DecodeRangeToWavAsync`
   (phase 6 streams the decode, so the whole file is not kept in memory), at most `MaxCandidates`
   of them. `EvpDetector.Version` stays 1 and `Score` is untouched, so
   `Version_one_scores_are_pinned` still passes; VoiceNet never drops a candidate.
3. **M5.** `EvpScanCandidates.VoiceLikelihood float null` (the 8th feature) and
   `AudioMarkers.VoiceLikelihood float null`; null = not measured. `EvpScans.VoiceModel nvarchar(64)
   null` names the model run that measured it, so a retrained model's numbers are never mixed with
   the old one's. `AudioMarkerRecord.VoiceLikelihood`; the scan answer carries
   `VoiceUnmeasuredReason` when the scorer is absent or failed ("Voice-likeness is not measured on
   this server: the model is not installed.").
4. **Editor.** Each queue row shows two readings, **Stood out** (today's 0–100) and **Voice-like**
   (0–100, or "not measured" with the reason in its title); sort by either. Fine-tune gains one
   slider, **Hide below voice-like …**, default 0: the person's filter, applied client-side, never
   the model's. Nothing is hidden by default.
5. **Wording.** Help and ad copy say it ranks how voice-shaped a sound is, never "likely EVP".
**Ben, 10/27/2026: hold off on retraining.** VoiceNet is never trained on anything sent to the site;
v1 is what runs. The stored readings and rulings are only a record until Ben says otherwise, and
using anybody's recordings to train would need their permission first.

6. **Gate before the default sort flips to voice-like first:** `tools/EvpLab score --voicenet` over
   Ben's real recordings (room tone, sessions with known talking) and the production Keep/Dismiss
   history must show kept-above-dismissed separation and under about one false alarm a minute on
   real room tone. Until then the column ships with the default order unchanged. If it fails,
   retrain with Ben's real room tone as backgrounds (scripts exist) rather than change the
   architecture. **Silero VAD is out**: it is blind below the noise floor and adds nothing VoiceNet
   does not.
7. **Tests.** Unit: model absent ⇒ not measured with the reason; present ⇒ a value in 0–1 per
   candidate; pinned scores unchanged; max-over-windows on a fixture with speech in one window.
   Controller: the feature stored on both tables and returned. Playwright: both readings visible,
   sort by voice-like, the slider hides rows client-side and 0 hides none.

---

### Phase 10 (S) — Batch scans and the Scans page · 1–2 days · no migration

1. `POST api/evp-scans {FileIds[], Sensitivity, Options}` → per item `{FileId, JobId | Refusal}` (non-audio, unviewable
   or already-running reported per item, never silently skipped); `GET api/evp-scans?status=` mine; cancel via
   `DELETE api/scan-jobs/{id}`. Per-user: 3 live jobs, one running per process.
2. `Media/MediaLibraryGrid.razor`: with `MultiSelect` and not `PickerMode`, bulk bar "Scan n for EVP" ("n of m selected
   are audio") → dialog hosting `EvpScanSettingsEditor` → enqueue → toast linking `/audio/scans`. Same bar on the case
   Files tab if it reuses the grid.
3. `Manage/Audio/AudioScansPage.razor` `/audio/scans`: rows file · preset/Custom · status · progress · found/proposed ·
   Cancel · Open in editor; polls every 3 s while anything runs. Editor Markers tab: "Rescan with these settings".
4. Tests: enqueue 3 → 3 jobs in order; running file → per-item 409; non-audio → per-item error; Playwright
   `/audio/scans` shows a queued batch reaching 100 %.

---

### Phase 11 (S) — Learning that reads its tables · 2 days · **migration M6 `EvpLearningReads`**

1. `Services/Audio/Fft.cs` lifted verbatim from `SmbPitchShifter.cs:155` (pitch shifter calls it; pin a shifted-fixture
   hash before the move). Features 8–9 per frame (Hann 512-point FFT of the 400-sample frame): `SpectralFlatness`
   (geometric/arithmetic mean of power inside the band) and `VoicedFrameFraction` (autocorrelation peak ≥ 0.5 at
   lags 40–200). Columns `EvpScanCandidates.SpectralFlatness float null`, `VoicedFrameFraction float null`. `Score`
   ignores them; Version stays 1. Record ms/min from `tools/EvpLab score` in the README.
2. Misses: `EvpMisses (Id, UploadFileId FK cascade, EvpScanId FK cascade, AudioMarkerId no FK, StartSeconds, EndSeconds?,
   DateCreated)` written by `AudioMarkerController.Create` for hand markers on a scanned file with no overlapping
   candidate at the scan's overlap fraction (`TryRecordMissAsync`, never fails the request).
3. Blind review: `AppUsers.EvpBlindReview bit`, on the profile page; `ReviewAudioMarkerRequest.Blind` →
   `EvpRulings.Blind bit null`; score meter/sort/popover hidden on Pending rows when on, revealed once ruled.
4. `/admin/evp` (`SuperAdmin/AdminEvp.razor`, nav entry, HelpLink) over `GET api/admin/evp/summary?days=` →
   `AdminEvpSummary(Scans, Candidates, Proposed, Exploratory, Rulings, Kept, Dismissed, PlayedFirstShare, BlindShare,
   KeptShareBlind, KeptShareShown, Misses, ScansPerDay[], FeatureHistograms[{Feature, Bins[{Lo, Hi, Kept, Dismissed}]}])`;
   stat tiles + `ApexChart` grouped bars per feature. Plus the "Audio decoding" health from phase 4.
5. Tests: flatness/voiced on the fixture (hum low flatness & voiced; room-tone burst high flatness; utterance voiced > 0.5);
   pinned scores unchanged; misses three cases; admin counts equal `db.Count` on the same seed; histogram bins sum to rulings.
   Playwright: Fine-tune band high → 1000 Hz → rescan → candidate count differs (captured both ways).

---

### Phase 12 — Help, screenshots, PDF, release 2.0.0 · ½–1 day

1. `using-the-audio-editor.md`: "Open Full View" → "Open in editor"; new sections "The toolbar and its menus",
   "Reading the lanes", "Keyboard shortcuts", "Scanning in the background", "Scanning a field session" (who can, the
   copy, where EVPs land), "Scan settings" (every dial in plain words, with the honest limits from item 253).
2. `HelpMediaCapture.Capture_UsingTheAudioEditor`: retake `editor-overview.png`, `spectrogram.png` (`#audio-editor-stage`),
   `evp-candidates.png` (`#evp-markers-panel`); new `detector-lane.png`, `session-scan.png`; `case-mixer.png` kept.
   Fresh e2e DB (Ben's help-capture rule). `python3 docs/build-documentation-pdf.py`.
3. `audio-editor.md` `## Unreleased` → `## 2.0.0 · date`; `AudioEditorRelease.Version = "2.0.0"` in the same commit;
   `website.md` entries for the session entrances and `/audio/scans`; `/changes` headings per Ben's versioning memory.
4. `Future-Improvements.md`: item 253 status (misses, blind review, exploration, features 8–9, admin page: BUILT;
   refit: future), item 242 (VoiceNet: phase 9b, default sort gated), the storage-cap bundle bug, stereo views, the
   never-built audio items from `Media-Editor-Phase.md` (frequency brush/notch, spectrogram PNG, clip comparison).
5. Memory: update `project_audio_editor_audit.md` / `project_evp_learning_253.md`.

---

### Later, gated (not in this plan's commitments)
- **VoiceNet as the default order**: the scorer itself is phase 9b; only flipping the default sort to
  voice-like first waits on its gate (phase 9b, step 6).
- **Refit job** (`EvpWeightRefitJob : IScheduledJob` like `WeightRefitJob`; `EvpScoringVersions`; gate = held-out
  AUC beats active AND fixture ordering passes; SuperAdmin activate/rollback on `/admin/evp`): after ≥ 500 rulings
  from ≥ 20 files and the phase-11 histograms show separation.
- **Known voices** (item 254), stereo channel views, frequency brush/notch.

## Reuse (found, not to be rewritten)
`EvpDetector` pipeline and `EvpDetectorTests` fixture generators · `AudioMarkerController.ReplaceCandidates`
semantics · `AvMetadataStripper` temp-file + process pattern · `BenBundleStore.OpenEntryAsync` · `FieldSessionTimelineService`
+ `FieldSessionTimelineBuilder` (clip placement, markers) · `MediaDuration` · `AccountStorageGuard` · `OnnxNsfwScreener`
(loud-degradation pattern) · `StoreReservationExpiryService` / `ScheduledWorkService` / `WeightRefitJob` ·
`Ben.Video.Editor/Components/Toolbar.razor` (TelerikToolBar) · `CaseVideoEditorPage.razor` shell · vendored minimap
plugin · `ApexChart` · `BenTabs`, `BenModal`, `BenContextMenu`, `BenTextBox`, `BenIcon` · `SignInLink.For` ·
`MediaLibraryGrid` multi-select.

## Verification (end to end)
1. Unit: `dotnet test Ben.Web.Tests --filter "Evp|AudioDecoder|FieldSessionAnalysis|AudioClip"` — pinned Version-1
   scores green through every phase; every "discriminates" test run once against the un-fixed code.
2. Hosts: `scripts/run-e2e.sh --filter "AudioEditor|AudioPlayer|CaseMixer|FieldKit|LookAndTruths"` on a fresh
   `BEN_E2E_DB` (all four hosts; never hand-start one; never edit the script mid-run). Export `MediaTools__FfmpegPath`.
3. In the browser (preview tools, dark + light): open `/audio/{id}` as Sarah (ordinary member), scan a 3-minute file,
   watch the mask clear left→right, open a candidate's popover, loop it, press K; open a session as the organizer from
   the event page, scan, pull an EVP, check the guest's library is unchanged; `/admin` shows ffmpeg health; `/admin/evp`
   numbers match SQL.
4. Walk: `BEN_E2E_DB=IsHauntedDb_audio_walk scripts/run-e2e.sh --keep --filter TestCategory=AudioEditorWalk`, read
   the verdicts and every screenshot (a capture is evidence — check it).
5. Help: rebuild screenshots + PDF; read the four pictures.
6. Deploy notes per phase README: which migration (M1–M6), `MediaTools:FfmpegPath` must be set, the worker is a hosted
   service (restart the API).
