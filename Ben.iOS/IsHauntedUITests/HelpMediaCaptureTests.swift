import XCTest

/// Captures the screenshots the WEBSITE's help pages embed, as distinct from the App Store set.
///
/// **A separate fixture from `AppStoreScreenshotTests` on purpose.** That one exists to satisfy
/// Apple: fixed device sizes, dark mode, no debug UI, a curated marketing sequence. These exist to
/// show somebody reading `/help/the-mobile-apps` what a screen actually looks like, so they follow
/// the documentation rather than the store listing, and they change whenever the documentation
/// does. Sharing one fixture would tie a help page's illustrations to Apple's requirements.
///
/// Skipped unless `BEN_SCREENSHOTS=1` (as `TEST_RUNNER_BEN_SCREENSHOTS=1` on the xcodebuild line),
/// like its sibling — a capture run is something you ask for, not something the suite does.
final class HelpMediaCaptureTests: XCTestCase {
    private var app: XCUIApplication!

    override func setUpWithError() throws {
        guard ProcessInfo.processInfo.environment["BEN_SCREENSHOTS"] == "1" else {
            throw XCTSkip("screenshot capture runs only when asked — set TEST_RUNNER_BEN_SCREENSHOTS=1")
        }
        continueAfterFailure = true
        app = XCUIApplication()

        let email = ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "daniel.park@benco.dev"
        let password = ProcessInfo.processInfo.environment["BEN_CLIENT_PASSWORD"] ?? "D@niel!Park2026"
        app.launchArguments += ["-autoSignIn", "\(email):\(password)"]
        app.launch()
    }

    private func snap(_ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }

    private func settle(_ seconds: TimeInterval = 3) {
        _ = app.wait(for: .runningForeground, timeout: seconds)
        Thread.sleep(forTimeInterval: seconds)
    }

    /// My evidence — the guest's own copy of what they photographed at somebody's public event.
    ///
    /// Daniel is the account on purpose: he belongs to no group and has a confirmed attendance at
    /// a past public event, so the screen shows what a ghost-walk guest actually sees rather than
    /// what an owner with every permission sees.
    func testCaptureMyEvidence() {
        settle(6)   // let -autoSignIn land its token in the Keychain

        // Relaunch signed in from the Keychain, the way a real session starts. Without this the
        // stores fetch while sign-in is still in flight and cache their anonymous answers — the
        // lesson AppStoreScreenshotTests learned by screenshotting empty surfaces.
        app.terminate()
        app.launchArguments = []
        app.launch()
        settle(5)

        XCTAssertTrue(AppNavigator.openSection("Profile", in: app),
                      "Could not reach Profile, so My evidence was never opened.")
        settle()

        let row = app.buttons["settings-my-evidence"].firstMatch
        if !row.waitForExistence(timeout: 10) {
            // Said plainly rather than captured blank: an empty picture in the help page is worse
            // than none, because nobody can tell it is wrong.
            XCTFail("The My evidence row is missing from Profile — nothing to capture.")
            return
        }

        row.tap()
        settle(4)
        snap("iphone-my-evidence")
    }
}
