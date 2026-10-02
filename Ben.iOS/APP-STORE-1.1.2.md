# App Store update — IsHaunted 1.1.2 (build 10)

A one-fix release. Ben, 2026-10-02: *"After downloading the update for the iOS app, the magnetometer
quit tracking."*

| | |
|---|---|
| Version | **1.1.2** (`MARKETING_VERSION`) |
| Build | **10** (`CURRENT_PROJECT_VERSION`) — above every build ever uploaded |
| Replaces | 1.1.1 (9), released 2026-10-01 |
| Server work first | None — no server change |

## 1. What was wrong

The magnetometer and the movement watcher each had their own `CMMotionManager`, and each started
the phone's device motion — the magnetometer in the magnetic-north frame (which is what computes the
field), the watcher in the default frame. Device motion is one service on the phone; two managers
asking it for two frames left the magnetometer's field un-updated. Before 1.1.1 the watcher only ran
when a sentry asked for it; 1.1.1 made **Watch for Motion** default to on, so every session started
the second stream and the gauge stopped moving.

**The fix** (`LiveSensors.swift`, `CameraSession.swift`): one `SharedDeviceMotion` — one manager,
one stream, always in a frame that computes the field — handed to both readers at the rate each
asked for. It starts with the first reader and stops with the last.

## 2. Check it on a phone BEFORE archiving

The simulator has no magnetometer, so this can only be proved on a device:

1. Run the app from Xcode on your iPhone (or install a TestFlight build of 1.1.2 (10)).
2. Field Kit → start a session with **Watch for Motion on** (the default) and the magnetic channel on.
3. Move a magnet, a phone speaker or a steel tool near the top of the phone: the gauge and the trace
   must move, and keep moving for a few minutes.
4. Turn on Video as well and repeat — the camera's motion watching uses the same stream.
5. Stop, review the session: the magnetic trace is there for the whole time.

## 3. Ship it

1. Xcode → destination **Any iOS Device (arm64)** → Product → **Archive** → Distribute App →
   App Store Connect → Upload.
2. App Store Connect → IsHaunted → **+ Version** → **1.1.2**.
3. **What's New in This Version**:

   > The magnetometer keeps reading through a whole session again. In 1.1.1, with Watch for Motion
   > on, it could stop updating partway through a session.

4. Select build **10**, leave the screenshots as they are (no screen changed), **Add for Review**.
