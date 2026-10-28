import XCTest

/// Scanning a room for its place, tapped for real (LiDAR plan, phase 1).
///
/// The simulator has no LiDAR, so `-scanFakeRoom` stands a fixture room in for the sensor: the
/// scanner "finds" a four-by-five-metre room with a table and a chair. Everything after Done is
/// real — the retention choice with its sizes, the `.scan` the phone packs, the upload to the API
/// the app is pointed at, and the viewer opening the scan the server now keeps for the place.
///
/// Needs an API with the place-scan routes and an account on it:
///
///     TEST_RUNNER_BEN_API_BASE_URL=http://localhost:5262
///     TEST_RUNNER_BEN_SCAN_EMAIL / TEST_RUNNER_BEN_SCAN_PASSWORD
@MainActor
final class RoomScanUITests: XCTestCase {

    nonisolated override func setUpWithError() throws {
        continueAfterFailure = false
        guard ProcessInfo.processInfo.environment["BEN_API_BASE_URL"] != nil else {
            throw XCTSkip("set TEST_RUNNER_BEN_API_BASE_URL, BEN_SCAN_EMAIL and BEN_SCAN_PASSWORD")
        }
    }

    private func launch(_ extra: [String] = ["-scanFakeRoom"]) -> XCUIApplication {
        let environment = ProcessInfo.processInfo.environment
        let app = XCUIApplication()
        app.launchArguments += extra
        app.launchArguments += ["-apiBaseURL", environment["BEN_API_BASE_URL"] ?? ""]
        let email = TestSecrets.required("BEN_SCAN_EMAIL")
        let password = TestSecrets.required("BEN_SCAN_PASSWORD")
        app.launchArguments += ["-autoSignIn", "\(email):\(password)"]
        app.launch()
        return app
    }

    /// A recording session, started, standing in a room this run has never scanned.
    private func recordingIn(_ room: String, app: XCUIApplication) {
        XCTAssertTrue(AppNavigator.openSection("Paranormal", in: app))
        AppNavigator.startNewSession(in: app)
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        app.buttons["confirm-start-session"].tap()
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 15))
        app.buttons["start-recording"].tap()

        let roomBar = app.descendants(matching: .any).matching(identifier: "room-bar").firstMatch
        XCTAssertTrue(roomBar.waitForExistence(timeout: 20))
        roomBar.tap()
        let field = app.textFields["room-name-field"]
        XCTAssertTrue(field.waitForExistence(timeout: 10))
        AppNavigator.replaceText(in: field, with: room)
        app.buttons["room-save"].tap()
        XCTAssertTrue(app.staticTexts[room].waitForExistence(timeout: 10))
    }

    /// Says which place this is, by naming it — the same place every run, which the server hands
    /// back rather than making again.
    private func chooseThePlace(_ app: XCUIApplication) {
        let choose = app.buttons["scan-choose-place-button"]
        guard choose.waitForExistence(timeout: 20) else { return }   // the session already knows its place
        choose.tap()

        let name = app.textFields["scan-place-name"]
        XCTAssertTrue(name.waitForExistence(timeout: 15), "a session with no place should ask for one first")
        name.tap()
        name.typeText("The Scan Test House")
        app.textFields["scan-place-city"].tap()
        app.textFields["scan-place-city"].typeText("Nashville")
        app.textFields["scan-place-state"].tap()
        app.textFields["scan-place-state"].typeText("TN")
        app.buttons["scan-place-save"].tap()
    }

    /// Scans the room and sends it, keeping the model and pictures, and waits for the server's word.
    private func scanAndSend(_ app: XCUIApplication) -> XCUIElement {
        let scan = app.buttons["scan-room"]
        XCTAssertTrue(scan.waitForExistence(timeout: 20), "an unscanned room should offer Scan this room")
        scan.tap()
        let done = app.buttons["scan-done"]
        XCTAssertTrue(done.waitForExistence(timeout: 15))
        expectation(for: NSPredicate(format: "isEnabled == true"), evaluatedWith: done)
        waitForExpectations(timeout: 10)
        done.tap()
        let pictures = app.buttons["scan-retention-pictures"]
        XCTAssertTrue(pictures.waitForExistence(timeout: 15))
        pictures.tap()
        let result = app.descendants(matching: .any).matching(identifier: "scan-result").firstMatch
        XCTAssertTrue(result.waitForExistence(timeout: 60), "the upload should finish and say what it became")
        return result
    }

    /// Phase 2: a second visit to a scanned room, with the chair moved and a box on the floor.
    func testComparingTheRoomFindsWhatMoved() throws {
        let app = launch()
        let room = "Compare test \(Int(Date().timeIntervalSince1970))"
        recordingIn(room, app: app)
        chooseThePlace(app)
        _ = scanAndSend(app)
        app.buttons["scan-result-done"].tap()

        let compare = app.buttons["compare-room"]
        XCTAssertTrue(compare.waitForExistence(timeout: 20), "a scanned room should offer Compare this room")
        compare.tap()

        let done = app.buttons["compare-done"]
        XCTAssertTrue(done.waitForExistence(timeout: 30), "the stand-in room is recognized at once and scanned")
        done.tap()

        // The chair, in words, and the box nobody named.
        let chair = app.buttons.matching(identifier: "scan-change-pin")
            .containing(NSPredicate(format: "label CONTAINS[c] 'chair moved'")).firstMatch
        XCTAssertTrue(chair.waitForExistence(timeout: 60), "the moved chair should be listed")
        XCTAssertTrue(app.buttons.matching(identifier: "scan-change-pin")
            .containing(NSPredicate(format: "label CONTAINS[c] 'Something is new'")).firstMatch.exists,
                      "the box on the floor should be listed")
        chair.tap()

        app.buttons["compare-view-in-room"].tap()
        XCTAssertTrue(app.descendants(matching: .any).matching(identifier: "scan-viewer").firstMatch.waitForExistence(timeout: 30))
        XCTAssertTrue(app.buttons.matching(identifier: "scan-change-pin").firstMatch.waitForExistence(timeout: 10),
                      "what changed should be pinned in the room")
        app.buttons["Close"].tap()

        // The comparison is in the session: its mark is on the timeline.
        XCTAssertTrue(app.staticTexts.containing(NSPredicate(format: "label CONTAINS[c] 'Compared'")).firstMatch
            .waitForExistence(timeout: 20), "the comparison should be marked in the session")
    }

    func testPickAPlaceScanTheRoomSendItAndOpenTheViewer() throws {
        let app = launch()
        let room = "Scan test \(Int(Date().timeIntervalSince1970))"
        recordingIn(room, app: app)
        chooseThePlace(app)

        // A room nobody has scanned offers Scan.
        let scan = app.buttons["scan-room"]
        XCTAssertTrue(scan.waitForExistence(timeout: 20), "an unscanned room should offer Scan this room")
        scan.tap()

        // The stand-in room "scans" in under a second; Done is there once something was found.
        let done = app.buttons["scan-done"]
        XCTAssertTrue(done.waitForExistence(timeout: 15))
        let enabled = NSPredicate(format: "isEnabled == true")
        expectation(for: enabled, evaluatedWith: done)
        waitForExpectations(timeout: 10)
        done.tap()

        // What to keep, each with its size.
        let pictures = app.buttons["scan-retention-pictures"]
        XCTAssertTrue(pictures.waitForExistence(timeout: 15), "after Done, the phone should ask what to keep")
        XCTAssertTrue(app.buttons["scan-retention-model"].exists)
        pictures.tap()

        // The server's own sentence about what the scan became.
        let result = app.descendants(matching: .any).matching(identifier: "scan-result").firstMatch
        XCTAssertTrue(result.waitForExistence(timeout: 60), "the upload should finish and say what it became")
        XCTAssertTrue(result.label.contains("is scanned"), "a first scan of a room becomes its scan: \(result.label)")

        app.buttons["scan-result-view"].tap()
        let viewer = app.descendants(matching: .any).matching(identifier: "scan-viewer").firstMatch
        XCTAssertTrue(viewer.waitForExistence(timeout: 30), "the scan the server keeps should open in the viewer")
        XCTAssertTrue(app.descendants(matching: .any).matching(identifier: "scan-joystick").firstMatch.exists)
        app.buttons["Close"].tap()

        // Back on the session, the room now says it is scanned and offers to view it.
        XCTAssertTrue(app.buttons["view-room-scan"].waitForExistence(timeout: 20),
                      "a scanned room should offer its scan rather than Scan again")
        XCTAssertFalse(app.buttons["scan-room"].exists)
    }

    /// Without LiDAR — and without the stand-in — there is nothing to scan with, and the card is
    /// not drawn at all: absent, not disabled.
    func testAPhoneWithoutLidarHasNoScanCard() throws {
        let app = launch([])
        recordingIn("No LiDAR \(Int(Date().timeIntervalSince1970))", app: app)
        XCTAssertFalse(app.descendants(matching: .any).matching(identifier: "scan-card").firstMatch.waitForExistence(timeout: 5))
        XCTAssertFalse(app.buttons["scan-room"].exists)
    }
}
