# Audio editor phase 2 — the full-view editor acts on what you drew

Branch: `feature/audio-editor-phase-2-full-view`, cut from `master` after phase 1 merged.
Findings: `ProjectNotes/AudioEditor-Audit-2026-09-06.md` (B, D, E, G, J, M, N, O).

## The one that matters

**B.** Draw a region at 1:14–1:33, turn on silence detection, and the edit target becomes
3:00.6–3:06.5 — a stretch the machine found — while the region you drew is gone. Cut and Silence
then act on detected silence. The walk saw this happen; it is not a code reading.

The cause is that "not user-drawn" was tracked in the wrong place. `AudioFilePreview` keeps a
`_programmaticRegionIds` set and adds to it whenever *it* draws an overlay, so marker and clip
overlays are correctly ignored. But silence regions are added in JavaScript, inside
`detectSilence`, so their ids never reach C#. Each one fires `region-created`, the handler sees an
id it does not recognise, treats it as something a person drew, clears every other region to
enforce "one user region at a time", and makes it the edit target. Twenty silence regions do that
twenty times.

So the region's kind has to live where the region does. Every region now carries one — `user`,
`silence`, `marker`, `clip` or `overlay` — recorded in JavaScript as it is created and reported
with every region event. A region nobody registered is a region a person dragged, which is the only
honest default. `clearUserRegions` clears by kind rather than by "everything except this list",
which is what swept the silence regions away.

The decision itself — *which region is the edit target* — moves out of the component into
`RegionSelection`, a plain class with tests, so it can be stated once and checked without a browser.

**E** follows from B: the walk's Silence edit produced nothing and said nothing, and the region it
would have used was a machine one. This phase reproduces it against a region a person actually
drew before deciding whether anything else is wrong.

## The rest

- **D** — the high-pass, low-pass, compressor and noise-gate enable checkboxes toggle a bound bool
  and nothing else. Ticking one does not change what you hear until you also nudge its slider.
- **G** — the edit panel shows the region readout next to Gain, Fade, Speed and Pitch, which ignore
  it. The readout moves in with Cut and Silence, and the other operations stop sending bounds.
- **J** — a confirmed marker's ▶ only seeks. A point marker has no span to play, so it plays a
  window of context around itself; a span plays its own audio.
- **M** — a region's note is written in the explorer and visible nowhere else.
- **N** — the two Save-as-clip buttons disagree about whether Normalize starts on.
- **O** — a failed marker or file-type load is swallowed, so "no markers" and "the request failed"
  look identical, and the eight edit buttons go dead with nothing said.

## Verification

`dotnet build Ben.slnx` clean; `Ben.Web.Tests` green. New tests run once against the un-fixed code.
Then the walk on the isolated stack: draw a region, turn silence detection on, and confirm the edit
target is still the region that was drawn.
