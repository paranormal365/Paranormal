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
