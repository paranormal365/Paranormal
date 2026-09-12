# Video editor — Phase 0: foundations

Branch: `feature/video-editor-phase0-foundations`

## Why

The 2026-09-05 audit (`ProjectNotes/VideoEditor-Audit-2026-09-05.md`, findings in
`…-Findings.md`) walked the editor as a person and read all twelve of its areas. Phase 0 is the
groundwork every later phase leans on: the client-side host must actually be the full editor, the
buttons that do the work must be visible, the keyboard must work on a Mac, and the tests must be
able to fail.

## What this branch does

1. **Host parity by construction.** New `Ben.Video.Editor/Extensions/VideoEditorHostDefaults.cs`
   with `ApplyEditingDefaults` and `ApplyServerIntegration`. Both hosts call it; the WASM host
   applies the editing defaults *before* its early return, so a deployment with no configured
   WebApi still gets multi-track, audio, transitions, titles, effects, ripple, project persistence,
   error log, background rendering and the sidecar. Fixes F2.
2. **Feature flags gate creation, not rendering.** `ExportService` no longer drops transitions or
   text overlays from a project that contains them because the host flag is off
   (transitions-15, titles-11).
3. **Toolbar.** Initialize, Open, Undo, Redo, Preview and Export stay in the bar
   (`Overflow="Never"`); the status label and progress bar are width-capped; only the Assets
   toggle and Save-to-server may collapse. The three panel toggles that were wired to parameters
   nobody passed are deleted along with their dead `LayoutService` members. Fixes F1.
4. **Keyboard.** Cmd counts as the modifier (undo/redo were dead on macOS — timeline-8); handled
   keys are `preventDefault`ed; keys are not forwarded while a Telerik popup or dialog has focus,
   and Escape closes an open popup instead of clearing the selection (F10); Escape and Delete now
   cover titles, transitions and clip art (titles-7, transitions-10); arrow keys step frames when
   no canvas item is selected (preview-6). File → "Keyboard shortcuts" opens the existing overlay.
5. **Watermark.** `GET /api/video-assets/watermark-config` exists, so the feature can turn on at
   all (F16); the watermarked export keeps the chosen filename (export-6) and the overlay is
   positioned by ffmpeg expressions instead of a guessed logo height (export-7); watermark assets
   are no longer offered as clip art (callouts-23).
6. **Small host fixes.** The WASM launch profile uses `localhost` so the API's CORS list and the
   sidecar's allowed origins match (wasm-11); the theme toggle merges into `layoutSettings`
   instead of overwriting the site's other settings (wasm-13).
7. **A pre-existing crash on load.** The editor's resizable divider passed a vanished element to
   its JS module, throwing inside the render loop, so the WebAssembly host greeted everyone with
   "An unhandled error has occurred" and a Reload link. Present before this branch (confirmed
   against the previous commit); fixed here because Phase 0's own verification could not be
   trusted otherwise.
8. **Tests that can fail.** The editor Playwright tests use real selectors and real assertions,
   `Assert.Pass` is banned by a guard, the WASM tests skip cleanly when :5180 is down, and the
   repo's seed media plus a generated clip and image are available as fixtures with helpers that
   drive `#bv-file-input`.

## Verifying

```
dotnet build Ben.sln
dotnet test Ben.Video.Tests
dotnet test Ben.Web.Tests
```

then the three hosts (api 5252, web 5078, wasm 5180) and the editor Playwright classes, and the
screen walk at 1440×900 and 1280×800 described in the plan's Verification section.

## What was verified on screen

Driven at http://localhost:5180 after `dotnet run --project Ben.Wasm.Video --urls http://localhost:5180`:

- The toolbar shows Initialize, Open, Preview, Export, Undo, Redo and File at 1440×900, and keeps
  them while the engine runs.
- The feature badges read Multi-Track, Audio, Transitions, Text; the timeline offers **+ Video**,
  **+ Audio** and **T + Text**; a default audio track exists.
- Importing a 3-minute mp3 places it on the audio track with a waveform and lists it under the
  Audio tab. It used to decode, report "Done" and land nowhere.
- A saved project is restored on reload.
- <kbd>⌘</kbd>+<kbd>Z</kbd> undoes and <kbd>⌘</kbd>+<kbd>Shift</kbd>+<kbd>Z</kbd> redoes.
- <kbd>Escape</kbd> closes the File menu, and still closes the shortcut overlay when no menu is open.
- No error banner on load.

Not verified on screen: the site host's signed-in pages, which need a password this session must
not type. They are covered by the build, the unit suites and the existing Playwright tests.

## Verified by test, not just by eye

`Ben.Video.Tests` gains `VideoEditorHostDefaultsTests` (a new option flag fails the build until it
is classified) and `HostsUseTheSharedDefaultsTests` (neither host may configure the editor by
hand). Both were checked against the previous commit first: all six primary toolbar buttons and
both host checks fail there, so they discriminate.
