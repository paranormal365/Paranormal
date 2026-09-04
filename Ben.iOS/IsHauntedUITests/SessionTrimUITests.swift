import XCTest

/// Choosing what of a session to send, on the phone, before anything is uploaded (item 210).
///
/// **Why this needs a real screen.** The trimmer's arithmetic is covered by `SessionTrimRangeTests`
/// and `SessionTrimTests` in BenKit, which run in a second and without a simulator. What those
/// cannot show is whether the handles are on screen, whether they can be dragged, and whether the
/// counts move when they are — and Ben asked specifically for a video-trimmer's feel, which is a
/// thing only a real drag can demonstrate.
final class SessionTrimUITests: XCTestCase {

    override func setUpWithError() throws {
        continueAfterFailure = false
        // The trimmer lives on the signed-in half of the Send screen, because there is nothing to
        // trim for until there is an account to send under. So these need a reachable API and an
        // account on it, and say so rather than passing vacuously without one.
        guard ProcessInfo.processInfo.environment["BEN_API_BASE_URL"] != nil else {
            throw XCTSkip("set TEST_RUNNER_BEN_API_BASE_URL, BEN_CLIENT_EMAIL and BEN_CLIENT_PASSWORD")
        }
    }

    /// Records a short session with fake sensors and opens the Send screen, signed in.
    private func sendScreen() -> XCUIApplication {
        let environment = ProcessInfo.processInfo.environment
        let app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors"]
        if let base = environment["BEN_API_BASE_URL"] {
            app.launchArguments += ["-apiBaseURL", base]
        }
        if let email = environment["BEN_CLIENT_EMAIL"],
           let password = environment["BEN_CLIENT_PASSWORD"] {
            app.launchArguments += ["-autoSignIn", "\(email):\(password)"]
        }
        app.launch()

        // Sign-in is a network round trip, and the Send screen decides which half to draw from
        // whether it has finished. Racing it is how this test would flake.
        sleep(5)

        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        app.buttons["start-field-session"].tap()
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        app.buttons["confirm-start-session"].tap()

        // Long enough that the track has a span worth dragging across. A two-second session
        // would pass every assertion below and prove nothing about a trimmer.
        XCTAssertTrue(app.buttons["stop-field-session"].waitForExistence(timeout: 20))
        sleep(6)
        app.buttons["stop-field-session"].tap()

        let share = app.buttons["open-share-menu"]
        XCTAssertTrue(share.waitForExistence(timeout: 25))
        share.tap()

        let send = app.buttons["Send to the server"].firstMatch
        XCTAssertTrue(send.waitForExistence(timeout: 10))
        send.tap()

        return app
    }

    func testTheTrimmerIsOnTheSendScreenWithTheWholeSessionSelected() throws {
        let app = sendScreen()

        let track = app.otherElements["trim-track"]
        XCTAssertTrue(track.waitForExistence(timeout: 20),
                      "the send screen should offer a trimmer before anything is uploaded")

        // It opens with everything selected. A trimmer that opened with a guess at the
        // interesting part would be guessing about evidence.
        let kept = app.staticTexts["trim-kept-duration"]
        XCTAssertTrue(kept.waitForExistence(timeout: 5))

        let whole = app.staticTexts.containing(
            NSPredicate(format: "label CONTAINS[c] 'the whole session'")).firstMatch
        XCTAssertTrue(whole.exists, "nothing should be trimmed until a handle is moved")

        // Ben's spec: a green dot to scroll for the start, a red one for the end.
        XCTAssertTrue(app.buttons["trim-handle-in"].exists, "the in point should be draggable")
        XCTAssertTrue(app.buttons["trim-handle-out"].exists, "the out point should be draggable")

        let shot = XCTAttachment(screenshot: app.screenshot())
        shot.name = "session-trimmer"
        shot.lifetime = .keepAlways
        add(shot)
    }

    func testDraggingTheInPointShortensWhatWillBeSent() throws {
        let app = sendScreen()

        let track = app.otherElements["trim-track"]
        XCTAssertTrue(track.waitForExistence(timeout: 20))

        let before = app.staticTexts["trim-kept-duration"].firstMatch.label

        // Drag the green dot towards the middle of the track.
        let handle = app.buttons["trim-handle-in"]
        XCTAssertTrue(handle.exists)
        // Coordinate to coordinate: press(forDuration:thenDragTo:) takes an ELEMENT, and there is
        // no element at the middle of a track to drag onto.
        handle.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5))
            .press(forDuration: 0.1,
                   thenDragTo: track.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5)))

        let summary = app.otherElements["trim-summary"]
        XCTAssertTrue(summary.waitForExistence(timeout: 5),
                      "moving a handle should say what will now be sent")

        // The kept span must actually have changed. A handle that moves visually while the
        // numbers stand still is the failure worth catching here.
        let after = app.staticTexts["trim-kept-duration"].firstMatch.label
        XCTAssertNotEqual(before, after, "dragging the in point should shorten what is sent")

        // And it must have landed near where the finger went — the middle of the track. The
        // first version of the slider double-counted the handle's own offset, so a drag to the
        // middle of a 9 s session put the in point at 0:08 and a drag of the out point could not
        // move it at all. "It changed" passed that; "it is roughly halfway" would not have.
        let inLabel = app.staticTexts.matching(identifier: "trim-in-point").allElementsBoundByIndex
            .map { $0.label }.first { $0.contains(":") } ?? ""
        let seconds = Self.seconds(inLabel)
        XCTAssertTrue((2...7).contains(seconds),
                      "a drag to the middle of the track should land the in point mid-session, "
                    + "not at \(inLabel)")

        let shot = XCTAttachment(screenshot: app.screenshot())
        shot.name = "session-trimmer-dragged"
        shot.lifetime = .keepAlways
        add(shot)
    }

    /// "m:ss" or "h:mm:ss" to seconds.
    private static func seconds(_ clock: String) -> Int {
        let parts = clock.split(separator: ":").compactMap { Int($0) }
        return parts.reversed().enumerated().reduce(0) { $0 + $1.element * Int(pow(60.0, Double($1.offset))) }
    }

    func testTheWholeSessionCanBePutBack() throws {
        let app = sendScreen()

        let track = app.otherElements["trim-track"]
        XCTAssertTrue(track.waitForExistence(timeout: 20))

        app.buttons["trim-handle-out"]
            .coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.5))
            .press(forDuration: 0.1,
                   thenDragTo: track.coordinate(withNormalizedOffset: CGVector(dx: 0.35, dy: 0.5)))

        let reset = app.buttons["trim-reset"]
        XCTAssertTrue(reset.waitForExistence(timeout: 5),
                      "a trimmed session should offer to go back to the whole thing")
        reset.tap()

        let whole = app.staticTexts.containing(
            NSPredicate(format: "label CONTAINS[c] 'the whole session'")).firstMatch
        XCTAssertTrue(whole.waitForExistence(timeout: 5),
                      "resetting should put both handles back at the ends")
    }
}
