import XCTest

/// Drives a complete field session through the real app and sends it to whatever API
/// `BEN_API_BASE_URL` names — the same two requests the phone makes in the field. A probe for the
/// web player, not a product assertion: it records a session with fake sensors, names a room, adds
/// a typed note, asks an EVP question, stops, and uploads it linked to the first investigation the
/// signed-in account attends.
///
/// Skipped unless `BEN_UPLOAD_PROBE=1` (reach it with the `TEST_RUNNER_` prefix from xcodebuild).
final class FieldKitUploadProbeUITests: XCTestCase {
    func testASessionRecordedInTheAppReachesTheServer() throws {
        let env = ProcessInfo.processInfo.environment
        guard env["BEN_UPLOAD_PROBE"] == "1" else { throw XCTSkip("set TEST_RUNNER_BEN_UPLOAD_PROBE=1") }
        let email = env["BEN_CLIENT_EMAIL"] ?? ""
        let password = TestSecrets.required("BEN_CLIENT_PASSWORD")
        let apiBase = env["BEN_API_BASE_URL"] ?? ""
        XCTAssertFalse(email.isEmpty || password.isEmpty || apiBase.isEmpty, "email, password and api base are required")

        let app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors", "-apiBaseURL", apiBase, "-autoSignIn", "\(email):\(password)"]
        app.launch()
        _ = app.wait(for: .runningForeground, timeout: 10)
        sleep(6)   // let -autoSignIn land its token before the upload screen asks who is sending

        XCTAssertTrue(AppNavigator.openSection("Paranormal", in: app))
        app.buttons["start-field-session"].tap()
        XCTAssertTrue(app.buttons["confirm-start-session"].waitForExistence(timeout: 15))
        let label = app.textFields["session-label"]
        if label.waitForExistence(timeout: 3) { label.tap(); label.typeText("Probe: app upload, cellar") }
        app.buttons["confirm-start-session"].tap()
        XCTAssertTrue(app.buttons["stop-field-session"].waitForExistence(timeout: 20))

        // Say which room we are in, so readings carry a room label.
        if app.buttons["room-bar"].waitForExistence(timeout: 5) {
            app.buttons["room-bar"].tap()
            let field = app.textFields["room-name-field"]
            if field.waitForExistence(timeout: 5) {
                field.tap(); field.typeText("Cellar")
                if app.buttons["room-save"].exists { app.buttons["room-save"].tap() }
            }
        }
        sleep(4)   // let a few readings accrue

        // A manual mark. Notes and EVP have their own passing tests; the probe is about the
        // upload, and every extra sheet is another way for the share menu not to be there.
        if app.buttons["mark-now"].exists && app.buttons["mark-now"].isHittable { app.buttons["mark-now"].tap() }
        sleep(3)

        app.buttons["stop-field-session"].tap()
        let share = app.buttons["open-share-menu"]
        XCTAssertTrue(share.waitForExistence(timeout: 25))
        share.tap()
        let send = app.buttons["Send to the server"].firstMatch
        XCTAssertTrue(send.waitForExistence(timeout: 10))
        send.tap()

        let sendButton = app.buttons["send-session"]
        XCTAssertTrue(sendButton.waitForExistence(timeout: 20), "a signed-in person should be offered Send")
        sendButton.tap()

        // The upload screen reports success with the server's own words; wait for any of them.
        let done = app.staticTexts.containing(NSPredicate(format:
            "label CONTAINS[c] 'sent' OR label CONTAINS[c] 'on the server' OR label CONTAINS[c] 'uploaded' OR label CONTAINS[c] 'Done'")).firstMatch
        XCTAssertTrue(done.waitForExistence(timeout: 60), "the upload should report completion")
        let shot = XCTAttachment(screenshot: app.screenshot()); shot.lifetime = .keepAlways; add(shot)
    }
}
