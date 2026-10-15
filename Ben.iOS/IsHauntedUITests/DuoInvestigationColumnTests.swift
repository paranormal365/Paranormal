import XCTest

/// The iPhone Duo opened: the ☰ menu and the investigation's controls down the side column
/// (Ben, 10/15/2026). Drives a recorded session through every control in the column and attaches a
/// frame at each step.
///
/// Run on the iPhone Duo simulator, opened (Device Hub → fully open), with
/// `TEST_RUNNER_BEN_SCREENSHOTS=1`. Uses the scripted sensors, so the gauge moves and no sign-in or
/// server is needed. Skipped on any screen where the column is not showing.
final class DuoInvestigationColumnTests: XCTestCase {

    private var app: XCUIApplication!

    override func setUpWithError() throws {
        guard ProcessInfo.processInfo.environment["BEN_SCREENSHOTS"] == "1" else {
            throw XCTSkip("capture runs only when asked — set TEST_RUNNER_BEN_SCREENSHOTS=1")
        }
        continueAfterFailure = true
        app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors"]
        let orientation = ProcessInfo.processInfo.environment["BEN_DUO_ORIENTATION"] ?? "landscape"
        XCUIDevice.shared.orientation = orientation == "portrait" ? .portrait : .landscapeLeft
        app.launch()
    }

    private func snap(_ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        let orientation = ProcessInfo.processInfo.environment["BEN_DUO_ORIENTATION"] ?? "landscape"
        attachment.name = "\(name)-\(orientation)"
        attachment.lifetime = .keepAlways
        add(attachment)
    }

    private func settle(_ seconds: TimeInterval) {
        _ = app.wait(for: .runningForeground, timeout: seconds)
        Thread.sleep(forTimeInterval: seconds)
    }

    private func tapIfThere(_ element: XCUIElement, timeout: TimeInterval = 5) -> Bool {
        guard element.waitForExistence(timeout: timeout) else { return false }
        element.tap()
        return true
    }

    func testTheInvestigationColumn() throws {
        settle(3)
        snap("duo-01-feed-menu-closed")

        let menu = app.buttons["shell-menu"].firstMatch
        guard menu.waitForExistence(timeout: 10) else {
            throw XCTSkip("no ☰ menu here — run on the iPhone Duo, opened")
        }
        menu.tap()
        settle(1)
        snap("duo-02-menu-open")

        XCTAssertTrue(AppNavigator.openSection("Paranormal", in: app))
        settle(2)
        snap("duo-03-paranormal")

        // ── A session, recording ──────────────────────────────────────────
        AppNavigator.startNewSession(in: app)
        let field = app.textFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 10))
        field.tap()
        field.typeText("Cellar stairs\n")
        // The keyboard covers "Open the session" on the open Duo; scroll the sheet to it.
        // The list builds the button only once it scrolls into view.
        let open = app.buttons["confirm-start-session"].firstMatch
        for _ in 0..<4 where !(open.exists && open.isHittable) {
            app.staticTexts["What to record"].firstMatch.swipeUp()
            settle(0.6)
        }
        XCTAssertTrue(open.waitForExistence(timeout: 3))
        open.tap()
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 15))
        settle(4)
        snap("duo-04-set-up")
        app.buttons["start-recording"].tap()
        XCTAssertTrue(app.buttons["stop-field-session"].waitForExistence(timeout: 10))
        settle(4)
        snap("duo-05-recording")

        // ── The column, top to bottom ─────────────────────────────────────
        XCTAssertTrue(tapIfThere(app.buttons["rail-mark"].firstMatch), "Mark should be in the column")
        XCTAssertTrue(app.descendants(matching: .any)["rail-notice"].waitForExistence(timeout: 2),
                      "a mark should say it happened")
        snap("duo-06-marked")
        settle(2)

        XCTAssertTrue(tapIfThere(app.buttons["rail-experience"].firstMatch), "Experience should be in the column")
        settle(1)
        snap("duo-07-experience-list")
        _ = tapIfThere(app.buttons["Cold spot"].firstMatch, timeout: 3)
        settle(0.8)
        snap("duo-08-experience-logged")
        settle(2)

        let ask = app.buttons["rail-ask"].firstMatch
        XCTAssertTrue(tapIfThere(ask), "Ask should be in the column")
        settle(3)
        snap("duo-09-asking")
        XCTAssertTrue(app.descendants(matching: .any)["call-and-response"].exists)
        ask.tap()
        settle(5)
        snap("duo-10-listening")
        XCTAssertTrue(app.staticTexts["Listening for an answer"].waitForExistence(timeout: 5),
                      "the second press should start listening")
        ask.tap()
        settle(3)
        snap("duo-11-next-question")
        ask.tap()
        settle(4)
        snap("duo-12-listening-again")
        XCTAssertTrue(tapIfThere(app.buttons["done-listening"].firstMatch), "Done listening should be offered")
        settle(2)
        snap("duo-12b-stopped-listening")
        XCTAssertFalse(app.descendants(matching: .any)["call-and-response"].exists,
                       "the strip should go once listening is done")

        XCTAssertTrue(tapIfThere(app.buttons["rail-room"].firstMatch), "Room should be in the column")
        settle(1)
        snap("duo-13-room")
        let roomField = app.textFields["room-name-field"].firstMatch
        if roomField.waitForExistence(timeout: 4) {
            roomField.tap(); roomField.typeText("Cellar")
            _ = tapIfThere(app.buttons["room-save"].firstMatch, timeout: 2)
        }
        settle(2)

        XCTAssertTrue(tapIfThere(app.buttons["rail-base"].firstMatch), "Base should be in the column")
        settle(3)
        snap("duo-14-base-reset")

        XCTAssertTrue(tapIfThere(app.buttons["rail-note"].firstMatch), "Note should be in the column")
        settle(2)
        snap("duo-15-note")
        _ = tapIfThere(app.buttons["Cancel"].firstMatch, timeout: 2)
        settle(2)

        // The scripted excursion swings the needle every 20 s: a frame with the gauge moving.
        settle(12)
        snap("duo-16-gauge-moving")
    }
}
