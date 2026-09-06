# Audio editor phase 3 — the explorer shows the region you opened

Branch: `feature/audio-editor-phase-3-region-explorer`, cut from `master` after phase 2 merged.
Findings: `ProjectNotes/AudioEditor-Audit-2026-09-06.md` (H, plus the explorer's own size).

## What was wrong

The explorer downloads one region's audio and decided whether to do it again by asking whether it
had ever loaded anything:

```csharp
if (!Visible || _source is not null) return;
```

So the first region a person explored was the audio they heard for every region afterwards, while
the title, the notes panel and the Save button all moved on to the new one. Listen to the second
region, decide it is worth keeping, save it — and the file that arrives is not the sound that was
playing. That is the worst shape a bug can take on a site about evidence.

The walk never reached this. It was blocked every time by finding I, which is fixed, so this is the
first run that could open a second region at all.

## What this phase does

- The reload decision becomes a comparison of three numbers — file, start, end — in
  `RegionExplorerKey`, a plain type with tests, with a tolerance so floating-point drift does not
  re-download the same audio on every render.
- Everything the panel shows or writes now describes the range that is **loaded**, not the
  parameter as it stands: the title, the clip-time and in-file readouts, the notes it filters, the
  range a new note is filed against, and above all what Save sends.
- While a new region is loading, the old region's audio is cleared first, so there is no moment
  where the panel is playing one stretch and would save another.
- The explorer's own modal was still `Size="sm"` — the same 300-pixel keyhole the main editor was
  rescued from in phase 1a, holding a waveform, a transport, a notes list and a save form side by
  side. It is fullscreen now.

## Verification

The browser test explores two regions in turn and fingerprints the waveform each one drew. Against
the old rule the two fingerprints are byte-identical; with the fix they differ.
