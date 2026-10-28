# iOS: LiDAR scanning — room map, comparison scan, walkthrough, person map

Ben, 10/28/2026: "Check if the phone being used has LiDAR. If it does, I would like to offer a new
feature." Two features, planned here before any code: a **room map** (scan a room into a 3D model,
come back later and find what moved, hand the phone to someone and let them walk through it) and a
**person map** (the "SLS cam" ghost hunters know: a stick figure drawn over a human shape in the
camera, kept with the video).

This is the plan of record. Phases ship one at a time, each on its own branch, each with its help.
Nothing here touches the website until phase 4.

## What is true before we start

**The app today.** `Ben.iOS` (SwiftUI, iOS 18.0 target, 1.1.7 on the store) with `BenKit`. A field
session is readings + media in a `.ben` bundle (`media/audio-001.m4a`, `media/video-001.mp4`,
`data.json`), uploaded whole and served as byte ranges. Markers are `MarkerKind` values
(`manual_marker`, `scene_motion`, `device_moved`, …). The server guard allows 500 files and 8 GB
per session; a personal account's cap is 2 GB for everything. The data format rule: a new kind of
hardware reading is a **new key** under `measurements`, never a reshaped one.

**What the hardware and iOS give us, verified against Apple's frameworks, not Gemini's prompt:**

| Need | What iOS has | Notes |
|---|---|---|
| Is there LiDAR? | `ARWorldTrackingConfiguration.supportsSceneReconstruction(.mesh)` | True on iPhone 12 Pro and every Pro since, iPad Pro 2020 on. Plain and Air models: no. |
| 3D mesh of the room | ARKit scene reconstruction (`ARMeshAnchor`, with classification: wall, floor, table, seat, …) | Live, free, ~2 cm triangles. The room's shape. |
| Depth per frame | `frameSemantics = .sceneDepth` / `.smoothedSceneDepth` | 256 × 192 depth + confidence, every frame. |
| Walls, doors, furniture as objects | **RoomPlan** (`RoomCaptureSession` → `CapturedRoom`, iOS 16+) | Parametric: each wall, door, window, table, bed, chair with a size and position. Exports USDZ under 1 MB. Multi-room `CapturedStructure` on iOS 17. |
| "Know where I am" on a return visit | `ARWorldMap` saved from the first scan, loaded as `initialWorldMap` | ARKit relocalizes against the saved map; this is exactly the comparison-scan starting point. |
| Textured model | RealityKit **Object Capture** (`PhotogrammetrySession`, on-device iOS 17+, uses LiDAR) or our own projective texturing of the ARKit mesh from keyframes | On-device. No server GPU. |
| Human figure | `frameSemantics = .bodyDetection` (2D skeleton, 17 joints) **alongside** scene depth in the same world-tracking session; `ARBodyTrackingConfiguration` for the full 3D 91-joint skeleton, which cannot run with scene reconstruction | The 2D joints + the depth map give a 3D figure. This is an RGB machine-learning detector: it fires on people, not on point clouds. |
| Look-around on the viewer's phone | `CoreMotion` device orientation → camera yaw/pitch/roll | Same as the readings' `motion` today. |
| Walk-through by walking | ARKit world tracking on the viewing phone (visual-inertial odometry) | Any iPhone with ARKit, LiDAR not needed to view. |

**What is not realistic, and why the plan does not promise it:**

- **3D Gaussian Splatting / NeRF.** Training a splat scene takes a GPU for minutes to hours per room.
  Phones cannot train it, and production is a shared Windows host with no GPU. Viewing a splat in the
  browser is easy; making one is not. The plan builds a **textured mesh** instead, which the phone can
  make itself. If a splat pipeline ever becomes cheap, the capture package in phase 1 (keyframes +
  poses + depth) is what it would need, so nothing is lost.
- **Fitting a skeleton to unclassified point clusters ("anomalous detection mode").** This is what
  SLS cameras do, and it is why they draw stick figures on coat racks. We will show an **unexplained
  shape**: a cluster of depth points that is not in the empty-room mesh, with its size, outlined.
  We will not draw joints on it. The same honesty rule as the EVP detector: say what was measured.
- **Millimeter accuracy.** iPhone LiDAR is good to about 1 cm up close and a few centimeters across
  a room. "Something moved" means more than ~3 cm; a leaf is 6 inches long, so the walkthrough shows
  the flowers from every angle, but a petal that turned is not a finding.

## Sizes — what each way of keeping a scan costs

Measured and published figures for one ordinary room (about 4 × 5 m), a five-minute scan. Phase 0
replaces these with numbers from Ben's own phone.

| What is kept | Size per room | Fits the 2 GB cap? |
|---|---|---|
| **Everything raw** — every depth frame (196 KB) + confidence (49 KB) at 60 fps, plus 1080p video | ~4.4 GB depth + ~300 MB video | No. Never kept. |
| **Keyframes** — a JPEG + depth + pose every ~0.5 m or 15° (≈150 of them) | ~70 MB (JPEG ≈ 400 KB, depth zlib ≈ 80 KB each) | A house of 8 rooms ≈ 560 MB. Uploaded with the scan (Ben, 10/28/2026); the retention screen shows the size first. |
| **ARWorldMap** — what relocalization needs | 10–30 MB | Yes. Needed for comparison scans; one per room. |
| **Textured mesh** — glTF with Draco + a 4K JPEG atlas | 5–15 MB | Yes. This is the walkthrough. |
| **Untextured mesh** — Draco | 1–4 MB | Yes. Enough for "what moved". |
| **RoomPlan model** — walls, doors, furniture as objects, USDZ | 0.2–1 MB | Yes. Enough for "the bed moved 40 cm"; the smallest useful record. |
| **Minimal** = RoomPlan + untextured mesh + world map | ~15–35 MB | Yes. |
| **Standard** = minimal + textured mesh | ~20–50 MB | Yes. An 8-room house ≈ 300 MB. |
| **Comparison scan, changes only** — object deltas (JSON), changed mesh patches, a JPEG crop per change | 0.5–3 MB | Yes. Plus a second world map only if the person asks to keep it. |
| **Person map** — video the phone already records (1080p30 ≈ 60 MB/min) + a joints sidecar | Sidecar ≈ 6 KB/s **only while a figure is in frame**; an hour of continuous figure ≈ 22 MB, a typical night under 2 MB | Yes. The video is the cost, and it is the same video as today. |

Three retention choices the person sees, in product words: **Model and pictures** (the default:
model, world map and keyframes, all sent with the session — Ben, 10/28/2026: "Upload the keyframe
pictures with the scan"), **Model only** (for a phone or account short on room), **Changes only**
(for a comparison scan). The size is shown before the choice is made, and a scan that would put
the account over its 2 GB is refused with the number, like any other session file.

The 500-file guard: keyframes are not 150 bundle members. Each scan is **one** stored zip inside the
bundle, `scans/room-001.scan`, holding its own `manifest.json`, `room.usdz`, `mesh.glb`,
`worldmap.arworldmap`, `keyframes/…`. The server indexes it like the bundle itself (same stored-zip,
byte-range reading as `BenBundle`), so a viewer can fetch `mesh.glb` alone.

## How it fits the app

- **Where it lives:** inside a field session, as a room's scan. The room picker already names
  rooms ("Bedroom 1"); a scan attaches to that room name and the session. Scans without a session
  (just mapping a house) are a session with no readings, which already exists as a shape.
- **The reading:** one reading per scan, marker `room_scan` (new `MarkerKind`), with a new
  `measurements.scan` key: `{ "ref": "scans/room-001.scan", "room": "Bedroom 1", "kind":
  "baseline" | "comparison", "baseline_ref": …, "retention": "model" | "model_pictures" |
  "changes" }`. `format_version` goes to 1.2. Every reader keeps ignoring unknown keys.
- **The comparison's findings** are readings too: one per change, marker `scan_change`, with
  `measurements.scan_change`: `{ "what": "moved" | "missing" | "new", "object": "chair",
  "distance_cm": 42, "where": [x, y, z], "picture": "scans/room-002.scan#changes/01.jpg" }`.
  They show in the timeline like any marker, and on the website like any reading.
- **Person map** writes readings with marker `figure_seen` and `measurements.figure`:
  `{ "joints_ref": "scans/figures-001.json", "from": t0, "to": t1, "confidence": … }`; the sidecar
  carries the per-frame joints. The video is the ordinary `media/video-NNN.mp4`. Playback draws the
  figure from the sidecar, so the raw video stays raw; a burned-in copy is an export, like the
  trimmed clip today.
- **Privacy and sharing** follow the session: a scan of a private home is in a private session.
  Publishing a session to a public place (the archive) would publish the room — so **scans are
  never published to a place**; the publish step strips them. "Hand my phone to a coworker" is the
  on-phone viewer, no account needed. A share link (phase 4) can show a scan to one person.
- **Storage cap:** scans count like every other session file. With pictures, a room is ~100 MB and
  an 8-room house ~850 MB, which is most of a free account's 2 GB — the retention screen shows the
  size before it is kept, and a group on a paid plan has no cap. Phase 3's close-up detail and the
  phase 4 web walkthrough both read the uploaded keyframes, which is what uploading them buys.
- **Android / web viewers:** phase 4 puts a three.js viewer in the website (look-around with the
  browser's device orientation, a joystick for walking). It reads `mesh.glb` by range from the
  bundle. No LiDAR needed to look.

## Phases

### Phase 0 — Feasibility spike (phone only, nothing shipped)

One branch, one week, Ben's iPhone Pro. Answers the questions the rest depends on.

1. `LidarSupport.isAvailable` (the one check above) and the devices it is true on.
2. A throwaway screen: RoomPlan capture → `CapturedRoom` → USDZ; ARKit mesh → Draco glTF;
   `ARWorldMap` saved; keyframes written at 0.5 m / 15°. **Measure every size** on Ben's rooms and
   replace the table above.
3. Relocalize against the saved map a few hours later, in different light. Measure how long it
   takes and how often it fails — this decides whether phase 2 is worth building.
4. Body detection + scene depth in one session, in the dark with a flashlight. Does it fire on a
   person at 5 m? Does it fire on nothing?
5. Textured mesh two ways — Object Capture on-device vs projective texturing of the ARKit mesh —
   and which looks like Ben's kitchen table with flowers.

Done = `ProjectNotes/FeatureHistory/README-ios-lidar-phase-0.md` with the numbers and a go/no-go per
feature.

### Phase 1 — Room scan, kept with the session

- `BenKit/Scan/`: `RoomScanner` (RoomPlan + mesh + world map + keyframes), `ScanPackage` (writes
  the `.scan` zip), `ScanRetention`.
- Paranormal tab: a **Scan this room** button on the room bar, only on LiDAR phones. Coaching
  overlay (RoomPlan's own), a progress ring, Done. Then the retention choice with the size.
- The reading, the `MarkerKind`, `format_version` 1.2, the spec update, `SessionFileStore` kinds.
- On-phone viewer: RealityKit scene of `mesh.glb`, **Look around** (CoreMotion) and a
  semi-transparent joystick for moving. Opened from the session's room list.
- Export: the `.scan` is inside the `.ben`, so "Export a bundle" already carries it.
- Tests: `RoomScanTests` (package round trip, manifest, sizes under the chosen retention),
  UI test with fake scanner (`-scanFakeRoom`), `ScanRetentionTests`.

### Phase 2 — Comparison scan

- **Compare this room** on a room that has a baseline. Loads the baseline's world map; the
  coaching says "look around until the room is recognized". Then scan.
- Differences, in order of trust: (1) **RoomPlan objects** — same category, moved more than 3 cm
  or rotated, missing, new; (2) **mesh voxels** (5 cm) present in one and not the other, clustered,
  larger than a shoebox; (3) a JPEG crop of each from the nearest keyframe.
- Shown on the baseline model with pins; tap a pin: "Chair moved 42 cm", "Something new here,
  about 30 × 20 cm", "Missing: a table-sized object". Each is a `scan_change` reading.
- Kept as **Changes only** by default. "Keep this scan as the new baseline" replaces the world map.
- Tests: synthetic room pairs (object moved / removed / added / nothing), the 3 cm rule, the
  lighting-failure path ("couldn't recognize the room — stand where you started").

### Phase 3 — Textured walkthrough

- Whichever texturing phase 0 picked. Output `mesh.glb` with a JPEG atlas, replacing the
  untextured one.
- Viewer gains **Walk** mode: ARKit world tracking on the viewing phone moves the camera through the
  model as the person walks the office; collision with the mesh so nobody walks through the table.
  Joystick mode stays for sitting down.
- Fine detail: keyframe-backed close-ups — when the camera is within 40 cm of a surface, the nearest
  keyframe is shown projected, which is where the 6-inch leaf comes from.
- Tests: viewer unit tests on camera binding (orientation → matrix), collision, and a UI test on a
  fixture scan.

### Phase 4 — Server and website

- `FieldSessionFileGuard`: `.scan` allowed; the server indexes a `.scan`'s members by range.
- Timeline builder reads `scan` / `scan_change` / `figure` readings; the web session player shows
  scan pins and a **View the room** button.
- Web viewer: three.js in `Ben.Web.Website/wwwroot/js/scan-viewer/` (vendored, SHA in a
  VENDORED.md like wavesurfer): orbit by default, device-orientation look-around on phones,
  joystick, comparison pins.
- Publishing strips scans; share links may include them (a checkbox, off by default).
- Storage cap accounting unchanged (it is a session file); the sizes page shows scans by room.
- Playwright: a fixture `.ben` with a tiny `.scan`; the viewer loads; a published session has no scan.

### Phase 5 — Person map

- **Figure overlay** toggle in video recording on LiDAR phones (and, without depth, on any A12+
  phone, labelled "2D"). World tracking + `.bodyDetection` + `.sceneDepth`: 17 joints lifted to 3D
  by the depth map, drawn as a glowing stick figure that sticks to the person, smoothed over 5
  frames so it does not jitter.
- The joints sidecar and `figure_seen` readings (one per continuous sighting). Playback draws from
  the sidecar; **Export with the figure drawn** burns it in.
- **Unexplained shape**: when the room has a baseline mesh (phase 1), depth clusters not in the
  mesh, bigger than 40 × 100 cm, get an outlined box with its size and distance, and a
  `scene_motion`-style reading. Never a skeleton. The help says why.
- Tests: synthetic depth frames with and without a person-sized cluster; the sidecar's size rule
  (nothing written with nobody in frame); UI test with `-figureFake`.

### Phase 6 — Help, store, changelog

- `using-the-paranormal-platform.md` gains "Scanning a room", "Finding what moved", "Walking
  through a scan", "The figure overlay" with captures from the simulator (scans from a fixture —
  the simulator has no LiDAR).
- App Store: a new screenshot pair, "What's new" lines, and the feature on the marketing PDF.
  LiDAR is a Pro-phone feature; the store copy says so, so nobody buys the app for it on an iPhone 16.
- Changelog (ios.md) and the product documentation PDF.

## Open questions for Ben

1. ~~Keyframes upload or stay on the phone?~~ Answered 10/28/2026: they upload with the scan.
2. Is the figure overlay allowed on non-LiDAR phones as a 2D-only mode, or is this a Pro feature
   end to end?
3. The walkthrough viewer on the website: Phase 4 is the biggest website piece. Fine to leave it
   last, after the audio/EVP plan is through?
