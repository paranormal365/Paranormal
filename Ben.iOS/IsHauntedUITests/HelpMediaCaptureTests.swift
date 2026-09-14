import XCTest

/// Captures the screenshots the WEBSITE's help pages embed, as distinct from the App Store set.
///
/// **A separate fixture from `AppStoreScreenshotTests` on purpose.** That one exists to satisfy
/// Apple: fixed device sizes, dark mode, no debug UI, a curated marketing sequence. These exist to
/// show somebody reading `/help/the-mobile-apps` what a screen actually looks like, so they follow
/// the documentation rather than the store listing, and they change whenever the documentation
/// does. Sharing one fixture would tie a help page's illustrations to Apple's requirements.
///
/// Skipped unless `BEN_SCREENSHOTS=1` (as `TEST_RUNNER_BEN_SCREENSHOTS=1` on the xcodebuild line),
/// like its sibling — a capture run is something you ask for, not something the suite does.
final class HelpMediaCaptureTests: XCTestCase {
    private var app: XCUIApplication!

    /// The API every launch in a capture talks to, as `-apiBaseURL`.
    ///
    /// **Required, and passed on every launch.** The address is deliberately not sticky, so a launch without it uses
    /// whatever the simulator last saved — or the shipped address, which is the live site. The captures relaunch the
    /// app to prove a session survives, and those relaunches once cleared the arguments, so help pictures could have
    /// been taken of real accounts without a word.
    private var apiArguments: [String] = []

    override func setUpWithError() throws {
        guard ProcessInfo.processInfo.environment["BEN_SCREENSHOTS"] == "1" else {
            throw XCTSkip("screenshot capture runs only when asked — set TEST_RUNNER_BEN_SCREENSHOTS=1")
        }
        guard let base = ProcessInfo.processInfo.environment["BEN_API_BASE_URL"], !base.isEmpty else {
            throw XCTSkip("set TEST_RUNNER_BEN_API_BASE_URL to the stack to photograph — without it the app could reach the live site")
        }
        continueAfterFailure = true
        app = XCUIApplication()
        apiArguments = ["-apiBaseURL", base]

        let email = ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "daniel.park@benco.dev"
        let password = TestSecrets.required("BEN_CLIENT_PASSWORD")
        app.launchArguments = apiArguments + ["-autoSignIn", "\(email):\(password)"]
        app.launch()
    }

    private func snap(_ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }

    private func settle(_ seconds: TimeInterval = 3) {
        _ = app.wait(for: .runningForeground, timeout: seconds)
        Thread.sleep(forTimeInterval: seconds)
    }

    /// My evidence — the guest's own copy of what they photographed at somebody's public event.
    ///
    /// Daniel is the account on purpose: he belongs to no group and has a confirmed attendance at
    /// a past public event, so the screen shows what a ghost-walk guest actually sees rather than
    /// what an owner with every permission sees.
    func testCaptureMyEvidence() {
        settle(6)   // let -autoSignIn land its token in the Keychain

        // Relaunch signed in from the Keychain, the way a real session starts. Without this the
        // stores fetch while sign-in is still in flight and cache their anonymous answers — the
        // lesson AppStoreScreenshotTests learned by screenshotting empty surfaces.
        app.terminate()
        app.launchArguments = apiArguments
        app.launch()
        settle(5)

        XCTAssertTrue(AppNavigator.openSection("Profile", in: app),
                      "Could not reach Profile, so My evidence was never opened.")
        settle()

        let row = app.buttons["settings-my-evidence"].firstMatch
        if !row.waitForExistence(timeout: 10) {
            // Said plainly rather than captured blank: an empty picture in the help page is worse
            // than none, because nobody can tell it is wrong.
            XCTFail("The My evidence row is missing from Profile — nothing to capture.")
            return
        }

        row.tap()
        settle(4)
        snap("iphone-my-evidence")
    }

    /// What I'm going to, and a pass — the help page's "An event you've booked" (item 235 phase 14).
    ///
    /// Daniel again: a guest with confirmed bookings and no group. The first booking whose pass has actually been
    /// issued is the one photographed, because a confirmed booking can still be waiting on the venue for its pass.
    func testCaptureMyEventsAndPass() {
        settle(6)
        app.terminate()
        app.launchArguments = apiArguments
        app.launch()
        settle(5)

        XCTAssertTrue(AppNavigator.openSection("Profile", in: app),
                      "Could not reach Profile, so What I'm going to was never opened.")
        settle()

        let row = app.buttons["settings-my-events"].firstMatch
        if !row.waitForExistence(timeout: 10) {
            XCTFail("The What I'm going to row is missing from Profile — nothing to capture.")
            return
        }
        row.tap()
        settle(4)
        snap("iphone-my-events")

        let passes = app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH 'my-events-pass-'"))
        for index in 0..<passes.count {
            passes.element(boundBy: index).tap()
            if app.descendants(matching: .any)["event-pass-code"].firstMatch.waitForExistence(timeout: 8) {
                settle(2)
                snap("iphone-event-pass")
                return
            }
            app.navigationBars.buttons.element(boundBy: 0).tap()
            settle(2)
        }
        XCTFail("None of the confirmed bookings has an issued pass — nothing to capture.")
    }

    /// During the event: the event's own screen, its programme, a menu and the room (item 235 phase 14b).
    ///
    /// Walks each booking's event screen until one has a programme to show, and photographs what that event has; a
    /// row that isn't there is reported rather than photographed blank.
    func testCaptureDuringTheEvent() {
        settle(6)
        app.terminate()
        app.launchArguments = apiArguments
        app.launch()
        settle(5)

        XCTAssertTrue(AppNavigator.openSection("Profile", in: app), "Could not reach Profile.")
        settle()
        let row = app.buttons["settings-my-events"].firstMatch
        guard row.waitForExistence(timeout: 10) else { return XCTFail("The What I'm going to row is missing from Profile.") }
        row.tap()
        settle(4)

        let hubs = app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH 'my-events-hub-'"))
        for index in 0..<hubs.count {
            hubs.element(boundBy: index).tap()
            let programme = app.buttons["hub-programme"].firstMatch
            if programme.waitForExistence(timeout: 8) {
                settle(2)
                snap("iphone-event-hub")

                programme.tap()
                settle(3)
                snap("iphone-event-programme")
                app.navigationBars.buttons.element(boundBy: 0).tap()
                settle(2)

                let menus = app.buttons["hub-menus"].firstMatch
                if menus.waitForExistence(timeout: 3) {
                    menus.tap()
                    settle(3)
                    snap("iphone-event-menus")
                    app.navigationBars.buttons.element(boundBy: 0).tap()
                    settle(2)
                } else {
                    XCTFail("This event has no menus to photograph.")
                }

                let room = app.buttons["hub-room"].firstMatch
                if room.waitForExistence(timeout: 3) {
                    room.tap()
                    settle(4)
                    snap("iphone-event-room")
                } else {
                    XCTFail("This event's room isn't open to the guest.")
                }
                return
            }
            app.navigationBars.buttons.element(boundBy: 0).tap()
            settle(2)
        }
        XCTFail("None of the guest's events has a published programme — nothing to capture.")
    }

    /// Tonight's door and a scanned reservation (item 235 phase 14c). Needs an account that may run a door — run it
    /// with `TEST_RUNNER_BEN_CLIENT_EMAIL` and `TEST_RUNNER_BEN_CLIENT_PASSWORD` set to the organizer seat — and
    /// `TEST_RUNNER_BEN_DOOR_SCAN_CODE` set to a pass token for that door, which stands in for the camera a
    /// simulator doesn't have.
    func testCaptureTheDoor() {
        settle(6)
        app.terminate()
        app.launchArguments = apiArguments
        if let code = ProcessInfo.processInfo.environment["BEN_DOOR_SCAN_CODE"] {
            app.launchArguments += ["-doorScanCode", code]
        }
        app.launch()
        settle(5)

        XCTAssertTrue(AppNavigator.openSection("Profile", in: app), "Could not reach Profile.")
        settle()
        let row = app.buttons["settings-door-duties"].firstMatch
        if !row.waitForExistence(timeout: 10) {
            app.swipeUp()
        }
        guard row.waitForExistence(timeout: 5) else {
            return XCTFail("Doors I'm running is missing — this account may not run any door. See the test's note.")
        }
        row.tap()
        settle(3)

        let door = app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH 'door-duty-'")).firstMatch
        guard door.waitForExistence(timeout: 8) else { return XCTFail("No door is listed.") }
        door.tap()
        guard app.staticTexts["door-count"].waitForExistence(timeout: 10) else { return XCTFail("The door did not open.") }
        settle(2)
        snap("iphone-door")

        guard ProcessInfo.processInfo.environment["BEN_DOOR_SCAN_CODE"] != nil else { return }
        app.buttons["door-scan"].firstMatch.tap()
        let reservation = app.buttons["door-scanned-reservation"].firstMatch
        guard reservation.waitForExistence(timeout: 10) else { return XCTFail("The scanned pass found no reservation.") }
        settle(1)
        snap("iphone-door-scanned")
        reservation.tap()
        guard app.buttons["reservation-check-in"].waitForExistence(timeout: 8) else { return XCTFail("The reservation did not open.") }
        settle(1)
        snap("iphone-door-reservation")
    }
}
