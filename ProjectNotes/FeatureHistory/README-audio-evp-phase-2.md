# Audio editor + EVP plan, phase 2: toolbar, tabs, one speed (10/26/2026)

Phase 2 of `ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`. Ben: "Put the most obvious and most used
buttons on the editor and group and put in menus lesser used and logically grouped methods."

## The toolbar (`AudioEditorToolbar.razor` + `.cs`)

One Telerik ToolBar, replacing a flat row of eight toggles whose sub-controls appeared inline and
wrapped the row:

| On the bar | In menus |
|---|---|
| **Select / Scrub** (pressed = scrub) · **Clear Regions** | **View ▾** Timeline · Spectrogram labels · Mel scale · Voice band · Detect silence · Display settings… |
| **Spectrogram** (pressed when on) · **Scan for EVP ▾** (split: runs at the current sensitivity; Low / Medium / High / Fine-tune…) · **EVP Markers (n)** | **Listen ▾** Play at 0.5–2× · Listening tools… |
| Zoom out · Fit · Zoom in | **Edit ▾** Make a clip from the selection… · Explore the selection… · Cut / Silence selection / Normalize / Reverse (new file) · Louder or quieter… · Fade… · Change speed… · Change pitch… |
| | **Clips (n)** |

- The toolbar is stateless. The editor builds an `AudioToolbarState` each render and handles one
  `AudioToolbarAction` callback, so the bar cannot disagree with the editor.
- Icons are the site's `BenIcon` as child content, never Telerik's SvgIcon set. No unknown
  attributes go on any Telerik component, because they throw: ids and titles use their own
  parameters (`#audio-menu-view`, `#audio-menu-listen`, `#audio-menu-edit`).
- Menu items that are on show a check (`.audio-toolbar__check`). That rule is global, in
  `signal.css`, because Telerik draws its popups outside the editor.
- At narrow widths Telerik moves what does not fit into its "⋮" overflow menu.

## The panels are tabs under the player

The stacked panels became `BenTabs` under the waveform: **EVP Markers**, **Clips**, **Listen**,
**Display** and **Edit**.
- Toolbar buttons only ever **open** a tab, and **Hide panels** folds the area.
- Opening a panel no longer pushes the waveform down, which settles audit finding U.
- The editor's root is no longer `h-100` / `overflow:hidden`. That suited the old fullscreen window
  and cut the tabs off on the page.
- A successful edit or saved clip switches to Clips, so you see where it went.

The plan put the tabs in a 320 px column at the right. They went **under** the player instead, so
the waveform keeps the full width, which is what an EVP is read from.

**Display tab** (`AudioDisplaySettings.razor`): resolution and colors (out of the toolbar), plus phase
1b's spectrogram dials: frequency scale (linear, mel, log, bark, erb), frequency range, window,
brightness (gain), contrast range, high-frequency lift (pre-emphasis), and one picture per channel.
It also holds the silence threshold. The View menu's Mel scale and the Display tab's scale stay in
step, including after a reload with a saved mel setting.

**Edit tab**: the menu items with a value to choose open it with that group outlined
(`.audio-edit-group.is-focused`). Cut, Silence selection, Normalize and Reverse run straight from
the menu.

## One speed

`_speed` in the editor is the only playback speed:
- the Listen menu, the Listen tab's slider, and the player's own speed menu (which now reports
  changes through `OnRateChanged`, and shows an off-preset speed as itself) all set it;
- the region explorer shares it (`Speed` / `SpeedChanged`), where it used to keep its own;
- the destructive speed change is only Edit ▸ Change speed, which says it makes a new file.

## Bugs fixed on the way

1. **Seventeen components logged "Unhandled exception in circuit" when a page closed.** Their
   `DisposeAsync` caught `JSDisconnectedException` but not the cancellation thrown while the browser
   is still going (`BenTabs` showed it here). Inside dispose methods only, those catches now take
   `OperationCanceledException` too. Elsewhere a broad cancellation catch could hide a real timeout,
   so those are untouched. Files: `BenTabs`, `BenModal`, `BenDropdown`, `BenEditor`, `BenLeaveGuard`,
   `BenMap`, `ApexChart`, `FieldKitPlayer`, `FieldKitEnginePlayer`, `CaseAudioMixPage`,
   `EventPhotoWall`, `CaseMessageThread`, `NearbyDiscovery`, `PublicCaseDiscovery`, `FeedPage`,
   `StoreCheckoutPage`, `StoreCartDrawer`.
2. **"Open in editor" could be missing for a signed-in person after a full page load** (phase 1,
   caught by `AuthReadyPrerenderGuardTests`). The compact player read sign-in before it was
   resolved. It now draws the waveform first, then redraws once sign-in is known.

## Tests

- New: `Toolbar_menus_hold_the_lesser_used_controls` (pressed state; View ▸ Mel scale changes the
  Display tab's scale; Edit ▸ Fade opens Edit with fade outlined) and
  `One_speed_control_drives_playback_everywhere`.
- `Ben.Web.Playwright/AudioEditorUi.cs` is the one place tests open a tab, choose a menu item, or set
  the spectrogram, so the next regrouping changes one file. Moved onto it: `AudioEditorTests`,
  `AudioScrubModeTests` (the toggle's `aria-pressed`, not its old titles), `LookAndTruthsTests`
  (`#audio-editor-toolbar`), `AudioEditorWalkTests` and `HelpMediaCapture`.
- `Silencing_a_region_you_drew_produces_a_clip` counts the toolbar's Clips (n), and checks the
  editor shows the Clips tab.
- AudioEditor + AudioPlayer + CaseMixer + ContentPicker + the toolbar test: 30 of 30. Unit tests: all
  pass after fix 2.

## Not done here (phase 3)

- The Listen and Edit tabs and the Telerik toolbar still have Bootstrap / default Telerik styling.
  Phase 3 restyles them with Signal tokens and `telerik-signal.css`.
- The new spectrogram dials, zoom, speed, voice band and the open tab are not remembered per
  recording yet (mel is). Phase 3 extends the saved view.

## Deploy

Website only. No migration, no API change.
