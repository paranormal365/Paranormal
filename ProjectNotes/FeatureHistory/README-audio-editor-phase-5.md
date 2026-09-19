# Audio editor phase 5a — the editor remembers how you look at a recording

Branch: `feature/audio-editor-phase-5-persistence`, cut from `master` after phase 4 merged.
Findings: `ProjectNotes/AudioEditor-Audit-2026-09-06.md` (L).

## What was wrong

`UploadFileAudioConfig` — the table, the controller, the client and the mapper — has existed since
2026-07-18, and **nothing had ever read or written it**. So the spectrogram, its colour ramp, its
resolution, the mel scale and the timeline were reset on every open. Somebody working through a
two-hour recording sets them up several times an hour.

## What this slice does, and what it leaves

The view rides in the config row's existing spectrogram-options column, so remembering it needs
**no schema change**: `WsSpectrogramOptions` grew a `colormap` and a `melScale`, and both are absent
in an older row, which reads as "never chosen".

`AudioViewState` is a plain record with the round trip and the merge, so it is testable without a
browser. It carries every setting on the row it does not own, because the upsert replaces the whole
row and would otherwise wipe the wave colour, the zoom bounds and the player's height — settings
this panel has no control for and would give no sign of having destroyed.

**The listening chain is not here.** EQ, the filters, the compressor and the noise gate have no
column to live in; that is the `EditStateJson` half of phase 5 and it needs a migration, which
reaches the live database only at deploy. Ben runs that, so it is deliberately not in this branch.

## A bug this turned up in its own work

The first version saved nothing at all, and the editor said only "these settings aren't yours to
change" — because `UpsertAudioConfigRequest`'s three height fields are **not nullable**, a null
fails model binding, and the 400 comes back as a `ProblemDetails` blob that the client correctly
refuses to show as prose. So a validation failure was reported as a permission failure. Found by
sending the same body by hand. The save now supplies the record's own defaults, a test pins it, and
the save reports the server's sentence when there is one.

## Verification

`How_you_set_the_editor_up_survives_closing_it` picks a colour ramp, closes the editor and reopens
it. With the restore removed it fails.
