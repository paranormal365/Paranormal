import XCTest

/// The Field Kit screenshots for the App Store — driven through a real scripted night (item 214).
///
/// Updated for 1.0.3 (2026-09-16) with the camera frame: the app takes photographs and clips itself
/// now, without handing the microphone to Apple's camera and leaving a hole in the recording.
///
/// Ben: *"Create records for the app in order to display when building the simulation. Include
/// all functionality of the field kit and make sure you are using dark mode and you have to
/// simulate setting a base level and there be changes in the gauge."*
///
/// So this is the actor: it names a session, names the room, lets the room settle, sets the base
/// level, presses Start, arms the sentry, waits for the scripted excursion to swing the needle,
/// marks it, stops, reviews, and opens the trimmer. Every frame it attaches shows a session with
/// something in it. Dark mode is the simulator's setting, not the app's — the runner sets it.
///
/// Skipped unless `BEN_SCREENSHOTS=1` (exported as `TEST_RUNNER_BEN_SCREENSHOTS`), like the rest
/// of the set. The Send screen needs a signed-in account and a reachable API:
/// `TEST_RUNNER_BEN_API_BASE_URL`, `BEN_CLIENT_EMAIL`, `BEN_CLIENT_PASSWORD`.
final class FieldKitScreenshotTests: XCTestCase {

    private var app: XCUIApplication!

    override func setUpWithError() throws {
        guard ProcessInfo.processInfo.environment["BEN_SCREENSHOTS"] == "1" else {
            throw XCTSkip("screenshot capture runs only when asked — set TEST_RUNNER_BEN_SCREENSHOTS=1")
        }
        continueAfterFailure = true
        let env = ProcessInfo.processInfo.environment
        app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors"]
        if let base = env["BEN_API_BASE_URL"] { app.launchArguments += ["-apiBaseURL", base] }
        if let email = env["BEN_CLIENT_EMAIL"], let password = env["BEN_CLIENT_PASSWORD"] {
            app.launchArguments += ["-autoSignIn", "\(email):\(password)"]
        }
        app.launch()
    }

    private func snap(_ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }

    private func settle(_ seconds: TimeInterval = 2) {
        _ = app.wait(for: .runningForeground, timeout: seconds)
        Thread.sleep(forTimeInterval: seconds)
    }

    /// The account every frame after this one is photographed as.
    private var captureEmail: String {
        ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "daniel.park@benco.dev"
    }

    /// Makes sure the sign-in actually landed, and as the right person.
    ///
    /// Whoever the simulator was signed in as LAST is still signed in: the Keychain survives a
    /// reinstall, and `SessionStore.signIn` returns at once unless the state is signed out, so
    /// `-autoSignIn` is a no-op over a restored session. The 2026-09-16 iPhone set photographed the
    /// Send screen signed out that way — a held session from another account, dead by the time the
    /// frame came round. Sign that session out and ask again; and if it still never lands, say so
    /// rather than shipping a set of somebody else's phone.
    private func waitUntilSignedIn() {
        if confirmSignedIn(as: captureEmail) { return }
        if app.descendants(matching: .any)["Sign out"].firstMatch.exists {
            app.descendants(matching: .any)["Sign out"].firstMatch.tap()
            settle(3)
            app.terminate()
            app.launch()
            settle(6)
            if confirmSignedIn(as: captureEmail) { return }
        }
        XCTFail("Signing in as \(captureEmail) never landed — the Field Kit set would be captured as "
                + "somebody else, or signed out. Check BEN_CLIENT_EMAIL / BEN_CLIENT_PASSWORD and that "
                + "BEN_API_BASE_URL is serving them.")
    }

    private func confirmSignedIn(as email: String) -> Bool {
        guard AppNavigator.openSection("Profile", in: app, timeout: 20) else { return false }
        return app.staticTexts.matching(NSPredicate(format: "label CONTAINS[c] %@", email))
            .firstMatch.waitForExistence(timeout: 30)
    }

    func testCaptureTheFieldKitSet() throws {
        // Let -autoSignIn land its token in the Keychain, then relaunch WITHOUT it — the way a
        // real user's session starts. Left as launched, the iPad capture on 2026-09-04 reached
        // the Send screen with "Your session ended" and photographed the signed-out fallback.
        settle(6)
        if app.launchArguments.contains("-autoSignIn") {
            waitUntilSignedIn()
            app.terminate()
            app.launchArguments.removeAll { $0 == "-autoSignIn" || $0.contains(":") && $0.contains("@") }
            app.launch()
            settle(4)
        }

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        settle(1)

        // ── A session, named for the place ────────────────────────────────
        app.buttons["start-field-session"].tap()
        let field = app.textFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 10))
        field.tap()
        field.typeText("Back bedroom, north wall")
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 5))
        app.buttons["confirm-start-session"].tap()

        // ── Pending: the room, then the base level while the room is quiet ─
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 15))
        // By any type: an identifier on a container is not an Other, and a tap on an element
        // that is not there aborts the whole set. Every step below checks before it taps.
        let roomBar = app.descendants(matching: .any).matching(identifier: "room-bar").firstMatch
        if roomBar.waitForExistence(timeout: 5) {
            roomBar.tap()
            let room = app.textFields["room-name-field"]
            if room.waitForExistence(timeout: 5) {
                room.tap(); room.typeText("Cellar")
                if app.buttons["room-save"].exists { app.buttons["room-save"].tap() }
            }
        }
        // The scripted magnetometer is quiet for 17 of every 20 seconds from the moment the
        // sensors start. Four seconds in is quiet; a base taken during the excursion would read
        // every calm minute afterwards as a dip.
        settle(3)
        let base = app.buttons["set-base-level"].firstMatch
        if base.waitForExistence(timeout: 5) {
            if !base.isHittable { app.swipeUp() }
            base.tap()
        }
        app.swipeDown(); settle(1)
        snap("10-fieldkit-pending-base-set")   // set up, base level taken, not yet started

        // ── Start, arm the sentry, and wait for the needle to swing ───────
        app.buttons["start-recording"].tap()
        XCTAssertTrue(app.buttons["stop-field-session"].waitForExistence(timeout: 10))

        let arm = app.buttons["arm-sentry"].firstMatch
        if !arm.isHittable { app.swipeUp() }
        if arm.waitForExistence(timeout: 5) {
            arm.tap()
            let confirm = app.buttons["confirm-arm"]
            if confirm.waitForExistence(timeout: 5) { confirm.tap() }
        }
        app.swipeDown(); settle(1); app.swipeDown()

        // The excursion: +60 mG over base for three seconds in every twenty. "over report
        // level" appears in the bar the moment it crosses, and that is the frame.
        let over = app.staticTexts["over report level"]
        _ = over.waitForExistence(timeout: 25)
        snap("11-fieldkit-live-excursion")     // the gauge swung, sentry watching, recording

        if app.buttons["mark-now"].firstMatch.exists { app.buttons["mark-now"].firstMatch.tap() }
        settle(1)
        app.swipeUp(); settle(1)
        snap("12-fieldkit-marks")              // the marker log: automatic and by hand

        // ── The camera, which is ours now (1.0.3) ─────────────────────────
        //
        // The headline change since 1.0.2: a photograph or a clip is taken inside the app, with the
        // session still running and the sound carrying straight on through it. The old build left
        // for Apple's camera, which took the microphone with it and put a hole in the recording, so
        // there was nothing here worth photographing. The simulator has no camera, so the frame is
        // the capture screen itself — its shutter, its photo/clip choice and the session still live
        // behind it — which is the thing that is new.
        let camera = app.buttons["capture-camera"].firstMatch
        if !camera.isHittable { app.swipeUp() }
        if camera.waitForExistence(timeout: 5) {
            camera.tap()
            if app.buttons["camera-shutter"].waitForExistence(timeout: 10) {
                settle(2)
                // A simulator has no camera and photographs a black screen with a sentence on it — no
                // frame at all beats that in the store set. On a device the capture goes through.
                if !app.staticTexts["No camera is available on this device."].exists {
                    snap("15-fieldkit-camera")     // photo or clip, without leaving the session
                }
            }
            if app.buttons["camera-close"].firstMatch.exists { app.buttons["camera-close"].firstMatch.tap() }
            settle(1)
        }
        app.swipeDown(); settle(1)

        // ── Stop, and the review ──────────────────────────────────────────
        app.swipeDown(); settle(1)
        if app.buttons["stop-field-session"].exists { app.buttons["stop-field-session"].tap() }
        XCTAssertTrue(app.buttons["open-share-menu"].waitForExistence(timeout: 25))
        settle(2)
        // Park the replay on the excursion so the trace and readouts have something to say.
        let scrubber = app.sliders["replay-scrubber"].firstMatch
        if scrubber.exists { scrubber.adjust(toNormalizedSliderPosition: 0.55); settle(1) }
        snap("13-fieldkit-review")             // trace, readouts, markers, map

        // ── The trimmer, on the Send screen ───────────────────────────────
        app.buttons["open-share-menu"].tap()
        let send = app.buttons["Send to the server"].firstMatch
        if send.waitForExistence(timeout: 10) {
            send.tap()
            let track = app.otherElements["trim-track"]
            if track.waitForExistence(timeout: 20) {
                // Drag the in point a third of the way in, so the band, the counts and the
                // "cut to" line all show — a whole-session trimmer is just a green bar.
                app.buttons["trim-handle-in"].coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5))
                    .press(forDuration: 0.1,
                           thenDragTo: track.coordinate(withNormalizedOffset: CGVector(dx: 0.35, dy: 0.5)))
                settle(1)
                snap("14-fieldkit-trim")       // in/out points, preview, what will be sent
            } else {
                snap("14-fieldkit-send")       // signed out: the screen that says why
            }
            if app.buttons["Done"].firstMatch.exists { app.buttons["Done"].firstMatch.tap() }
        }

        // ── Back home: the list with a night in it ────────────────────────
        if app.navigationBars.buttons.firstMatch.exists { app.navigationBars.buttons.firstMatch.tap() }
        settle(1)
        if AppNavigator.openSection("Field Kit", in: app) {
            settle(1)
            snap("04-field-kit")               // replaces the 1.0.0 empty-list shot
        }
    }
}
