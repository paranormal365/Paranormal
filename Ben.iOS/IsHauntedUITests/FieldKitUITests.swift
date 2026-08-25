import XCTest

/// Field Kit, tapped for real on whichever device is running.
///
/// The point of this suite is that a field session survives things: leaving the screen, leaving
/// the app, and relaunching. Those are not edge cases here — they are Tuesday night in a cellar.
final class FieldKitUITests: XCTestCase {

    private var app: XCUIApplication!

    override func setUp() {
        continueAfterFailure = false
        app = XCUIApplication()
        app.launch()
    }

    /// Field Kit deliberately needs no account — everything it does happens on the device.
    func testFieldKitIsReachableWithoutSigningIn() {
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app),
                      "Field Kit should be reachable on this shell")
        XCTAssertTrue(app.navigationBars["Field Kit"].waitForExistence(timeout: 20))
        XCTAssertTrue(app.buttons["start-field-session"].waitForExistence(timeout: 10),
                      "a signed-out person must still be able to start recording")
    }

    func testAStartedSessionSurvivesLeavingTheAppAndComingBack() throws {
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        app.buttons["start-field-session"].tap()

        let label = app.textFields["session-label"]
        XCTAssertTrue(label.waitForExistence(timeout: 15))
        label.tap()
        let marker = "Cellar \(Int(Date().timeIntervalSince1970))"
        label.typeText(marker)

        app.buttons["confirm-start-session"].tap()

        // Straight into the live screen — starting a session and then hunting for it would be
        // wrong in the dark.
        XCTAssertTrue(app.buttons["stop-field-session"].waitForExistence(timeout: 20),
                      "starting a session should open it")

        // Terminate WITHOUT stopping the session: the phone dying mid-session is the case that
        // matters, and the session must still be there afterwards.
        app.terminate()
        app.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        XCTAssertTrue(app.staticTexts[marker].waitForExistence(timeout: 25),
                      "a session interrupted by termination must still be listed")

        // And it is no longer claiming to be recording — the app cannot know when it stopped,
        // so it says interrupted rather than inventing an end.
        XCTAssertTrue(
            app.staticTexts.containing(
                NSPredicate(format: "label CONTAINS[c] 'interrupted'")
            ).firstMatch.waitForExistence(timeout: 15),
            "an interrupted session should say so")
    }

    func testStoppingASessionOpensItsReview() {
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        app.buttons["start-field-session"].tap()
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        app.buttons["confirm-start-session"].tap()

        let stop = app.buttons["stop-field-session"]
        XCTAssertTrue(stop.waitForExistence(timeout: 20))
        stop.tap()

        // Review is where a stopped session lands, because the next thing anybody does is look
        // at what they got.
        XCTAssertTrue(
            app.staticTexts.containing(
                NSPredicate(format: "label CONTAINS[c] 'Readings'")
            ).firstMatch.waitForExistence(timeout: 20),
            "stopping should open the session's review")
    }
}
