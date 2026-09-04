# App Store screenshots and previews — 1.0.2

The upload set for **IsHaunted 1.0.2 (build 2)**. Kept beside the 1.0.0 set in `../screenshots/`
with the version in the folder name so the two can never be confused in an upload dialog.

Captured 2026-09-04 on the iPhone 17 Pro Max and iPad Pro 13-inch simulators, **dark mode**,
against a local API with a throwaway account (item 214).

| Folder | Device | Size | Files |
|---|---|---|---|
| `iphone-6.5-dark` | iPhone 17 Pro Max, dark | 1242×2688 — captured at native 1320×2868, scaled to width and centre-cropped 10 px | 10 |
| `ipad-13-dark` | iPad Pro 13-inch, dark | 2064×2752 native — the required 13" slot, no scaling | 10 |
| `app-preview` | Field Kit demo, dark | iPhone 886×1920, iPad 1200×1600, 28 s, 30 fps, H.264, video only | 2 |
| `tools` | `preview.swift` — probe, poster, cut | | 1 |

## The Field Kit set — new for 1.0.2

`FieldKitScreenshotTests` is the actor. It names a session *Back bedroom, north wall*, names the
room *Cellar*, lets the room settle, **sets the base level**, presses **Start**, arms the sentry,
waits for the scripted excursion to swing the needle, marks it, stops, opens the review, and drags
the trimmer's in point. Every frame shows a session with something in it:

| File | What it shows |
|---|---|
| `10-fieldkit-pending-base-set` | Set up and not started: room named, base taken, *not started*, Discard and Start (item 215) |
| `11-fieldkit-live-excursion` | Recording: the needle at ~+60 mG from base, *over report level*, 27 readings |
| `12-fieldkit-marks` | Capture bar, sentry armed and watching, channels, the marker log with automatic spikes and a hand mark |
| `13-fieldkit-review` | Replay: transport, field/sound/heading readouts, the trace with markers, the map with the room and track |
| `14-fieldkit-trim` | The Send screen's trimmer: in/out points, preview readouts and chart, *cut to 0:25* (item 210) |
| `04-field-kit` | The Field Kit home with sessions in it — replaces the 1.0.0 empty list |

## Carried from 1.0.0, unchanged

`01-home`, `02-cases`, `03-investigations`, `05-events` are the 1.0.0 files. Those screens have not
changed, they were captured dark with real seeded data, and recapturing them needs a seeded
persona's password, which lives only in Ben's environment. Refresh them with
`AppStoreScreenshotTests` per `../screenshots/README.md` if they are ever wanted new.

## The previews — new for 1.0.2

`FieldKitDemoDriveUITests` acts while `simctl io recordVideo` films: name the room, settle, **set
base while pending, press Start**, then the needle swinging through two scripted excursions. Cut to
the 28 s from Start onward. The 1.0.0 previews predate both Start and the trimmer and are not used.

No ffmpeg on this Mac, so `tools/preview.swift` (AVFoundation) does the resize and cut:

    swiftc -O -o /tmp/preview tools/preview.swift
    /tmp/preview probe  <recording.mp4>
    /tmp/preview poster <recording.mp4> <seconds> <frame.jpg>      # to choose the window
    /tmp/preview cut    <recording.mp4> <out.mp4> 886 1920 30 28   # iPhone: fill+crop, H.264
    /tmp/preview cut    <recording.mp4> <out.mp4> 1200 1600 27 28  # iPad

Video only — no audio track. App Store Connect accepts silent previews; if it ever objects, add a
silent AAC track at upload time.

## Refreshing the Field Kit set

    xcrun simctl boot <udid>; xcrun simctl ui <udid> appearance dark
    export TEST_RUNNER_BEN_SCREENSHOTS=1 TEST_RUNNER_BEN_API_BASE_URL=http://localhost:5252 \
           TEST_RUNNER_BEN_CLIENT_EMAIL=<account> TEST_RUNNER_BEN_CLIENT_PASSWORD=<password>
    xcodebuild test-without-building -project IsHaunted.xcodeproj -scheme IsHaunted \
      -destination 'platform=iOS Simulator,id=<udid>' \
      -only-testing:IsHauntedUITests/FieldKitScreenshotTests -resultBundlePath /tmp/shots.xcresult
    xcrun xcresulttool export attachments --path /tmp/shots.xcresult --output-path <dir>

Then, for the iPhone only: `sips -Z 2698 --resampleWidth 1242 <png>; sips --cropToHeightWidth 2688 1242 <png>`.

Two things the first captures taught: `TEST_RUNNER_*` variables must be **exported in the shell**
(passed as `xcodebuild KEY=value` they never reach the runner), and the test **relaunches after
`-autoSignIn` lands** so the Keychain-restored session is used — left as launched, the iPad reached
the Send screen with "Your session ended" and photographed the signed-out fallback.

For the previews:

    xcrun simctl io <udid> recordVideo --codec h264 --force /tmp/demo.mp4 &
    TEST_RUNNER_BEN_DEMO_DRIVE=1 xcodebuild test-without-building … -only-testing:IsHauntedUITests/FieldKitDemoDriveUITests
    kill -INT %1

No profile screenshot from a Debug build — it carries the DEBUG-only Developer section, and debug
UI in a screenshot is a rejection.
