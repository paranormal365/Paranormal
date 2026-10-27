# Audio phase 11: learning that reads its tables (10/27/2026)

Branch `feature/audio-phase-11-learning-reads`, off develop after phase 10 (`cb6b293e`). Plan:
`ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 11 (item 253). **Migration M6 `EvpLearningReads`.**

Nothing here changes the scanner's score or retrains anything (Ben, 10/27: hold off on retraining).
It measures more, records what was missed and what was decided blind, and reads it all back.

## Features 8 and 9 (`Services/Audio/EvpSpectralFeatures`)

- **The FFT moved** unchanged from `SmbPitchShifter` into `Services/Audio/Fft.cs`. `FftTests` pinned
  the pitch shifter's output (SHA-256 of a shifted two-tone signal) before the move and it still
  matches after.
- **Spectral flatness**, 0–1: in the listening range, the geometric over the arithmetic mean of each
  25 ms frame's power (Hann, 512-point FFT), averaged. Hiss is flat; a voice or hum is not.
- **Voiced share**, 0–1: the share of frames whose normalized autocorrelation reaches 0.5 between
  lags 40 and 200 (400 Hz down to 80 Hz). Frames below −60 dBFS are skipped.
- **Where:** the scan's second stage, which now always runs: the candidates' audio is read once more
  (`CandidateAudio`, shared with VoiceNet) and the features are measured on the sound itself, inside
  its padding (`EvpScanService.EventSpan`). So they are measured on a server without the VoiceNet
  model too. Stored as `EvpScanCandidates.SpectralFlatness` and `VoicedFrameFraction`. `Score`
  ignores them; `EvpDetector.Version` stays 1 and the pinned scores pass.
- **Cost:** 390 ms per minute of candidate audio (measured by
  `EvpSpectralFeaturesTests.Cost_per_minute_of_candidate_audio_is_reported` on the Mac). A full
  list of 500 two-second candidates adds about 6 s.

## Misses (`EvpMisses`)

A marker placed by hand on a scanned recording, where the latest scan found no candidate over it
(the scan's own overlap rule; every candidate it found counts, proposed or not), is recorded with
the scan and the marker. Written by `EvpScanRecorder.TryRecordMissAsync` from
`AudioMarkerController.Create`, and never fails the request. Only the recording is a foreign key.

## Blind review

- `AppUsers.EvpBlindReview`, the **Review blind** switch on `/profile` (saved on click).
- In the editor, pending candidates then show no readings, no Why?, no sort or voice-like filter,
  and list in time order. The strip above the waveform shades them all alike, and the Keep dialog
  leaves out the score.
- Each ruling carries `Blind` (`EvpRulings.Blind`).

## `/admin/evp` (SuperAdmin, System ▸ EVP Scans)

`GET api/admin/evp/summary?days=` (7/30/90/365): scans (with a per-day sparkline), candidates found,
proposed and samples, decisions kept and dismissed, misses, the share decided after listening and
blind, and the kept share blind against shown. Then a kept-vs-dismissed bar chart for each of nine
measurements (score, the six detector features, flatness, voiced share, voice-like). A marker ruled
more than once counts by its latest ruling. The ffmpeg health line from phase 4 sits at the top.

## Changed from the plan

- **Features 8–9 come from the second pass**, not from every frame of the first: the first pass keeps
  no audio, and measuring every frame of 90 minutes (autocorrelation at 161 lags) would cost far more
  than measuring the candidates.
- **The e2e checks scores, not the count.** The plan asked for a different number of candidates after
  narrowing the band. Tried on the test audio (tones at 500/1,500/2,500 Hz), every band from
  300–400 to 3,000–8,000 still finds all three: the room's noise is filtered to the same band, so a
  tone leaking past the filter still stands out from it. What moves is the score (94 at 300–3,400 Hz,
  72 at 3,000–3,400), through its "in the listening range" part. The test now checks that, and that
  putting the range back restores it. This is how the detector was designed (phase 5); worth knowing
  when tuning the band on a real recording.
- `EvpMisses.EvpScanId` has no foreign key (a second cascade path from `UploadFiles` that SQL Server
  refuses).

## Tests

- `FftTests` (the pin), `EvpSpectralFeaturesTests` (6): a voice-pitched buzz is tonal and voiced;
  hiss is flat and unvoiced; the fixture's utterance is mostly voiced; silence has nothing to measure;
  the sound is found inside its padding at either end; the cost.
- `VoiceNetTests`: a scan stores both features, with or without the model.
- `AudioMarkerControllerTests` (+5): a miss is recorded; a point or a span over a candidate is not;
  a recording never scanned has no misses; a blind ruling is stored blind, and every admin figure
  equals the table it counts, with each histogram's bins summing to its measured count; the bins
  place every value and skip the unmeasured.
- e2e: `Moving_the_listening_range_changes_how_a_rescan_scores`,
  `Blind_review_hides_the_readings_until_it_is_turned_off`, `The_evp_admin_page_shows_the_figures`.

- Results: 9,117 unit tests pass (two guards caught the admin page on the first run: it must wait
  for sign-in, and the product walk must open it; both fixed). e2e audio + mixer + Field Kit +
  LookAndTruths: 45 passed, 1 opt-in capture skipped, and the band test failed on its first form
  (above); rewritten, it passes (94 → 72 → 94).

## Deploying

Run `ProjectNotes/sql/M6-EvpLearningReads.sql` on production before the API. Safe to run twice.
