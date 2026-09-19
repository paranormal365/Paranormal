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
        let password = TestSecrets.required("BEN_CLIENT_PASSWORD")
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
        // iOS-2 added a fourth allowed state: a group MEMBER with no cases of their own is now
        // told this list is the client's and where their group's cases actually are, rather than
        // being asked to imagine asking a group for help.
        assertSettled("My Cases", anyOf: [
            "it appears here", "between you and the group", "#20",
            "This list is for cases you asked"])
    }

    /// A member can reach the case behind a visit they are rostered on (iOS-8).
    ///
    /// The app had no group-side case view at all: My Cases is the client's list, so somebody
    /// standing in the house they were sent to could not open the case from the phone.
    ///
    /// Written to pass on a seat that has no rostered visit with a case, because the seed data
    /// decides that and this test is about the door working, not about the data. What it will not
    /// tolerate is the door being there and leading to a blank screen — which is what the route
    /// did before the screen existed, since it resolved onto a "Coming soon" placeholder.
    func testAMemberCanOpenTheCaseBehindAVisit() {
        XCTAssertTrue(AppNavigator.openSection("Investigations", in: app))

        // By identifier, not `cells.firstMatch`. The first cell on this screen is the "Where
        // you've been" map card, so tapping it stayed exactly where it was — and the test then
        // reported "this visit has no case" about a visit it had never opened.
        let visit = app.descendants(matching: .any)["investigation-row"].firstMatch
        guard visit.waitForExistence(timeout: 20) else {
            return // no visits on this seat — nothing to walk
        }
        visit.tap()

        // The detail is where the door lives; without this the next guard would again be
        // measuring the wrong screen.
        guard app.descendants(matching: .any)["start-session-for-investigation"]
                 .firstMatch.waitForExistence(timeout: 20) else {
            XCTFail("tapping a visit did not open its detail screen")
            return
        }

        // `descendants`, not `buttons`: a SwiftUI Button with .buttonStyle(.plain) inside a List
        // surfaces as a CELL in the accessibility tree, not as a button. Querying app.buttons
        // found nothing and the test returned early — reporting "this visit has no case" about a
        // visit whose case the API was serving perfectly.
        let openCase = app.descendants(matching: .any)["open-group-case"].firstMatch
        guard openCase.waitForExistence(timeout: 15) else {
            // The visit has no case, or this person may not read it — the roster nulls the case
            // for anybody who cannot open it, which is the correct absence.
            return
        }
        openCase.tap()

        // The header is the part that decides whether the case opened at all.
        let settled = app.staticTexts["Read-only on the phone. Adding to a case is done on the website."]
                        .firstMatch.waitForExistence(timeout: 30)
                   || app.staticTexts["Couldn't open the case"].firstMatch.exists
                   || app.staticTexts["Timeline"].firstMatch.exists
        XCTAssertTrue(settled,
                      "the group case screen opened but drew neither the case nor a reason")

        // And it must never be the placeholder the route used to land on.
        XCTAssertFalse(app.staticTexts["Coming soon"].firstMatch.exists,
                       "the group case route is still landing on the placeholder screen")
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
