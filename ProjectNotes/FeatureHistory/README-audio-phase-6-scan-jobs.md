# Audio phase 6: EVP scans in the background, with live progress (10/26/2026)

Branch `feature/audio-phase-6-scan-jobs`, off develop after phase 5 (`004e2bb6`). Plan:
`ProjectNotes/AudioEditor-EVP-Plan-2026-10-26.md`, phase 6. Ben chose (planning, 10/26): "Background
job, but with an async, non-blocking percentage complete notification", the waveform
"semi-transparent where not yet scanned, turning solid".

## The detector reads a piece at a time

`EvpDetector.AnalyzeAsync(IAsyncEnumerable<ReadOnlyMemory<float>>, …, IProgress<EvpScanProgress>)`
keeps per-frame measurements only (about 24 bytes per 10 ms: 13 MB for 90 minutes, where the samples
were 345 MB). The filters keep their state between pieces, each frame's level is summed oldest
sample first as before, and the zero crossings are counted as the samples pass, so the results
are identical to the bit however the audio is split. The array `Detect` is now one piece through the
same path, and `Version_one_scores_are_pinned` still passes untouched.

`Streaming_gives_exactly_what_the_whole_recording_gives` (7, 160 and 65,536-sample pieces) fails
when the sign carried between pieces is disturbed, so it tests the carry.

Progress reports `ProcessedSeconds` and `SettledSeconds` (read minus half the background window:
the background at a moment depends on what follows it, so later audio can still change it).

## Jobs (migration M2, `20261026210214_EvpScanJobs`)

- `EvpScanJobs`: who asked (no cascade, see the entity), the settings, status, percent, a sentence
  when it failed, cancel requested. `EvpScanJobSegments`: one per recording, its progress and,
  when done, its candidate count, top score and `EvpScan` id. **One unfinished segment per
  recording**, held by a filtered unique index (two scans at once would replace each other's
  Pending candidates).
- `EvpScanService.RunAsync`: the one way a recording is scanned. The quick scan
  (`POST …/audio-markers/scan`) and every job use it.
- `EvpScanWorker` (hosted): one job at a time per server, oldest first; woken by `EvpScanQueue`
  and looks at the table every 30 s anyway. On start, a job left Running by a restart becomes
  Failed: "The server restarted during this scan. Start it again."
- `EvpScanRunner`: progress written at most once a second from a loop beside the scan, never
  blocking it. A recording that cannot be read fails with its sentence; the job fails only if none
  could be scanned. **A cancel somebody asked for is not a shutdown**: only the flag set by Cancel
  ends as Canceled; a shutdown leaves the job Running for the restart tidy-up to explain. Canceled
  mid-recording keeps the old Pending candidates (they are only replaced when a scan completes).
- `EvpScanJobRetentionJob`: deletes jobs finished more than a week ago. Results live on as markers
  and `EvpScans`.

## API

| | |
|---|---|
| `POST api/upload-files/{id}/scan-jobs` | 202 + the job. 400 `ScanRefusalRecord` when this server can't read it (refused now, not queued to fail); 409 when that recording is already being scanned; 429 at 3 unfinished scans per person |
| `GET api/scan-jobs/{id}` | the requester only (others 404); ETag, 304 when unchanged |
| `GET api/upload-files/{id}/scan-jobs/live` | the caller's unfinished scan of it, or 204 |
| `DELETE api/scan-jobs/{id}` | a queued job stops at once; a running one at its next piece |

## Editor

- **Scan for EVP** starts a job. The button reads "Scanning 42%", a **Stop** button appears, the
  toolbar's Scan says the same, and the page stays usable.
- The waveform is dimmed beyond what is settled, with a moving hatch over the part being worked on
  (static under reduced motion), and solid again when it finishes (`setScanProgress` in
  `WaveSurferPlayer.razor.js`: layers inside WaveSurfer's own wrapper, in percent, so zoom and
  scroll need no redraw).
- Watched every 1.5 s (the first look after 0.3 s), with the ETag. Leaving the page stops the
  watching, never the scan; reopening picks it up ("Picking up the scan that was already running.").
- Finished: the same sentences as before ("3 candidates to review.", "Nothing stood out from the
  background."), or the server's reason plus "This recording has not been examined."

## Changed from the plan

- **The editor always uses a job**, not only for recordings over 10 minutes. One path, the same
  progress for every scan; a short one finishes within the first look.
- **Phase 9's tables are not in M2.** The plan put them here to save phase 9 a migration; they are
  left to phase 9, which will know their shape, rather than guessed at now.
- No minimap mask yet (the minimap arrives in phase 7).

## Tests

- Detector: streaming identical (3 piece sizes), progress to the end, cancel.
- `EvpScanJobTests` (20): the runner (lands candidates, can't-read sentence, canceled before and
  during, shutdown left for restart, deleted recording, never run twice, percent weighting,
  progress written mid-scan with a slowed decoder), the worker's restart tidy-up, the API (queued
  and the worker woken, 409, 429, refused before queueing, forbidden, only the requester, ETag 304
  then 200 after a change, cancel a queued job frees the recording, live), the weekly sweep.
- e2e: `A_scan_runs_in_the_background_and_shows_how_far_it_has_got` (a 10-minute recording; the
  button said "Scanning 0%", the waveform was dimmed, the dimming went, 3 candidates),
  `A_scan_can_be_stopped`. A frame-by-frame capture was read: dimmed while scanning, solid with
  the three marks after.

## Deploy

**Migration M2.** Ben runs `ProjectNotes/sql/M2-EvpScanJobs.sql` on MonsterASP, then deploys the API
(the worker is a hosted service and starts with it) and the website.
