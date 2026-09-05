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

### The rest of the phase

**Track state means something.** `IsMuted` was documented as "audio suppressed during playback and
export" and nothing read it — the menu flipped the flag straight on the model, so muting changed
the icon and the render was identical, and it could not be undone. It goes through the store now,
is undoable, and export asks one question (`ClipStore.IsAudible`) so preview and render cannot
disagree. A locked track's context-menu items are disabled rather than enabled-and-silent, and
Delete on a locked track says so instead of appearing broken.

**Animation follows its layer.** Keyframes are stored in project seconds and nothing connected them
to the layer they animate, so dragging a callout left its movement behind, playing over whatever
was there instead. The store now announces when an item moves or is removed, and the editor
forwards that to the keyframe service: the animation travels with the layer, and a removed layer
does not leave an orphan path to be written into the project file.

**Cross-track crossfades are retired.** What they produced could not be rendered correctly: the
export replaced the first clip's segment with a merged one longer than the two it replaced, while
every later offset stayed measured against the old length — and the preview never showed any of it.
Fading a clip up from black or down to it does the job honestly, was already exported and saved
(`ClipEffects.FadeInSeconds`/`FadeOutSeconds`), and is now offered on the clip's own right-click
menu. A project made earlier can still hold one; it is skipped rather than rendered wrongly.

**Resizing a transition can be undone.** The drag mutated the transition live for a smooth preview
and the commit then handed `UpdateTransition` the duration it had just written, so the undo step
recorded "from 2s to 2s".

**One place decides what a file is.** Two extension lists used to, and anything they did not name
took the video path — a `.heic` from a phone or a `.caf` recording became a 0×0 video clip with an
empty filmstrip. `MediaKindRouter` asks the browser's own type first and the extension second, and
knows the formats the lists missed. Imports land where the track actually ends, measured by trimmed
length and ignoring overlays. Nothing is dropped in silence: an import the host has switched off
now says so on its own row instead of reporting a successful import that produced nothing.

## Not done in Phase 2

**The media bin** — unplaced media that lives in the panel, survives a save, and is placed on
request (plan item D3). The defects behind it are fixed: imports no longer land on top of each
other, no longer use the wrong end of the track, and no longer vanish silently. What remains is the
feature itself, and it is a real one: the Media tab's three lists are hand-built render-tree code
that today shows the timeline's own items, so a bin means a new concept in the model, in the
project file, and in the riskiest component in the editor. It deserves its own pass rather than
being bolted onto the end of this one.

## Verifying## Verifying

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
