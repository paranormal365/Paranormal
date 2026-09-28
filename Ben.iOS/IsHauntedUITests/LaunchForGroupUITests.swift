import XCTest

/// The lead's side of item 252: Field Kit offers "Launch a session for your group" when something
/// they lead is on, the page lists it, Launch asks first, and the launch's page says who it reached
/// and shows the QR code somebody else can scan.
///
/// Needs a reachable API and an event the lead may launch that has not been launched in the last ten
/// minutes: `TEST_RUNNER_BEN_API_BASE_URL`, `TEST_RUNNER_BEN_SUPERADMIN_PASSWORD` (the seeded lead)
/// and `TEST_RUNNER_BEN_LEAD_EVENT_ID`.
final class LaunchForGroupUITests: XCTestCase {

    func testALeadLaunchesAndIsShownWhoItReachedAndTheCode() throws {
        let environment = ProcessInfo.processInfo.environment
        guard let base = environment["BEN_API_BASE_URL"], let eventId = environment["BEN_LEAD_EVENT_ID"] else {
            throw XCTSkip("set TEST_RUNNER_BEN_API_BASE_URL and TEST_RUNNER_BEN_LEAD_EVENT_ID")
        }
        continueAfterFailure = false
        let app = XCUIApplication()
        let password = TestSecrets.required("BEN_SUPERADMIN_PASSWORD")
        app.launchArguments += ["-fieldKitFakeSensors", "-apiBaseURL", base, "-autoSignIn", "haveben@msn.com:\(password)"]
        app.launch()
        sleep(5)

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        let open = app.buttons["open-launch-for-group"].firstMatch
        for _ in 0..<3 where !open.waitForExistence(timeout: 8) { app.swipeDown() }
        if !open.exists { attach(app, "lead-no-launch-offered") }
        XCTAssertTrue(open.exists, "Field Kit should offer the lead a launch")
        open.tap()

        let launch = app.buttons["launch-\(eventId.lowercased())"].firstMatch
        // A lead with a busy night has several things listed; this one may be further down.
        for _ in 0..<8 where !(launch.waitForExistence(timeout: 4) && launch.isHittable) { app.swipeUp() }
        XCTAssertTrue(launch.waitForExistence(timeout: 10), "the lead's page should list the event")
        attach(app, "lead-launch-page")
        launch.tap()

        let confirm = app.buttons["confirm-launch"].firstMatch
        XCTAssertTrue(confirm.waitForExistence(timeout: 10), "Launch should ask first")
        confirm.tap()

        XCTAssertTrue(app.staticTexts["launch-sent"].waitForExistence(timeout: 20)
                      || app.descendants(matching: .any)["launch-sent"].waitForExistence(timeout: 5),
                      "the launch's page should say who it reached")
        XCTAssertTrue(app.descendants(matching: .any)["launch-qr"].waitForExistence(timeout: 10),
                      "the launch's page should show the code to scan")
        attach(app, "lead-launched")
    }

    private func attach(_ app: XCUIApplication, _ name: String) {
        let shot = XCTAttachment(screenshot: app.screenshot())
        shot.name = name
        shot.lifetime = .keepAlways
        add(shot)
    }
}
