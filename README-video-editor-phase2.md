# Video editor — Phase 2: a timeline you can trust

Branch: `feature/video-editor-phase2-timeline-model` (on top of Phase 1)

## Why

Two findings from the 2026-09-05 audit, both rated as breaking the core promise:

- **F5.** Clips could overlap, and you could not see it. A drag wrote whatever position the pointer
  ended on, and the lane drew its clips end to end with any negative gap clamped away — so the
  model had them stacked while the picture showed them politely adjacent. The track's length, the
  ruler and the export dialog each reported something different.
- **F6 / timeline-1.** One playhead was read in two coordinate systems. Selecting a clip loaded its
  raw source into the preview, which reset the clock to zero and counted from that clip's own
  start — and split, markers and title placement all read that clock as if it were the timeline's.
  Split cut early by exactly the clip's start position. For the first clip on a track the two
  clocks agree, which is why it looked right.

## What this branch does

### One playhead

`PlaybackState` now has two clocks: `CurrentTime` (media time — where the player is inside whatever
it is showing) and `TimelineTime` (where the playhead is on the timeline). Everything that means a
position on the timeline reads the second: split, markers, `+ Text`, callouts, asset placement, the
playhead itself, and the split slider's starting value.

Selecting a clip on the timeline no longer hijacks the preview. It moves the playhead to that
clip's start; previewing a single clip is still what clicking a card in the Media tab does.

`ClipStore.SplitClipAtTimelineTime` takes an absolute position and converts it, and returns false
when the playhead is not inside the clip rather than throwing into a swallowed catch. `AudioClip`
gained `TrimmedDuration`, and `TrackItem.EffectiveLength` is now the one place to ask how much of
the timeline an item occupies — audio silently used its untrimmed source length everywhere before.

### No overlap, and nothing hidden

`TrackLayout` states the rule: the clips that own a place in time — video, audio, image — run one
after another and never overlap. Overlays and transitions are exempt by design.

`ClipStore` enforces it. Every drag commit resolves the drop, re-sorts the track and renumbers
`Order` (export sequences by `Order`, so a drag that changed what plays first used to render a
different arrangement than the one on screen), and asserts the invariant in debug builds.

The lane draws each clip at the time it actually starts, so a gap is a gap and an overlap would be
visible if one could exist.

**A drop onto an occupied spot is now a decision.** With ripple on it inserts — the clip stays
where you dropped it and what was there moves on. With ripple off you are asked: Insert, Overwrite
or Cancel, through the prompt that already existed for "Add to Timeline".

### A ripple move is a lift and an insert

The ripple commit shifted every later clip by the **drag distance**, which is a different tool
("move this clip and everything after it") and broke outright on a backwards drag: dragging a clip
eighteen seconds earlier moved the clips behind it eighteen seconds earlier too, through zero into
negative time. The new no-overlap assertion is what surfaced it. It now lifts (the clips after it
close up by its own length) and inserts (what is where it lands moves on).

### Transitions take time

A crossfade makes two clips play at once for its length, and ffmpeg's xfade output is A + B − d.
The store centred the transition on the junction and moved nothing, so the timeline claimed a
length the render never produced — every marker, overlay and audio clip after the junction sat
later than whatever it had been lined up with on screen.

Adding one now pulls the second clip back by its duration and everything after it follows;
removing it gives the time back; lengthening it pulls further. The pair is allowed to overlap by
exactly the crossfade and no more, which `TrackLayout` knows about, and the transition chip covers
the stretch where both clips actually play. Durations are clamped to what the two clips can spare —
the 1.0s every caller hard-coded was a request, not a promise.

Nothing checked that a transition's junction still existed, so removing, splitting or moving either
clip left it behind pointing at a clip that was gone, and the export matched transitions to
junctions by position — applying it to whichever pair happened to be there. A reconciler now drops a
stranded transition and closes the overlap it was justifying.

### Imports land where they fit

`AddClipToTrack` never set a position, so every clip added through it sat at zero and a second
import landed exactly on top of the first. It appends in time now, not just in list order. A
position the caller has already chosen is respected, so the Server tab still places at the playhead
and restoring a project still uses the positions in the file.

## Still to do in Phase 2

Deliberately not in this branch, in the plan's order:

- the media bin and one placement policy for every import (plan item C / D3)
- retiring cross-track transitions in favour of clip fade-in/out (D2, transitions-9)
- an undoable commit for the transition edge-drag resize (transitions-6)
- motion keyframes stored relative to their layer (motion-3)
- track mute and lock actually honoured by preview and export (audio-5, timeline-11/12)

## Verifying

```
dotnet build Ben.slnx
dotnet test Ben.Video.Tests
dotnet test Ben.Web.Tests
```

On screen, with the WebAssembly host running: import a video and an image, drag the image onto the
video with ripple on (it inserts) and with ripple off (it asks), undo, and split a clip that does
not start at zero.

## What was verified on screen

- Clips are positioned by time: video at 0, image at 4.8s, total 9.8s.
- Ripple drag onto the video: image lands at 2.8s, video pushed to 7.8s, total 12.6s, no overlap.
- Ripple off: the "Not Enough Room" prompt appears and the clips stay put until it is answered.
  Insert applies it; one undo puts both clips back.

40 new tests cover the two clocks, absolute-time splitting, the layout rule, every commit path and
the transition time model. The four that matter were run against the old code first and fail there.

Also verified on screen: adding a Dissolve between a 4.8s and a 29.5s clip moves the second clip
back by one second and takes the timeline from 34.3s to 33.3s — the length the render will be.
