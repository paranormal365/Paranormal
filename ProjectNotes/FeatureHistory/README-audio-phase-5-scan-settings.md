# Audio phase 5: every scan setting, and presets (10/26/2026)

Branch `feature/audio-phase-5-scan-settings`, off develop after phase 4b (`167fb3c8`). Plan:
`ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 5. Ben: "Provide a good deal of visual
analysis and ability to tweak all the settings used to scan for evps."

## The settings (`EvpDetectionOptions`)

Five were adjustable before. Nine more were constants inside `EvpDetector`, and are now settings
whose defaults are those constants, so a scan that changes nothing scores exactly as before
(`Version_one_scores_are_pinned` passes untouched; `EvpDetector.Version` stays 1).

| Group in Fine-tune | Setting | Default | Range |
|---|---|---|---|
| What counts as a sound | Threshold (dB) | 6 (Low 9, High 4) | 2–20 |
| | Ends when it drops by (dB) — `ReleaseHysteresisDb` | 2 | 0–10, ≤ threshold |
| | Shortest sound, join within, longest, context | 0.15 / 0.35 / 5 / 0.4 s | as before |
| Where it listens | From / To (Hz) — `BandLowHz` / `BandHighHz` | 300 / 3400 | 50–8000, from < to |
| The room's background | Judged over (s) — `FloorWindowSeconds` | 10 | 2–60 |
| | Background level (%) — `FloorPercentile` | 20 | 5–50 |
| What reaches the list | Most candidates — `MaxCandidates` | 500 | 10–500 |
| | Lowest score — `MinScore` | 0 | 0–100 |
| | Places for lower-ranked (%) — `ExplorationShare` | 10 | 0–50 |
| | Overlap with reviewed (%) — `ReviewedOverlapFraction` | 50 | 10–100 |

Not adjustable, on purpose: the 25 ms frame and 10 ms hop. Every stored measurement is in hops, so
letting it vary would make scans incomparable.

- **Places for lower-ranked** (item 253): when a scan finds more than fit, `floor(share × cap)` places
  go to every k-th remaining candidate in playback order, never random. They are marked
  (`EvpScanCandidates.Exploratory`, `AudioMarkers.IsExploratory`) and the row shows a dashed
  **sample** pill. Below 10 places nothing is set aside. With 500 places and fewer found, nothing
  changes.
- **Release margin** may equal the threshold: the lowest threshold (2 dB) with the default 2 dB
  margin releases at the background, and refusing that would have broken a setting people could
  already choose. The plan said "less than"; the test that caught it is
  `A_threshold_inside_the_range_is_accepted(2)`.
- The listening range also moves the spectrogram's **voice band** (`setVoiceBand` in
  `WaveSurferPlayer.razor.js`).

## Presets and the remembered settings

- `EvpScanPresets` (M1): a person's own, named, unique per person, at most one default (a filtered
  unique index, `IX_EvpScanPresets_OneDefault`). `api/evp-scan-presets` GET / POST / PUT / DELETE;
  another person's preset is a 404. Stored settings are checked again on the way out; one the
  scan would now refuse comes back with `Problem`, and Fine-tune says why instead of applying it.
- `UploadFileAudioConfigs.EvpScanOptionsJson` (M1): the settings a recording was last scanned
  with, written by the scan for someone who may manage the file (the people whose view is saved)
  and by nothing else, so a view save cannot undo it.
- On opening: the recording's last settings, else the person's default preset, else Medium.
- `EvpScans.OptionsJson`, `BandLowHz`, `BandHighHz` (M1): every setting a scan ran with.

## Editor

`EvpScanSettingsEditor.razor` replaces the old Fine-tune block: a preset list (Low, Medium, High,
your presets, Custom), Update / Set as default / Delete for a saved one, **Save as preset…**, and
the four groups, each field with a plain sentence in its tooltip.

## Bugs fixed on the way

- **A toolbar click as the editor opened could be undone.** The toolbar enabled when the player was
  ready, then the saved view was restored on top of whatever had been clicked meanwhile (Select /
  Scrub went back to Select). This phase's preset fetch made the window long enough for
  `DragModeToggle_DefaultsToCreateRegion_AndTogglesToScrub` to hit it. The toolbar now waits for
  the restore (`_restoreFinished`, set in a `finally`, so a failed restore still frees it), and the
  presets load after the view.
- **The Scan button's tooltip and menu check claimed Low/Medium/High** after the settings had been
  adjusted. They now say "your adjusted settings" and tick Fine-tune.

## Tests

- Detector: `A_narrower_band_changes_the_scores`,
  `A_longer_background_window_follows_a_louder_room_more_slowly`,
  `No_release_margin_breaks_a_wavering_sound_into_more_pieces`,
  `A_lowest_score_leaves_out_everything_below_it`, `Most_candidates_caps_the_scan`,
  `Places_for_lower_ranked_go_to_marked_samples_from_below_the_top`,
  `Nothing_is_set_aside_when_everything_fits`. The first three were run against the detector with
  its old constants put back and all three failed, so they test the change.
- Options: old JSON without the new fields reads as the old behavior; preset names; every new range.
- Controllers: `Scan_RecordsEverySettingItRanWith`, `Scan_HonorsTheOverlapItWasGiven`,
  `Scan_RemembersItsSettingsOnTheRecording_ForItsOwner`, `Scan_ByAViewer_RunsButIsNotRemembered`,
  `Scan_RefusesABandThatIsUpsideDown`; `EvpScanPresetControllerTests` (7).
- e2e: `Scan_settings_change_the_scan_and_come_back_with_their_preset` (Medium finds three; a lowest
  score of 95 finds none; on reopening the range, the score and the saved preset come back). Its
  first version waited for the Scan button to be enabled, which it already was before the click took
  effect, and read the first scan's results; it now waits for the answer it expects.
- Unit tests 8,994 pass; audio + mixer + picker + LookAndTruths e2e 42 of 42.

## Deploy

**Migration M1, `20261026201802_EvpScanSettings`.** Ben runs `ProjectNotes/sql/M1-EvpScanSettings.sql`
on MonsterASP before deploying the API (idempotent; safe to run twice). Then deploy API + website.
