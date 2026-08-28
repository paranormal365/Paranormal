import XCTest

/// Walks the app's main surfaces and attaches a screenshot of each — the App Store set.
///
/// A UI test rather than a script because the Field Kit tab has no deep link (the parser mirrors
/// the website's grammar, and the website has no /fieldkit), so reaching every screen means
/// tapping, and tapping is what this target does. Attachments are exported from the xcresult
/// with `xcrun xcresulttool export attachments`.
///
/// Skipped unless `BEN_SCREENSHOTS=1` (as `TEST_RUNNER_BEN_SCREENSHOTS` on xcodebuild — plain
/// env vars never reach the runner), so ordinary full-suite runs don't pay for it.
final class AppStoreScreenshotTests: XCTestCase {

    private var app: XCUIApplication!

    override func setUpWithError() throws {
        guard ProcessInfo.processInfo.environment["BEN_SCREENSHOTS"] == "1" else {
            throw XCTSkip("screenshot capture runs only when asked — set TEST_RUNNER_BEN_SCREENSHOTS=1")
        }
        continueAfterFailure = true   // one bad screen should not cost the rest of the set
        app = XCUIApplication()
        let email = ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "haveben@msn.com"
        let password = ProcessInfo.processInfo.environment["BEN_CLIENT_PASSWORD"] ?? "Y@ung615"
        app.launchArguments += ["-autoSignIn", "\(email):\(password)"]
        app.launch()
    }

    private func snap(_ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }

    /// Settles a screen the cheap way: give the network and the render loop a moment, rather
    /// than asserting content — this test's job is pictures, not correctness.
    private func settle(_ seconds: TimeInterval = 3) {
        _ = app.wait(for: .runningForeground, timeout: seconds)
        Thread.sleep(forTimeInterval: seconds)
    }

    func testCaptureTheSet() {
        settle(6)   // let -autoSignIn finish and land its token in the Keychain

        // Relaunch, now signed in from the Keychain — the way a real user's session starts.
        // Without this, the tab stores fetch while -autoSignIn is still in flight, cache their
        // anonymous answers, and every signed-in surface screenshots as empty.
        app.terminate()
        app.launchArguments = []   // no -autoSignIn (or its value) — don't race the restored session
        app.launch()
        settle(5)

        // 1. Whatever the app opens on (Feed tab / sidebar first item).
        snap("01-home")

        // 2. Cases — the group's working record.
        if AppNavigator.openSection("My Cases", in: app) {
            settle()
            snap("02-cases")
        }

        // 3. Investigations.
        if AppNavigator.openSection("Investigations", in: app) {
            settle()
            snap("03-investigations")
        }

        // 4. Field Kit — the flagship, and the reason this is a UI test.
        if AppNavigator.openSection("Field Kit", in: app) {
            settle()
            snap("04-field-kit")
        }

        // 5. Events. No tab on iPhone (Field Kit took the fifth slot) — the row lives on
        //    Profile there; on iPad the sidebar carries it. Try the section first, then the row.
        if AppNavigator.openSection("Events", in: app, timeout: 5) {
            settle()
            snap("05-events")
        } else if AppNavigator.openSection("Profile", in: app) {
            let row = AppNavigator.section("Public events", in: app, timeout: 10)
            row?.tap()
            settle()
            snap("05-events")
        }

        // 6. Profile — account, security, About & Privacy in one frame.
        if AppNavigator.openSection("Profile", in: app) {
            settle(2)
            snap("06-profile")
        }
    }
}
