# Audio phase 13: the score learns from Keep and Dismiss (10/27/2026)

Branch `feature/audio-phase-13-score-refit`, off develop after phase 12 (`55c5597c`). Ben, 10/27/2026,
asked whether training could be included and chose re-weighting the score (VoiceNet fine-tuning is a
separate, later step). Plan: `ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 13.
**Migration M7 `EvpScoringVersions`.**

## What it learns

Only how much each of the score's three parts counts: how far the sound stood out, how much was in the
listening range, and its length. The built-in weights are 0.5 / 0.3 / 0.2. A fit keeps each weight at or
above 0 and their sum at 1, so a score is still 0–100, still the same three parts, and Why? still explains
it (with the points the scan used). Every combination in steps of 0.05 is tried (231) and the one that most
often ranks a kept candidate above a dismissed one wins; ties go to the weights nearest the built-in ones.
A decision made after listening counts twice, as the editor's "How it works" already promised.

Decisions are each candidate's latest Keep or Dismiss, with the measurements stored when it was found
(those don't depend on the weights, so decisions under any version are usable). No audio, no names.

## The gate (`EvpScoreRefit`)

A fit may be switched on only if all of these hold, and its row says which failed:

- at least 500 decisions, from at least 20 recordings;
- on every fifth recording (chosen by id, so always the same, and never learned from) it ranks kept above
  dismissed at least 0.01 better than the built-in weights;
- it still ranks a speech-length sound in the listening range above a loud short click, a 30-second hum
  and 12 seconds of talking (the order the detector's own tests are built on).

## Where

- `EvpScoreWeights` (in `EvpDetector.cs`): the weights, threaded through the detector so the cap and
  the lower-ranked samples are chosen by the same weights that score. The built-in weights keep the
  original arithmetic, so `Version_one_scores_are_pinned` still passes bit for bit.
- `EvpScoringVersions` (M7): every fit, append-only, with its weights, decisions, held-back results, gate
  and whether it is on (at most one, held by a filtered unique index). `EvpScans.ScoringVersion` says which
  weights scored a scan (1 for every scan before this).
- `EvpWeightRefitJob` (scheduled): at most once a day, only with enough decisions, only when something
  changed. It records; it never switches anything on.
- `/admin/evp` ▸ **How candidates are scored**: the weights in use, how close the decisions are to enough,
  every fit with how well it ranks on held-back recordings against the built-in weights, **Use these** on a
  passing fit, **Go back to the built-in weights**, and **Fit now**.
- The editor's Why? explains a candidate with the weights its scan used. Scans already made keep their
  scores; nothing is re-scored.

## Tests

- `EvpScoreRefitTests` (13): built-in weights are exactly the version-1 score; Why? adds up with any
  weights; the ranking measure (perfect, reversed, ties, listening counts twice, nothing to compare); a
  fit learns from a world the built-in weights rank badly, and passes; too few decisions or recordings
  never pass; a fit no better than the built-in weights doesn't; weights that put a click above a word are
  refused; held-back recordings are stable and about a fifth; decisions count each candidate once by its
  latest ruling; only a passing fit can be switched on, and the built-in weights come back; a scan uses
  the switched-on weights and records the version; fitting with nothing to learn from records nothing.
- `EvpDetectorTests` unchanged and passing (pinned scores).
- e2e `The_evp_admin_page_shows_the_figures` now opens the scoring section: built-in weights, the 500
  decisions sentence, and Fit now answering in words without switching anything on.

## Fixed on the way

- **The profile's Review blind switch looked saved before it was.** The box ticks at once and the save
  follows; a reload in between read the old value. It now says **Saved** when the server has it. The
  e2e found this when it reloaded too quickly and then left the shared seat blind, which failed seven
  other tests; the test now waits for Saved and always switches it back off.

## Results

9,129 unit tests pass. e2e audio + mixer + Field Kit + LookAndTruths: 44 passed, 1 opt-in capture skipped;
`Quick_look_still_plays` and `A_group_page_says_which_group_it_is_and_keeps_its_tabs_on_one_line` timed out
under the full run's load and passed on their own run straight after (neither is touched by this phase).

## Deploying

Run `ProjectNotes/sql/M7-EvpScoringVersions.sql` on production before the API. Safe to run twice. Nothing
changes for anybody until a fit passes its gate and a SuperAdmin chooses **Use these**.
