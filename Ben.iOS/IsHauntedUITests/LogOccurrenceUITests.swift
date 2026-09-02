import XCTest

/// Tap-level proof that the screens are actually reachable and do what they claim.
///
/// BenKit's tests prove the bytes; the live suite proves the server accepts them. Neither
/// touches a button. This is the layer where a store method with no way to reach it — the
/// write-only-feature trap this codebase keeps finding — shows up as a failure.
///
/// Needs the dev API on :5252 and a client account with at least one case. Skips cleanly
/// otherwise rather than failing, in the same spirit as the opt-in live suite.
final class LogOccurrenceUITests: XCTestCase {

    private var app: XCUIApplication!

    override func setUp() {
        continueAfterFailure = false
        app = XCUIApplication()
        // Reuses the app's existing DEBUG-only `-autoSignIn` hook rather than adding a second
        // way in: one test door is auditable, two is a habit.
        let email = ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "haveben@msn.com"
        let password = TestSecrets.required("BEN_CLIENT_PASSWORD")
        app.launchArguments += ["-autoSignIn", "\(email):\(password)"]
        app.launch()
    }

    func testLoggingAnOccurrenceFromTheCaseScreen() throws {
        // Signed in by the launch argument, so the case section has something in it.
        try AppNavigator.openFirstCase(in: app)

        // THE thing under test: a store method is worthless if no button reaches it.
        let logButton = app.buttons["Log what happened"]
        XCTAssertTrue(logButton.waitForExistence(timeout: 20),
                      "an open case must offer a way to log something")
        logButton.tap()

        let title = app.textFields["What happened?"]
        XCTAssertTrue(title.waitForExistence(timeout: 10))
        title.tap()
        let marker = "UI test \(Int(Date().timeIntervalSince1970))"
        title.typeText(marker)

        app.buttons["Save"].tap()

        // The sheet closing is not proof — the entry has to come back on the timeline.
        XCTAssertTrue(app.staticTexts[marker].waitForExistence(timeout: 30),
                      "the logged entry should appear on the case timeline")
    }
}
