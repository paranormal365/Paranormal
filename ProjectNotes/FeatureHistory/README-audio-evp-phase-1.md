# Audio editor + EVP plan, phase 1: the editor gets its own page (10/26/2026)

Phase 1 of `ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`.

## What changed

- **`Manage/Audio/AudioEditor.razor`** is the whole editor, moved out of `AudioFilePreview.razor`
  verbatim. That file is now 240 lines; the editor is 2,393. It takes an `IAudioEditorSource`
  (`IAudioEditorSource.cs`) rather than FileId/FileName/ContentType/FileSize/SourceUrl. Closing the
  modal used to save the view and then reset every field. The editor is now created fresh for each
  visit, so `DisposeAsync` only saves.
- **`Manage/Audio/AudioEditorPage.razor`** serves `/audio/{fileId}`, and `/field-sessions/{sid}/audio/{fileId}`
  (which says "can't be opened here yet" until phase 9).
  - It waits for sign-in to be ready, then loads the file with the new `GetUploadFileAsync`
    (`ItemResult`), so it can tell signed out, refused (the server's sentence), unreachable (no
    sentence) and "not a recording" apart.
  - `?return=` sets where Back goes. Only a path on this site is accepted, the same rule
    `Login.razor` applies.
  - It has no feature flag, because the editor was reachable from cases, the media library, places
    and admin pages.
- **The compact player** right-click menu is now Play / Pause / Rewind / **Quick look** /
  **Open in editor**. Quick look is the old window at `Size="lg"` with a 160 px player and an "Open
  in editor" button. "Open in editor" is offered only to someone signed in, because these players
  also sit on public place pages.
- **`signal.css`** hides the "Work waiting" card on editor pages (`.ben-editor-page`), as it already
  hid the section banner and the rail. The card pushed the transport below the fold. This applies
  to the video editor pages too.
- The duplicate help "?" in the editor's info bar is gone; the page header has one.

## Safety step

Commit d171b2fe kept the fullscreen window hosting `<AudioEditor>` unchanged. With that, the
existing AudioEditor/AudioPlayer tests passed 12/13, the same as before the move.

## Tests

- Moved to the page: `AudioEditorTests` (`Modal` is now `#audio-editor`, which wraps exactly what the
  window did; `LeaveAndReopenEditorAsync` leaves by Back, which saves, and loads the address again),
  `AudioScrubModeTests`, `LookAndTruthsTests`, `AudioEditorWalkTests` (Walk01 now checks that Back
  leaves the editor and the editor can be reopened), and `HelpMediaCapture`.
- New: `The_editor_has_its_own_address`, `Back_returns_to_where_you_came_from`,
  `Quick_look_still_plays`.
- Full run (`AudioEditor|AudioPlayer` + the toolbar test): 16 of 17. Re-run of the five page tests
  after the last edits: 4 of 5.

**The one failure is older than this phase, and it moves.** Whichever test is first in a run to open
a case's Files tab finds the tab click does nothing; the page stays on Overview and "Upload File"
never shows. In the full run that was `DragModeToggle_DefaultsToCreateRegion_AndTogglesToScrub`,
which fails the same way on develop. In the short re-run it was
`The_audio_editors_toolbar_wraps_and_uses_the_sites_icons`, which passed in the full run when it came
later. It is not the editor; it is the case page on a cold host.

## Checked in the browser (dark, light, 375 px)

`/audio/{id}?return=%2Fmedia-library` opens from the right-click menu. The tab title is the file
name, and an .m4a plays and draws. At phone width the page has no sideways scroll; the toolbar
stacks one button per row, which phase 2's grouping addresses.

Not fixed: `.ben-editor-page` is sized to the window minus the app bar, but it sits under the
content area's 21 px top padding. The main area therefore scrolls by about 21 px plus the footer.
This is shared with the video editor; phase 3 (styling) takes it.

## Deploy

Website only. No migration, no API change.
