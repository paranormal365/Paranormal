# LiDAR phase 4 — the person map (iOS + API readers)

Branch `feature/ios-lidar-4`, on top of phase 2. Plan of record: `README-ios-lidar-scanning.md`
(§3 the person map's size, §5 `FigureTracker` and `ShapeWatcher`, §7 phase 4). No website: the web
player drawing the figure over its `<video>` is phase 4's web half and waits with phase 3, behind
`features.lidar-scans`.

## What it does

On a LiDAR phone the live screen's viewfinder gains a **Person map** switch under Watch for motion
(absent, not disabled, on any other phone). Turned on, ARKit takes the camera:

- **The figure.** ARKit's body detection finds a person's seventeen joints in each frame. Each
  joint is lifted to 3D with the depth map at its pixel (the median of a 5 × 5 window, counting
  only depth the sensor is at least fairly sure of), smoothed over five frames, and drawn over the
  camera as a stick figure.
- **Shapes.** When the room the session is in has a current scan, the phone loads it, looks around
  until it recognizes the room in it, and then checks the depth for anything person-sized the scan
  does not have: points more than 10 cm off the scan, grouped, at least 40 cm across and 100 cm
  tall, there for at least a second. Each is outlined (amber, dashed) with its size and distance —
  never jointed. A group with three of the figure's joints in it is the figure, not a shape; one in
  the dark, where body detection finds nobody and the depth still sees, is exactly what this is for.

**Kept with the video.** The session's video carries on, written from ARKit's frames. Each sighting
of a figure (a gap of more than two seconds ends one) leaves a joints file and the picture of the
moment it was clearest, `figures/figure-001.json` and `.jpg`, and a **Figure seen** mark carrying
`figure`. Each shape leaves its picture, `figures/shape-001.jpg`, and an **Unexplained shape** mark
carrying `shape` — "45 × 150 × 5 cm, 2.6 m away, not in the room's scan." Nothing is written while
nobody is in view.

**Playback.** The review draws the figure over the video from its joints file, at the player's own
time (**Show the figure** hides it), and **Export with the figure drawn** makes a copy of the moment
with the figure burned in, to send on.

## Pieces

| Piece | Where |
|---|---|
| Joints by name, 5 × 5 depth lift, five-frame smoothing | `BenKit/Scan/FigureTracker.swift` (`FigureTracker`, `FigureSmoother`, `DepthFrame`, `PinholeCamera`) |
| Picture turn, the joints file, sightings | `BenKit/Scan/FigureSidecar.swift` (`PictureTurn`, `PictureOnScreen`, `FigureSidecar`, `FigureSightings`) |
| Shapes against the scan: a dense 5 cm grid of where the scan is, per-frame groups, followed for a second | `BenKit/Scan/ShapeWatcher.swift` |
| The video from frames handed to it | `BenKit/Scan/VideoClipWriter.swift` |
| Everything per frame, whichever camera: video, figure, shapes, pictures | `BenKit/Scan/PersonMapPipeline.swift` |
| The stand-in for a simulator (`-figureFake`): the stand-in room, a person walking across, a box it does not have, with depth | `BenKit/Scan/PersonMapFixture.swift` |
| A clip's figures and shapes into the session's `figures/`; the readings read back | `BenKit/Scan/PersonMapFiling.swift`, `ActiveFieldSession.notePersonMap` |
| `figure_seen` / `unexplained_shape` readings | `FieldSessionEngine.noteFigure`, `noteShape` |
| Burned-in copy | `SessionVideoConverter.drawingFigure` |
| ARKit (body detection + scene depth, 30 fps 4:3), relocalizing in the room's scan | `IsHaunted/Features/Scan/PersonMapCamera.swift` |
| The switch, the preview, the figure and shape layers, the playback overlay, export | `IsHaunted/Features/Scan/PersonMapViews.swift` |
| The camera changes hands: clips, photos and motion come from the person map while it is on | `FieldCameraSession` (`turnOnPersonMap`, `turnOffPersonMap`) |
| A video's figures are filed with it | `SessionVideoRecorder.finish` |
| A session's joints files are taken (only under `figures/`, only lists) | `FieldSessionFileGuard` |
| Timeline: `Figures` and `Shapes`, each with the ids of its joints file, video and picture | `SessionTimeline`, `FieldSessionTimelineBuilder.PersonMapReadings` |
| Never in a public copy or the public archive's files | `PublicSessionBundle`, `ArchiveMediaPublication`, `PublicFieldSessionController` |

No table and no migration: the timeline reads the readings, and the files are members of the
session's bundle like any recording, served by the existing `files/{fileId}` route.

## Decisions made while building (for Ben to overrule)

- **ARKit has the camera while the person map is on.** It and the session's own capture cannot
  both hold it, so the session's video is written from ARKit's frames (H.264, 8 Mbps — the plan's
  60 MB a minute — in ARKit's 4:3 picture at 30 frames a second), and photos and Watch for Motion
  come from the same frames. Turning it on or off files the video so far and starts the next
  stretch under the other camera.
- **Off until turned on, then remembered on the phone.** ARKit with body detection and depth all
  night is heavier on the battery than the plain camera.
- **The joints file's points are where the joint is in the video as it plays** (0–1 from its top
  left), so every player draws it without knowing how the phone was held; `z` is meters from the
  camera; `c` is how much of the depth around the joint the sensor trusted. Fifteen entries a second
  at most (≈ 6 KB/s, the plan's figure). Spec 1.2 now describes the file.
- **A `picture` beside `figure` and `shape`** (spec 1.2, not yet released, amended): the timeline's
  thumbnail.
- **Readings are written when their video is filed, dated to when the figure was first seen.** The
  server refuses a document whose readings are out of time order, so the exporter now puts the
  log's lines in time order (a stable sort of whole lines; a log already in order is untouched).
  Such readings carry nothing of the room as it is when they are written — no levels, position or
  room.
- **Export with the figure drawn** is the sighting nearest the playhead with three seconds either
  side, not the whole video: a night's video with a figure in it for four seconds is an hour nobody
  wants to send for those four.
- **A video cut short to send keeps its figures** (10/29/2026): each figure's and shape's `from` and
  `to`, and each joints file sent, are moved onto the cut video's clock, the way the audio offsets
  are; frames outside the kept stretch are left out of the copy. The session's own files are untouched.
- **Public copies leave `figures/` out**, like comparisons: they are of the people in the building.
- **ARKit's 2D joints are mapped by name** (`head_joint`, `right_forearm_joint`…), so the file's
  order never depends on ARKit's.
- **The viewfinder and the replay's media pane are accessibility containers now.** The identifier
  on each whole stack was handed to everything in it, so Watch for motion, the person map's switch
  and the replay's figure answered to `camera-preview` / `replay-media` instead of their own names.
  The existing tests that find the two panes still pass.

## Tests

- BenKit (`PersonMapTests`, 26): smoothing stays within the last five frames, a step settles in
  five, a glitch moves it a fifth; somebody new is not averaged with somebody gone; the depth
  median ignores what the sensor did not trust; the picture turn and the video's own transform
  agree for all four turns; **nothing is written with nobody in view**; ten seconds of a figure is
  under 80 KB and at most fifteen entries a second; a two-second gap ends a sighting, a shorter one
  does not; **synthetic depth frames** of the stand-in room — as scanned, nothing; a person-sized
  box for two seconds, one shape of the right size and distance; for half a second, nothing; a
  shoebox or something wide and low, nothing; the walking person with joints, no shape, and the
  same person with none found, a shape; the stand-in's whole script through the pipeline leaves an
  upright video, one figure with its file and picture, and one shape; filing numbers on after
  what is there; the readings carry what the spec says and are dated to the sighting; a late
  figure still lands in time order in the document; the bundle carries a video's figures and not a
  left-out video's; a burned-in copy has the figure where the person was, the right way up.
  `DeviceDataSchemaTests`: the pictures validate; a picture path that climbs out is refused.
- API (`PersonMapSessionTests`): the guard takes a joints file only under `figures/` and only a
  list; a session carrying figures is taken and its timeline lists the figure and the shape with
  the ids of their files, neither picture on the photo strip; the public copy leaves `figures/` out.
- UI (`PersonMapUITests`, no API needed): with `-figureFake` the switch is there, the figure is drawn,
  the shape outlined, both marks are in the review, the replay draws the figure and the copy is
  made; without LiDAR there is no switch.

## Not done here

- The web player drawing the figure from the joints file — phase 4's web half, with phase 3.
- Phase 0 on Ben's phone: that ARKit runs body detection and scene depth together in world tracking
  at 30 frames a second (the code falls back to body detection alone, with no depth and no shapes),
  how warm the phone gets in an hour, and how the figure looks in a dark room.
