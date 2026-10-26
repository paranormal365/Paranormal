# Audio phase 7: shortcuts, loop, zoom to a stretch, spectrogram marks and height, overview strip, volume line (10/26/2026)

Branch `feature/audio-phase-7-visual-tools`, off develop after phase 6 (`f83ce5f7`). Plan:
`ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 7. No migration.

## What's new

| | Where |
|---|---|
| **Keyboard** — Space, ← →, [ ], N P, K, D, Z, F, L, Esc, ? | `AudioEditor.razor.js` (one `keydown` listener; ignored while typing, in a dialog, or with Ctrl/Cmd/Alt) → `OnShortcutAsync`. Shortcuts window from the toolbar's **?** |
| **Loop** toggle (toolbar, L) | `setLoop`: `_stopAtEnd` sends the playhead back to the start instead of pausing, so candidates, markers, regions and the selection all loop |
| **Zoom to the selection** (toolbar, Z) and per candidate (row's ⤢) | `zoomToRange`: px/s from the window's width and the span with 15 % either side, then `setScrollTime` |
| **Marks on the spectrogram** | `setSpectrogramOverlays`; drawn on the overlay canvas from the waveform's scroll position and width, redrawn on scroll and zoom. Pushed from `OnAfterRenderAsync` whenever the marker list changes |
| **Spectrogram height** (Display tab, 100–480 px) | `setSpectrogramHeight`; the player grows with it, so the waveform keeps its height. Remembered (`AudioWorkspace.SpectrogramHeight`) |
| **Overview strip** (View ▸) | the vendored minimap plugin in a pane of its own under the waveform (inside the waveform's shadow DOM, its default, the waveform's fixed height hid it). Remembered (`AudioWorkspace.Minimap`) |
| **Turn parts down…** (Edit ▸) | the vendored envelope plugin, flat at full volume, points at both ends; playback follows the line. **Save as a new file** posts `AudioEditOperation.Envelope` with `Volume` points; `AudioEditor.Envelope` applies it exactly as the plugin plays it (`VolumeAt`: straight lines, (0,0) before the first point and (duration,0) after the last). Cut only (0–1): the plugin sets the player's volume, which tops out at the recording's own level, so a boost could not be heard while drawing |

Ben, 10/26/2026, on the envelope: "If you think there is benefit in the envelope plugin or it can be
used as one of our features, then you can include it." It is in, as **Turn parts down**.

## Bugs found and fixed on the way

- **Keys were handled side by side.** JS sent each key without waiting, so `]` read the playhead
  before the arrow pressed just before had moved it. Each key now waits for the one before (a
  promise chain in `AudioEditor.razor.js`).
- **A `WsSpan[]` to `params object?[]` is the params array.** The spectrogram marks arrived as
  separate arguments and the module saw no list. A `List` is passed instead, with the reason on the
  method.
- **The spectrogram plugin's `setOptions({ height })` never settled**, so the height change never
  reached its save. It is no longer awaited (the pane has its height already).
- **Clips (n) went into the toolbar's overflow menu at 1280 px** once Loop was on the bar with its
  word. Loop is icon-only now, like the zoom buttons, with "Loop" as its accessible name.
- **Phase 6's progress test was flaky in a full run**: the in-memory provider applies a save's rows
  one at a time, so a look could land between the segment's percent and the job's. The test now asks
  that they agree at some look, not every one.

## Tests

- Unit: `VolumeCurveTests` (10: the dip measured in the saved file, interpolation at four times, the
  silent ends, every refusal); `AudioWorkspaceTests` with the strip and the height.
- e2e: `Keyboard_shortcuts_drive_the_editor` (N highlights, Z zooms past 2×, F fits, D dismisses and
  the highlight moves on, [ ] make a selection, ? opens the list), `The_spectrogram_marks_the_candidates`
  (overlay pixels at the three sounds, none between), `The_overview_strip_and_spectrogram_height_are_remembered`,
  `A_candidate_loops_with_loop_on` (playhead every half second: 4.0, 4.5, 5.0, 3.9, 4.5, 5.0, 3.9…),
  `A_volume_line_can_be_drawn_and_saved` (untouched says so; a point dragged down saves a file).
- Audio + mixer + picker + LookAndTruths e2e: 49 of 49. Unit tests: all pass.

## Not here

- The minimap has no scan mask yet (phase 6 drew it on the waveform only).
- Compare original (B) is phase 8, with the meters.

## Deploy

Website only, plus the API for the new `Envelope` edit. No migration.
