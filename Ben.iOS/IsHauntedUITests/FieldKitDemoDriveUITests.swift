import XCTest

/// Drives a live field session for the App Store preview video — this test is the actor, and
/// `simctl io recordVideo` (run alongside by the capture script) is the camera.
///
/// `-fieldKitFakeSensors` plays its scripted night through the real engine and screens: a quiet
/// ~48 µT room, one clear excursion past the report threshold every twenty seconds, a slow walk.
/// Twenty-six seconds of watching covers a full quiet-spike-settle cycle, which is the story the
/// preview needs a viewer to see: not a needle, a needle MOVING and the log catching it.
///
/// Skipped unless `BEN_DEMO_DRIVE=1` (via `TEST_RUNNER_BEN_DEMO_DRIVE` — plain env vars never
/// reach the runner), so suite runs never sit through half a minute of cinema.
final class FieldKitDemoDriveUITests: XCTestCase {

    func testDriveALiveSessionForTheCamera() throws {
        guard ProcessInfo.processInfo.environment["BEN_DEMO_DRIVE"] == "1" else {
            throw XCTSkip("demo drive runs only for the capture script — set TEST_RUNNER_BEN_DEMO_DRIVE=1")
        }

        let app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors"]
        app.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))

        let start = AppNavigator.section("Start a session", in: app, timeout: 15)
        XCTAssertNotNil(start, "the Field Kit home should offer a session")
        // A beat on the landing screen so the cut can open there rather than mid-tap.
        Thread.sleep(forTimeInterval: 2)
        start?.tap()

        // "Start a session" opens the naming sheet, not the session — the first cut of this
        // video was 26 seconds of that sheet, which is the sort of thing you only learn from
        // the footage. Name the room (it reads well on screen) and actually start.
        let field = app.textFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 10), "the new-session sheet should appear")
        field.tap()
        field.typeText("Cellar stairs")
        let begin = app.buttons["Start recording"].firstMatch
        XCTAssertTrue(begin.waitForExistence(timeout: 5))
        Thread.sleep(forTimeInterval: 1)
        begin.tap()

        // The needle draws only against a baseline — AnalogMeterView renders an empty dial
        // until one is set, because a delta from nothing would be a lie. So do what a real
        // investigator does: let the room settle for a beat, then set base. The scripted
        // excursion that follows swings the needle off that base, which is the entire shot.
        XCTAssertTrue(app.wait(for: .runningForeground, timeout: 5))
        Thread.sleep(forTimeInterval: 4)
        let base = app.buttons["set-base-level"].firstMatch
        XCTAssertTrue(base.waitForExistence(timeout: 10), "the live session should offer Set base")
        if !base.isHittable { app.swipeUp() }
        base.tap()

        // The swipe that reached the button left the gauge above the fold, and the first cut
        // with a baseline was 26 seconds of the controls it swung out of frame. Come back up:
        // the needle is the shot.
        app.swipeDown()
        Thread.sleep(forTimeInterval: 1)
        app.swipeDown()

        // Two full scripted cycles against the base. The test asserts almost nothing on
        // purpose: its judgement criteria are visual, applied by a person reviewing footage.
        Thread.sleep(forTimeInterval: 26)
    }
}
