# Audio phase 8: the detector's own view (10/26/2026)

Branch `feature/audio-phase-8-detector-view`, off develop after phase 7 (`bedf8cbb`). Plan:
`ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 8. Ben: "Provide a good deal of visual
analysis."

## Server

- **`EvpTrace`** on every analysis: per 0.1 s the loudest voice-band and full level (a short sound
  is never averaged away), the mean background, and the gate's raw openings before joining and the
  shortest-sound rule. The threshold line is worked out where it is drawn.
- **Migration M3 `EvpScanTraces`**: `EvpScans.TraceBlob varbinary(max)`, `TraceBucketMs int`.
  `EvpTraceCodec` ("EVPT" v1: hundredths of a dB in 16 bits, gate openings in ms; 90 min ≈ 324 KB).
  Written by `EvpScanRecorder` with the scan's record, allowed to fail like the rest of it. Kept, not
  worked out again: redrawing would otherwise mean decoding the recording again.
- `GET …/audio-markers/scan-trace` (latest scan; thinned to ≤ 20,000 points keeping each group's
  loudest level). Never scanned, or scanned before traces: a 404 with a sentence ("Scan it again").
- `EvpDetector.Explain` → three terms in points with a sentence each; `Score` now shares its length
  rule (`DurationShape`). `GET …/audio-markers/{id}/detection` reads the candidate's stored
  measurements; a hand-placed marker gets "nothing to explain". Version stays 1; pinned scores pass.

## Editor

- **View ▸ Level lane** (client only: RMS per 10 ms from the decoded audio, silence line when on)
  and **View ▸ Detector lane** (the trace: band level, background, background + threshold dashed,
  gate openings shaded). Panes under the waveform, redrawn on scroll and zoom; both remembered
  (`AudioWorkspace.LevelLane`, `DetectorLane`). A lane with nothing to show says why once.
- **Why?** (ⓘ) on each candidate row: a three-part bar, the three sentences, the measurements in
  plain words, and "the score is how much a sound stood out, not how likely it is to be a voice".
- **Candidate strip** above the waveform: every candidate (bolder = higher score) and kept marker;
  a click highlights and plays it.
- **Live meter** on the hint line: 64 log-spaced bands and the level with a one-second peak hold,
  from an `AnalyserNode` at the end of the listening chain.
- **Hear the original** (Listen ▸, B): the source routed straight to the meter and the speakers,
  round every listening tool; an "Original" tag while on.

## Changed from the plan

- No spectrogram thumbnail in the Why? panel; the candidate's row zooms the spectrogram to it already
  (phase 7).
- The trace is fetched whole (thinned to 20,000 points) rather than per visible window: at 90 minutes
  that is a point per 0.27 s, finer than the lane can draw at fit, and one request instead of one per
  scroll.
- Stereo channel views: later, as the plan said.

## Tests

- Detector: the trace covers the recording at 0.1 s; every candidate lies in a gate opening; at each
  candidate the level crosses background + threshold; the explanation adds up to the score; a short
  sound is called short.
- `EvpTraceCodecTests` (round trip to 0.01 dB, 90 min ≈ 324 KB, anything else is no trace, thinning
  keeps the loudest moment). Controller: trace after a scan, never scanned, from before traces,
  forbidden, the explanation adds up, a hand-placed marker.
- e2e: `The_detector_lane_and_why_explain_a_candidate` (both lanes have ink, Why? has three terms,
  both lanes come back after reopening), `The_strip_the_compare_switch_and_the_meter_work`. A light
  and a dark capture were read: the lanes show the three sounds, the dashed line and the shaded gate.
- Audio + mixer + picker + LookAndTruths e2e: 51 of 51. Unit tests: all pass.

## Deploy

**Migration M3.** Run `ProjectNotes/sql/M3-EvpScanTraces.sql` on MonsterASP before the API. Scans
made before it have no trace; the lane says to scan again.
