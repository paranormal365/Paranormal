# Audio editor + EVP plan, phase 0: fixes in place (10/26/2026)

Phase 0 of the plan in `ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md` (copied from the approved
plan). The editor is still the fullscreen modal inside `AudioFilePreview.razor`; phase 1 moves it
to its own page. These are the defects found while reading it, fixed where they stand so the move
starts from a correct editor.

## Fixed

| # | What was wrong | Where |
|---|---|---|
| 1 | A marker's "Ends at" box had `max="100"`: arrows stopped at 100 s and any later end was invalid | `AudioFilePreview.razor`, `MaxSecondsAttribute` |
| 1b | The "Mark a stretch of audio" label pointed at the "Ends at" box, which does not exist until the box is ticked, so clicking the sentence did nothing | same; checkbox is `#marker-is-span` |
| 2 | Fade boxes full-size with `max="100"` | same |
| 3 | Speed select hard-coded `selected` on 1×, so it showed 1× after any re-render | `WaveSurferPlayer.razor` `_rate`, `RateOptions` |
| 4 | Spectrogram frequency labels always linear, even with Mel on | `WaveSurferPlayer.razor.js` `_spectrogramFreqToY` shared by labels and voice band |
| 5 | `_drawSpectrogram` never called and referenced undefined variables | deleted |
| 6 | Voice band overlay 300–3000 Hz; the detector listens 300–3400 Hz | `VOICE_BAND_HZ` |
| 7 | Envelope and spectrogram-windowed plugin wiring, `ExtraControls`: no consumer | removed from the player and `WsPluginConfig`; DB columns and API untouched, `AudioViewState` still passes them through so a save does not blank them |
| 8 | "Settings not saved" sat between the Spectrogram button and its options | moved to the hint line |
| 9 | Emoji in the edit buttons, context menus, transport, explorer | `BenIcon` / plain labels |
| 10 | Clear Regions used the trash icon | `x-square` |
| 11 | Explorer's Save as WAV was `btn-success` | `btn-primary` |
| 12 | Two buttons named "Silence" (finding E was the walk clicking the wrong one) | **Detect silence** (toolbar) and **Silence selection** (edit) |

## Tests

- New: `AudioEditorTests.A_markers_end_can_pass_100_seconds`. Checks the arrow step past 100 and the
  box's `validity.rangeOverflow`, not just a save: typing 150 was always accepted, so a save-only
  test would have passed on the broken code. Also clicks the sentence to tick the box (1b).
- Updated to the new names: `Silence_detection_does_not_steal_the_region_you_drew`, the walk.

## Test run (10/26/2026, `IsHauntedDb_evp_p0`)

`TestCategory=AudioEditor|TestCategory=AudioPlayer`: 12 of 13 passed. The new test was also run
against the old `max="100"` and failed with "the arrows stopped at 100 seconds", so it catches the
defect.

**Not this phase's:** `AudioScrubModeTests.DragModeToggle_DefaultsToCreateRegion_AndTogglesToScrub`
fails before any audio is opened. Clicking the case's Files tab never shows "Upload File", and the
page stays on Overview. It fails the same way on `develop` with these changes stashed, three runs out
of three. Its sibling `DragModeToggle_UpdatesHintText` passes through the same helper when it runs
second. Phase 1 changes how these tests reach the editor; look at it then.

## Help and change log

`using-the-audio-editor.md` uses the new button names. The screenshots still show the old toolbar;
they are retaken in phase 12, after the toolbar is rebuilt. `audio-editor.md` has the user-visible
fixes under `## Unreleased`.

## Deploy

Website only. No migration, no API change.
