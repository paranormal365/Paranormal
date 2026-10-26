# Audio editor + EVP plan, phase 3: Signal styling and the remembered view (10/26/2026)

Phase 3 of `ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`.

## The remembered view

`AudioWorkspace` (new) holds what the 09/06 audit found reset on every open, plus what phase 2 added:
- zoom and playback speed;
- the voice band, Select or Scrub, and Detect silence;
- whether the panels are open, and which tab;
- the Display tab's spectrogram dials (scale, range, window, gain, contrast, pre-emphasis, split
  channels).

It is stored in `UploadFileAudioConfigs.EditStateJson` beside the listening chain, under
`"workspace"`, so **there is no migration**.
- `AudioListeningChain.FromJson` ignores the key, and `AudioWorkspace.Compose` writes it back beside
  the chain on every save. The chain's `ToJson` builds a fresh object, so anything written beside it
  any other way would be dropped.
- It is read field by field and range-checked: a stored NaN speed would play nothing.
- A row from before simply opens with the defaults.
- `AudioWorkspaceTests`: old row, round trip, the chain still reads, the request carries it, and
  bad values fall back.

## Bugs found while making zoom stick (all fixed)

1. **Every `setOptions()` reset the zoom.** This includes the spectrogram pane resizing the
   player, and a window resize. WaveSurfer's `zoom()` never records the zoom in `ws.options`, and
   `setOptions` re-renders from it. The zoom event now writes it there.
2. **The load-time repaints re-applied the starting zoom.** The `ready` handler runs them at 50,
   200 and 400 ms to repaint the timeline, which undid any zoom set in that time. They now use the
   zoom in effect.
3. **Restore read the saved zoom into a field that the player's own load-time "zoom 0" reports reset
   during the restore's awaits.** A saved zoom always came back as "fit". The restore now uses the
   saved value directly, and zoom reports are ignored until the restore has finished.
4. **A reloaded page's lingering circuit overwrote newer settings.** Phase 1 made `DisposeAsync`
   save the view (the old modal's close did that). After a full reload the old circuit is disposed
   minutes later and saved its stale view over the newer page's. Nothing saves on dispose now. Every
   change saves as it happens; zoom saves a second after it settles, because the wheel fires many
   times a second. No save happens before the saved view is restored.
5. **Overflowed zoom buttons were bare icons** in Telerik's "⋮" menu. They carry labels now, hidden
   on the bar.

`Zoom_speed_and_tab_survive_leaving_and_reopening` was run with fix 3 taken out and failed: zoom ratio 2.02
before leaving, 1.00 after reopening. With the fixes it passes, 2.02 and 2.02.

## Tests

- AudioEditor + AudioPlayer + CaseMixer + ContentPicker + the toolbar test: **31 of 31**, with no
  unhandled exceptions in the web log.
- `AudioWorkspaceTests` (new), `AudioViewStateTests`, `AudioListeningChainTests` and
  `SignalCopiesStayInStepTests` pass.

## Signal styling

- **Audio mark colors are tokens** in `signal-tokens.css` (light and dark):
  `--ben-audio-{region,clip,candidate,confirmed,probable,possible,silence,voice}-rgb`. Region colors
  are inline styles in WaveSurfer's shadow DOM, which custom properties inherit into. The voice
  band canvas reads the token on each draw and redraws when `data-bs-theme` changes. The copies in
  both WASM hosts were updated (`SignalCopiesStayInStepTests`).
- **Editor panels:** the facts bar, the player card, the Listen and Edit panels and the Clips list
  are on tokens (`AudioEditor.razor.css`). Bootstrap's `bg-body-secondary` and the inline Kendo
  colors are gone.
- **Sitewide** (`signal.css`): `.form-range` thumbs and tracks use the accent and line tokens.
  Every slider on the site was Bootstrap's compiled blue.
- **Sitewide** (`telerik-signal.css`, copied to both WASM hosts): pressed ToolBar toggles, the
  DropDownButton / SplitButton menus as cards, and a divider on the split arrow. The video editor's
  toolbar and track menus pick this up too.

## Checked in the browser

- Speed 1.5× and the Listen tab came back after a reload, and so did zoom once bugs 1–4 were fixed.
- Sliders and menus are in the Signal colors.
- The overflow menu shows "Zoom out", "Fit the whole recording", "Zoom in" and "Clips (0)".

## Deploy

Website only. No migration (the workspace rides in an existing column).
