import XCTest

/// The person map, tapped for real (LiDAR plan, phase 4).
///
/// The simulator has no camera and no LiDAR, so `-figureFake` stands the stand-in room in for both:
/// a person walks across it a second after the person map starts, and a person-sized box the
/// room's scan does not have stands in it at seven and a half. Everything after the camera — the
/// video written from its frames, the joints file, the marks, the replay and the copy with the
/// figure drawn — runs as it does on a phone. Field Kit needs no account, so neither does this.
@MainActor
final class PersonMapUITests: XCTestCase {

    nonisolated override func setUpWithError() throws {
        continueAfterFailure = false
    }

    private func recordWithVideo(_ app: XCUIApplication) {
        XCTAssertTrue(AppNavigator.openSection("Paranormal", in: app))
        AppNavigator.startNewSession(in: app)
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        app.buttons["confirm-start-session"].tap()
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 15))
        app.buttons["start-recording"].tap()

        let videoSwitch = app.switches["channel-video"]
        XCTAssertTrue(videoSwitch.waitForExistence(timeout: 20))
        videoSwitch.tap()
        XCTAssertTrue(app.otherElements["camera-preview"].waitForExistence(timeout: 15),
                      "switching video on shows the viewfinder")
    }

    /// Turns a switch on. A plain tap first, which scrolls it into view; if that landed on its long
    /// label rather than the switch, then the switch itself, at the right-hand end.
    private func turnOn(_ toggle: XCUIElement, in app: XCUIApplication) {
        let on = NSPredicate(format: "value == '1'")
        toggle.tap()
        if XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: on, object: toggle)], timeout: 2) != .completed {
            toggle.coordinate(withNormalizedOffset: CGVector(dx: 0.93, dy: 0.5)).tap()
        }
        XCTAssertEqual(XCTWaiter.wait(for: [XCTNSPredicateExpectation(predicate: on, object: toggle)], timeout: 5),
                       .completed, "the switch turned on")
    }

    func testTheFigureIsDrawnKeptWithTheVideoAndExportedWithItDrawn() throws {
        let app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors", "-figureFake"]
        app.launch()
        recordWithVideo(app)

        let toggle = app.switches["figure-overlay"]
        XCTAssertTrue(toggle.waitForExistence(timeout: 10), "a LiDAR phone offers the person map")
        turnOn(toggle, in: app)

        XCTAssertTrue(app.otherElements["person-map-figure"].waitForExistence(timeout: 20),
                      "the person walking through is drawn as a figure over the camera")
        XCTAssertTrue(app.otherElements["person-map-shape"].waitForExistence(timeout: 20),
                      "something person-sized the room's scan doesn't have is outlined")

        // Stopping files the video, the joints and the marks, and opens the review.
        let stop = app.buttons["stop-field-session"]
        XCTAssertTrue(stop.waitForExistence(timeout: 10))
        stop.tap()
        XCTAssertTrue(app.buttons["replay-play"].waitForExistence(timeout: 30), "a finished session opens its review")

        let shape = app.buttons.matching(NSPredicate(format: "identifier == 'replay-marker-row' AND label CONTAINS 'Unexplained shape'")).firstMatch
        XCTAssertTrue(shape.waitForExistence(timeout: 15), "the shape is a mark on the session")
        let figure = app.buttons.matching(NSPredicate(format: "identifier == 'replay-marker-row' AND label CONTAINS 'Figure seen'")).firstMatch
        XCTAssertTrue(figure.waitForExistence(timeout: 15), "the figure is a mark on the session")
        figure.tap()

        // A mark parks the replay three seconds before it, so what led up to it is heard; played on
        // from there, the video comes up with the figure drawn over it.
        let play = app.buttons["replay-play"]
        play.tap()
        XCTAssertTrue(app.descendants(matching: .any).matching(identifier: "replay-figure").firstMatch
                        .waitForExistence(timeout: 15),
                      "the replay draws the figure from the joints kept with the video")
        play.tap()
        XCTAssertTrue(app.switches["replay-figure-overlay"].exists, "and it can be hidden")

        let export = app.buttons["export-with-figure"]
        XCTAssertTrue(export.waitForExistence(timeout: 10))
        export.tap()
        XCTAssertTrue(app.descendants(matching: .any).matching(identifier: "export-with-figure-share").firstMatch
                        .waitForExistence(timeout: 60),
                      "a copy with the figure drawn in is made, ready to send")
    }

    /// The plan's rule: a phone without LiDAR has no person map — the switch is absent, not
    /// disabled.
    func testAPhoneWithoutLidarHasNoPersonMapSwitch() throws {
        let app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors"]
        app.launch()
        recordWithVideo(app)

        XCTAssertTrue(app.switches["watch-for-motion"].waitForExistence(timeout: 10),
                      "the viewfinder's own switches are there")
        XCTAssertFalse(app.descendants(matching: .any).matching(identifier: "figure-overlay").firstMatch.exists,
                       "no person map on a phone that cannot do it")
    }
}
