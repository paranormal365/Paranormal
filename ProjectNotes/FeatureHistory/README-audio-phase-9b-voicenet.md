# Audio phase 9b: VoiceNet as the scan's second stage (10/27/2026)

Branch `feature/audio-phase-9b-voicenet`, off develop after phase 9 (`008b5de7`). Plan:
`ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 9b. Ben's decisions: VoiceNet runs inside the
existing scan rather than as a separate one, and the model is committed rather than fetched (10/26).
**Ben, 10/27: hold off on retraining.** Nothing on the site trains VoiceNet; v1 is what runs.

## What it does

After the detector finds its candidates, each one is measured for how voice-shaped it is, 0 to 1.
The reading is the highest one from one-second windows across the candidate's span, a quarter second
apart; a span under a second is measured in the second centered on it. It is stored beside the
detector's score and never changes it: `EvpDetector.Version` stays 1, `Version_one_scores_are_pinned`
still passes, and nothing is dropped, reordered or rescored.

- **The model** is `Ben.Data.WebApi/Models/voicenet-v1.onnx`: trained in `tools/EvpLearn` on
  LibriSpeech test-clean (CC BY 4.0) and synthetic noise, the run of 09/15/2026 (`runs/20260915-162340`,
  SHA-256 `15f5f4ec…3d76`), 1.1 MB. It is published with the API (`CopyToPublishDirectory`). Its
  front end is inside the network, so the server passes plain 16 kHz samples.
- **Where:** `Services/Audio/Voice/`: `IVoiceScorer`, `OnnxVoiceScorer` (batches of 64 windows, one
  thread), `NullVoiceScorer` (registered when the file is missing: logged at startup, and the editor
  shows the reason), and `VoiceScoring` (the windows and the measuring).
- **In `EvpScanService`**, so every scan gets it: the editor's, background jobs, and session copies.
  If the model fails, the candidates are proposed unmeasured and the scan succeeds.
- **Stored** (migration M5 `EvpVoiceLikelihood`): `EvpScanCandidates.VoiceLikelihood` (the eighth
  feature), `AudioMarkers.VoiceLikelihood`, and `EvpScans.VoiceModel` (`voicenet-v1`), so a future
  model's readings are never mixed with these. A reading sent from outside with bulk candidates is
  dropped: only this server's own measurement is kept.
- `GET api/evp/voice-model` says whether this server measures it, with which model, or why not.

## The editor

- Each candidate row has **Stood out** (the scan's score) and **Voice-like**, or "not measured" with
  the reason in its tooltip.
- Sort by **Stood out** (still the default), **Voice-like** or **Time**.
- **Hide below voice-like** (0–100) above the list: the person's own filter, applied on the
  page only. At 0 it hides nothing; unmeasured candidates are never hidden. It compares the number as
  shown, so a reading shown as 50 stays at "hide below 50". "n hidden" says how many are out of view.
- Help explains both readings; neither one says a sound is a voice or an EVP.

## Changed from the plan

- **The audio is read again with the same decoder the scan used**, keeping only the stretches the
  windows need, rather than through `DecodeRangeToWavAsync` once per candidate. One more pass at
  16 kHz, nothing to resample, and memory of a few seconds per candidate.
- **The slider is above the candidate list, not in Fine-tune.** Fine-tune holds the scan's settings,
  which go to the server; this filter never does.
- **The default sort stays Stood out**, as the plan says, until the gate in its step 6 is met (Ben's
  real recordings and the production Keep/Dismiss history). That gate has not been run.

## Tests

- `VoiceNetTests` (11): window placement for long and short spans and at both ends; the highest
  window wins, however the audio arrives (7-sample and 64k pieces); overlapping spans each get their
  own reading; a scan stores the reading on both tables with the model's name; the detector's spans
  and scores are identical with and without VoiceNet; without the model nothing is measured and the
  scan still proposes; a failing model leaves candidates unmeasured; the committed model gives one
  reading in 0–1 per window, and a window scored alone matches it scored in a batch of 70; a steady
  60 Hz hum reads below 0.5.
- e2e `Candidates_show_both_readings_and_sort_and_filter_by_voice_like`: both readings on every row,
  Voice-like puts the highest first, the top reading hides the rest and says how many, 0 brings
  them all back. The test audio's candidates all read 100: they are built to imitate speech (three
  voice-range tones rising and falling four times a second), and the hum test reads low.
- `The_evp_lab_advertises_the_smarter_detector_and_explains_it` now picks the scan's own score bar,
  since each row has two.
- Results: 9,099 unit tests pass. e2e audio + mixer + Field Kit + LookAndTruths: the two above failed
  on the first run (the test's assumptions, not the feature), and both pass after the fix.

## Deploying

Run `ProjectNotes/sql/M5-EvpVoiceLikelihood.sql` on production before the API, after M2, M3 and M4.
Safe to run twice. Check the API's startup log has no "will not measure voice-likeness" warning;
if it does, `Models/voicenet-v1.onnx` did not reach the server.
