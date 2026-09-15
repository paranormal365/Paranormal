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

- 2026-09-15: branch opened.
