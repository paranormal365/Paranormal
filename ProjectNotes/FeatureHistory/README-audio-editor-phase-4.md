# Audio editor phase 4 — the case mixer

Branch: `feature/audio-editor-phase-4-case-mixer`, cut from `master` after phase 3 merged.
Findings: `ProjectNotes/AudioEditor-Audit-2026-09-06.md` (K-length, K-9th, K-transport, K-remove,
K-export, K-perm, 12) plus one this phase turned up.

## The one nobody was looking for

The mixer drew every clip the same width because it had no lengths to draw with — and it had none
because **no MP3 uploaded to this site has ever been measured**. `FileMetadataExtractorService`
constructed `Mp3FileReader` directly, which defaults to the ACM codec: `Msacm32.dll`, a Windows
system library. Off Windows every MP3 threw `DllNotFoundException`, a bare `catch` swallowed it,
and the row was written with no duration, no sample rate and no channel count. Silently — an MP3
nobody had measured looked exactly like one that could not be.

This is the same Windows-only decoder `AudioSourceReader` was built to replace, still living in the
extractor. The site runs on Linux. Nothing caught it because no unit test had ever decoded an MP3.

## The rest of the mixer

- **K-transport.** Play, Pause and Stop were three permanently disabled buttons with a tooltip
  saying preview was not available, so the only way to hear an arrangement was to render it, look
  at the case page, and come back. There is a real Web Audio preview now: each clip fetched and
  decoded once, scheduled at its offset through a gain and a pan. It shares the mute-and-solo rule
  with the export through `MixAudibility`, and it uses the pan law the export uses, so a preview
  and the thing it previews cannot be different mixes.
- **K-length.** Clips are drawn at their real length, an unmeasured one is drawn dashed and says
  "length unknown", and the timeline reaches past the longest thing on it instead of stopping at
  three minutes.
- **K-9th.** A ninth clip is refused with a sentence rather than stacked on top of the first.
- **K-remove.** The block's drag handler called `preventDefault` on every press, which swallowed
  the click meant for the ✕ inside it, so a clip once placed could not be taken off.
- **K-export.** Every failure said "Please try again", including a 403 that never will.
- **K-perm.** The Mixer button was shown to everyone while the export needs `Cases.Create`.
- **12.** The mixer averaged every source to mono, applied `tanh` to everything whether or not it
  had summed, and resampled by linear interpolation. Stereo survives now, the soft knee only
  touches what is over full scale, and resampling goes through NAudio's band-limited resampler —
  linear interpolation folds content above the new limit back into the audible band as tones that
  were never in the room.

## Verification

Five browser tests on the isolated stack, each run once against the un-fixed page. Six new unit
tests on the mixer's audio, each run once against the old downmix, the old `tanh` and the old
linear resampler. The MP3 fixture is now shared with `Ben.Web.Tests`, because the decode path had
no other way to be tested.
