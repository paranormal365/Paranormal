# Media Editor Phase — Image Editor & Enhanced Audio

**Planned phase:** Post-Report Builder (after Investigation Report Builder is complete)
**Purpose:** Give investigators and case managers professional-grade tools to analyze, annotate, and enhance media evidence directly inside the platform — removing the need to export files to third-party apps like Pixlr, Adobe Express, or Audacity.

---

## Context

Ghost hunting teams collect large volumes of audio recordings, photographs, and video stills as evidence. Today, members download files to their device, edit them in an external tool, and re-upload the result. This breaks the chain of custody for evidence and makes annotation history invisible to other case members. Building native media editors solves both problems: edits are tied to the original file record, a full edit history is preserved, and edited exports are automatically saved as new file versions under the same case folder.

The image and audio outputs produced by these editors are also foundational building blocks for the upcoming **Ben.Video** integration. Edited and annotated images will be embeddable as video title cards, still frames, and chapter thumbnails. Processed and mixed audio tracks will be usable directly as the audio layer in composed investigation video productions. All derived files share the same `UploadFile` record model and storage path conventions, so Ben.Video will be able to reference them by `UploadFileId` without any additional migration work.

---

## Part 1 — Image & Photo Editor

### Goals

A browser-based photo editor comparable in scope to Pixlr E or Adobe Express. Investigators can open any uploaded image (evidence photo, floor plan scan, screenshot) directly in the editor without downloading it, make adjustments or annotations, and save the result as a new version linked to the original file record.

### Technology

- **Rendering engine:** HTML5 `<canvas>` via **Fabric.js** (MIT) or **Konva.js** (MIT). Both are well-maintained, support layers, object transforms, and export to PNG/JPEG. Fabric.js is the recommended choice — it has better text/object model and wider community support.
- **Color manipulation:** Pure canvas pixel operations via `ImageData` (built-in) — no extra lib needed for brightness/contrast/filter work.
- **Integration point:** A new `ImageEditorPlayer.razor` Blazor component in `Ben.Web.Library/Manage/Media/` — mirrors the pattern of `AddressMapPlayer.razor` (Blazor host + JS interop module).
- **JS module:** `Ben.Web.WebApp/wwwroot/ts/image-editor/` — TypeScript source, compiled via rollup (same pipeline as WaveSurfer).

### Feature Set

#### Adjustment Panel
| Feature | Notes |
|---|---|
| Brightness / Contrast | Canvas pixel-level `ImageData` manipulation |
| Saturation / Hue | HSL conversion in JS |
| Sharpness / Blur | Convolution kernel (3×3) |
| Noise reduction (light) | Median filter approximation |
| Temperature (warm/cool) | RGB channel bias |
| Levels (shadows / midtones / highlights) | Histogram-guided sliders |
| Exposure | Multiplicative gamma |

#### Transform & Crop
- Free crop with aspect-ratio lock (1:1, 4:3, 16:9, custom)
- Rotate 90° CW/CCW, free rotate by degree
- Flip horizontal / vertical
- Perspective correction (drag four corners)
- Resize with scale lock

#### Drawing & Annotation (primary value for investigators)
- Freehand pen (color, line weight, opacity)
- Arrow tool — mark and point to anomalies
- Rectangle / circle / line shapes
- Text overlay — font, size, color, background box option
- Measurement ruler — draws a line and shows pixel distance (useful for comparing object sizes in frame)
- Redaction box — solid fill to obscure faces or identifying info when publishing with pseudonyms
- Magnify lens — creates a zoomed circular inset, draggable anywhere on canvas

#### Layers
- Layer panel: add, delete, reorder, toggle visibility, set opacity
- Each annotation or imported overlay lives on its own layer
- Background photo always on bottom layer (locked by default)
- Layers export flattened to final image

#### Filters (one-click presets)
- None (original)
- Grayscale
- Sepia
- High Contrast (boosts evidence visibility in dark photos)
- Invert (negative — useful for thermal/IR images)
- Night Vision (green tint + grain)
- Heat Map (pseudo-color overlay mapped from luminance — useful for thermal stills)

#### Evidence-Specific Tools
- **Anomaly highlight:** Draw a glowing halo around a selected region (configurable color and pulse — rendered as a layer)
- **Grid overlay:** Configurable grid lines for spatial reference
- **Timestamp/watermark stamp:** Pulls EXIF date/time from file metadata and overlays it in a configurable corner
- **Strip EXIF:** Exports a version with metadata removed (for public sharing)

#### Export & Save
- Save as new file version — creates a new `UploadFile` record in the DB with `SourceUploadFileId` pointing at the original; stores the edit as a JSON blob (`EditStateJson`) so it can be re-opened and continued
- Export to PNG or JPEG (quality slider)
- Export to clipboard (browser `navigator.clipboard`)
- Download to device

### Data Model Additions

```
UploadFile
  + EditStateJson (nvarchar(max), nullable) — Fabric.js JSON snapshot for "continue editing"
  + SourceUploadFileId (int, nullable, FK → UploadFile) — already exists for org file copies; reuse it
  + IsEditedVersion (bool) — flag to distinguish derived files from originals
```

Migration name: `AddImageEditorMetadata`

### UI Entry Points

1. **Case evidence panel** — every image file gets an "Edit" button alongside the existing download/vote buttons. Opens `ImageEditorPlayer` in a full-screen `TelerikWindow`.
2. **User Files tab** (AdminUserDetail) — same "Edit" button.
3. **Org Files** (OrganizationFiles.razor) — same.
4. **Report Builder** — when inserting an image into a report, the "Edit before inserting" option opens the editor inline.

---

## Part 2 — Enhanced Audio Editor

### Current State

The platform already uses **WaveSurfer.js v7** (rollup ESM build) with:
- Waveform visualization
- Spectrogram plugin (configurable resolution 128–4096 FFT)
- Regions plugin (mark and loop sections)
- Async Worker spectrogram rendering with pre-rendered OffscreenCanvas cache

### Goals

Extend the existing `WaveSurferPlayer` into a full editing workstation suitable for EVP (Electronic Voice Phenomena) analysis, noise filtering, and multi-clip assembly. The editor should be powerful enough to replace Audacity for the use cases investigators actually need.

### New Capabilities

#### Destructive Editing (export-based, non-destructive to source)
All edits produce a new derived file (same `SourceUploadFileId` pattern). The original is never modified.

| Feature | Implementation |
|---|---|
| Trim to region | Encode only the selected time range using `Web Audio API` + `MediaRecorder` or `AudioWorkletProcessor` offline render |
| Cut region | Remove a marked region; stitch remaining audio; export |
| Silence region | Replace a region with silence (privacy redaction for case exports) |
| Normalize | Apply peak normalization to the offline buffer |
| Gain / Amplify | Multiply all samples by a factor; warn on clipping |
| Fade in / Fade out | Linear or exponential ramp on start/end or selected region |
| Speed change (pitch-preserving) | `SoundTouchJS` (WSOLA algorithm) for time-stretch without pitch shift |
| Pitch shift | Semitone offset via phase vocoder |
| Reverse | Flip buffer samples for "reverse EVP" analysis |

#### Non-Destructive Processing (real-time, applied at playback)
Implemented as a chain of `AudioNode`s in the `AudioContext` graph — zero writes to disk until the user exports.

| Processor | Control |
|---|---|
| Graphic EQ (10-band) | 10 `BiquadFilterNode` nodes, drag sliders per band |
| High-pass / Low-pass filter | Cutoff frequency + resonance (Q) — common for removing wind rumble or hiss |
| Noise gate | `AudioWorkletProcessor` — attenuates signal below a threshold in dB |
| Compressor / Limiter | `DynamicsCompressorNode` (native Web Audio) |
| Reverb / Room (removal assist) | Convolution node with a flat impulse — helpful for de-reverbing field recordings |
| Stereo → Mono collapse | Channel merger / splitter + average |

#### Spectrogram Enhancements
- **Frequency brush tool:** Click-drag on the spectrogram to select a frequency band × time region. Combined with the notch filter, this lets investigators surgically remove a persistent hum (60 Hz electrical, HVAC, etc.) without affecting the rest of the recording.
- **Spectrogram export:** Save the current spectrogram view as a PNG (evidence documentation).
- **Frequency ruler:** Labeled Y-axis with major grid lines at 100 Hz, 500 Hz, 1 kHz, 5 kHz, 10 kHz, 20 kHz.
- **Mel-scale toggle:** Switch Y-axis from linear to mel-scale (closer to human hearing perception — better for voice EVP detection).
- **Colormap selector:** Jet, Viridis, Inferno, Grayscale — different palettes reveal different anomalies.

#### EVP Analysis Tools (paranormal-specific)
- **EVP Marker:** Click the waveform to drop a named marker with a timestamp, label, and confidence rating (Possible / Probable / Confirmed). Markers are stored in `UploadFileAudioConfig` or a new `AudioMarker` table and are visible when the file is played back in the report or evidence panel.
- **Voice frequency overlay:** Toggleable band shading on the spectrogram highlighting the 300 Hz–3 kHz human voice range — anomalies outside normal voice that fall in this band are flagged visually.
- **Silence detection:** Automatically marks regions below a configurable dB threshold with a light shading (helps investigators quickly find moments of activity).
- **Clip waveform comparison:** Side-by-side waveform view of two clips for quick comparison (e.g., original vs. processed).

#### Multi-Track Mixer (Phase 2 of the audio editor)
For assembling final investigation audio reports from multiple clips:
- Up to 8 tracks in a vertical timeline grid
- Drag clips from the case evidence panel onto any track
- Per-track: mute, solo, volume fader, pan control
- Time-ruler at top for alignment
- Single-pass export to one mixed file (Web Audio offline rendering)
- Exported mix is saved as a new `UploadFile` of type "Audio Mix" linked to the case

### Data Model Additions

```
UploadFileAudioConfig (existing)
  + EditStateJson (nvarchar(max), nullable) — serialized editor state (EQ values, markers, region labels)

AudioMarker (new table)
  + AudioMarkerId (int PK)
  + UploadFileId (int FK → UploadFile)
  + TimeSeconds (decimal(10,4))
  + Label (nvarchar(200))
  + ConfidenceLevel (tinyint — 0=Possible, 1=Probable, 2=Confirmed)
  + Note (nvarchar(max), nullable)
  + CreatedByAppUserId (nvarchar(450) FK)
  + DateCreated (datetime2)
```

Migration name: `AddAudioEditorAndMarkers`

### UI Entry Points

1. **Evidence panel** — Audio files get an "Edit" button; opens the enhanced `WaveSurferPlayer` in full-screen editor mode (extra toolbar row with processing tools).
2. **Case report builder** — "Edit audio before inserting" opens the editor so investigators can trim to the relevant clip before embedding.
3. **Multi-track mixer** — Separate page at `cases/{caseId}/audio-mix` — accessible to case manager and assigned investigators with Audio permission.

---

## Implementation Sequencing

**Status as of 2026-08-05:** Phases A–C and Phase D steps 1 and 4–8 are complete and merged into `develop`. Remaining Phase D work (steps 2, 3, 6) is in progress on `feature/media-editor-phase-d3`. Phase E has not started.

### Phase A — Image Editor Foundation ✅ Complete
1. ✅ Add Fabric.js to the rollup build
2. ✅ Build `image-editor.ts` module: canvas init, load from URL, adjustment pipeline, layer model, export
3. ✅ Build `ImageEditorPlayer.razor` (Blazor host, JS interop bridge, save/export buttons)
4. ✅ Add `IsEditedVersion` + `EditStateJson` columns to `UploadFile` (migration)
5. ✅ Add API endpoint `PUT /api/upload-files/{id}/edit-state` to persist editor JSON
6. ✅ Wire "Edit" button into case evidence panel (images only)

### Phase B — Image Editor Full Feature Set ✅ Complete
1. ✅ Drawing and annotation tools (pen, arrow, shapes, text, redaction box)
2. ✅ Layers panel
3. ✅ Evidence-specific tools (anomaly highlight, timestamp stamp, grid overlay)
4. ✅ Perspective correction and measurement ruler
5. ✅ Export to new file version (controller + service)

### Phase C — Enhanced Audio: Spectrogram & EQ ✅ Complete
1. ✅ Frequency ruler and mel-scale toggle in existing spectrogram
2. ✅ Colormap selector
3. ✅ Graphic EQ panel (10-band `BiquadFilterNode` chain)
4. ✅ High-pass / low-pass filter controls
5. ✅ Noise gate `AudioWorkletProcessor`
6. ✅ Compressor controls (expose existing `DynamicsCompressorNode`)

### Phase D — Audio Editing & EVP Tools
1. ✅ EVP Marker tool + `AudioMarker` entity + API endpoints (`AudioMarkerController`, waveform overlay, marker panel)
2. ⬜ Silence detection shading — **in progress** on `feature/media-editor-phase-d3`
3. ⬜ Voice frequency overlay — **in progress** on `feature/media-editor-phase-d3`
4. ✅ Trim / cut / silence region
5. ✅ Normalize, gain, fade in/out
6. ⬜ Speed change — **in progress**; will use a ported SMB phase-vocoder in C# (see note below), not SoundTouchJS
7. ⬜ Pitch shift — **in progress**; same SMB phase-vocoder approach
8. ✅ Reverse
9. ⬜ `EditStateJson` persistence to `UploadFileAudioConfig` — not started; not required for the destructive-edit tools shipped so far

> **Architecture deviation from the original plan (steps 4, 5, 8, and the planned 6/7):** these were originally spec'd as client-side Web Audio API + SoundTouchJS. Implemented instead as **server-side NAudio** operations (new `AudioEditor.cs` static helper, extending the existing `AudioClipper` pattern from the pre-existing "Trim to region" / Save Clip feature) exposed through a single `POST /api/upload-files/{fileId}/audio-edit` endpoint. Reasoning: the app had no client-side audio decode/encode infrastructure at all (no `OfflineAudioContext`, no WAV encoder) while server-side NAudio processing already existed and worked for large files without loading them into browser memory. Speed/pitch (steps 6–7, not yet built) will follow the same server-side approach using a ported public-domain SMB phase-vocoder algorithm rather than SoundTouchJS, to avoid introducing a client-side audio pipeline for just those two operations.

### Phase E — Multi-Track Mixer
1. Multi-track timeline grid component
2. Drag-from-evidence-panel to track
3. Per-track controls (mute, solo, gain, pan)
4. Offline mix export
5. Save as new case `UploadFile`

---

## Ben.Video Integration

When **Ben.Video** is completed and integrated into the platform, the outputs from both editors feed into it as first-class sources:

### Image → Video
| Image editor output | Ben.Video use |
|---|---|
| Annotated evidence photo (PNG/JPEG) | Still-frame clip in video timeline |
| Annotated floor plan or map screenshot | Intro or chapter title card |
| Edited photo with anomaly highlights | Thumbnail for video chapter |
| Redacted/public-safe version | Embed in publicly published video report |

### Audio → Video
| Audio editor output | Ben.Video use |
|---|---|
| Trimmed EVP clip | Audio track for a dedicated EVP segment |
| Multi-track mix export | Full investigation audio bed for video |
| Silence-gated field recording | Background ambient layer |
| Pitch-shifted or reversed clip | Side-by-side comparison segment |

### Shared infrastructure
- All editor outputs are stored as `UploadFile` records with `SourceUploadFileId` lineage — Ben.Video references them by `UploadFileId` directly; no file duplication.
- The `EditStateJson` column on `UploadFile` and `UploadFileAudioConfig` means a video producer can re-open the source editor, refine the clip, and re-export; the video project picks up the new file version.
- `AudioMarker` timestamps map directly to Ben.Video chapter/cue points — EVP markers can be imported as video chapter markers automatically.
- The same `UploadFile` permissions model applies: a Ben.Video project can only reference a file the current user already has access to.

---

## Design Principles

- **Non-destructive first:** Edits always produce a new derived file. The source file is read-only. The edit state JSON allows the derived file to be re-opened and further adjusted.
- **Chain of custody:** Every derived file stores `SourceUploadFileId`, who created it, and when. The SuperAdmin can see the full lineage.
- **Evidence integrity:** The original file download endpoint will include a notice if derived versions exist, so reviewers know the originals are preserved.
- **No third-party cloud processing:** All audio/image processing runs in the browser (Web Audio API, canvas pixel ops). No bytes leave the server to an external service.
- **Permission model:** Editing requires the same permission as uploading — `Create` permission on the relevant file table for the case or org. Creating EVP markers requires at minimum `Read` + participation in the case.
