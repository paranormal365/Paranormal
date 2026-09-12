# Video editor phase 8 — overlays, keyframes, placement and redaction

Branch: `feature/video-editor-phase5-persistence` (phase 8 continues on it).
Plan: `ProjectNotes/VideoEditor-Audit-2026-09-05.md`, phase 8.
Follows `README-video-editor-phase7.md`.

The largest phase in the plan. Six commits, `e4c9990d` to `363a60a4`.

Two threads run through it. The first is making the annotation layers behave like everything else
in the editor — undoable, consistent, and drawn where they will render. The second is two features
this product needed and did not have: hiding part of a picture, and putting a clip somewhere other
than the whole frame.

## Slice 1 — titles behave like everything else (titles-2, -4, -5, -6, part of -12)

Four things a title could not do that every other layer could.

- **Nothing done to a title could be undone.** Its edits went into the model with nothing pushed
  onto the stack, so Ctrl+Z after changing a title undid whatever had happened before it — worse
  than doing nothing.
- **The panel was edit-then-Apply**, alone among the property panels, and held the typed values
  locally meanwhile. Three high-frequency events refresh that panel: a mutation anywhere in the
  project, a keyframe change, a playback tick. Any of them arriving before Apply discarded the
  edit. There is no Apply button now because there is nothing left for it to do.
- **The first drag jumped, usually off the frame.** Without a position, a centred bottom title
  anchors at the middle of its bottom edge, where the handle sits. The first drag wrote a position
  and the renderer switched to treating the same numbers as the box's top-left. Alignment decides
  the anchor now whether or not the position was set by dragging.
- **Titles never wrapped.** Callouts had for a while and the wrapping code is shared; only titles
  had no way to ask for it.

The renderer test that asserted the old anchor behaviour asserted the bug. It states the contract
now.

## Slice 2 — callouts and clip art (callouts-3, -4, -6, -8, -9, -10, -12, -15, -17)

| what | it did |
|---|---|
| An arrow's control points | Canvas fractions with no relationship to the shape, so moving or resizing a callout left its arrow behind. Now fractions of its own box; older projects convert on open |
| Changing a callout's shape | Left the old shape's points and none of the new shape's — a star with no radii and a stray corner radius |
| Clip-art edits | Went through the store method that mutates and pushes nothing, so none of them could be undone |
| A missing clip-art height | Read three different ways: as a fraction by the preview and the selection box, as pixels by the export. One artwork, three shapes |
| Duplicate | Video and audio only, so three matching callouts meant building each from scratch |
| The Assets search box and type filter | Bound to fields nothing read again |
| "My Imported Files" | Listed every clip ever imported as artwork, each labelled PNG and each drawing nothing |
| The admin uploader | Accepted Lottie files nothing in the editor renders |

## Slice 3 — keyframes (motion-2, motion-4, motion-5)

Adding a keyframe part-way through an animation dropped per-axis scale and rotation from its seed,
so the layer stopped stretching and turning from that point on. And removing a keyframe removed the
first one within reach rather than the nearest, so with two closer together than the tolerance —
ordinary on a short animation — asking to remove the second removed the first.

## Slice 4 — part of a picture can be hidden

The completeness critic's first item, rated S for this product because of the private-engagement
rule: members cut evidence reels from footage shot inside people's homes, and what identifies the
client or the address does not go out. The editor had a whole-frame blur and nothing that could
obscure part of a picture, so a clip with one identifying detail in it could only be left out.

A clip carries any number of hidden areas, blurred or pixelated. The render splits the frame, crops
each area, obscures the crop and lays it back — core filters only, so it works in the browser
engine and the paired sidecar alike.

Three decisions worth stating:

- **Fractions, not pixels.** An area placed against the preview covers the same thing when the
  export runs at another resolution. Getting that wrong moves the box off what it was hiding, which
  is the one failure this feature cannot have.
- **Even boundaries throughout.** Chroma-subsampled output cannot crop on an odd one; ffmpeg either
  refuses or shifts by a pixel, which leaves the edge of what was hidden visible.
- **A failure here fails the export.** Every other optional step in this pipeline degrades. This one
  cannot: carrying on hands somebody a finished video with the face still in it, and they have no
  reason to check.

## Slice 5 — a clip can sit somewhere other than the whole frame

Video and image clips carried a width and a height and nothing else, so a clip on a second track
could replace the picture underneath and never sit beside it or in a corner of it. Two cameras side
by side, a corner inset, portrait phone footage turned upright, a DVR's black bars cut off: none of
it was possible.

Cropping and turning happen before the scale to the canvas, so what fills the frame is the part
being kept at its own proportions. The picture keeps its shape inside whatever box it is given — a
plain scale would stretch a 16:9 camera into the box's shape, which is not what dragging a corner
inset means. Presets cover the four corners, the two halves and turning a sideways clip upright.

## What the screen found that the tests did not

All three arrived after everything above was green.

- **The redaction pass put the audio in twice.** It mapped the sound itself and then called the
  passthrough helper, which maps it again. Visible in one line of the export's own ffmpeg log.
- **The Working Window ignored a clip's crop and turn**, so the preview drew the picture at full
  frame while the export cut its edges off — the exact disagreement that preview exists to prevent.
  Its segment cache also keyed on everything about a clip except its placement, so even once the
  preview applied a crop it would have handed back the segment encoded before the crop was drawn.
- **My new sliders reintroduced the label bunching** the editor had already fixed once by hand.
  The template labels the first tick and the last, deciding "last" by asking whether this tick plus
  the step it was given runs past the maximum; give it a step larger than the slider's own and
  every tick near the end answers yes. There is a source scan for it now, and it immediately found
  the same mismatch on two sliders that predate this phase.

## Verified on screen

Standalone host at 1440×900, storage cleared.

- Import a clip, select it, and the properties panel carries **Placement** and **Hidden areas**.
- Add a hidden area: it appears over the picture in the preview, with the dashed edge that says it
  is an editing marker.
- Place the clip: the sliders and presets are there and take effect.
- Reload: both survive.
- Export: the render's own log shows the redaction graph
  (`split=2 … crop=384:218:448:250,gblur=sigma=32.7 … overlay=448:250`) and the export finishes
  with a real file.

## Not done in this phase

The plan's phase 8 is long and three parts of it are still open. Each is listed with why.

- **Keyframe edits are still not undoable** (motion-6), and the "+ Keyframe" button is still
  offered outside a layer's own span (motion-7). Both are real; both mean going through
  `MotionKeyframeEditor` and the two canvas overlays field by field, which is a slice in its own
  right rather than a tail on this one.
- **Zoom-n-pan for video and image clips** (motion-9). The placement built here is the model it
  needs, and the missing half is teaching the keyframe editor to accept a video clip as a layer.
  Worth doing next, on top of what this phase added.
- **The overlays still ignore rotation** (callouts-13): a rotated callout draws rotated and its
  selection box and control-point handles do not follow it. The handles are usable because the box
  is drawn unrotated, so it reads as untidy rather than broken — but it is untidy.
- **Pointer capture on overlay drags** (motion-10) and **snap guides from other layers'
  edges** (motion-19), both polish rather than defects.
