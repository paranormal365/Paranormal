# Large-media walk — where the browser editor gives out

Run 2026-09-06 on the standalone host (`/editors/video/`, local build) in the Claude browser pane,
on a 2026 MacBook Pro. This is the measurement phase 11 promised and phase 12 finished.

## What was measured

Real footage, imported through the editor's own file input, on a timeline that already held one
short clip.

| File | Size | Fetch | Import (to a usable card) | Stored in the browser |
|---|---|---|---|---|
| stock loop | 47 MB | instant (local) | ~20 s | 47 MB |
| ghost-covered-in-sheet | 284 MB | 0.1 s | 52.9 s | 284 MB |
| eye-level-mansion | 348 MB | 0.2 s | 56.2 s | 348 MB |

Storage offered by the browser on this machine: **6377 MB**. Usage after the three imports: 735 MB.
Nothing is compressed or re-encoded on import — a file costs its own size.

## After placing the 348 MB clip on the timeline

| | Time |
|---|---|
| Rendered preview rebuilt (status back to Ready) | 66 s |
| Switching the same timeline to Live | 2.9 s |

That contrast is the whole argument for the live player, measured rather than asserted.

## Export

An export of a two-clip timeline containing the 348 MB source trimmed the large clip successfully
in roughly three minutes, then stalled on the second clip — whose media had been wiped from browser
storage earlier in the session, deliberately. The chip was marked missing in the timeline and the
live player said so, but the export gave no such reason and simply sat at 22 %.

**Worth following up:** an export containing a clip whose media is missing should refuse with a
reason, or skip the clip and say so in the job's warnings. Not fixed here; it needs a decision
about which of the two it should do.

## Found and fixed during the walk

Cancelling that export answered with a window titled **Export Complete** and a prompt asking
whether to save the project. `ExportDialog.StartExportAsync` awaited the render and then invoked
`OnExportComplete` whatever the job's final state. Both sides now check the state, and
`ExportCompletionGuardTests` fails against the un-fixed dialog.

## Not measured

The 511 MB file in ~/Downloads is a personal family video, not test media, so the ladder stops at
348 MB. A larger stock file would be needed to find the point where the browser engine actually
fails rather than merely gets slow.
