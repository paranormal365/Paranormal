# LiDAR phase 2 — comparison scan and textured walkthrough (iOS + API)

Branch `feature/ios-lidar-2`, on top of phase 1. Plan of record: `README-ios-lidar-scanning.md`
(§3 comparison file, §5 `Texturer` and `RoomComparer`, §7 phase 2). No website.

## What it does

A scanned room offers **Compare this room**. The phone fetches the room's scan, loads its world
map, and asks the person to look around until ARKit recognizes the room (relocalization) — only
then is a new scan in the same frame as the old. The room is scanned as usual, and the phone works
out what changed:

- **Objects** — RoomPlan names the furniture, so each kind of thing is matched to the same kind
  nearest it (an optimal assignment, Kuhn–Munkres, not first-come). A pair more than **3 cm** or
  **5°** apart is *moved*; a thing with no partner within 3 m is *missing* or *new*.
- **Surfaces** — both meshes in 5 cm cells; cells one has and the other does not, grouped, at
  least a shoebox (30 × 20 × 15 cm), and where the other visit looked (it found floor or wall in
  the same spot from above) — a box on the floor, a picture off a wall. Furniture already counted
  is left out, so a moved chair is one change, not a chair, a gap and a shape.

Each change gets the picture of it the visit's keyframes saw best, cropped around it. The
comparison is written into the **session** as `scans/compare-NNN.scan` (manifest of kind
`comparison` naming the baseline version, `changes.json`, `changes/NN.jpg`, `patches.glb`), with a
`room_compared` mark carrying `scan` and one `scan_change` mark per change — "The chair moved
42 cm." The results screen lists them, each read aloud on a tap, and **See them in the room**
opens the viewer with the changes pinned (gold moved, red gone, cyan new).

If the room is not recognized within a minute: "The room wasn't recognized, so this scan can't be
lined up with the room's scan. Stand where the first scan began, with the lights as they were, and
try again."

**Textured walkthrough.** A finished scan's mesh is painted from its keyframes (projective
texturing, below) into a 4096-pixel atlas embedded in `mesh.glb`. The viewer gains **Walk**: the
viewing phone's own world tracking (any ARKit iPhone, no LiDAR) moves the camera through the scan
as the person walks their own room, never into a wall or the furniture; standing where a keyframe
was taken shows that picture close up.

## Pieces

| Piece | Where |
|---|---|
| Object and surface comparison, Kuhn–Munkres | `BenKit/Scan/RoomComparer.swift` |
| Comparison file in the session; relocalization watch | `BenKit/Scan/ComparisonPackage.swift` |
| Projective texturing, keyframe projection and crops | `BenKit/Scan/Texturer.swift` |
| Walk binding, collision (`ScanOccupancy`), close-ups | `BenKit/Scan/ScanViewerCamera.swift` |
| `room_compared` mark; `scan` + `scan_change` readings | `FieldSessionEngine.noteComparison`, `MarkerKind.roomCompared` |
| Comparisons ride in the `.ben`, and come back out on import | `DeviceDataExporter`, `SessionImporter` |
| Scanner takes an existing AR session; textures at Done | `RoomScanner` |
| Stand-in room's second visit: chair moved 40 cm, a box | `ScanFixture.Visit.chairMovedAndABox` |
| Compare screen, results (spoken), viewer pins/walk/close-ups | `IsHaunted/Features/Scan/CompareRoomView.swift`, `ScanViewerView.swift` |
| `PlaceScanChanges` table + `PlaceScanChangeKind` | entity, context config |
| Rows written from the document on the session upload (both routes); replaced on resend; unknown scans skipped | `Services/Places/PlaceScanChangeRows.cs` |
| `GET api/field-sessions/{id}/scans/{file}/{**member}` — a comparison or its picture, a range of a range of the `.ben` | `FieldSessionUploadController.Scans.cs` |
| Timeline: `RoomScans` and `ScanChanges` | `SessionTimeline`, `FieldSessionTimelineBuilder` |
| Comparisons never in a public copy or the public archive's files | `PublicSessionBundle`, `ArchiveMediaPublication`, public timeline |

## Decisions made while building (for Ben to overrule)

- **Projective texturing, not Object Capture** — the plan left the choice to phase 0's kitchen
  table. One rectangle of each chosen keyframe goes into the atlas; triangles no keyframe saw stay
  gray. Each triangle checks the keyframe's depth so a wall does not paint what was behind it.
- **A fifth mark, `room_compared`** ("Room compared"), for the comparison reading itself; the plan
  listed four. Without it a comparison that found nothing would leave no mark.
- **Each `scan_change` reading carries `place_scan_id`** as well as the comparison's `scan`
  reading, so a change row needs nothing but its own reading.
- A picture path in a URL: `#` never reaches a server, so the member follows the file as a path
  (`…/scans/compare-001.scan/changes/01.jpg`).
- `PlaceScanChanges → PlaceScans` is NoAction (a scan row is never deleted, only marked removed),
  `→ FieldSessionUploads` cascades; a second cascade path from Places would be refused.

## Tests

- BenKit: `RoomComparerTests` (moved 2 cm → nothing; 4 cm → moved; turned; removed → missing;
  added → new; nothing → none; nearest pairing; a box only the surfaces see; smaller than a shoebox;
  a corner never looked at), `ComparisonPackageTests` (the stand-in room's two visits),
  `RelocalizationWatchTests` (including the not-recognized sentence), `TexturerTests`,
  `ComparisonReadingTests`, `ScanViewerWalkTests` (the camera binding, walls, furniture, close-ups).
- API: `PlaceScanChangeTests` — rows match the document, a resend replaces them, unknown scans are
  skipped, the picture is served from inside the session, the public copy leaves comparisons out,
  the timeline lists the comparison and the change.
- UI: `RoomScanUITests.testComparingTheRoomFindsWhatMoved` — fixture baseline, fixture visit,
  the chair and the box listed, pins in the viewer, the mark in the session. Passed on 10/29/2026
  against an API on 5262 using `IsHauntedDb_lidar`.

`PlaceScanChanges` is in the `PlaceScans` migration with phase 1's tables; see phase 1's README.

## Not done here

- Phase 0 has to confirm on Ben's phone that RoomPlan keeps the relocalized frame when it takes
  over the AR session, and whether it keeps a symmetric table's axes between scans.
- The website's session player showing comparisons is phase 3.
