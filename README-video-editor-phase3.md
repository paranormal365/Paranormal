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

## Slice 2 — layers, overlay order and locale (done)

| id | what was wrong | fix |
|---|---|---|
| export-1 | Secondary video tracks were never exported. A clip on track 2 was on the timeline, in the properties panel, and absent from the file. | Each clip on a track above the primary is composited at its own timeline position, shown only across its own span, with its sound mixed in at the same offset. The unused composite that existed for this would have put every layer at zero and frozen its last frame over the rest. |
| motion-11 | Overlays on any track but the first were exported and drawn nowhere, so they could be neither seen nor clicked. | Both canvas layers read every video track. |
| titles-9 | Three passes in a fixed order meant stacking was decided by what kind of thing each overlay was, not the order they were added. | One pass, bottom layer first, across all three kinds. |
| callouts-2 | Rectangles and ellipses went through `drawbox`, which cannot round a corner or draw an ellipse. | Every callout goes through the renderer that draws it on screen. |
| titles-10 | Filter graphs used the browser's culture, so a comma decimal separator broke `enable='between(t,…)'`. | Explicit invariant formatting, plus invariant globalization in the standalone host. |

## Slice 3 — the zoom effects (done)

Seven effects (Zoom In, Zoom Out, Ken Burns for video and images, plus Pulse) each hand-wrote a
`zoompan` with the same three faults: progress in `on/fps`, a variable `zoompan` does not define; an
output size written as an expression, which its `s` option cannot take, leaving it to silently
resize the frame to 1280x720; and `d` set to the whole frame count, repeating every frame hundreds
of times. None of them did anything on export. They now share one builder, and the frame size is
passed in by the caller (motion-8).

## Slice 4 — settings honesty and a saved frame (done)

Source resolution now means the source's size rather than 1920x1080 (export-5). The last step is a
real container pass instead of a rename, so WebM is WebM, H.265 gets `hvc1`, and MP4 gets faststart
(export-14). Opus is offered for WebM only. The frame rate default matches its own documentation
(export-13). Progress only moves forward (export-8). One predicate decides whether there is
anything to export (export-20). Add to Queue works while a job runs (export-9). Reopening the
dialog re-attaches to a running job (export-11). Closing the destination prompt asks before
deleting a finished render (export-17). And **Save Frame** writes the frame under the playhead as a
PNG, from the source at full resolution.

## Slice 5 — the last of the pipeline (done)

The sidecar and the browser now agree on the canvas for video segments (export-4). A wedged engine
is reported as a failure rather than as the person's own cancellation (export-12). Audio segments
are PCM so the mix encodes once (audio-24). Clips and artwork the export could not include land in
the job's warnings, shown in the dialog.

## Found on screen, not by the tests

Two defects the suites could not have caught, both fixed here:

- **The media bin was invisible.** `TelerikTabStrip`'s root does not carry the component's Blazor
  scope attribute, so `.bv-browser__tabs {}` compiled to a selector that could never match. The tab
  strip kept its default sizing and its content box came out one pixel high under
  `overflow: hidden`. Every card was in the page and none of it could be seen, so importing a
  picture looked like nothing had happened. This is the same trap the side panel's own tab strip
  fell into in phase 1, and the fix is the same: reach it through `::deep` from an authored element.
- **An odd pixel size aborted the encode.** A 1007x675 photo — an ordinary screenshot or phone crop
  — was handed to the encoder as its own canvas, and H.264 in 4:2:0 cannot encode an odd dimension.
  In the browser the abort showed up as nothing at all: the preview stopped updating and kept
  showing the timeline as it had been. Every scale/pad now rounds down to even.

## Verified on screen

Standalone host at localhost:5180, a 4.8s .mov and a 1007x675 .png, ripple off, the image dragged
right to leave a one-second gap.

- The timeline reads 0:10.8 with a visible gap.
- The export's phase list reports `Rendered 1 gap(s)`.
- The commands show `-f lavfi -i color=c=black:s=1280x720:r=30 -f lavfi -i anullsrc=… -t 1.008` for
  the gap; `-ss 0.000 -to 4.802 -i …` (seek before input); `anullsrc` and `-shortest` on the image
  segment rather than `-an`; `scale=1006:674` for the odd-sized picture; and
  `-i output.mp4 -c copy -movflags +faststart`.
- The dialog reports **Export complete in 35.0s (1.0 MB · 0:10.8)** — the rendered file is the
  length of the timeline that produced it. Before this work the same project exported 9.8s with the
  gap closed and everything after it shifted.

One thing noted and not chased: the Working Window's proxy file measures longer than the plan says
it should for an image segment, while the scrubber's range uses the plan. That is preview fidelity,
which is phase 4's subject (preview-18, preview-7), not export correctness.

## Deliberately left for later

- **A queued job renders the live timeline, not a snapshot** (the other half of export-9). The
  settings are copied at enqueue; the timeline is not, so editing while a job waits changes what it
  renders. Fixing it properly means the export service rendering a project it is handed rather than
  the one scoped service it reads, which is a larger change than the rest of this phase and belongs
  with the persistence work in phase 5.
- **Embedded fonts in rasterised SVG** (titles-1, callouts-11). Google Fonts render in the live
  preview and fall back to a system font in the export. The loading is already there; embedding the
  faces as base64 in the SVG is a change to the renderer, not to the pipeline.
