# Audio phase 12: help, pictures, PDF, and the 2.0.0 release (10/27/2026)

Branch `feature/audio-phase-12-release`, off develop after phase 11 (`c4adc4c3`). Plan:
`ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 12. No migration (M1–M6 are all applied on
production as of 10/27).

## Release

- **Audio editor 2.0.0**, dated 10/27/2026: `## Unreleased` in `audio-editor.md` became `## 2.0.0 · 2026-10-27`,
  and `AudioEditorRelease.Version` is `2.0.0` in the same commit (`ChangelogServiceTests` pass). The
  site-level lines (session scan entrances, Scans page) are under `webapp.md`'s Unreleased, for the
  website's own release.

## Help (`using-the-audio-editor.md`)

Each phase kept its own part current, so this was an audit. New: **Scanning a field session** (who
may, the copy, the group's EVPs, question notes) and **Reading the lanes**. Brought up to date: the
candidates caption (two readings), the listening range (it changes scores more than counts), **A
smarter EVP detector** (the new measurements, misses, blind decisions; nothing re-scores yet), and
**What is stored** (session copies).

## Pictures (`HelpMediaCapture.Capture_UsingTheAudioEditor`, fresh databases)

Retaken: `editor-overview.png` (Editor 2.0.0), `spectrogram.png`, `case-mixer.png`,
`evp-candidates.png`. New: `detector-lane.png`, `session-scan.png`. Every one was read:

- The candidates shot now uses the same speech-shaped test recording as the editor's tests (the
  case's recording gives nothing to show), on a taller window: at 900 px the crop ended inside the
  first row.
- The candidates picture showed the banner saying every Keep and Dismiss "trains the next version",
  which wasn't true with nothing re-fitting. Ben then chose to include training (phase 13); the
  banner now says decisions are "kept to teach the scanner which sounds are worth your time", true
  before and after it.
- The overview showed the idle live meter as an empty box; it now rests on a flat baseline, with a
  tooltip saying what it is.
- The capture uploads through `/upload-files` and waits for the upload to finish before leaving the
  page; leaving at once cut it off (first attempt).

## PDF

`python3 docs/build-documentation-pdf.py`, then headless Chrome to
`docs/IsHaunted-Product-Documentation.pdf`: 417 pages, 32.6 MB.

## Decisions recorded on the way (Ben, 10/27)

- **Training is included, as phase 13:** a gated re-weighting of the score from Keep/Dismiss.
- **VoiceNet will later be fine-tuned from v1 on the site's recordings**, keeping what it already
  knows; no permission step (Ben). Before that, the site's "No audio is copied" wording must change.
- `Future-Improvements.md`: items 242 and 253 updated; new item 262 lists what 2.0.0 left for later.

## Tests

9,117 unit tests pass. e2e audio + mixer + Field Kit + LookAndTruths: 45 passed, 1 opt-in capture skipped, and
`Blind_review_hides_the_readings_until_it_is_turned_off` failed once: its profile click landed before the page was
live (the box ticked, nothing saved). It now waits for the circuit first, and passed twice running.
