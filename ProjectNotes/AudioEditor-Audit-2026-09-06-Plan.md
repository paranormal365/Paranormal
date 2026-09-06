# Audio editor — full audit and remediation plan

## Context

Ben asked (2026-09-06) for the same treatment the video editor got: step through every aspect of
the audio editor, find gaps, programming issues and missing functionality, bring the tests in line
with the current code, walk every use case, and produce a plan for getting it all to work as it
should. He added a second question mid-way: could the audio editor become a WASM app alongside the
video editor without losing functionality, and is that worth doing?

**What "the audio editor" is.** Not a separate project. It is the site's Blazor Server audio
surface: `Ben.Web.Website.Library/Manage/Audio/` (`AudioFilePreview.razor` 2044 lines — the
editor; `WaveSurferPlayer.razor` + `.razor.js` 1588 lines — vendored WaveSurfer 7 wrapper with a
custom spectrogram, Web Audio EQ/filters/gate/compressor and silence detection;
`WsRegionExplorer.razor`; `WaveSurferOptions.cs`), the case mixer at
`Organization/Cases/CaseAudioMixPage.razor`, and the server side in
`Ben.Data.WebApi/Services/Audio/` (`AudioEditor`, `AudioMixer`, `AudioSourceReader`,
`EvpDetector`, `SmbPitchShifter`) behind `UploadFileAudioEditController`,
`UploadFileAudioClipController`, `AudioMarkerController`, `CaseAudioMixController`,
`UploadFileAudioConfigController`. `ProjectNotes/Audio Editor.md` describes an unbuilt WASM
"Ben.Audio"; that is the subject of the assessment at the end, not of the fixes.

**How the evidence was gathered.** Three read-only sweeps (UI surface and JS interop; server
pipeline and endpoints; tests, docs, backlog and harness) plus my own reading of the three most
serious UI claims. No screen walk yet — the dev settings point at the live database, so the walk
needs an isolated stack and is Phase 0 below. Decisions Ben has already made: walk as Phase 0;
long files get a ceiling on edits plus a streamed EVP scan; the mixer gets a real live preview;
WASM is an assessment, not a build phase.

Corrections to the sweeps after checking: there is **no** CDN load of wavesurfer on master
(`WaveSurferPlayer.razor.js:48` imports the vendored copy); drop that item. `CaseAudioMixPage`
lives under `Organization/Cases/`, not `Manage/Audio/`.

## What works

The server layer is well built and well tested: 8 destructive operations all implemented and all
wired to buttons; EVP detection with a 26-test accuracy fixture; marker CRUD, candidate replace,
review flow (48 tests); clip endpoint with `SourceMarkerId` lineage; the mixer renders; every
mutating endpoint gates on `FileAudienceAccess.CanViewFileAsync`; the MP3 decode path uses NLayer
so it works off Windows. All JS functions the components call exist (46 of 46); all
`[JSInvokable]` targets exist (20 of 20). The existing server tests are **current**, not stale —
"update the tests" here means adding what is missing and fixing two weak ones, not rewriting.

## Findings

Severity: **S** the editor loses or exposes work, or the API can be taken down; **M** a person
hits it in a normal session; **L** polish. Letters are UI, numbers are server. Phase 0 confirms or
corrects each on screen.

### UI (verified in source: A, B, H, I)

| id | sev | finding | where |
|---|---|---|---|
| A | S | **The full-view editor cannot really close.** `<BenModal Visible="@_showModal">` has no `@bind-Visible` (every sibling modal in the file does); `_showModal` is only ever set `true`; the reset handler `OnModalVisibleChanged` is dead. Closing hides the modal until the parent's next render re-supplies `true`. | `AudioFilePreview.razor:83, 1352, 1403` |
| B | S | **Silence detection destroys its own regions and hijacks the edit target.** Silence regions are added in JS with ids `silence-…`, each fires `region-created`, the handler treats them as user regions, clears the others and sets `_lastDrawnRegion` to a machine region — so Cut/Silence act on detected silence, not on what the person drew. | `WaveSurferPlayer.razor.js:1366`; `AudioFilePreview.razor:1254-1265` |
| H | S | **The region explorer plays the first region's audio for every later region** (`OnParametersSetAsync` bails when `_source` is set) and saves the *new* region's coordinates — the saved file is not what was heard. | `WsRegionExplorer.razor:357-361, 482` |
| I | M | Explorer's `@bind-Visible` binds to its own parameter without invoking `VisibleChanged`; nested explorer drops `OnClipSaved`. | `WsRegionExplorer.razor:22, 258, 294` |
| C | M | Changing FFT resolution or colormap silently resets colormap to jet and mel scale off while the controls say otherwise. | `WaveSurferPlayer.razor.js:739, 822`; `WaveSurferPlayer.razor:331` |
| D | M | The HP/LP/compressor/noise-gate enable checkboxes are bare `@bind` with no handler; only nudging a slider applies them. | `AudioFilePreview.razor:186, 191, 200, 212` |
| E | M | All eight edit buttons go dead, silently, if the file-type list fails to load (`_saveClipTypeId`). | `AudioFilePreview.razor:1022-1028, 1926` |
| F | M | Edited files show "0:00–0:00" in Saved Clips (edit endpoint never sets `RegionStart/End`). | `UploadFileAudioEditController.cs:117-133`; razor `:523` |
| G | M | Region bounds are sent with every operation but honoured only by Cut/Silence; the panel layout implies Gain/Fade/Speed/Pitch respect the region. | razor `:1941-1952`; controller `:93-103` |
| J | M | Marker ▶ only seeks, never plays; `playRegion` on a point marker is silent. | razor `:268, 1246`; js `:596-623` |
| K | M | **Mixer**: transport hard-disabled with no player; every clip drawn 15 s wide regardless of length; 9th clip stacks at offset 0; ✕ remove suppressed by `preventDefault` on pointerdown; every export failure says "please try again" including 403; the Audio Mixer button on the case page is ungated while export needs `Cases.Create`. | `CaseAudioMixPage.razor:36-38, 130-153, 230, 262-271`; `.razor.js:19-25`; `CaseDetail.razor:82` |
| L | M | `UploadFileAudioConfig` (table, controller, client, `ToWsConfig`) is fully orphaned — nothing reads or writes it; zoom/colours/spectrogram/EQ never persist. | `WaveSurferOptions.cs:598-660` |
| M | L | Region notes exist only inside the explorer. | `WsRegionExplorer.razor:516-585` |
| N | L | Two Save-as-clip buttons disagree on the Normalize default. | razor `:1305`; explorer `:482` |
| O | L | Marker and child-clip loads swallow every failure — no markers looks the same as failed. | razor `:1069, 1086` |

Also dead: envelope plugin and its 5 wrappers, `Minimap`/`SpectrogramWindowed` flags, 8 player
methods and 8 player parameters with no consumer, `_currentTime` written and never rendered.

### Server

| # | sev | finding | where |
|---|---|---|---|
| 1 | S | Every path decodes the whole file to `float[]` (peak ~3× PCM) with no ceiling; a 1.7 h stereo WAV — the EVP use case — is an OOM 500. | `AudioEditor.cs:187-193`; `AudioMixer.cs:107`; `EvpDetector.cs:334` |
| 2 | S | `SpeedRatio` has no lower bound: `0.001` allocates 1000× the samples then phase-vocodes them. | `UploadFileAudioEditController.cs:67-69`; `AudioEditor.cs:161` |
| 3 | S | Mixer `OffsetSeconds` unvalidated → multi-GB allocation or `int` overflow → 500. | `AudioMixer.cs:32-57` |
| 6 | S | **Privacy laundering**: a viewer of a private file can derive a copy with `IsPublic=true`; the source's `IsPublic` is never a ceiling. Edit and clip endpoints. | edit `:80,130`; clip `:104,158` |
| 9 | S | Any viewer can overwrite or delete the owner's audio config; GET has no per-file check. | `UploadFileAudioConfigController.cs:25-88` |
| 4 | M | Mixer dereferences `StoragePath!` with no `FileData` fallback (legacy rows) → 500. | `CaseAudioMixController.cs:76` |
| 5 | M | Corrupt or mislabelled audio is a 500 on edit/clip but a 400 on scan (only `NotSupportedException` caught). | edit `:107`; clip `:82,133`; scan `:303` |
| 7 | M | Bytes written before the row is validated; an unbounded `Label` throws on save and orphans the file. | edit `:137-150`; clip `:168-194`; mix `:92-128` |
| 8 | M | Marker Create/Update accept inverted/negative spans and >200-char labels; Review/Candidates reject them. | `AudioMarkerController.cs:91-154` |
| 10 | M | A clip wholly past EOF persists a 44-byte WAV with 201 and a false duration. | clip `:94,181`; `AudioClipper:246` |
| 11 | M | Derived files never get `DurationSeconds`/sample rate; the comment claiming otherwise is wrong. | edit `:144-148`; mix `:114-118` |
| 12 | M | Mixer downmixes stereo to mono, applies `tanh` to everything, linear-interp resample aliases. | `AudioMixer.cs:34-99` |
| 13 | M | `GainDb`/fades accept NaN and absurd values (NaN → silent file, 201). | edit `:64-65,99` |
| 14 | M | Mix controller never checks the user-id claim → FK 500 after bytes are written. | `CaseAudioMixController.cs:46` |
| 15 | L | Mix has no `ParentFileId` lineage. | `:97-104` |
| 16 | L | `GET /clips` has no `CanView` check. | `UploadFileController.cs:755` |
| 17 | L | No dedicated rate limit on the heavy synchronous endpoints. | `RateLimiting.cs` has the plumbing |

### Tests, docs, harness

- `scripts/run-e2e.sh` exports only `BEN_BASE_URL`; `AudioScrubModeTests` needs `BEN_USER_PASSWORD`
  and silently `Assert.Ignore`s — **the only audio browser test never runs under the harness**.
- Weak tests: `CaseAudioMixControllerTests.Export_SoloedTrack_ExcludesNonSoloedTracks` asserts
  only a 200; `UploadFileAudioConfigControllerTests.Upsert_ThrowsUnauthorized…` pins a 500 as the
  contract; the config test mapper projects 22 of ~35 record fields.
- No test for: fade longer than clip, `GainDb` bounds, MP3/M4A decode (the NLayer fix has no
  regression test; `basement-evp.m4a` is never decoded by a unit test), any case-membership branch
  of `FileAudienceAccess` on an audio endpoint, `EvpDetectionOptions.Validate` (six rules),
  non-preset scan options, 15 of 18 adapter methods, the E0 pointerdown regression, anything in
  the EVP panel / EQ / spectrogram / edits / mixer in a browser.
- **No help article exists for the audio editor**; `your-files.md` never mentions audio.
- `Media-Editor-Phase.md` promises never delivered: `EditStateJson` persistence (marked ⬜),
  frequency brush + notch, spectrogram PNG export, clip comparison, de-reverb, markers → video
  chapters, "download notice when derived versions exist"; its "all processing in the browser"
  principle contradicts the shipped server-side design. E5 (keyboard review shortcuts,
  spectral-flatness score, batch dismiss) deferred. `Audio Editor.md` describes the unbuilt
  Ben.Audio.

## Plan

Rules for every phase, from the video editor arc: own branch `feature/audio-editor-phase-N-…`
with `README-audio-editor-phase-N.md` at creation; `git branch --show-current` before the first
commit; merge `--no-ff` and check `git diff <tip>..HEAD --stat` is empty; every new test run
against the un-fixed code once to prove it discriminates; help updated in the same PR as the
feature; verify on screen, not by tests alone; test as an ordinary member (Sarah), never
SuperAdmin; passwords come from env via Playwright's `RequiredSecret`, never typed. Sizes:
S ≈ half a day, M ≈ 1–2 days, L ≈ 3+.

### Phase 0 — Screen walk on the isolated stack (S/M)

**Goal.** Replace the static findings with observed behaviour and produce before-screenshots.

- `scripts/run-e2e.sh`: map the seeded passwords into the suite's env (`BEN_SUPERADMIN_PASSWORD`,
  `BEN_USER_PASSWORD`, `BEN_MEMBER_PASSWORD`, `BEN_VIEWER_PASSWORD`) the way `SA_TOKEN` is derived
  from the gitignored `appsettings.Development.json`; never echo them. Add `AudioPlayer` to the
  category lists in `Ben.Web.Playwright/README.md` and `ProjectNotes/Running-Playwright-Tests.md`.
- New `Ben.Web.Playwright/Tests/AudioEditorWalkTests.cs` (`[Category("AudioEditorWalk")]`,
  `[Explicit]`), modelled on `AudioScrubModeTests.cs` (upload `Fixtures/test-audio.mp3` to the
  seeded case's Files tab, right-click → Open Full View) and `Capture/HelpMediaCapture.cs`
  (screenshots). One test per use case, each continuing past failure and recording a verdict,
  PNGs to `ProjectNotes/AudioEditor-Walk-2026-09-06/`: compact preview; open/close/reopen full
  view (A); draw two regions; silence detection then a user region (B); spectrogram colormap →
  resolution (C); tick HP without touching the slider, read the JS graph state (D); each of the 8
  edits and the Saved Clips range (E/F/G); explore two regions in turn and compare
  `getDuration()` (H/I); scan at each sensitivity, keep/dismiss, marker ▶ (J/O); mixer with 9
  clips, remove, export, and again as the Viewer persona (K); region note visibility (M);
  Normalize defaults (N).
- Long-file probe by hand with `--keep`: generate a 90-minute stereo WAV into the scratchpad with
  `ffmpeg -f lavfi -i anoisesrc`, upload it, run Normalize and a scan, record API RSS and the
  HTTP outcome (server 1).
- Write `ProjectNotes/AudioEditor-Audit-2026-09-06.md` in the format of
  `VideoEditor-Audit-2026-09-05-Findings.md`, then re-rank Phases 1–7 from what was seen.

**Verify.** `BEN_E2E_DB=IsHauntedDb_audio_walk scripts/run-e2e.sh --keep --filter AudioEditorWalk`
and open `http://localhost:5078` yourself for A, B, H and K — a modal that reopens on the next
SignalR render does not show in a screenshot. Stack teardown afterwards (drop the throwaway DB,
remove `.uploads-IsHauntedDb_audio_walk`).

### Phase 1 — Server safety: ceilings, validation, privacy (M)

- `AudioSourceReader.cs`: a `Probe` that reads `TotalTime`/`WaveFormat` before decoding; an
  `AudioTooLargeException : NotSupportedException` (existing `catch (NotSupportedException)`
  sites already answer 400) with a stated ceiling for destructive edits (30 min of source,
  configurable); wrap `FormatException`/`InvalidDataException`/`EndOfStreamException`/NAudio's
  `MmException` the same way so corrupt input is 400 everywhere (5).
- `EvpDetector.cs`: `ReadMonoDownsampled` — decode in a streaming window to 16 kHz mono (the
  detector band-passes 300–3400 Hz, so nothing is lost) so an hour-long recording scans without
  the ceiling (1, Ben's decision).
- `UploadFileAudioEditController.cs`: `SpeedRatio` in [0.25, 4]; `GainDb` finite in [−60, 24];
  fades finite, ≥ 0, sum ≤ duration; `Start/End` finite and within duration; `Label` ≤ 200;
  **`IsPublic = request.IsPublic && source.IsPublic`** with a 400 saying a private recording
  cannot be made public from here (6); validate the row before `WriteAsync` and delete the file
  if `SaveChanges` fails (7); set `DurationSeconds`/sample rate from the produced WAV and
  `RegionStart/End` for Cut/Silence (11, F).
- `UploadFileAudioClipController.cs`: same `IsPublic` ceiling; reject a start past EOF (10);
  duration from the clamped range; `GET clips` gets `CanViewFileAsync` (16).
- `AudioMarkerController.cs`: one private `ValidateSpan(start, end, label)` reused by Create,
  Update, Review and Candidates (8).
- `UploadFileAudioConfigController.cs`: PUT/DELETE require `CanManageFileAsync`; GET requires
  `CanViewFileAsync` (9).
- `CaseAudioMixController.cs`: `OffsetSeconds` finite in [0, 3600], `GainDb` in [−60, 24], `Pan`
  in [−1, 1], ≤ 8 tracks (3); `FileData` fallback shared with the edit controller (4); user-id
  claim checked before any write (14); `ParentFileId` = first track (15).
- `RateLimiting.cs`: `AudioProcessingPolicy` (config `RateLimits:AudioProcessingPerMinute`,
  default 10) on edit, clip, scan and mix POSTs (17).

**Tests (Ben.Web.Tests).** Move `CreateSilentWav`/`CreateSineWav`/`ReadWavPcm16`/`SeedFileAsync`
into `TestMedia.cs` so every audio test shares them. New: `Speed_below_quarter_is_400`,
`Gain_NaN_is_400`, `Fade_longer_than_clip_is_400`, `Label_over_200_writes_nothing`,
`Private_source_cannot_produce_public_derivative` (edit and clip), `Corrupt_bytes_are_400`,
`Oversize_source_is_400` (ceiling made settable for the test), `Derived_file_has_duration_and_region`,
`Case_member_can_edit` (seed membership + `CaseFile` as `FileAudienceAccessTests` does),
`Range_past_eof_is_400`, `Create_rejects_inverted_span`, `Viewer_cannot_put_or_delete_config`,
`Get_config_requires_view`, `Offset_over_ceiling_is_400`, `Legacy_row_with_FileData_mixes`,
`Unknown_user_claim_writes_nothing`, `Mix_has_parent_file_id`. New `AudioSourceReaderTests` with
a checked-in MP3 fixture (link `Ben.Web.Playwright/Fixtures/test-audio.mp3`) and
`basement-evp.m4a`; `EvpDetectionOptionsTests` for every `Validate()` rule;
`EvpDetectorTests.Downsampled_reader_matches_full_rate`.

**Verify.** Re-run the Phase 0 long-file probe: Normalize answers 400 with the reason; the scan
completes with API RSS well under the earlier figure.

### Phase 2 — The full-view editor (L)

- **A**: `@bind-Visible="_showModal"` and route closing through `OnModalVisibleChanged` so the
  player is torn down and state reset (fix the `_spectrogramLabels` default disagreement there).
- **B**: tag every region with a kind in JS (`user` / `silence` / `marker` / `clip`) and pass it
  through `region-created`; `OnModalRegionCreated` ignores non-user kinds; `ClearUserRegionsAsync`
  clears only user regions. Extract the "which region is the edit target" decision to a plain
  `RegionSelection` class in `Manage/Audio/` with tests.
- **C**: resolution and colormap changes carry the current colormap and mel state through
  (`SetSpectrogramResolutionAsync` and `setSpectrogramResolution/Colormap` read
  `instance.spectrogramMeta` rather than rebuilding it from defaults).
- **D**: `@bind:after="Apply…Async"` on the four enable checkboxes.
- **E/O**: file-type and marker load failures render an inline reason; `CanApplyEdit`'s tooltip
  says why it is off.
- **G**: Start/End move into the Cut/Silence group; other operations send null bounds.
- **J**: marker ▶ plays (span → `PlayRegionAsync`; point → seek + play a context window).
- **N**: one shared Normalize default. **M**: show a region's note on its saved-clip card.
- Surface Phase 1 refusals verbatim in the edit/clip error path.

**Tests.** `RegionSelectionTests`; Playwright `AudioEditorTests` (category `AudioEditor`, not
Explicit): `FullView_closes_and_reopens`, `SilenceDetection_survives_a_user_region`,
`HighPass_checkbox_applies_without_the_slider`, `Cut_creates_a_clip_with_its_range`,
`EditButtons_explain_when_file_types_fail` (route `file-types` to 500).

### Phase 3 — The region explorer (S)

Key the load on `(FileId, Start, End)` and reload when it changes; `Visible` + `VisibleChanged`
instead of binding to its own parameter; nested explorer passes `OnClipSaved`; `SaveClipAsync`
uses the loaded key. Tests: a plain `RegionExplorerKey.ShouldReload` unit test; Playwright
`RegionExplorer_second_region_plays_its_own_audio` (compare `getDuration()`),
`Nested_explorer_save_refreshes_the_parent`.

### Phase 4 — The case mixer (M)

- Live preview in `CaseAudioMixPage.razor.js`: decode each clip once (`decodeAudioData`),
  schedule `AudioBufferSourceNode`s at their offsets through `GainNode`/`StereoPannerNode`,
  honour mute/solo — the browser twin of `AudioMixer.Mix`, and what a WASM mixer would reuse.
- Clip width from `DurationSeconds` (populated by Phase 1; dashed "length unknown" fallback);
  a 9th clip is refused with a message; `preventDefault` only when the target is not the ✕;
  403 → "you need the Create permission on this case", 400 → server text.
- `CaseDetail.razor:82`: gate the Mixer button on the same `Cases.Create` check the export uses.
- `AudioMixer.cs`: keep stereo sources stereo, soft-clip only when the peak exceeds 1, resample
  with NAudio's `WdlResamplingSampleProvider` (12).

**Tests.** Fix `Export_SoloedTrack…` to assert the muted track's tone is absent (reuse
`EstimateFrequencyHz`); `Stereo_source_keeps_its_channels`, `Unity_mix_is_not_compressed`;
Playwright `CaseMixer_viewer_sees_no_button`, `_remove_clip_works`, `_ninth_clip_is_refused`,
`_preview_plays` (assert `AudioContext.state === 'running'`).

### Phase 5 — Persistence (M)

Use the orphaned `UploadFileAudioConfig`: load on full-view open through the existing adapter
(`BenAdminClientAdapter.Media.cs`) and `ToWsConfig()`; debounce-save when the caller can manage
the file. Add `EditStateJson` (migration in `Ben.Data.Source/Migrations/`, regenerate the
`.Generated.cs`, update record and profile) carrying EQ bands, HP/LP/compressor/gate, FFT size,
colormap, mel, silence threshold. Extract `AudioEditorState` (record + `ToJson/FromJson/Merge`)
as a plain class. "Reset to defaults" calls DELETE. Fix the config test mapper to project every
field and turn the `Upsert_ThrowsUnauthorized` test into a 401 contract. Tests:
`AudioEditorStateTests`, `Put_persists_edit_state_json`, Playwright
`AudioEditor_settings_survive_reload`. **Migration reaches the live DB only at deploy — Ben runs it.**

### Phase 6 — Test close-out (M)

`BenAdminClientAdapterMediaTests` for the 15 untested adapter methods (fake `IWebApiClient`;
verify URL, verb, body, and the null-vs-empty scan contract in `WebApiClient.cs:601-625`);
`EvpDetectorTests.Custom_options_change_the_candidate_count`; a Playwright guard for the E0
pointerdown regression (region move/resize reachable by mouse); promote the Phase 0 walk's
non-destructive cases into `AudioEditorTests`/`CaseMixerTests` and keep the walk `[Explicit]`.
Full run: `scripts/run-e2e.sh --filter "AudioPlayer|AudioEditor|CaseMixer"` green on a fresh
`BEN_E2E_DB`, then the whole suite once.

### Phase 7 — Help and docs (S)

New `Ben.Web.Services/Help/Content/using-the-audio-editor.md` (front matter like
`using-the-video-editor.md`; guarded by `HelpCatalogCompletenessTests`): opening full view,
regions, listening tools ("these change what you hear, not the file"), spectrogram, silence
detection, EVP scan and review, edits ("these make a new file; the original is never changed"),
saved clips, the case mixer, limits (the ceiling, private stays private). Screenshots via a
`HelpMediaCapture` case into `wwwroot/help/media/using-the-audio-editor/`; a `HelpLink` on the
full-view toolbar and the mixer page. `Media-Editor-Phase.md`: mark D9 done, retire the
"all in the browser" principle with a pointer to the deviation note, move the never-built items
(frequency brush + notch, spectrogram PNG, clip comparison, de-reverb, markers → chapters,
download notice, E5) into `Future-Improvements.md` labelled not built. `Audio Editor.md` →
replaced by a pointer to the assessment below.

### WASM assessment (document only, Ben's decision afterwards)

**Would it lose functionality? No.** Everything the editor does maps onto what
`Ben.Video.Editor`/`Ben.Wasm.Video` already has:

| Capability today | In the browser | Notes |
|---|---|---|
| 8 destructive edits (NAudio) | ffmpeg.wasm filters already in `FfmpegService` (trim, fade, volume, `atempo`, `asetrate`/pitch, reverse, resample) | Better resampling than `AudioMixer`'s linear interpolation; `SmbPitchShifter` not needed |
| EQ / HP / LP / gate / compressor, spectrogram, silence detection | Already client-side JS | Move as-is |
| EVP scan | `EvpDetector.Detect(float[] mono, …)` is pure C# with no NAudio dependency — compiles to WASM unchanged; feed it 16 kHz mono from ffmpeg.wasm | Extract to a shared `Ben.Audio.Core` used by API and WASM |
| Mixer | `amix` (already in `AudioMixPlanner`) for export; the Phase 4 Web Audio preview for listening | |
| Markers, review, notes, saved-clip rows, config, permissions | **Stay server-side**; WASM calls the same APIs signed in through the phase 12 handoff | |
| Local files, long recordings, sidecar, OPFS, media library, project save, publish back | All exist in `Ben.Wasm.Video` | |

**Memory reality for 1–2 h recordings.** A 1.7 h stereo 44.1 kHz recording is ~1.1 GB as float32.
WaveSurfer's `decodeAudioData` materialises that today, in the browser, under Blazor Server — the
current editor already crawls or fails at this size; only the server ceiling changes. In WASM the
workable pattern is never to decode the whole file: peaks and the spectrogram from a 16 kHz mono
proxy (~100 MB int16 for 1.7 h), edits and scans as ffmpeg passes over the OPFS source, and the
sidecar beyond that. That is *more* capable on long recordings than the server path, not less.

**What it would cost.** Each person pays the decode on their own machine (minutes for a long MP3
in ffmpeg.wasm); the 1588-line WaveSurfer module would be rebuilt on `AudioWaveform.razor` or
carried across as a second waveform stack; iPad/phone WASM memory limits make it a desktop tool;
the compact in-page preview and marker review on the case Files tab must stay in Blazor Server,
so two players coexist during any transition.

**Recommendation.** Do not build a separate Ben.Audio. Fix the shipped editor (Phases 1–7) first —
its defects live in the components any move would carry along. Then extract `Ben.Audio.Core`
(`EvpDetector`, `EvpDetectionOptions`, `RegionSelection`, `AudioEditorState`, a mix plan shared
with `AudioMixPlanner`). If long offline recordings become the ask, add an "audio workstation"
mode inside `Ben.Wasm.Video` — same shell, handoff, OPFS, sidecar, media library — as a video
editor phase 13+, starting from "open this recording in the editor" on a Saved Clips card. Keep
Blazor Server for the quick preview and for marker review, which are network-cheap and
permission-heavy.

## Verification (every phase)

1. `dotnet build Ben.slnx` with 0 warnings; `dotnet test` for `Ben.Web.Tests` (audio classes at
   minimum) and `Ben.Video.Tests`.
2. Every new test run once against the un-fixed code and seen to fail.
3. Browser: `scripts/run-e2e.sh --keep` on a throwaway `BEN_E2E_DB`, the audio categories, then
   open `http://localhost:5078` as Sarah and repeat the phase's walk items by hand. Screenshots
   before/after into `ProjectNotes/AudioEditor-Walk-2026-09-06/`.
4. Help and `Media-Editor-Phase.md` updated in the same PR; each closed finding recorded in
   `ProjectNotes/AudioEditor-Audit-2026-09-06.md`.

## Notes

- Sizing: Phase 0 S/M, 1 M, 2 L, 3 S, 4 M, 5 M, 6 M, 7 S. Phase 2 depends on 1 (refusal text),
  4 on 1 (durations), 5 on 1 (config auth) and 2, 6 on 2–5, 7 on 2–5 (screenshots).
- Item 6 (privacy laundering) and 9 (config auth) are security fixes and ship in Phase 1
  regardless of how the walk re-ranks the rest.
- Deploy note: Phase 5's migration must reach the live database at deploy time; Phases 1–4 need
  no schema change.
