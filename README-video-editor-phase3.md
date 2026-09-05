# Video editor phase 3 — the render matches the timeline

Branch: `feature/video-editor-phase3-render-fidelity`.
Plan: `ProjectNotes/VideoEditor-Audit-2026-09-05.md`, phase 3.
Findings: `ProjectNotes/VideoEditor-Audit-2026-09-05-Findings.md`.

The audit's summary of this phase: a Camtasia-class editor that exports something other than
what the timeline shows is broken however good the editing feels. Phase 2 made the timeline model
trustworthy; this phase makes the export agree with it.

## Slice 1 — gaps, junctions and audio streams (done)

| id | what was wrong | fix |
|---|---|---|
| export-2 | Gaps between clips were closed on export while the audio, overlays and chapter marks kept timeline time, so everything after the first gap played against the wrong picture. | New pure `ExportSegmentPlanner` inserts a black-and-silence filler for every gap, including a leading one. Both the export and the Working Window build from the same plan. |
| transitions-2 | Transitions were matched to junctions by position, so one transition anywhere on the track gave every other junction an unrequested one-second fade. | `ExportSegmentPlanner.MatchTransitions` resolves each junction by the pair of clips the transition names. A junction with no transition is a cut. |
| transitions-1 | Any export containing a transition came out silent; with an audio track it failed. The filter graph produced only `[vout]`, so the audio codec arguments applied to a stream nothing selected. | `BuildXfadeFilterComplex` builds a parallel `acrossfade` chain and labels `[aout]`; both callers map it. |
| export-3 / audio-2 | Image segments and muted clips were written with `-an`. Concat takes its stream layout from the first segment, so a slideshow with music, or any timeline opening on a photo, lost its audio. | Every segment carries an audio stream when the export includes audio — a real one, or `anullsrc` silence. |
| audio-4 | `-ss` came after `-i`, so the filter graph saw the source's own timestamps and a fade or volume envelope was applied to the head that had just been trimmed away. | Seek before the input in `BuildTrimArgs`. |
| audio-1 | The mix referenced `[0:a]` unconditionally, so a silent assembled video failed the export outright. | `BuildAmixArgs(videoHasAudio:)` leaves it out of the graph. |
| audio-3 | `amix` defaults meant adding one music track dropped the dialogue about 6 dB, and the mix swelled for two seconds each time an input ended. | `normalize=0:dropout_transition=0`, with an `alimiter` catching the peaks the averaging used to hide. |

New tests: `ExportSegmentPlannerTests` (14), plus rewritten argv assertions in
`ExportArgBuildersTests` and `AmixArgBuilderTests`. Several existing tests pinned the old
behaviour and now describe the new contract, each with the reason in place.

## Still to do in this phase

- Secondary video tracks are never exported (export-1); the canvas overlays only enumerate the
  first video track (motion-11).
- One overlay pass ordered by `LayerIndex`; every callout through the SVG renderer rather than
  `drawbox`; invariant number formatting; embedded fonts; zoompan as scale+crop
  (titles-9/10, callouts-2/11, motion-8).
- Still-frame export.
- Settings honesty: source resolution, fps, codec/container pairings, monotonic progress, one
  exportable-content predicate, a wedged worker reported as Failed.
- Queue: `CanQueue` separate from `CanExportNow`, a snapshot at enqueue, re-attaching the dialog
  to a running job.
- Audio segments as PCM so the mix encodes once (audio-24); skipped clips into `job.Warnings`.
