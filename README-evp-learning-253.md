# Item 253, step one: the EVP detector starts keeping what it needs to learn

Ben, 10/02/2026: *"I would like to be able to use the audio editor EVP detector to learn the more it
is used."* Backlog item 253 in `ProjectNotes/Future-Improvements.md` is the whole plan; this branch
is its first step: **record, don't change scoring.**

## Where the records live

Three new tables, one migration (`EvpLearningRecords`), all hanging off the recording
(`UploadFiles`, cascade delete) and none pointing at a person.

| Table | One row per | What it holds | Why |
|---|---|---|---|
| `EvpScans` | scan somebody ran | detector version, preset, the five settings that actually ran, recording length, found/proposed counts | which scoring and settings produced the candidates; turns a hand-placed marker on a scanned recording into a known **miss** |
| `EvpScanCandidates` | thing a scan found | span, score, seven measurements, `Proposed`, `AudioMarkerId` (no FK) | the features a re-fit re-weights; immutable even when the marker is relabeled, re-bounded, replaced or deleted |
| `EvpRulings` | Keep or Dismiss on a detected candidate | ruling, `PlayedFirst`, `BoundsAdjusted`, score at the time | append-only labels, change of mind included; survive the marker |

The label for a candidate is its marker's `ReviewStatus` (Confirmed = kept, Dismissed) and its
`EvpRulings` history. Misses are hand-placed markers (`IsAutoDetected = false`) on a recording with
an earlier `EvpScans` row and no overlapping candidate.

## The seven measurements (`EvpFeatures`)

`PeakProminenceDb`, `MeanBandGapDb`, `EventSeconds` are what the version 1 score is made of, and
`EvpDetector.Score(EvpFeatures)` reads nothing else, so the stored values reproduce the stored score.
`MeanFloorDb`, `PeakBandDb`, `BandLevelSpreadDb` (syllables rise and fall, hum holds steady) and
`ZeroCrossingRate` (hiss is high, voiced sound is low) are cheap extras for a re-fit to try.

## Rules kept

- **Scoring unchanged.** Old and new code gave bit-identical scores on all three presets of the
  standard fixture; `Version_one_scores_are_pinned` now fails if scoring moves without raising
  `EvpDetector.Version`.
- **Never in the way.** Both writes are separate saves after the person's own save, and a failure is
  logged, not returned (`Scan_StillAnswers…`, `Review_StillAnswers…`; the first found the logger
  itself throwing).
- **Only server-measured candidates.** `POST candidates` accepts browser-sent scores; those are not
  recorded.
- **"Played first"** comes from the full-view editor: the candidate's ▶, or the playhead entering it
  while the recording plays. Null from any caller that does not say.
- Told to people where it happens: a notice in the review panel, the help article, the privacy page
  (updated 10/02/2026) and the changelog.

## Deploy

The migration must reach the live database at deploy — **Ben runs it** (`dotnet ef database update
--connection …`, never the default). Until it has, every scan and ruling still works and each
learning write logs an error instead.
