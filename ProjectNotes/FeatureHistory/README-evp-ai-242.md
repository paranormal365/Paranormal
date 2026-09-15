# Item 242: EVP analysis that can tell a voice from noise, and says how sure it is

Branch: `feature/evp-ai-242` (off develop, 2026-09-15). Plan of record: backlog item 242 in
`ProjectNotes/Future-Improvements.md`. Ben: work on it "until my usage allowance runs out" (resets 2026-09-16 3pm).

## Why

The EVP detector (`Services/Audio/EvpDetector.cs`) narrows hours of tape to a review queue, but it is an energy
detector: it cannot say whether a burst is a voice. Ben wants a small AI that can, and wants Claude to teach it —
not to be it.

## Rules the work keeps

1. A speech detector decides "is it speech"; Whisper only suggests words, and only on flagged clips.
2. Whisper invents words from pure noise — every model is graded on noise-only controls, and that rate is shown.
3. Claude never labels audio. Training and grading use material whose answer is known because we made it.
4. Private audio never leaves the server: models run locally.
5. Nothing here enters the product until the measurement says it earns its place.

## Phase 1 — measure (in progress)

`tools/EvpLab/` — a console app outside `Ben.slnx`:
- **Known-truth generator:** backgrounds (room tone, tape hiss, hum; Ben's real recordings when supplied) with speech
  inserted at known times and loudness, plus non-speech impostors (knocks, steps, thumps) and noise-only controls.
- **Scoring harness:** catch rate by loudness, false alarms per minute on controls, impostor false alarms, time per
  minute of audio — the same report for every detector.
- **Detectors graded:** today's `EvpDetector` first; then Silero VAD and Whisper (tiny/base/small) once their model
  files are downloaded with Ben's go-ahead.

## Status

- 2026-09-15: branch opened. Ben approved downloading Whisper small only (not Silero VAD, the smaller Whisper models or
  LibriSpeech); speech comes from macOS `say` voices.
- 2026-09-15: bench built and run — findings in `EVP-Bench-2026-09-15.md`. Whisper invents nothing from steady noise
  but confidently turns reversed speech into phrases, its confidence cannot filter that, and it cannot read voices
  below the noise level. - 2026-09-15: Ben approved Silero VAD, noting the whole app runs under IIS on Windows. Graded the same day: clean on
  noise and knocks, ~125 ms per minute of audio on one thread, runs on the ONNX Runtime the API already loads — but, like
  Whisper, it finds almost no voices below the noise level. Direction: VAD as a queue-sorting signal in the API; Whisper
  (if at all) in a separate Windows service, never the IIS worker; phase 2 fine-tunes a small classifier on buried voices
  from the generator. Next input needed: Ben's real recordings (`generate --backgrounds <folder>`).

## Windows and IIS (Ben, 2026-09-15: "the whole app is being run from IIS and on a windows box")

- Anything in-process must be small, CPU-only, single-threaded per request and on a runtime already proven there
  (ONNX Runtime — the feed screener). Silero VAD qualifies.
- Whisper does not: separate Windows service, queued, one job at a time; model loaded once, not per app-pool recycle.
- Bench timings are from macOS; `score` runs on Windows, so the deciding numbers are measured on the server itself.
