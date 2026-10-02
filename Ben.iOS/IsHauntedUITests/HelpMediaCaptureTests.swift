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

    /// Relaunches signed in as somebody else, with scripted sensors.
    ///
    /// Signs the current account out first. `-autoSignIn` is a no-op over a restored session
    /// (`SessionStore.signIn` returns unless signed out), so relaunching with James's credentials
    /// after `setUp` signed Daniel in kept Daniel — and the Field Kit pictures were of an account
    /// with nothing on the server, with no error anywhere (2026-09-27).
    private func relaunch(as email: String, password: String) {
        settle(4)   // let setUp's own sign-in land before undoing it
        if AppNavigator.openSection("Profile", in: app, timeout: 20) {
            let signOut = app.descendants(matching: .any)["Sign out"].firstMatch
            for _ in 0..<4 where !signOut.exists { app.swipeUp() }
            if signOut.exists { signOut.tap(); settle(3) }
        }
        app.terminate()
        app.launchArguments = apiArguments + ["-fieldKitFakeSensors", "-autoSignIn", "\(email):\(password)"]
        app.launch()
        settle(6)
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

    /// Field Kit: opening a `.ben`, what the server still holds, and a session pulled back down playing as its own.
    ///
    /// James is the account — a member whose own sessions were sent up as one file, so "On the server, not on this
    /// phone" has a row to show. Needs `TEST_RUNNER_BEN_MEMBER_PASSWORD`. The first session the server can hand
    /// back is downloaded and photographed; the home picture stands on its own when there is nothing to pull.
    func testCaptureFieldKitBundles() {
        let email = ProcessInfo.processInfo.environment["BEN_MEMBER_EMAIL"] ?? "james.thornton@benco.dev"
        let password = TestSecrets.required("BEN_MEMBER_PASSWORD")
        // Scripted sensors: a simulator has no magnetometer, and the Field Kit refuses to open without one.
        relaunch(as: email, password: password)

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app), "Could not reach the Field Kit.")
        settle(3)
        app.swipeUp()
        settle(1)
        snap("iphone-fieldkit-home")

        let download = app.buttons["download-field-session"].firstMatch
        guard download.waitForExistence(timeout: 5) else { return }
        download.tap()
        settle(8)
        // The facts, with the Source line, are below the chart and the map. Swiping on the map pans it,
        // so the scroll starts on the readouts above the chart.
        let readouts = app.staticTexts["Field"].firstMatch
        if readouts.waitForExistence(timeout: 5) {
            let start = readouts.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5))
            start.press(forDuration: 0.05, thenDragTo: start.withOffset(CGVector(dx: 0, dy: -600)))
        } else {
            app.swipeUp()
        }
        settle(1)
        snap("iphone-review-imported")
    }

    /// The New session sheet, Public sessions, and a review at the moment a photograph glows and the
    /// camera saw motion (2026-09-27).
    ///
    /// James again, for the same reason as above — and because the review is his own "Stage check"
    /// sent up by the browser tests, the one session on the stack with a video, its sound, three
    /// photographs and a Motion detected mark at 0:08. Set the simulator's location near the seeded
    /// places first (`xcrun simctl location <udid> set 36.1627,-86.7816`), or the sheet has no place
    /// to offer and the public list is empty. Runs on either device; the names follow it.
    func testCaptureStartAndPublicAndReview() throws {
        let email = ProcessInfo.processInfo.environment["BEN_MEMBER_EMAIL"] ?? "james.thornton@benco.dev"
        let password = TestSecrets.required("BEN_MEMBER_PASSWORD")
        let device = UIDevice.current.userInterfaceIdiom == .pad ? "ipad" : "iphone"
        relaunch(as: email, password: password)

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app), "Could not reach the Field Kit.")
        settle(2)
        AppNavigator.startNewSession(in: app)
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        // The place and the nearby count arrive after the sheet does — a first launch's position
        // takes a while, and the first capture was of "Finding where you are…".
        let label = app.textFields["session-label"].firstMatch
        let placeholder = label.placeholderValue ?? ""
        for _ in 0..<30 {
            let value = label.value as? String ?? ""
            if !value.isEmpty, value != placeholder { break }
            settle(1)
        }
        settle(2)
        snap("\(device)-new-session")
        app.buttons["Cancel"].firstMatch.tap()
        settle(1)

        let find = app.buttons["find-public-sessions"].firstMatch
        XCTAssertTrue(find.waitForExistence(timeout: 10), "Find public sessions should be on the Field Kit.")
        find.tap()
        XCTAssertTrue(app.descendants(matching: .any)["public-session-row"].firstMatch.waitForExistence(timeout: 20),
                      "Public sessions near the simulator's location should be listed.")
        settle(2)
        snap("\(device)-public-sessions")
        app.navigationBars.buttons.firstMatch.tap()
        settle(2)

        // Downloaded by an earlier run, it is in the phone's own list instead of the server's.
        let download = app.buttons.matching(NSPredicate(format: "label == %@", "Download Stage check to this phone")).firstMatch
        let onThePhone = app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Stage check")).firstMatch
        // A List draws rows only near the screen, and a simulator that has run the suite holds a long list.
        _ = download.waitForExistence(timeout: 8)
        for _ in 0..<12 where !(download.exists && download.isHittable) && !(onThePhone.exists && onThePhone.isHittable) {
            app.swipeUp()
        }
        if download.exists {
            download.tap()
        } else if onThePhone.exists {
            onThePhone.tap()
        } else {
            let tree = XCTAttachment(string: app.debugDescription)
            tree.name = "field-kit-without-stage-check"
            tree.lifetime = .keepAlways
            add(tree)
            throw XCTSkip("no \"Stage check\" on the server or the phone for this account — run the browser FieldSessionStageTests first")
        }
        let scrubber = app.sliders["replay-scrubber"].firstMatch
        XCTAssertTrue(scrubber.waitForExistence(timeout: 30), "The downloaded session should open on its review.")
        settle(3)
        // 0:08.6 of 0:18: the second photograph is lit and Motion detected is showing.
        scrubber.adjust(toNormalizedSliderPosition: 8.6 / 18)
        settle(1)
        snap("\(device)-review")
    }

    /// A client's visits, read on the phone's clock with the visit's own a switch away (2026-09-28).
    ///
    /// Daniel, the setUp account: a client whose case has a visit.
    func testCaptureCaseVisits() throws {
        settle(6)
        XCTAssertTrue(AppNavigator.openSection("My Cases", in: app), "Could not reach My Cases.")
        let firstCase = app.cells.firstMatch
        XCTAssertTrue(firstCase.waitForExistence(timeout: 15), "The client should have a case.")
        firstCase.tap()
        let visit = app.staticTexts["visit-when"].firstMatch
        for _ in 0..<4 where !(visit.exists && visit.isHittable) { app.swipeUp() }
        XCTAssertTrue(visit.waitForExistence(timeout: 10), "The case should list its visits.")
        settle(1)
        snap("iphone-case-visits")
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
        XCTFail("None of the guest's events has a published program — nothing to capture.")
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
