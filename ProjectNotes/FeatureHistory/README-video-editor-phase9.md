# Video editor phase 9 — audio like a real NLE

Branch: `feature/video-editor-phase5-persistence` (phase 9 continues on it).
Plan: `ProjectNotes/VideoEditor-Audit-2026-09-05.md`, phase 9.
Follows `README-video-editor-phase8.md`.

Four commits, `2d1ec7e7` to `fe979ff7`.

Sound was the part of the editor that had been left behind. Most of what a person does to a clip
could not be done to a sound, and several of the things that could be done were quietly wrong.

## Slice 1 — sound behaves like the rest of the timeline

| what | it did |
|---|---|
| Audio and clip-art chips | Had no trim handles at all, so the only way to shorten a sound was to type numbers into a panel — while help said every clip trims by its edges |
| A trim drag | Pushed an undo entry per pointermove, so a two-second drag left dozens of steps and undoing a trim meant pressing Ctrl+Z until something moved |
| Separate Audio | Ignored the clip's trim and its speed, and reset the level. A head-trimmed clip's sound was out of step with its own picture from the first frame |
| A chip's waveform | Drew the whole recording however the clip was trimmed, so a thirty-second excerpt of three minutes showed the whole three minutes, and the halves of a split looked identical |
| The balance control | Silenced the right channel of a mono recording, which is what most handheld recorders produce: `pan` reads c1 and a mono file has none |
| A fade | Was clamped to half the source rather than half the clip, so a ten-second excerpt accepted a ninety-second fade |
| Silencing one sound | Meant dragging its volume to zero, losing the level it was at |
| The audio properties panel | Reloaded its fields on every render, and re-renders several times a second during playback — so a slider dragged while anything played snapped back |
| A locked track | Refused to be moved and left its levels wide open: volume, balance, fades and volume keyframes all went through |

Two builder tests asserted the bare `pan` filter, which was the bug. They state the contract now.

## Slice 2 — cleaning up a recording, and ducking

The editor had no audio effects at all. A recording made in a house at two in the morning is mostly
room tone, fridge hum and the recorder's own noise floor, and the two things anybody wants are to
lift the voice out of the hiss and to stop the level jumping between clips.

Both are expressed as things a person wants rather than as filter parameters. The reduction is
measured in decibels over a range nobody should have to learn, and pushing it past about thirty
turns speech into a warble — so the dial covers the part of the range that helps. The hiss comes
out before the level is measured, because measuring first means levelling to the hiss.

An audio track can also be set to duck the others. The mix sums the narration, sums everything
else, and runs the second through a compressor keyed off the first. The release is slow on purpose:
a fast one pumps audibly between words, which is more distracting than the loud music it was meant
to fix.

Nothing changes for a project that asks for none of it.

## What the screen found that the tests did not

Three, and all three were mine.

- **The trim handles never rendered.** The gate deciding which chips get them still listed video
  and image clips, so the audio branch behind it was unreachable. Every test passed because the
  tests exercise the store, not the markup.
- **Dragging one then appeared to do nothing.** The chip's width and duration label came from a
  helper that special-cased video, so a trimmed audio chip kept the full source's width however
  short it had been made. It asks the item for its own effective length now.
- **A Razor comment inside a `<div>`'s attribute list took the whole editor down**, with `Cannot
  set attribute on non-element child` repeated until it gave up. Phase 5 added a guard for exactly
  this mistake and scanned only components; a plain element breaks just as badly and less legibly.
  Four phases after writing that guard I walked straight past it. It covers both now.

Also caught by inspection while fixing the above: the chip stayed natively draggable during an
audio trim, which the file already documents as suppressing pointerup and stranding the drag.

## Verified on screen

Standalone host at 1440×900, storage cleared, with the 186-second recording from the test media.

- The audio chip has a trim handle at each end.
- Dragging the end handle shortens the chip, and its label follows.
- One Ctrl+Z restores the whole drag, not one pointermove of it.
- The properties panel reads **Source 3:06.5 / Trimmed 0:52.2**, its fade sliders now stop at 26
  seconds — half the trimmed clip, where they used to offer half the source — and the Clean up
  section carries the hiss dial, the levelling switch and the per-clip mute.

## Not done in this phase

- **A split does not insert an interpolated volume keyframe at the cut** (audio-10), so splitting a
  clip mid-ramp still loses the ramp. Contained, and it belongs with the volume-envelope work
  below rather than on its own.
- **The envelope lane does not observe resize and draws its baseline at zero rather than at the
  clip's scalar volume** (audio-17, audio-18). Both are in the canvas drawing code, which is one
  piece of work.
- **dB labels on the meter** (audio-22) and **the Properties waveform being a seek surface rather
  than a second player** (audio-26). Polish, and the second needs the playback service to accept a
  seek from a component that is not the preview.
- **Link and unlink from the audio side, with a visible link glyph** (audio-21). The link exists
  and is honoured; only the audio-side control is missing.
