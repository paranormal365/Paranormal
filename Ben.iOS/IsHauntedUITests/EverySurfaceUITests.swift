import XCTest

/// Every Phase-1 surface, opened for real, on whichever device is running the suite.
///
/// Written after an iPad-only bug — a sheet that never presented — sat undetected because only
/// two screens had tap-level tests. These are deliberately shallow: they open each surface and
/// assert it drew SOMETHING it is supposed to draw. A screen that crashes, hangs on a spinner,
/// or renders an empty shell fails here, which is the class of failure that had been getting
/// through.
///
/// They assert nothing about the DATA, because the dev database changes underneath them. Each
/// asserts instead that the screen settled into one of the states it is ALLOWED to be in — a
/// navigation title alone would still appear over a blank body or a spinner that never resolves,
/// which is exactly the failure worth catching.
final class EverySurfaceUITests: XCTestCase {

    private var app: XCUIApplication!

    override func setUp() {
        continueAfterFailure = false
        app = XCUIApplication()
        let email = ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "haveben@msn.com"
        let password = ProcessInfo.processInfo.environment["BEN_CLIENT_PASSWORD"] ?? "Y@ung615"
        app.launchArguments += ["-autoSignIn", "\(email):\(password)"]
        app.launch()
    }

    /// The screen opened, and then settled into one of the states it is allowed to be in.
    ///
    /// `anyOf` are the legitimate outcomes — content, or an empty state, or a refusal that says
    /// why. Passing on ANY of them is the point: the dev database decides which, and a test that
    /// demanded data would fail for reasons that are not the app's fault. What none of them
    /// allow is a blank body or a spinner that never resolves.
    private func assertSettled(_ title: String, anyOf snippets: [String],
                               file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertTrue(app.navigationBars[title].waitForExistence(timeout: 25),
                      "\(title) should open", file: file, line: line)

        let deadline = Date().addingTimeInterval(25)
        repeat {
            // Reading the labels directly rather than through an NSPredicate query: the
            // predicate is not Sendable, and this is clearer about what it actually checks.
            var labels: [String] = []
            for element in app.staticTexts.allElementsBoundByIndex
                         + app.buttons.allElementsBoundByIndex
                         + app.cells.allElementsBoundByIndex {
                labels.append(element.label)
            }
            for snippet in snippets where labels.contains(where: {
                $0.range(of: snippet, options: .caseInsensitive) != nil
            }) {
                _ = snippet
                return
            }
            _ = app.wait(for: .runningForeground, timeout: 0.5)
        } while Date() < deadline

        XCTFail("\(title) opened but never settled into a state it is allowed to be in",
                file: file, line: line)
    }

    func testTheFeedOpensAndSaysWhatItIsDoing() {
        XCTAssertTrue(AppNavigator.openSection("Feed", in: app))
        // The feed is switched off sitewide on this database, and the app must SAY so rather
        // than showing an empty list — a refusal read as "nothing here" is the bug this
        // codebase keeps finding.
        assertSettled("Feed", anyOf: ["isn't available right now", "Latest", "posted"])
    }

    func testNotificationsOpens() {
        // iPhone reaches it by the bell on the feed; iPad has a sidebar row. Both are named
        // "Notifications" — the count is a VALUE, not part of the name.
        if !AppNavigator.openSection("Notifications", in: app, timeout: 8) {
            XCTAssertTrue(AppNavigator.openSection("Feed", in: app), "the feed should be reachable")
            XCTAssertTrue(AppNavigator.openSection("Notifications", in: app),
                          "the feed's bell should reach notifications")
        }
        assertSettled("Notifications", anyOf: [
            "waiting", "all caught up", "Sign in to see what's waiting", "Couldn't load"])
    }

    func testInvestigationsOpens() {
        XCTAssertTrue(AppNavigator.openSection("Investigations", in: app))
        assertSettled("Investigations", anyOf: [
            "it appears here", "Where you've been", "belong to the group running them"])
    }

    func testEventsOpensWithoutNeedingAnything() {
        // Events has a sidebar row on iPad and a Profile row on iPhone — Field Kit took the
        // fifth tab. AppNavigator finds whichever this shell drew; if it is not a section here,
        // Profile is where it lives.
        if !AppNavigator.openSection("Events", in: app, timeout: 8) {
            XCTAssertTrue(AppNavigator.openSection("Profile", in: app))
            let events = app.buttons["Public events"].firstMatch
            XCTAssertTrue(events.waitForExistence(timeout: 15),
                          "a phone should reach public events from Profile")
            events.tap()
        }
        // "Reserve" and "spaces left" are what an event row actually says — my first guess at
        // these was wrong, and the failure was the TEST's, not the app's.
        assertSettled("Events", anyOf: [
            "will show up here", "Reserve", "spaces left", "couldn't be reached"])
    }

    func testTheCaseListOpens() {
        XCTAssertTrue(AppNavigator.openSection("My Cases", in: app))
        assertSettled("My Cases", anyOf: [
            "it appears here", "between you and the group", "#20"])
    }

    func testFieldKitOpens() {
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        assertSettled("Field Kit", anyOf: ["Start a session", "Nothing recorded yet",
                                           "can't be stored"])
    }

    func testSecurityOpensFromProfile() {
        XCTAssertTrue(AppNavigator.openSection("Profile", in: app))

        let security = app.buttons["Password & two-step sign-in"].firstMatch
        XCTAssertTrue(security.waitForExistence(timeout: 20),
                      "a signed-in profile should offer its security settings")
        security.tap()
        assertSettled("Security", anyOf: ["Password", "two-step"])
    }
}
