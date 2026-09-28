import XCTest

/// A session does not record the moment it is created (item 215).
///
/// Ben: *"They may want to set everything up first and then start."* So the live screen opens
/// PENDING: the gauge runs, the room and base level can be set, and nothing is logged until
/// Start. Stop then ends it. A session that turned out to be nothing can be discarded on the
/// spot, and the space comes straight back.
///
/// What only a real screen can show: that the bar says Start and not Stop before anything has
/// happened, that Mark is genuinely absent rather than merely disabled, and that Discard removes
/// the row from the list rather than leaving an "interrupted" corpse behind.
final class SessionStartUITests: XCTestCase {

    override func setUpWithError() throws {
        continueAfterFailure = false
    }

    /// Creates a session and lands on the live screen, still pending.
    private func pendingSession() -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors"]
        app.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        AppNavigator.startNewSession(in: app)
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        app.buttons["confirm-start-session"].tap()
        return app
    }

    func testANewSessionWaitsForStartAndOffersNothingToMark() throws {
        let app = pendingSession()

        let start = app.buttons["start-recording"]
        XCTAssertTrue(start.waitForExistence(timeout: 15),
                      "a new session should offer Start, not begin on its own")
        XCTAssertFalse(app.buttons["stop-field-session"].exists,
                       "nothing is recording yet, so there is nothing to stop")
        XCTAssertTrue(app.staticTexts["pending-hint"].exists,
                      "the screen should say what to do before Start")

        // Absent, not disabled: a mark before the clock began would belong to no moment.
        XCTAssertFalse(app.buttons["mark-now"].exists)
        XCTAssertFalse(app.buttons["open-evp"].exists)

        // The set-up controls are exactly what IS available while pending.
        XCTAssertTrue(app.buttons["set-base-level"].exists,
                      "the base level is set before Start — that is the point of the pause")
    }

    func testStartTurnsTheBarIntoStopAndBringsTheControls() throws {
        let app = pendingSession()

        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 15))
        app.buttons["start-recording"].tap()

        XCTAssertTrue(app.buttons["stop-field-session"].waitForExistence(timeout: 10),
                      "once started, the bar should offer Stop")
        XCTAssertFalse(app.buttons["start-recording"].exists)
        XCTAssertTrue(app.buttons["mark-now"].waitForExistence(timeout: 5),
                      "marking arrives with Start")

        // And it really is logging: the readings counter appears and moves off zero.
        sleep(4)
        let counter = app.staticTexts.containing(
            NSPredicate(format: "label ENDSWITH 'readings'")).firstMatch
        XCTAssertTrue(counter.exists)
        XCTAssertNotEqual(counter.label, "0 readings", "readings should be logged after Start")

        app.buttons["stop-field-session"].tap()
        XCTAssertTrue(app.buttons["open-share-menu"].waitForExistence(timeout: 25),
                      "stopping should land on the review, as before")
    }

    func testAPendingSessionCanBeDiscardedAndLeavesNothingBehind() throws {
        // Since 2026-09-27 a new session REPLACES one set up and never started (Ben: "just save it
        // and ask if they want to create a new one"), so whatever earlier tests left pending is
        // gone by the time this one opens — and after the discard there is nothing pending at all.
        // (This used to count the run's leftovers before and after; the new rule makes the stricter
        // answer the right one.)
        let app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors"]
        app.launch()
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        XCTAssertTrue(app.buttons["start-field-session"].waitForExistence(timeout: 15))

        AppNavigator.startNewSession(in: app)
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        app.buttons["confirm-start-session"].tap()
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 15))

        let shot = XCTAttachment(screenshot: app.screenshot())
        shot.name = "session-pending"; shot.lifetime = .keepAlways; add(shot)

        let discard = app.buttons["discard-session"]
        XCTAssertTrue(discard.exists, "a session that never started can be thrown away")
        discard.tap()

        // Back on the Field Kit home, and the discarded session is not in the list.
        XCTAssertTrue(app.buttons["start-field-session"].waitForExistence(timeout: 10))
        let pending = app.buttons.matching(identifier: "field-session-row")
            .matching(NSPredicate(format: "label CONTAINS 'not started'")).count
        XCTAssertEqual(pending, 0, "discarding must remove the session it was pressed on")
    }

    func testAPendingSessionIsNeverReportedAsInterrupted() throws {
        // Leave the app with a pending session, relaunch, and check the recovery path left it
        // alone: nothing was logged, so nothing was lost, so "interrupted" would be a lie.
        var app = pendingSession()
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 15))
        app.terminate()

        app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors"]
        app.launch()
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))

        XCTAssertTrue(app.staticTexts["Set up, not started"].waitForExistence(timeout: 10),
                      "a pending session should survive a relaunch as pending")
        let rows = app.buttons.matching(identifier: "field-session-row")
        XCTAssertTrue(rows.firstMatch.exists)
        XCTAssertTrue(rows.firstMatch.label.contains("not started"))
        XCTAssertFalse(rows.firstMatch.label.contains("interrupted"))
    }
}
