# LiDAR phase 1 — room scans kept by the place (iOS + API)

Branch `feature/ios-lidar-1`. Plan of record: `README-ios-lidar-scanning.md` (§2 model, §3 data,
§4 API, §5 iOS, §7 phase 1). No website: phase 3 is the website and waits for the audio/EVP plan.

## What it does

On a LiDAR iPhone, the live session screen shows a **Scan this room** card under the room bar. A
room's scan belongs to the **place**, so the first scan asks which place the session is at (places
near the phone, or name one). Tapping Scan takes a twenty-minute hold on the room so the next
person at the building is told "Dana is scanning Bedroom 1 right now". RoomPlan and ARKit capture
the room model, the mesh, the world map and a keyframe every half metre or fifteen degrees. After
Done the phone asks what to keep — **Model and pictures** or **Model only**, each with its size —
packs one `.scan` file and sends it. The server decides what it becomes:

- no scan yet: the room's scan (version 1);
- a scan the person may replace: the new current, the old kept as a previous version;
- otherwise: 409 with the choices — keep it as a comparison, offer it to replace, or discard. An
  offer waits for the custodian, who is told and answers on the phone's offers screen.

A scanned room offers **View the scan**: the mesh in RealityKit at eye height, a drag to look, a
joystick to walk (never through a wall), **Look around** with the phone itself, and a menu to walk
room to room. A `room_scan` reading goes into the session ("Room scanned").

## Server (Ben.Data.WebApi)

| Piece | Where |
|---|---|
| Tables `PlaceScans`, `PlaceScanHolds`, `PlaceScanCustodies` | `Entities/BenDataModel.PlaceScan.cs`, context config |
| Enums `PlaceScanState`, `PlaceScanRetention` (tinyint) | `Ben.Data.Common/Enums` |
| The rules: may scan/view, custodian, `WhyCannotReplaceAsync`, `NormalizeRoomKey`, `RefreshAfter` = 365 days | `Services/Places/PlaceScans.cs` |
| `.scan` validation (members, manifest, headers, 500 keyframes) | `Services/Places/PlaceScanManifest.cs` |
| Routes: list, holds, upload, get, entries by range, file, versions, accept, decline, delete | `Controllers/Entities/PlaceScanController.cs` |
| SuperAdmin custody grant (PUT/DELETE/GET) | `Controllers/Admin/AdminPlaceScanCustodyController.cs` |
| Hold sweep; a note to the custodian when a scan turns a year old | `Scheduling/PlaceScanHoldSweepJob.cs`, `PlaceScanAgeJob.cs` |
| `PlaceFilePath` (`places/{id}/…`) | `IFileStorageService` + both stores |
| A person's place scans count against their free allowance | `AccountStorageGuard.UsedBytesAsync` |
| `.scan` inside a session bundle | `FieldSessionFileGuard` |
| Bundle reader takes a scan's word and member limit | `BenBundle.ReadAsync(stream, BenBundleRules.Scan)` |
| Scans move on a place merge; census counts them; purges clear holds and custody | merge controller, `PlaceUsageCensus`, both purges |
| A verified venue claim closes a custody grant | `VenueClaims.ApproveAsync` |
| Marker titles: Room scanned, Something changed, Figure seen, Unexplained shape | `FieldSessionTimelineBuilder.Title` |

### Decisions made while building (for Ben to overrule)

- **Stored at `places/{placeId}/scans/{scanId}/room.scan`**, not `…/{roomKey}/v{n}/…`. The version
  is decided after the bytes are in, and a room name is not a safe folder name; two uploads must
  never share a path.
- **Custodian acts through a group's owners and administrators.** An ordinary member of the
  venue's group offers, and an owner accepts. **Whoever made a scan may scan it again** while no
  venue or grant looks after the place (a scan somebody just made badly is theirs to redo).
- **Private residence:** a group's Viewer may look at the scans but not scan.
- **Offers are answered on the phone** (`ScanOffersView`), because phase 3's web page waits; the
  custodian's notification says so.
- **Holds are a courtesy, not a lock**: the upload decides whatever the holds say.
- `YearNoticeSentUtc` added to `PlaceScans` so the year-old note is sent once.
- The admin custody route has no screen until phase 3's admin page; it is listed in
  `EveryApiRouteHasACallerTests.NoVisibleCaller` with that reason, to come off when the page calls it.

## iPhone (BenKit + IsHaunted)

| Piece | Where |
|---|---|
| `LidarSupport` (absent, not disabled, without LiDAR) | `BenKit/Scan/LidarSupport.swift` |
| `RoomKey` — the server's normalization, held to shared vectors | `BenKit/Scan/RoomKey.swift` |
| `.scan` writer/reader, capture folder, local scan store | `BenKit/Scan/ScanPackage.swift`, `ScanManifest.swift` |
| `PlaceScanClient` + records; raw upload for structured 409s | `BenKit/Scan/PlaceScanClient.swift`, `APIClient.uploadRaw` |
| `RoomScanner` (RoomPlan with an external `ARSession`), `KeyframePolicy`, `DepthCodec`, `MeshExporter`, `ScanRoomModel` | `BenKit/Scan/` |
| Viewer camera rules (walk, clamp, look, device attitude) | `BenKit/Scan/ScanViewerCamera.swift` |
| The simulator's stand-in room (`-scanFakeRoom`) | `BenKit/Scan/ScanFixture.swift` |
| Place on the session (`placeId`, from the investigation or the picker) | `FieldSession`, store `setPlace`, `ActiveFieldSession` |
| `room_scan` reading | `FieldSessionEngine.noteRoomScan` |
| Measurements keep fields beside their value; JSON lists and objects | `FieldReading.Measurement.details`, `JSONValue` |
| Screens: scan card, choose place, scanner, keep what, sending, conflict, result, viewer, offers | `IsHaunted/Features/Scan/` |
| Room sheet lists the place's rooms with "Scanned 03/12/2026" / "Not scanned", and snaps spellings | `RoomBar.swift` |

### Decisions made while building

- **Plain glTF, no Draco.** RealityKit reads neither glTF nor Draco, so the phone reads its own
  `mesh.glb`; Draco would add a C++ encoder and a decoder in every viewer for a few megabytes
  beside tens of megabytes of keyframes. Three.js reads the plain file with no extra loader.
- **`room.json` is our own room model**, not RoomPlan's `CapturedRoom` coding, so it is written
  down, readable anywhere and testable on a Mac.
- **RoomPlan's delegate is left alone**: frames are polled ten times a second for keyframes.
  Phase 0 still has to confirm on Ben's phone that RoomPlan's own configuration carries scene
  depth and mesh anchors; when it does not, keyframes have no depth and the mesh is drawn from
  the room model's boxes — the scan still uploads and opens.

## Spec 1.2 (`ProjectNotes/specs/DeviceDataFormat-v1.md`, schema)

`room_scan`, `scan`, `scan_change`, `figure`, `shape`: each a measurement with a `value` (so a 1.0
reader shows something and skips the rest) and its other facts beside it. The plan wrote them as
objects without a `value`; that would have failed every existing reader, the phone's included,
because a measurement's `value` is required. The app now writes `format_version` 1.2.0. Five
malformed documents are kept as schema tests.

## Tests

- API: `PlaceScanControllerTests` (25), `PlaceScanManifestTests` (16), `PlaceScanRoomKeyTests`,
  `PlaceScanMergeTests`, `PlaceScanFixtureTests` (captures the phone's fixtures from the real
  controller and fails when the server's answer changes shape), `RoomScanChannelsDocumentTests`.
- BenKit: `ScanPackageTests`, `MeshExporterTests`, `KeyframePolicyTests`, `DepthCodecTests`,
  `ScanViewerCameraTests`, `PlaceScanClientTests` (on the captured fixtures), `RoomKeyParityTests`,
  `RoomScanReadingTests`, and the 1.2 schema tests.
- UI: `RoomScanUITests` — pick a place, Scan, Done, keep what, upload, open the viewer; and a
  phone without LiDAR has no scan card. Needs an API with these routes (`TEST_RUNNER_BEN_API_BASE_URL`).
  All three passed on 10/29/2026 against an API on 5262 using the side database `IsHauntedDb_lidar`.

## The migration

`20261028200857_PlaceScans` creates this phase's three tables and phase 2's `PlaceScanChanges`, on
top of the session player's `FileProvenances`; added 10/28/2026 with Ben's yes. On 10/29/2026 it went
into a fresh `IsHauntedDb_lidar` with every migration before it, cleanly. Production waits for Ben:
`Claude outputs/PlaceScans-20261028.sql`, idempotent, before develop is deployed.

## Not done here

- Phase 0's measurements on Ben's phone (sizes, relocalization, RoomPlan's session contents).
- The e2e (Playwright) checks the plan lists for phase 1 are website tests; they wait for phase 3.
