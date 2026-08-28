import XCTest

/// The privacy statement has to be reachable by somebody with no account.
///
/// This is an App Review test as much as a product one. A reviewer works through the build
/// signed out for as long as they can, and "where does this app say what it does with my data"
/// has to be answerable from that state — an in-app privacy statement behind a sign-in wall is
/// a routine rejection under Guideline 5.1.1, and so is one that is only a link.
///
/// Deliberately launched with NO auto-sign-in, unlike every other suite here. That is the
/// condition being tested: put the About row back inside the `if let me = session.me` branch and
/// this fails, while every signed-in test keeps passing.
final class AboutPrivacyUITests: XCTestCase {

    private var app: XCUIApplication!

    override func setUp() {
        continueAfterFailure = false
        app = XCUIApplication()
        // Point the app at an API built from the working tree when one is supplied. Without it
        // the app uses whatever host the Dev environment names, which is fine for screens that
        // do not need a new endpoint and useless for the ones that do.
        if let apiBase = ProcessInfo.processInfo.environment["BEN_API_BASE_URL"], !apiBase.isEmpty {
            app.launchArguments += ["-apiBaseURL", apiBase]
        }
        app.launch()   // signed out on purpose — see the note above
    }

    func testAboutAndPrivacyIsReachableWithoutAnAccount() {
        XCTAssertTrue(AppNavigator.openSection("Profile", in: app),
                      "Profile should be reachable signed out")

        let row = app.buttons["settings-about"].firstMatch
        let cell = app.cells["settings-about"].firstMatch
        if row.waitForExistence(timeout: 20) {
            row.tap()
        } else if cell.waitForExistence(timeout: 5) {
            cell.tap()
        } else {
            XCTFail("the About & Privacy row should be on the Profile screen when signed out")
            return
        }

        XCTAssertTrue(app.navigationBars["About & Privacy"].waitForExistence(timeout: 20),
                      "About & Privacy should open")

        // The claim itself, not just the screen. Apple's reviewer reads this sentence, and so
        // does anybody who went looking for it — a title over an empty body would pass a test
        // that only checked navigation.
        //
        // Scrolling is not optional here: a SwiftUI List builds its rows lazily, so the Privacy
        // section simply does not exist in the hierarchy until it comes near the screen. The
        // first version of this test asserted without scrolling and failed while the screen was
        // perfectly correct — which is the failure mode worth naming rather than deleting.
        var sawTheStatement = false
        var sawTheLink = false
        for _ in 0..<8 {
            var labels: [String] = []
            for element in app.staticTexts.allElementsBoundByIndex { labels.append(element.label) }
            sawTheStatement = sawTheStatement
                           || labels.contains { $0.contains("We collect nothing about you") }
            sawTheLink = sawTheLink
                      || app.buttons["about-privacy-policy"].exists
                      || app.links["about-privacy-policy"].exists
                      || labels.contains { $0.contains("Read the full privacy policy") }
            if sawTheStatement && sawTheLink { break }
            app.swipeUp()
        }

        XCTAssertTrue(sawTheStatement,
                      "the screen should state plainly that nothing is collected beyond what you give")
        XCTAssertTrue(sawTheLink, "the full policy at ishaunted.com/privacy should be linked")
    }

    /// What the app is and who it is for — the other half of what a reviewer looks for, and the
    /// answer to the question a person asks before they will grant a microphone permission.
    func testAboutSaysWhatTheAppIsAndWhoItIsFor() {
        XCTAssertTrue(AppNavigator.openSection("Profile", in: app))
        let row = AppNavigator.section("About & Privacy", in: app, timeout: 20)
        XCTAssertNotNil(row, "About & Privacy should be reachable")
        row?.tap()

        XCTAssertTrue(app.navigationBars["About & Privacy"].waitForExistence(timeout: 20))

        let deadline = Date().addingTimeInterval(20)
        var sawWhatItIs = false, sawWhoItIsFor = false
        repeat {
            var labels: [String] = []
            for element in app.staticTexts.allElementsBoundByIndex { labels.append(element.label) }
            sawWhatItIs = labels.contains { $0.contains("field companion for paranormal investigators") }
            sawWhoItIsFor = labels.contains { $0.contains("built for the people who do this work") }
            if sawWhatItIs && sawWhoItIsFor { break }
            _ = app.wait(for: .runningForeground, timeout: 0.5)
        } while Date() < deadline

        XCTAssertTrue(sawWhatItIs, "the screen should say what the app is")
        XCTAssertTrue(sawWhoItIsFor, "the screen should say who it is for")
    }
}
