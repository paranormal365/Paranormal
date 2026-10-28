# iOS: LiDAR scanning — room scans kept per place

Ben, 10/28/2026: "Check if the phone being used has LiDAR. If it does, I would like to offer a new
feature." Then: scans are **per location, not per person**, "so there are not multiple people
uploading the same rooms"; after "like a year" a scan may be updated. Keyframe pictures upload with
the scan. The figure overlay is a LiDAR (Pro) feature end to end.

Two features. A **room scan**: scan a room into a 3D model that belongs to the place, come back
later and find what moved, hand the phone to someone and let them walk through it. A **person map**
(the "SLS cam" of ghost hunting): a stick figure drawn over a human shape in the camera, kept with
the video.

This is the plan of record, written to the atomic level: every table, column, route, file, screen
and test. Phases ship one at a time on their own branches. The website is touched from phase 3 on.

---

## 1. What is true before we start

### The app and the server today

- `Ben.iOS` (SwiftUI, iOS 18.0 target, 1.1.7 on the store) + `BenKit`. A field session is readings
  and media in one `.ben` bundle (`data.json`, `media/audio-001.m4a`, `media/video-001.mp4`),
  uploaded whole, served as byte ranges (`BenBundle`, `BenBundleStore`). The upload guard allows 500
  files and 8 GB per session. A personal account has a 2 GB cap on everything under `users/{id}/`
  (`AccountStorageGuard`); members of paid groups have none.
- **The room is free text.** `RoomBar.swift` / `RoomSheet` (`room-name-field`) suggest a hard-coded
  list and the rooms already visited; `FieldSessionEngine.setRoom` stamps `measurements.room`.
  Nothing links it to a server room.
- **The app does not know the place while recording.** `FieldSessionUpload.PlaceId` is set only by
  Publish (`FieldSessionPublishController.cs:399`), from `GET api/places/archive-candidates`
  (nearest public places within a radius, by GPS) or a newly named place. An investigation carries
  a `placeId` (`InvestigationRecords.swift:55`), but it is never copied to the session.
- **Rooms on the server belong to a group:** `PlaceRoom(OrganizationId, PlaceId, Name, Floor,
  Capacity, IsBookable, …)`, unique on (org, place, name), edited by org admins at
  `api/organizations/{orgId}/places/{placeId}/rooms`. Two groups at one place keep two lists.
- **The custodian of a place already exists:** `OrganizationVenueProfile.VerifiedUtc != null` is the
  confirmed venue (one per place; `VenueGrants.VerifiedVenueAtAsync`). It is earned by a
  `VenuePlaceClaim` (code to the place's contact, an objection window, `VenueClaimJob` approves).
  Venue photos are stored under the org (`OrgFilePath(orgId, "venue/…")`). Place files added by
  anyone (`PlaceEvidence`) are stored under the person who added them and are reviewed by the
  verified venue's admins or moderators (`PlaceFileReviewController.Reviewable`).
- `PlaceKind` is `PrivateResidence` or `PublicLocation`. Place files and the field archive are
  public-location only (`ArchiveMediaPublication`).
- A year as a lifetime already exists: `EventCredits.Life = 365 days`, with `ExpiresUtc` columns and
  `EventCreditExpiryJob` sending one warning. Nothing yet says "may be refreshed after N".
- Data-format rule (`DeviceDataFormat-v1.md`): a new kind of reading is a **new key** under
  `measurements`; additive = minor version; every reader ignores unknown keys.

### What the hardware and iOS give us (Apple's frameworks, not the Gemini prompt)

| Need | What iOS has | Notes |
|---|---|---|
| Is there LiDAR? | `ARWorldTrackingConfiguration.supportsSceneReconstruction(.mesh)` | iPhone 12 Pro and every Pro since; iPad Pro 2020 on. Plain, Air, e: no. |
| 3D mesh of the room | ARKit scene reconstruction (`ARMeshAnchor`, classified: wall, floor, table, seat, …) | Live, ~2 cm triangles. |
| Depth per frame | `frameSemantics = .sceneDepth` / `.smoothedSceneDepth` | 256 × 192 depth + confidence. |
| Walls, doors, furniture as objects | **RoomPlan** (`RoomCaptureSession` → `CapturedRoom`, iOS 16+; `CapturedStructure` joins rooms, iOS 17+) | Parametric, under 1 MB in USDZ. "The chair moved 42 cm" comes from here. |
| Know where I am on a return visit | `ARWorldMap` saved from the first scan, loaded as `initialWorldMap` | ARKit relocalizes. This is the comparison scan's starting point. |
| Textured model | RealityKit **Object Capture** (`PhotogrammetrySession`, on-device, iOS 17+) or our own projective texturing of the ARKit mesh from keyframes | On the phone. No server GPU exists. |
| Human figure | `frameSemantics = .bodyDetection` (2D, 17 joints) **together with** scene depth in one world-tracking session | RGB machine learning: it fires on people. `ARBodyTrackingConfiguration` (3D, 91 joints) cannot run with scene reconstruction, so it is not used. |
| Look-around on a viewer's phone | `CoreMotion` orientation → camera | Same sensor the readings' `motion` uses. |
| Walk through by walking | ARKit world tracking on the viewing phone | Any ARKit iPhone; LiDAR not needed to view. |

### What the plan does not promise, and why

- **Gaussian splats / NeRF.** Training takes a GPU for minutes to hours per room. Phones cannot;
  production is a shared Windows host with no GPU. The phone builds a **textured mesh** itself. The
  keyframes + poses + depth kept in a scan are exactly what a splat pipeline would need, so nothing
  is lost if that ever becomes cheap.
- **Skeletons fitted to point clusters.** That is how SLS cameras put stick figures on coat racks.
  We show an **unexplained shape** — a depth cluster that is not in the empty-room mesh — with its
  size, outlined, never jointed. Same honesty rule as the EVP detector: say what was measured.
- **Millimetres.** iPhone LiDAR is ~1 cm up close, a few cm across a room. "Moved" means more than
  3 cm. The walkthrough shows a 6-inch leaf from every angle; a petal that turned is not a finding.

---

## 2. The model: scans belong to the place

### Rules

1. **One current scan per room per place.** Key `(PlaceId, RoomKey)`, where `RoomKey` is the room
   name normalized (trimmed, lower-cased, inner whitespace collapsed, "bedroom 1" = "Bedroom 1").
   The display name is kept as typed by whoever scanned first. Not keyed on a group's `PlaceRoom`
   (two groups, two lists); a scan may *link* to one (`PlaceRoomId?`) when the names match.
2. **Who may scan at all:** anyone signed in at a `PublicLocation`; at a `PrivateResidence` only an
   active member of a group with an investigation, case, event or verified venue at that place
   (`VenueClaims.InterestedGroupsAsync` is the existing list of such groups).
3. **The custodian** of a place's scans is the verified venue's org when there is one. When there
   is none: for a public location, the person who made the current scan; for a private residence,
   the group whose member made it. Custodian = may replace at any time, may accept offers, may
   publish a scan to the place page, may remove it.
   **Handing custody over** (Ben, 10/28/2026): a SuperAdmin may give a place's scan custody to a
   group (`PlaceScanCustody`, §3); a verified venue claim coming through later takes over
   automatically and closes the grant. Audited like every admin act.
4. **Everyone else compares.** A comparison scan is *changes only*, kept in the person's own
   session, pointing at the place's scan version it was made against.
5. **Replacing a current scan**, three doors, each a server decision with a reason:
   - the custodian, any time;
   - anyone who may scan, once the current scan is **older than 365 days**
     (`PlaceScans.RefreshAfter`, mirroring `EventCredits.Life`);
   - otherwise an **offer**: the scan is uploaded as a version in state `Offered`; the custodian
     accepts (it becomes current) or declines; it is never shown to anyone else meanwhile.
   A replaced scan stays as a previous version; comparisons made against it still resolve.
6. **Nobody scans the same room twice by accident:**
   - the phone's room picker at a place is fed by the server: existing scan rooms (with their
     dates) + the verified venue's `PlaceRoom` names; a scanned room offers **Compare**, not
     **Scan**;
   - tapping **Scan** takes a **20-minute hold** on `(place, room)` (`PlaceScanHold`); a second
     person sees "Dana is scanning Bedroom 1 right now — compare it instead, or wait";
   - the upload re-checks the unique key; if a current scan landed meanwhile the upload is not
     thrown away: the app offers *Keep as a comparison / Offer it to replace / Discard*;
   - offline, the app says before the scan starts that it cannot tell whether the room is
     scanned and that the scan may become a comparison when sent.
7. **Privacy.** A private residence's scan is visible only to the groups that may scan there. A
   public location's scan is visible to anyone signed in; it appears on the public place page
   only when the custodian ticks **Show the walkthrough on the place page** (off by default).
   Publishing a session to the archive never carries a scan (the baseline is not in the session;
   a comparison's changes are readings and travel, its JPEG crops do not).
8. **Storage.** Scans live under the place (`places/{placeId}/scans/{roomKey}/v{n}/…`, a new
   `IFileStorageService.PlaceFilePath`). They count against the verified venue's org when there
   is one, else against the uploader's account (`AccountStorageGuard` learns the `places/` prefix
   with an `OwnerAppUserId` on the scan). The retention screen shows the size before upload; an
   upload that would cross a cap is refused with the number.

### Sizes (per ordinary room, ~4 × 5 m, a five-minute scan; phase 0 re-measures on Ben's phone)

| What | Size | Kept where |
|---|---|---|
| Everything raw — every depth frame + confidence at 60 fps + video | ~4.7 GB | Never. |
| **Keyframes** — JPEG + depth + pose every ~0.5 m / 15° (≈150) | ~70 MB | In the scan (Ben, 10/28). |
| **World map** (relocalization) | 10–30 MB | In the scan. |
| **Textured mesh** — glTF, Draco, 4K JPEG atlas | 5–15 MB | In the scan. |
| Untextured mesh — Draco | 1–4 MB | Until phase 2 textures it. |
| **RoomPlan model** — walls, doors, furniture, USDZ | 0.2–1 MB | In the scan. |
| **A current scan, "Model and pictures"** | **~90–120 MB** | Place storage. An 8-room house ≈ 850 MB. |
| A current scan, "Model only" | ~20–50 MB | For a short account. |
| **Comparison, changes only** — object deltas JSON, changed mesh patches, one JPEG crop per change | 0.5–3 MB | The person's session. |
| **Person map** — the video already recorded (1080p30 ≈ 60 MB/min) + a joints sidecar | Sidecar ≈ 6 KB/s only while a figure is in frame; an hour of continuous figure ≈ 22 MB, a typical night < 2 MB | The person's session. |

Retention choices in product words: **Model and pictures** (default), **Model only**, and for a
comparison always **Changes only**.

---

## 3. Data

### New tables (one migration per phase that adds them; Ben applies on MonsterASP as usual)

**`PlaceScans`** (phase 1) — one row per scan *version*.

| Column | Type | Meaning |
|---|---|---|
| `Id` | guid | |
| `PlaceId` | guid, FK Places, cascade | |
| `RoomKey` | nvarchar(120) | normalized name |
| `RoomName` | nvarchar(120) | as typed |
| `PlaceRoomId` | guid?, no FK | a group's room with the same name, if any |
| `Version` | int | 1, 2, … per (place, room) |
| `State` | tinyint | `Current = 1`, `Previous = 2`, `Offered = 3`, `Declined = 4`, `Removed = 5` |
| `Retention` | tinyint | `ModelAndPictures = 1`, `ModelOnly = 2` |
| `ScannedUtc` | datetime2 | when the phone finished scanning |
| `ScannedByAppUserId` | guid, FK | |
| `ScannedForOrganizationId` | guid? | the group the scanner was acting for, if any |
| `OwnerAppUserId` | guid? | whose cap it counts against (null when the venue org pays) |
| `OwnerOrganizationId` | guid? | the verified venue's org at upload time |
| `StoragePath` | nvarchar(400) | `places/{placeId}/scans/{roomKey}/v{n}/room.scan` |
| `FileSize` | bigint | |
| `Sha256` | char(64) | |
| `ShownOnPlacePage` | bit | custodian's switch, default 0 |
| `DeviceModel` | nvarchar(60) | e.g. iPhone16,1 |
| `ScannerVersion` | int | the capture pipeline version, pinned like `EvpDetector.Version` |
| `FloorAreaM2`, `TriangleCount`, `KeyframeCount` | float, int, int | shown on the place page |
| `DecidedUtc`, `DecidedByAppUserId`, `DecisionNote` | | for offers |
| audit fields | | |

Unique filtered index `(PlaceId, RoomKey) WHERE State = 1`. Index `(PlaceId, RoomKey, Version)`.

**`PlaceScanCustody`** (phase 1): `Id`, `PlaceId` (unique), `OrganizationId`, `GrantedUtc`,
`GrantedByAppUserId`, `Note`, `ClosedUtc`, `ClosedReason` (`venue_verified` | `admin`). Read by
`PlaceScans.CustodianAsync` after the verified-venue check and before the first-scanner fallback.

**`PlaceScanHolds`** (phase 1): `Id`, `PlaceId`, `RoomKey`, `RoomName`, `AppUserId`, `TakenUtc`,
`ExpiresUtc` (taken + 20 min). Unique filtered on `(PlaceId, RoomKey) WHERE ExpiresUtc > now` is
not expressible, so the controller checks and the index is plain `(PlaceId, RoomKey, ExpiresUtc)`.
Expired rows are ignored and swept by the existing five-minute scheduler
(`PlaceScanHoldSweepJob`).

**`PlaceScanChanges`** (phase 2) — one row per change a comparison found, so the place page can say
"3 things moved since March".

| Column | Meaning |
|---|---|
| `Id`, `PlaceScanId` (the baseline version compared against), `FieldSessionUploadId` | |
| `Kind` | `Moved = 1`, `Missing = 2`, `New = 3` |
| `ObjectCategory` | RoomPlan category (`chair`, `table`, `bed`, …) or `unclassified` |
| `DistanceCm`, `RotationDeg` | for Moved |
| `SizeCm` (x, y, z) | for New / Missing |
| `WhereX/Y/Z` | in the baseline's coordinate frame |
| `PictureEntryPath` | `scans/compare-001.scan#changes/01.jpg` inside the session bundle |
| `ComparedUtc` | |

### Enum additions

- `PlaceScanState`, `PlaceScanRetention`, `PlaceScanChangeKind` in `Ben.Data.Common/Enums/`.
- `MarkerKind` (iOS) + the server's marker labels: `room_scan`, `scan_change`, `figure_seen`,
  `unexplained_shape`.

### The `.scan` file (one stored zip, like the `.ben`)

```
room.scan
├── manifest.json        { "format": "ishaunted-scan/1", "room": "Bedroom 1", "placeId", "scannedAt",
│                          "device", "scannerVersion", "retention", "frame": { "origin", "up" },
│                          "entries": { "roomplan": "room.usdz", "mesh": "mesh.glb",
│                                       "worldmap": "worldmap.arworldmap", "keyframes": 150 } }
├── room.usdz            RoomPlan CapturedRoom export (parametric)
├── room.json            CapturedRoom as JSON (walls, openings, objects with transforms) — what the
│                        comparison diffs; USDZ is for viewers
├── mesh.glb             ARKit mesh, Draco; textured from phase 2 on (JPEG atlas embedded)
├── worldmap.arworldmap  ARWorldMap, NSKeyedArchiver, compressed
├── keyframes/
│   ├── index.json       [{ "file": "0001.jpg", "depth": "0001.depth", "pose": [16 floats],
│   │                       "intrinsics": [9], "at": seconds }]
│   ├── 0001.jpg         1920 × 1440
│   └── 0001.depth       256 × 192 float16, zlib
└── preview.jpg          one picture for lists (1280 wide)
```

A comparison scan is `compare-NNN.scan` inside the session bundle:
`manifest.json` (`"kind": "comparison"`, `"baseline": { "placeScanId", "version" }`),
`changes.json` (the rows above), `changes/NN.jpg`, `patches.glb` (changed mesh regions only), and
optionally `worldmap.arworldmap` when the person chose to offer it as a replacement.

The server reads a `.scan` with `BenBundle.ReadAsync` unchanged (stored entries, central directory)
and serves members by range through `BenBundleStore` — no new container code.

### The session document (`DeviceDataFormat` 1.2)

- `measurements.scan` on the reading that marks the moment a comparison was taken:
  `{ "ref": "scans/compare-001.scan", "room": "Bedroom 1", "place_scan_id": "…",
  "place_scan_version": 3, "changes": 3 }`.
- `measurements.scan_change`, one reading per change: `{ "what": "moved", "object": "chair",
  "distance_cm": 42, "where": [x, y, z], "picture": "scans/compare-001.scan#changes/01.jpg" }`.
- `measurements.figure`, one reading per continuous sighting: `{ "joints_ref":
  "figures/figure-001.json", "from": s, "to": s, "video": "media/video-001.mp4", "peak_confidence" }`.
- `measurements.shape`, one per unexplained shape: `{ "size_cm": [w, h, d], "distance_m",
  "where", "from", "to", "video" }`.
- A baseline scan made during a session also gets a `room_scan` reading with
  `{ "place_scan_id", "version", "room" }` so the timeline shows "Scanned Bedroom 1 (12 min)".
- Spec and schema updated; the 14 malformed-document tests gain four for these keys;
  `FieldSessionDocumentGuard` keeps ignoring unknown keys (test).

---

## 4. API

All under `[Authorize]`. "Not allowed" answers 404 for reads and 403 with a reason for writes, like
the rest of the field-session API. Every refusal carries a sentence the app shows as-is.

| Route | Phase | Who | Does |
|---|---|---|---|
| `GET api/places/{placeId}/scans` | 1 | may-view | Rooms at the place with their current scan (room, version, scannedUtc, by, size, `canScan`, `canCompare`, `canReplace`, `replaceReason`, `holdBy`), plus the verified venue's `PlaceRoom` names without scans. The phone's room picker. |
| `POST api/places/{placeId}/scans/holds` `{ room }` | 1 | may-scan | 20-minute hold; 409 `{ heldBy, until }` if another person holds it; 409 `{ reason: "already scanned" }` if a current scan exists and the caller may not replace. Refreshing your own hold extends it. |
| `DELETE api/places/{placeId}/scans/holds/{holdId}` | 1 | holder | Release early. |
| `POST api/places/{placeId}/scans` (multipart: `file`, `room`, `retention`, `holdId?`, `intent` = `baseline` \| `replace` \| `offer`) | 1 | may-scan | The upload. Guard: `.scan` only, 2 GB max, `BenBundle.ReadAsync` must index it, manifest validated. Decides: no current → Current v1; current and caller may replace → old → Previous, new → Current; current and may not → `Offered` (or 409 with the three choices when `intent` was `baseline`). Cap check by owner rule. |
| `GET api/places/{placeId}/scans/{scanId}` | 1 | may-view | Metadata + member list. |
| `GET api/places/{placeId}/scans/{scanId}/entries/{*path}` | 1 | may-view | A member by range (`mesh.glb`, `preview.jpg`, `keyframes/0001.jpg`…). |
| `GET api/places/{placeId}/scans/{scanId}/file` | 1 | may-view | The whole `.scan` by range (the phone caches it to view and to compare). |
| `GET api/places/{placeId}/scans/rooms/{roomKey}/versions` | 1 | may-view | History. |
| `POST .../scans/{scanId}/accept` / `decline` `{ note }` | 1 | custodian | Offers. Accept → Current, the old → Previous; tells the scanner by the usual notification. |
| `PUT .../scans/{scanId}/shown-on-place-page` `{ shown }` | 3 | custodian | Public walkthrough switch. |
| `DELETE .../scans/{scanId}` | 1 | custodian | → Removed; file deleted; comparisons keep their rows. |
| `PUT api/admin/places/{placeId}/scan-custody` `{ organizationId, note }` / `DELETE` | 1 | SuperAdmin | Hand custody to a group / take it back. Refused with the sentence when a verified venue exists ("The Cole Family is this place's confirmed venue, so it holds custody"). |
| `GET api/field-sessions/{id}/scans/{entry}` | 2 | `MayReadAsync` | A comparison member from the session bundle (through the existing `files` route; this is an alias that resolves `#` paths). |
| `GET api/public/places/{placeId}/scan` | 3 | anyone | The current scans with `ShownOnPlacePage`, `mesh.glb` + `preview.jpg` only, public locations only. |

Services: `Services/Places/PlaceScans.cs` (`RefreshAfter`, `MayScanAsync`, `MayViewAsync`,
`CustodianAsync`, `WhyCannotReplaceAsync`, `NormalizeRoomKey`), `PlaceScanStorage.cs` (paths,
owner rule), `PlaceScanManifest.cs` (validation), `Scheduling/PlaceScanHoldSweepJob.cs`,
`Scheduling/PlaceScanAgeJob.cs` (phase 1: a note to the custodian when a scan turns a year old:
"Bedroom 1's scan is a year old; anyone who may scan here can now replace it").

---

## 5. iOS

### Shared

- `BenKit/Scan/LidarSupport.swift` — `static let isAvailable`, `static let reason` ("This iPhone
  has no LiDAR scanner. iPhone 12 Pro and later Pro models do.").
- `BenKit/Scan/ScanPackage.swift` — writes/reads the `.scan` zip with the existing `ZipWriter`
  (stored entries); `ScanManifest` Codable.
- `BenKit/Scan/PlaceScanClient.swift` — the routes above; `PlaceScanRoom`, `PlaceScanRecord`,
  `ScanHold` Codable records, captured from the real API as fixtures (never invented).
- `BenKit/Scan/RoomKey.swift` — the same normalization as the server, with a parity test that
  reads the C# source's test vectors (like the other parity fixtures).
- `FieldSessionEngine` gains `placeId: UUID?` (set when the session starts at a chosen place, or
  from the investigation's `placeId`), persisted with the session; a `room_scan` reading writer.
- `RoomBar` / `RoomSheet`: when `placeId` is known, the suggestions come from
  `GET /scans` (room name + "scanned 03/12/2026" / "not scanned"); typing a new name is still
  allowed; a typed name that normalizes to an existing key snaps to it.

### Screens (feature folder `IsHaunted/Features/Scan/`)

- `ChoosePlaceSheet` — reused `ArchiveActions.candidates` by GPS + the investigation's place +
  "name a place"; required before the first scan in a session. Identifier `scan-choose-place`.
- `ScanRoomButton` on the room bar — only when `LidarSupport.isAvailable` and a place is chosen:
  **Scan this room** / **Compare this room** / "Dana is scanning this room" (disabled, with the
  name) / "Offline — this scan may become a comparison". Identifiers `scan-room`, `compare-room`.
- `RoomScanView` — RoomPlan coaching overlay, a progress ring (coverage), **Done**, **Cancel**.
  `scan-done`, `scan-cancel`.
- `ScanRetentionSheet` — Model and pictures (size) / Model only (size); the cap line from the
  server's `WhyCannotStore` when it applies. `scan-retention-pictures`, `scan-retention-model`.
- `ScanConflictSheet` — the three choices after a 409: Keep as a comparison / Offer it to replace
  / Discard. `scan-conflict-compare`, `scan-conflict-offer`, `scan-conflict-discard`.
- `ScanUploadView` — progress, resumable (the upload is one file; on failure it retries from the
  start, the phone keeps the file until the server has it, like the session upload).
- `ScanViewerView` — RealityKit `mesh.glb`, **Look around** (CoreMotion), joystick overlay
  (`scan-joystick`), **Walk** (phase 2: ARKit world tracking on the viewing phone, collision with
  the mesh), a room switcher when the place has several scans. `scan-viewer`.
- `CompareRoomView` (phase 2) — loads the baseline's world map, coaching "look around until the
  room is recognized" (`ARSession` relocalization state), scan, then `CompareResultsView`: pins on
  the baseline with labels; tapping reads the change aloud in words (`scan-change-pin`).
- `FigureOverlayToggle` (phase 4) in video recording, LiDAR phones only (absent otherwise), and
  `FigureOverlayLayer` (the stick figure, 5-frame smoothing), `UnexplainedShapeLayer` (outlined
  boxes with size + distance), both drawn over the camera preview and, on playback, over the video
  from the sidecar. `figure-overlay`, `export-with-figure`.

### Capture pipeline (`BenKit/Scan/`)

- `RoomScanner` — one `ARSession` with `ARWorldTrackingConfiguration` (`sceneReconstruction =
  .meshWithClassification`, `frameSemantics = [.sceneDepth]`), driving `RoomCaptureSession` in
  parallel for the parametric model (RoomPlan runs its own session; on iOS 17+ `RoomCaptureSession`
  accepts an external `ARSession` — use that so there is one camera stream).
- `KeyframePolicy` — a keyframe when the camera moved ≥ 0.5 m or turned ≥ 15° since the last one,
  and the frame's tracking state is `.normal`.
- `MeshExporter` — `ARMeshAnchor`s merged, welded, Draco-encoded to glTF (vendored Draco encoder
  via a Swift package; licence noted in `VENDORED.md`).
- `Texturer` (phase 2) — projective texturing: for each triangle, the keyframe whose view is most
  frontal and closest; a 4K atlas; or `PhotogrammetrySession` with `.reduced` detail if phase 0
  shows it looks better on the kitchen table.
- `RoomComparer` (phase 2) — `room.json` objects matched by category + nearest transform
  (Hungarian on position), thresholds 3 cm / 5°; then a 5 cm voxel grid from both meshes,
  XOR, clusters ≥ shoebox (30 × 20 × 15 cm) → New/Missing; each change gets the nearest keyframe
  crop. Writes `compare-NNN.scan`.
- `FigureTracker` (phase 4) — `.bodyDetection` 2D joints, lifted to 3D with the depth map at each
  joint's pixel (median of a 5 × 5 window, confidence ≥ medium), smoothed over 5 frames; writes
  `figures/figure-NNN.json` (`[{ "t", "joints": [[x,y,z,c] × 17] }]`) only while a figure is
  present; a `figure_seen` reading per continuous sighting (gap > 2 s ends it).
- `ShapeWatcher` (phase 4) — when the room has a current scan: depth points more than 10 cm off the
  baseline mesh, clustered per frame, a cluster ≥ 40 × 100 cm that persists ≥ 1 s → an
  `unexplained_shape` reading + outline; never joints.

---

## 6. Website

- `Shared/PlaceScans.razor` on the place page (signed-in view, below Rooms): the rooms with scans,
  version and date, who made it, size, **View** (phase 3 viewer), the custodian's **Show on the place
  page** switch, **Replace** (file picker for a `.scan` exported from a phone — rare, but the server
  route exists), offers waiting (accept/decline), history.
- `Shared/ScanViewer.razor` + `wwwroot/js/scan-viewer/` (three.js vendored, SHA in
  `VENDORED.md`): `GLTFLoader` + `DRACOLoader` reading `mesh.glb` by range; orbit by default;
  device-orientation look-around on phones; a joystick; comparison pins from `PlaceScanChanges`.
- The web session player: `scan`, `scan_change`, `figure`, `shape` readings in the timeline with
  their pictures; **View the room** opens the viewer at the baseline version the comparison used.
- Public place page: the walkthrough when `ShownOnPlacePage`, mesh + preview only.
- Admin: `/admin/place-scans` — scans by size, offers older than 14 days, removed scans.

---

## 7. Phases

### Phase 0 — Feasibility spike (phone only; one week; nothing shipped)

Branch `spike/ios-lidar-0`. A hidden screen behind `-lidarSpike`.

1. `LidarSupport` and a list of the devices it is true on.
2. RoomPlan → USDZ + `room.json`; ARKit mesh → Draco glTF; `ARWorldMap` saved; keyframes at
   0.5 m / 15°. **Measure every size** in four of Ben's rooms; replace the table in §2.
3. Relocalize hours later, lights on and off: time to recognize, failure rate. Decides phase 2.
4. `.bodyDetection` + `.sceneDepth` in one session, in the dark with a flashlight: does it fire on
   a person at 5 m, does it fire on nothing, what does a coat on a chair do.
5. Textured mesh two ways on the kitchen table with the flowers; pick one.
6. `RoomCaptureSession` with an external `ARSession`: confirm one camera stream suffices.

Done = `README-ios-lidar-phase-0.md` with the numbers, pictures, and a go/no-go per feature.

### Phase 1 — Room scans at a place (iOS + API, no website)

API: `PlaceScans` + `PlaceScanHolds` migration; `PlaceScanController` (list, holds, upload, get,
entries, file, versions, accept, decline, delete); `PlaceScans` service (`RefreshAfter = 365 d`,
`MayScanAsync`, `CustodianAsync`, `WhyCannotReplaceAsync`, `NormalizeRoomKey`); storage path +
cap owner rule; hold sweep job; age job; `FieldSessionFileGuard` learns `.scan` for session
bundles (comparisons later, but the type is one list).

iOS: `LidarSupport`, `ScanPackage`, `PlaceScanClient`, `RoomKey`, `RoomScanner`, `KeyframePolicy`,
`MeshExporter`; `placeId` on the engine; `ChoosePlaceSheet`, `ScanRoomButton`, `RoomScanView`,
`ScanRetentionSheet`, `ScanConflictSheet`, `ScanUploadView`, `ScanViewerView` (look-around +
joystick, untextured mesh); the `room_scan` reading; spec 1.2.

Tests:
- `PlaceScanControllerTests` — first scan → Current v1; second by a stranger → 409 with three
  choices; second with `intent=offer` → Offered; custodian accept → versions flip; replace by a
  stranger after 366 days → allowed, old → Previous; at 364 days → refused with the sentence; hold
  taken, second hold 409 names the holder, expired hold ignored; private residence refuses a
  non-interested group; cap: venue org pays when verified, else the uploader; delete keeps
  comparisons; room key normalization vectors (the parity file).
- `PlaceScanManifestTests` — bad manifest, missing mesh, 501 keyframes, non-stored zip entries → 400.
- iOS: `ScanPackageTests` round trip; `RoomKeyParityTests`; `KeyframePolicyTests`;
  `PlaceScanClientTests` on captured fixtures; UI test `RoomScanUITests` with `-scanFakeRoom`
  (a fixture scan stands in for the sensor: pick a place, Scan, Done, retention, upload to the
  e2e API, then the viewer opens).
- e2e: a fixture `.scan` uploaded by Sarah; James (same group) sees Compare, not Scan; Dana (other
  group, public location) sees Compare; the hold message appears while Sarah's hold is live.

### Phase 2 — Comparison scan + textured walkthrough (iOS)

iOS: `Texturer` (phase 0's choice), `RoomComparer`, `CompareRoomView`, `CompareResultsView`,
Walk mode in the viewer (ARKit world tracking on the viewing phone, mesh collision, keyframe
close-ups within 40 cm); `compare-NNN.scan` in the session bundle; `scan` + `scan_change` readings.
API: `PlaceScanChanges` migration; the timeline builder reads the new keys; `PlaceScanChanges`
written from the session document on upload (the session upload path, not a new route).

Tests: `RoomComparerTests` on synthetic room pairs (moved 2 cm → nothing; 4 cm → Moved; removed
→ Missing; added → New; nothing → no changes); the relocalization-failed path shows the sentence;
`PlaceScanChanges` rows match the document; viewer camera binding unit tests; UI test with a fixture
baseline + fixture comparison.

### Phase 3 — Website (waits until the audio/EVP plan is through — Ben, 10/28/2026)

Phases 1, 2 and 4 are iOS + API and may run while the audio/EVP work is on the site. Phase 3 is the
website piece and starts only once `AudioEditor-EVP-Plan-2026-10-26.md` is finished, so two plans
never edit the site at once. Phase 4 (person map) does not depend on phase 3 and may go before it.

`PlaceScans.razor`, `ScanViewer.razor` + three.js, session player readings, public place page
switch + route, admin list. Playwright: a fixture `.scan` on a seeded public place; the viewer loads
`mesh.glb`; the custodian's switch shows/hides it for a visitor; an offer accepted on the web flips
versions; a private residence's scan is 404 to a stranger.

### Phase 4 — Person map (iOS + API readers)

`FigureOverlayToggle` (LiDAR only, absent elsewhere), `FigureTracker`, `FigureOverlayLayer`,
`ShapeWatcher`, `UnexplainedShapeLayer`, the sidecar, `figure_seen` + `unexplained_shape` readings,
playback overlay, **Export with the figure drawn** (burned-in copy via `SessionVideoConverter`).
Server: timeline shows sightings with a thumbnail; the web player draws the figure from the sidecar
over the `<video>`.

Tests: synthetic depth frames with/without a person-sized cluster; the sidecar's size rule (nothing
written with nobody in frame); 5-frame smoothing bounds; UI test with `-figureFake`; a non-LiDAR
simulator has no toggle (asserted absent, not disabled).

### Phase 5 — Help, store, changelog

`using-the-paranormal-platform.md`: "Scanning a room", "Finding what moved", "Walking through a
scan", "The figure overlay — what it is and is not"; captures from fixtures (the simulator has no
LiDAR). App Store screenshot pair + What's New; the marketing PDF; `ios.md` and `webapp.md`
changelogs; the product documentation PDF. Store copy says LiDAR phones only.

---

## 8. Open questions for Ben

1. ~~Keyframes upload or stay on the phone?~~ Answered 10/28/2026: they upload with the scan.
2. ~~Figure overlay on non-LiDAR phones?~~ Answered 10/28/2026: Pro (LiDAR) only, end to end.
3. ~~Website viewer timing?~~ Answered 10/28/2026: phase 3 waits for the audio/EVP plan.
4. ~~SuperAdmin handing custody?~~ Answered 10/28/2026: yes (`PlaceScanCustody`, §2 rule 3, §4).

No open questions remain. Next step is phase 0 on Ben's phone.
