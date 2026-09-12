# Audio editor phase 5b — the editor remembers how you listen

Branch: `feature/audio-editor-phase-5b-listening-chain`, cut from `master` after phase 6 merged.
Findings: `ProjectNotes/AudioEditor-Audit-2026-09-06.md` (L, the half phase 5a could not do).

## What this finishes

Phase 5a made the editor remember what you **see** — the spectrogram, its resolution, its colour
ramp, the mel scale, the timeline — because all of that fitted a column that already existed. The
listening chain did not: the equaliser's ten bands, the high- and low-pass filters, the compressor,
the noise gate and the silence threshold are fourteen numbers with nowhere to live, so every one of
them was reset on every open.

That is the half that matters most. Somebody working through a two-hour recording finds the filter
setting that lifts a voice out of a hiss; the settings that got them there are the work.

## The migration

One nullable `EditStateJson` column on `UploadFileAudioConfigs` — `AddColumn` up, `DropColumn` down,
no table rebuild and nothing that can lose data. **It has not been applied to the live database**;
it was applied to a throwaway one through `scripts/run-e2e.sh`, which uses `dotnet ef database
update --connection` rather than an environment override, and the sixteen audio browser tests then
passed against it.

One JSON column rather than fourteen of their own, because the chain will grow and a column each
means a migration every time a filter is added — and because this is a private working state that
nothing queries.

## What it took

- `AudioListeningChain` — a plain record with a tolerant round trip. Reading is field by field, so a
  row written before a setting existed keeps everything it does have; every value is range-checked
  and every non-finite one discarded, because a stored `NaN` gain multiplies the output into silence
  with nothing to show why. The equaliser is padded or trimmed to ten bands, since the component
  indexes ten sliders directly and a nine-band row would throw mid-render.
- It rides on `AudioViewState`, so there is still one save path and one gate. That needed an
  element-wise comparison: the equaliser is a list, and a list compares by reference, so two
  identical chains would otherwise read as different and every control would send a save.
- The chain is applied to the Web Audio graph **after** the graph is built, not when the controls
  are restored. The graph is made fresh on every open, so the other order would show a high-pass
  switched on over audio that had none — worse than not remembering it, because the panel would be
  lying about what you are hearing.
- The help article says so, including that what you hear on reopening is not the recording as it was
  captured.

## Verification

`dotnet build Ben.slnx` clean. `Ben.Web.Tests` 4475 green. Sixteen audio browser tests on a fresh
database, nothing skipped. The round-trip tests were run against code with the padding, the finite
guard and the element-wise comparison removed, and six failed; the browser test was run with the
restore removed and reported the high-pass coming back off.

**For Ben:** the migration reaches production at deploy. Nothing else in this branch needs a schema
change.
