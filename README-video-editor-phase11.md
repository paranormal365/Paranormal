# Video editor phase 11 — tests on real media

Branch: `feature/video-editor-phase11-tests`, cut from `master` after phases 5–10 merged.
Plan: `ProjectNotes/VideoEditor-Audit-2026-09-05.md`, phase 11.
Follows `README-video-editor-phase10.md`.

## Why this phase exists

Every phase so far found things on screen that its tests had passed over, and several of those
things were mine, added in the same phase. The pattern is consistent enough to be worth naming:
the service layer is heavily tested and the layer between a person and those services is not, so a
control that never renders, a gate that excludes the case it was written for, or a comment in the
wrong place all pass a green suite.

Phase 11's job is to close that gap where it is cheapest to close: an end-to-end pass that imports
real media and drives the editor the way a person does.

## What the audit asked for

- **The Playwright layer cannot fail** (F19). `VideoEditorTests` matches `[class*='toolbar']` and
  its ffmpeg test calls `Assert.Pass()` unconditionally; `AudioScrubModeTests` soft-passes twice;
  `WasmEditorTests` fails raw when the host is down and exercises only the Server tab and one clip.
  Nothing drives the file input, Play, split, export, the preview popout, the sidecar panel or any
  dialog.
- **A new `WasmEditorEditingTests`** that imports each media type, splits at a known time, adds a
  marker, a callout and a title, plays to the end, exports, and probes the result.
- **Extract the seams the sweeps named** and unit-test them: `TimelineDragSession`, `EditorKeyMap`,
  `AssetFilter`, `OverlayPlacement`, `ProjectsDialogState`, `ExportDestinationPromptState`.
- **Cover what has no tests at all**: `VideoExportPublisher`, `BenProjectServerStore`,
  `BenMediaLibraryProvider`, the two site pages (site-17), `SidecarPreviewAssembler`.
- **A manual walk with the large media** from the audit's table, recording where ffmpeg.wasm and
  the storage quota actually give out, and putting those numbers in help.

## Carried forward, deferred from earlier phases

These were left with reasons at the time and belong to whoever picks up the next feature phase
rather than to this one:

| From | What |
|---|---|
| Phase 8 | Keyframe-edit undo (motion-6), "+ Keyframe" outside a layer's span (motion-7), zoom-n-pan for video and image clips (motion-9), overlays ignoring rotation (callouts-13), pointer capture (motion-10), snap guides (motion-19) |
| Phase 9 | An interpolated volume keyframe at a split (audio-10), the envelope lane's resize and baseline (audio-17/18), dB meter labels (audio-22), the Properties waveform as a seek surface (audio-26), link and unlink from the audio side (audio-21) |
| Phase 6 | The Projects dialog's local and server sections, Server-tab previews consulting the local cache (media-5) |

## Notes for running the suite

Three hosts, not two: api `:5252`, web `:5078` and **wasm `:5180`**. Eight tests fail in about
ninety milliseconds without the third, which reads as a code failure and is not one.

## What phase 11 did

### Drove the editor the way a person does

`Ben.Web.Playwright/Tests/WasmEditorEditingTests.cs` — thirteen tests against the standalone host
at `:5180`, each starting from a Ready engine: import video, audio and an image; seek into the
first clip and split there; add a marker, a callout and a title; open the export dialog; save,
reload and find the clips still there; and a check that nothing on the page reaches a third party.

Two of them needed a fix before they could pass, and both were real: a ruler click at a fixed
x-coordinate landed on the label gutter rather than inside the clip, and the engine test counted
the sidecar's own loopback probe as an outside request.

### Covered the site services that had none

`VideoExportPublisherTests` and `BenMediaLibraryProviderTests` in `Ben.Web.Tests` — project
resolution and which id wins, the failure contract the destination prompt depends on, and the
difference between a library that refused and a library that is empty.

### Pulled six decisions out of the components

| Seam | What it decides | Where it was |
|---|---|---|
| `EditorKeyMap` | What each keystroke means | A fifteen-case switch in `VideoEditor.razor` |
| `AssetFilter` | What the asset search box matches | `AssetBrowser.razor` |
| `TimelineDragSession` | Where a dragged clip lands, clamped and snapped | `VideoTimeline.razor` |
| `OverlayPlacement` | Where a new callout, title or clip art goes | Three call sites, three answers |
| `ExportDestinationPromptState` | Busy, failed, and "discard this render?" | `ExportDestinationPrompt.razor` |
| `ProjectsDialogState` | Rename and confirm-delete | `ProjectListDialog.razor` |

Moving them found two live bugs. The timeline's own Callout button placed the callout at the media
clock, which in clip preview counts from the selected clip's start rather than the timeline's — the
same mistake already fixed for "+ Text" and the assets gallery, missed here. And double-clicking a
project name opened a rename box nothing was focused in, so the first thing typed went to the page
and the Enter after it did nothing.

### Numbers

| Suite | Before | After |
|---|---|---|
| `Ben.Video.Tests` | 2372 | 2479 |
| `Ben.Web.Tests` | 4191 | 4209 |
| Editor Playwright | 0 | 13 |

## Still open from this phase

- The manual walk with the large Downloads media (538 MB `.mov`, 365 MB `.mp4`, 20 MB stills) to
  record where ffmpeg.wasm and the storage quota give out, and put those numbers in help.
- `SidecarPreviewAssembler` and a `Ben.Wasm.Video.Tests` project for `TokenStore`, `AuthService`
  and `BearerTokenHandler`.
