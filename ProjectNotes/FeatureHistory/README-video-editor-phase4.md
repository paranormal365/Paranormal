# Video editor phase 4 — playback, preview and robustness

Branch: `feature/video-editor-phase4-playback-robustness`.
Plan: `ProjectNotes/VideoEditor-Audit-2026-09-05.md`, phase 4.
Follows `README-video-editor-phase3.md`.

Phase 3 made the export agree with the timeline. This phase is about the hours before the export:
the preview you actually edit against, and what happens when the engine underneath it stops.

## Slice 1 — a stopped engine says so (F7, preview-4)

Every failure was treated the same: state to Error, and there it stayed until somebody pressed
Initialize again. Nothing announced it, so after a crash the editor went quiet — preview frozen,
exports refusing to start, one status chip most people never look at.

| what | how |
|---|---|
| A bad command, a trapped instance and a full heap were one case | `WorkerFailureClassifier` reads the failure and the engine's own log tail, and says which |
| A crash left the editor dead | The editor restarts the engine itself, rate limited to one a minute by `FfmpegCrashRecoveryPolicy`, because whatever crashed it is usually still on the timeline |
| Out of memory was restarted for, pointlessly | A fresh engine has the same memory. It says the work is too big for a browser tab and points at the native helper |
| A wedge showed only on the diagnostics chip | The main status chip says "Stuck — reset it", and a **Restart engine** button appears beside it for everyone |
| The full-quality preview could not be stopped | The job is held: closing stops it, there is a Stop button, and ten seconds without progress offers to restart the engine |
| A failed preview rebuild vanished into a fire-and-forget task | Logged; the last good preview stays on screen and the person is told it is behind |
| Every full-quality preview left a full-size render in browser storage | Deleted, like the download path's copy |

## Slice 2 — the preview behaves like a player (preview-2/5/8/9/18/19/20, audio-6)

- Rebuilds keep the playhead and carry on playing. An explicit load still starts at the beginning.
- Audio tracks are mixed into the Working Window. `AudioMixPlanner` is shared with the export, so
  the two agree about the soundtrack by construction.
- Removing the last clip clears the player instead of leaving the old render playing.
- Image-only playback drives the counter, the scrubber and the overlays, not just the pictures.
- The popout keeps its own playback state; it and the Working Window were overwriting each other's.
- The quality dropdown rebuilds the preview instead of waiting for an unrelated edit.
- `AutoPreviewGate` keeps the rebuild out of a running export, and gives up on a stopped engine
  rather than polling it forever.

## Slice 3 — frames (preview-7/10/13/14, timeline-17)

Three numbers disagreed about the frame rate: editing said 24, export said 30, the ruler assumed a
constant 30. They agree now, and the ruler follows the session. The test meant to prevent that
drift asserted the literal 24, so it passed straight through it — it asserts the relationship now.

The playhead follows painted frames rather than the browser's four-a-second `timeupdate`, the frame
counter no longer reads one past the end, and **Set In** / **Set Out** trim a clip to the playhead.

## Slice 4 — importing (media-1, media-9)

Building the filmstrip seeked after the input, so a half-hour clip was decoded once per thumbnail.
Each frame now seeks before its own input and decodes keyframes only. The exit code is honoured.
Local imports can be cancelled, which the button always claimed and never did.

The sidecar's own copy of that argv was caught by its parity fixture test and updated in the same
pass — the two would otherwise have differed by which engine happened to be paired.

## Also here

Browsers that have the storage but cannot write to it — Safari before 26 — are detected by probing
a real write rather than trusting a feature check, and the person is told up front that a project
reopened there will have missing clips. Every write was failing and being swallowed as non-fatal,
so the editor worked perfectly until a reload.

## Verified on screen

Standalone host at 1440x900: a 29.5 second clip and a 186 second music track.

- The frame-rate picker opens at **30**, matching the export, not 24.
- The preview's commands show the mix running in the Working Window:
  `-ss 0.000 -to 186.540 -i …mp3 -vn -filter:a volume=… -c:a pcm_s16le preview_audio_000_….wav`
  then `amix=inputs=2:duration=longest:normalize=0:dropout_transition=0` with the limiter, mapping
  `[aout]`. The music is audible while editing for the first time.
- The preview's duration is **186.5s**, matching the timeline, so the scrubber covers the music
  that runs past the last clip rather than stopping at the picture.
- Playing from 1:00 and then dragging a clip: playback carried straight on and was at **2:07**
  after the rebuild, with the new length. It used to jump to 0:00 and stop.
- **Set In** and **Set Out** appear on a video clip and are correctly disabled while the playhead
  is elsewhere. With the playhead at 12s, Set Out trimmed the clip from 0:29.5 to exactly **0:12.0**.

## Not done in this phase

- **A `ProjectSettingsService` merging `ExportResolutionService` and `PlaybackService.SessionFps`.**
  The plan called for one, but the defect was three numbers disagreeing, and they agree now. A
  rename on top of that is churn without a behaviour change, so it is left for whenever something
  actually needs the combined object.
- **A sync-access-handle worker for OPFS writes on Safari.** The gate is honest about the limit;
  routing writes through a worker to lift it is a piece of work in its own right.
