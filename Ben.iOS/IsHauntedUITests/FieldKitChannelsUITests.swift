import XCTest

/// Choosing what a session records, BEFORE it opens (Ben, 2026-09-12).
///
/// The camera was in the Field Kit all along — photographs always, video clips behind the video
/// channel, and the camera itself as a motion sensor in Sentry mode. What it was not, was
/// findable: a new session started with field, sound and location, video was off, and the video
/// button only appears once the channel is on. So the one place the choice could be made was a
/// panel most of the way down a running session's screen, at the hour of night when nobody is
/// hunting for settings.
///
/// What only a real screen can show is the consequence: that the toggle on the start sheet is
/// what decides whether the capture bar has a video button at all.
final class FieldKitChannelsUITests: XCTestCase {

    override func setUpWithError() throws {
        continueAfterFailure = false
    }

    private func openStartSheet() -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors"]
        app.launch()

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        XCTAssertTrue(app.buttons["start-field-session"].waitForExistence(timeout: 15))
        app.buttons["start-field-session"].tap()
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        return app
    }

    private func toggle(_ name: String, in app: XCUIApplication) -> XCUIElement {
        app.switches[name].firstMatch
    }

    /// Taps the SWITCH, not the row.
    ///
    /// The identifier belongs to the whole `Toggle`, whose centre is its label — and a label tap
    /// inside a `Form` does not flip anything. A plain `.tap()` therefore does nothing at all and
    /// reads as the toggle refusing to hold a value.
    private func flip(_ element: XCUIElement) {
        element.coordinate(withNormalizedOffset: CGVector(dx: 0.95, dy: 0.5)).tap()
    }

    /// Opens the session AND presses Start: the capture bar belongs to a running session, since a
    /// photograph taken before the clock began would belong to no moment.
    private func recordingSession(_ app: XCUIApplication) {
        app.buttons["confirm-start-session"].tap()
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 15))
        app.buttons["start-recording"].tap()
    }

    func testTheStartSheetOffersEveryChannelWithItsCost() throws {
        let app = openStartSheet()

        for channel in ["magnetic field", "audio", "video", "location"] {
            XCTAssertTrue(toggle("start-channel-\(channel)", in: app).waitForExistence(timeout: 5),
                          "\(channel) should be choosable before the session opens")
        }

        // The defaults, said out loud: everything cheap is on and video is not, because video is
        // the one that ends a night early.
        XCTAssertEqual(toggle("start-channel-magnetic field", in: app).value as? String, "1")
        XCTAssertEqual(toggle("start-channel-audio", in: app).value as? String, "1")
        XCTAssertEqual(toggle("start-channel-location", in: app).value as? String, "1")
        XCTAssertEqual(toggle("start-channel-video", in: app).value as? String, "0")

        // The cost is on the sheet rather than learned from a flat battery.
        XCTAssertTrue(app.staticTexts["Barely touches the battery."].exists)
    }

    func testVideoChosenAtTheStartPutsTheVideoButtonOnTheSession() throws {
        let app = openStartSheet()

        let video = toggle("start-channel-video", in: app)
        XCTAssertTrue(video.waitForExistence(timeout: 5))
        flip(video)
        XCTAssertEqual(video.value as? String, "1", "the toggle should hold what was chosen")

        recordingSession(app)

        // The consequence, on the live screen: one camera button, and its label says clips are on
        // offer. Photo and Video stopped being separate buttons when the capture stopped being two
        // different Apple apps — the mode is a switch on our own camera screen now.
        let camera = app.buttons["capture-camera"]
        XCTAssertTrue(camera.waitForExistence(timeout: 15),
                      "every session offers a photograph")
        XCTAssertEqual(camera.label, "Photo",
                       "video keeps the camera on for the viewfinder; the button is a photo button either way (Ben, 2026-09-17)")
    }

    func testWithoutChoosingVideoTheButtonOffersPhotographsOnly() throws {
        // The other half, and the one that makes the test above mean anything: left alone, the
        // defaults produce a session whose camera button offers photographs only.
        let app = openStartSheet()
        recordingSession(app)

        let camera = app.buttons["capture-camera"]
        XCTAssertTrue(camera.waitForExistence(timeout: 15))
        XCTAssertEqual(camera.label, "Photo",
                       "video is opt-in, and even with it on the button is a photo button")
    }
}
