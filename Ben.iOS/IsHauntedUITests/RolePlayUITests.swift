import XCTest

/// Item 252, played as the people would play it — one step per test, each run on its own phone by
/// an orchestration script, alongside the website (Playwright) and the API as the lead's other side.
///
/// The guide/host is on the iPad, the guest on one iPhone, a walk-up on another. Every step signs in
/// as the person named by `TEST_RUNNER_BEN_RP_EMAIL`, with the password in the environment variable
/// named by `TEST_RUNNER_BEN_RP_PASSWORD_VAR`, and is skipped unless the orchestrator sets
/// `TEST_RUNNER_BEN_RP=1`.
final class RolePlayUITests: XCTestCase {

    private var env: [String: String] { ProcessInfo.processInfo.environment }

    private func app(openLink: String? = nil) throws -> XCUIApplication {
        guard env["BEN_RP"] == "1", let base = env["BEN_API_BASE_URL"], let email = env["BEN_RP_EMAIL"],
              let passwordVariable = env["BEN_RP_PASSWORD_VAR"] else {
            throw XCTSkip("run by the role-play orchestrator (TEST_RUNNER_BEN_RP=1)")
        }
        continueAfterFailure = false
        let app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors", "-apiBaseURL", base,
                                "-autoSignIn", "\(email):\(TestSecrets.required(passwordVariable))"]
        if let openLink { app.launchArguments += ["-openLink", openLink] }
        return app
    }

    private var springboard: XCUIApplication { XCUIApplication(bundleIdentifier: "com.apple.springboard") }

    private func allowNotificationsIfAsked(timeout: TimeInterval = 10) {
        let allow = springboard.buttons["Allow"]
        if allow.waitForExistence(timeout: timeout) { allow.tap() }
    }

    private func snap(_ app: XCUIApplication?, _ name: String) {
        let shot = XCTAttachment(screenshot: app?.screenshot() ?? XCUIScreen.main.screenshot())
        shot.name = name
        shot.lifetime = .keepAlways
        add(shot)
    }

    /// Waits for the send to have landed: the button becomes "Send anything still waiting". A refusal
    /// (not let in, not registered) leaves it as "Send", and fails here rather than passing quietly.
    private func assertSent(_ app: XCUIApplication, _ label: String) {
        let sent = app.buttons.matching(NSPredicate(format: "label CONTAINS[c] %@", "still waiting")).firstMatch
        XCTAssertTrue(sent.waitForExistence(timeout: 60), "\(label): the session should have been sent")
    }

    /// Records a few seconds in the session already open, stops, and sends it — to the group's night.
    private func recordAndSend(_ app: XCUIApplication, seconds: UInt32 = 6, label: String) {
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 30), "\(label): the session should be open, waiting for Start")
        XCTAssertFalse(app.buttons["confirm-start-session"].exists, "\(label): nothing should have had to be chosen")
        snap(app, "\(label)-joined")
        app.buttons["start-recording"].tap()
        XCTAssertTrue(app.buttons["stop-field-session"].waitForExistence(timeout: 20))
        sleep(seconds)
        app.buttons["stop-field-session"].tap()
        let share = app.buttons["open-share-menu"]
        XCTAssertTrue(share.waitForExistence(timeout: 25))
        share.tap()
        let toServer = app.buttons["Send to the server"].firstMatch
        XCTAssertTrue(toServer.waitForExistence(timeout: 10))
        toServer.tap()
        let destination = app.switches["upload-send-to-event"].firstMatch
        XCTAssertTrue(destination.waitForExistence(timeout: 20), "\(label): the send screen should name the group's night")
        snap(app, "\(label)-send")
        let send = app.buttons["send-session"].firstMatch
        for _ in 0..<5 where !(send.exists && send.isHittable) { app.swipeUp() }
        send.tap()
        assertSent(app, label)
        snap(app, "\(label)-sent")
    }

    // ── The guest ────────────────────────────────────────────────────────────

    /// The guest opens their tour date (its seat now approved): the seat reminders ask for
    /// notifications, and allowing them registers the phone for the guide's launch too.
    func testGuestOpensTheirSeatAndAllowsNotifications() throws {
        let app = try app(openLink: "ishaunted://events/\(env["BEN_RP_TOUR_DATE"] ?? "")")
        app.launch()
        allowNotificationsIfAsked(timeout: 25)
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS[c] %@", "reserved")).firstMatch.waitForExistence(timeout: 20)
                      || app.staticTexts.containing(NSPredicate(format: "label CONTAINS[c] %@", "seat")).firstMatch.exists,
                      "the guest's tour date should show their seat")
        snap(app, "guest-seat")
        sleep(8)   // Apple hands over the token; the app gives it to the API
    }

    /// The guest is out of the app when the guide launches: the notification arrives, and a tap
    /// joins — then they record and send.
    func testGuestIsNotifiedJoinsRecordsAndSends() throws {
        let app = try app()
        app.launch()
        sleep(5)
        XCUIDevice.shared.press(.home)
        let expect = env["BEN_RP_EXPECT"] ?? "is starting"
        let banner = springboard.descendants(matching: .any)
            .matching(NSPredicate(format: "label CONTAINS[c] %@", expect)).firstMatch
        XCTAssertTrue(banner.waitForExistence(timeout: 180), "the guide's launch should reach the guest's phone")
        snap(nil, "guest-notified")
        banner.tap()
        recordAndSend(app, label: "guest-tour")
    }

    /// The guest who missed the notification: the card in the feed, Join, record, send.
    func testGuestJoinsFromTheFeedCardRecordsAndSends() throws {
        let app = try app()
        app.launch()
        sleep(5)
        XCTAssertTrue(AppNavigator.openSection("Feed", in: app))
        let title = env["BEN_RP_EXPECT"] ?? ""
        let card = app.staticTexts.containing(NSPredicate(format: "label CONTAINS[c] %@", title)).firstMatch
        for _ in 0..<4 where !card.waitForExistence(timeout: 6) { app.swipeDown() }
        snap(app, "guest-feed-card")
        let join = app.buttons["feed-join-launch"].firstMatch
        XCTAssertTrue(join.waitForExistence(timeout: 10), "the launch's card should offer Join")
        join.tap()
        recordAndSend(app, label: "guest-event")
    }

    // ── The walk-up ──────────────────────────────────────────────────────────

    /// At a PUBLIC tour, somebody who never registered scans the guide's code and is straight in —
    /// a public launch is open to anybody, from the card or the code — records, and sends.
    func testWalkUpAtAPublicTourIsStraightInRecordsAndSends() throws {
        let app = try app(openLink: "https://ishaunted.com/field-kit/join/\(env["BEN_RP_JOIN_TOKEN"] ?? "")")
        app.launch()
        XCTAssertFalse(app.buttons["join-code-ask"].waitForExistence(timeout: 8), "a public night asks nobody's leave")
        recordAndSend(app, label: "walkup-public")
    }

    /// At a PRIVATE night, somebody who never registered scans the guide's code: signs in, asks,
    /// allows notifications, and records while the guide decides.
    func testWalkUpScansAsksAndRecordsWhileWaiting() throws {
        let app = try app(openLink: "https://ishaunted.com/field-kit/join/\(env["BEN_RP_JOIN_TOKEN"] ?? "")")
        app.launch()
        let ask = app.buttons["join-code-ask"].firstMatch
        XCTAssertTrue(ask.waitForExistence(timeout: 30), "somebody not registered should be offered Ask to join")
        snap(app, "walkup-ask")
        ask.tap()
        allowNotificationsIfAsked()
        XCTAssertTrue(app.descendants(matching: .any)["join-code-pending"].waitForExistence(timeout: 15))
        snap(app, "walkup-waiting")
        app.buttons["join-code-record"].tap()
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 20))
        app.buttons["start-recording"].tap()
        XCTAssertTrue(app.buttons["stop-field-session"].waitForExistence(timeout: 20))
        sleep(6)
        app.buttons["stop-field-session"].tap()
        XCTAssertTrue(app.buttons["open-share-menu"].waitForExistence(timeout: 25))
        sleep(8)   // the token reaches the API, so the guide's yes can be heard
    }

    /// Let in: the session recorded while waiting is sent to the night.
    func testWalkUpIsLetInAndSendsWhatTheyRecorded() throws {
        let app = try app()
        app.launch()
        sleep(5)
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        let row = app.buttons["field-session-row"].firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 20))
        row.tap()
        let share = app.buttons["open-share-menu"]
        XCTAssertTrue(share.waitForExistence(timeout: 25))
        share.tap()
        app.buttons["Send to the server"].firstMatch.tap()
        let destination = app.switches["upload-send-to-event"].firstMatch
        XCTAssertTrue(destination.waitForExistence(timeout: 20), "the waiting session should go to the night once let in")
        snap(app, "walkup-private-send")
        let send = app.buttons["send-session"].firstMatch
        for _ in 0..<5 where !(send.exists && send.isHittable) { app.swipeUp() }
        send.tap()
        assertSent(app, "walkup-private")
        snap(app, "walkup-sent")
    }

    // ── The guide / host ─────────────────────────────────────────────────────

    /// The lead launches the thing named by `TEST_RUNNER_BEN_RP_TARGET` from Field Kit.
    func testLeadLaunches() throws {
        let app = try app()
        app.launch()
        sleep(6)
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        let open = app.buttons["open-launch-for-group"].firstMatch
        for _ in 0..<3 where !open.waitForExistence(timeout: 8) { app.swipeDown() }
        XCTAssertTrue(open.exists, "Field Kit should offer the lead a launch")
        open.tap()
        let launch = app.buttons["launch-\((env["BEN_RP_TARGET"] ?? "").lowercased())"].firstMatch
        for _ in 0..<8 where !(launch.waitForExistence(timeout: 4) && launch.isHittable) { app.swipeUp() }
        XCTAssertTrue(launch.exists, "the lead's page should list it")
        snap(app, "lead-list")
        launch.tap()
        let confirm = app.buttons["confirm-launch"].firstMatch
        XCTAssertTrue(confirm.waitForExistence(timeout: 10))
        confirm.tap()
        XCTAssertTrue(app.descendants(matching: .any)["launch-sent"].waitForExistence(timeout: 20), "the lead should be told who it reached")
        snap(app, "lead-launched")
    }

    /// The guide lets the walk-up in from the launch's page — opened from their "wants to join"
    /// notification's link.
    func testLeadLetsTheWalkUpIn() throws {
        let app = try app(openLink: "ishaunted://field-kit/launch/\(env["BEN_RP_LAUNCH"] ?? "")/requests")
        app.launch()
        // The lead's page asks for notifications on a fresh install, and the prompt sits over the
        // very list being photographed (10/05/2026).
        allowNotificationsIfAsked(timeout: 8)
        let approve = app.buttons["approve-\((env["BEN_RP_WALKUP_ID"] ?? "").lowercased())"].firstMatch
        for _ in 0..<6 where !(approve.waitForExistence(timeout: 6) && approve.isHittable) { app.swipeUp() }
        XCTAssertTrue(approve.exists, "the guide should see the walk-up asking")
        snap(app, "lead-asking")
        approve.tap()
        XCTAssertTrue(app.staticTexts["Let in"].waitForExistence(timeout: 15))
        snap(app, "lead-let-in")
    }
}
