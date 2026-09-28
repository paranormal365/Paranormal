import XCTest

/// Joining the group's session a lead launched (item 252), the three ways a person reaches it: the
/// card in the feed, Field Kit's "Happening now", and the link a push or a QR code carries.
///
/// Ben, 2026-09-28: "Clicking the link would open the Field Kit into a session where they don't have
/// to complete picking the location before the session" — and "instead of forcing them to join". So
/// each way in must land on the live screen, set up and waiting for Start, with no New session sheet
/// on the way and nothing recording.
///
/// Needs a live launch the client was sent, on a reachable API: `TEST_RUNNER_BEN_API_BASE_URL`,
/// `TEST_RUNNER_BEN_CLIENT_EMAIL`, `TEST_RUNNER_BEN_CLIENT_PASSWORD`, and `TEST_RUNNER_BEN_LAUNCH_ID`.
final class JoinLaunchUITests: XCTestCase {

    private var app: XCUIApplication!
    private var launchId = ""

    override func setUpWithError() throws {
        continueAfterFailure = false
        let environment = ProcessInfo.processInfo.environment
        guard let base = environment["BEN_API_BASE_URL"], let launch = environment["BEN_LAUNCH_ID"] else {
            throw XCTSkip("set TEST_RUNNER_BEN_API_BASE_URL and TEST_RUNNER_BEN_LAUNCH_ID (a launch the client was sent)")
        }
        launchId = launch
        app = XCUIApplication()
        let email = environment["BEN_CLIENT_EMAIL"] ?? "daniel.park@benco.dev"
        let password = TestSecrets.required("BEN_CLIENT_PASSWORD")
        app.launchArguments += ["-fieldKitFakeSensors", "-apiBaseURL", base, "-autoSignIn", "\(email):\(password)"]
    }

    /// The live screen, waiting for Start — and never the New session sheet on the way.
    private func assertTheSessionIsReadyForStart(_ how: String) {
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 25),
                      "\(how) should open the live session, waiting for Start")
        XCTAssertFalse(app.buttons["confirm-start-session"].exists,
                       "\(how) should not ask anything — the session is already set up")
        XCTAssertFalse(app.buttons["stop-field-session"].exists, "nothing should be recording before Start")
        let shot = XCTAttachment(screenshot: app.screenshot())
        shot.name = "joined-\(how.replacingOccurrences(of: " ", with: "-"))"
        shot.lifetime = .keepAlways
        add(shot)
    }

    func testTheFeedCardJoinsTheGroupsSession() {
        app.launch()
        sleep(5)   // sign-in is a round trip; the feed decides what to draw from it
        XCTAssertTrue(AppNavigator.openSection("Feed", in: app))
        let join = app.buttons["feed-join-launch"].firstMatch
        for _ in 0..<3 where !join.waitForExistence(timeout: 8) { app.swipeDown() }   // pull to refresh
        XCTAssertTrue(join.exists, "the launch's card should offer Join")
        let feedShot = XCTAttachment(screenshot: app.screenshot())
        feedShot.name = "feed-launch-card"
        feedShot.lifetime = .keepAlways
        add(feedShot)
        join.tap()
        assertTheSessionIsReadyForStart("the feed card")
    }

    func testFieldKitListsItUnderHappeningNow() {
        app.launch()
        sleep(5)
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        let join = app.buttons["happening-join"].firstMatch
        for _ in 0..<3 where !join.waitForExistence(timeout: 8) { app.swipeDown() }
        XCTAssertTrue(join.exists, "Field Kit should list the launch under Happening now")
        let shot = XCTAttachment(screenshot: app.screenshot())
        shot.name = "field-kit-happening-now"
        shot.lifetime = .keepAlways
        add(shot)
        join.tap()
        assertTheSessionIsReadyForStart("Happening now")
    }

    func testTheLinkAPushOrQRCodeCarriesJoinsIt() {
        app.launchArguments += ["-openLink", "ishaunted://field-kit/launch/\(launchId)"]
        app.launch()
        assertTheSessionIsReadyForStart("the link")
    }
}
