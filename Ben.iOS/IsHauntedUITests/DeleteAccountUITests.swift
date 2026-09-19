import XCTest

/// The delete-account screen is reachable, and its button is not armed by accident.
///
/// App Review Guideline 5.1.1(v) requires account deletion inside the app, and a reviewer looks
/// for it in the account settings rather than hunting. This opens it the way they will.
///
/// **This test never completes a deletion, and must not be changed to.** It signs in with the
/// seeded account the rest of the suite uses; confirming here would delete that account and take
/// every other UI test with it. What it checks is the guard — that the destructive button stays
/// disabled until the confirmation word is typed — which is exactly the part worth pinning and
/// can be checked without pressing anything.
final class DeleteAccountUITests: XCTestCase {

    private var app: XCUIApplication!

    override func setUp() {
        continueAfterFailure = false
        app = XCUIApplication()
        let email = ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "haveben@msn.com"
        let password = TestSecrets.required("BEN_CLIENT_PASSWORD")
        app.launchArguments += ["-autoSignIn", "\(email):\(password)"]
        // Point the app at an API built from the working tree when one is supplied. Without it
        // the app uses whatever host the Dev environment names, which is fine for screens that
        // do not need a new endpoint and useless for the ones that do.
        if let apiBase = ProcessInfo.processInfo.environment["BEN_API_BASE_URL"], !apiBase.isEmpty {
            app.launchArguments += ["-apiBaseURL", apiBase]
        }
        app.launch()
    }

    private func openDeleteAccount() {
        XCTAssertTrue(AppNavigator.openSection("Profile", in: app), "Profile should be reachable")

        let row = app.buttons["settings-delete-account"].firstMatch
        let cell = app.cells["settings-delete-account"].firstMatch
        if row.waitForExistence(timeout: 25) {
            row.tap()
        } else if cell.waitForExistence(timeout: 5) {
            cell.tap()
        } else {
            XCTFail("a signed-in account must have a Delete account row in its settings")
            return
        }
        XCTAssertTrue(app.navigationBars["Delete account"].waitForExistence(timeout: 25),
                      "the Delete account screen should open")
    }

    func testTheScreenIsReachableFromAccountSettings() {
        openDeleteAccount()
    }

    /// The screen settles into one of the two states it is allowed to be in, and neither of them
    /// is a spinner that never resolves or a blank body.
    func testItSaysEitherWhatWillHappenOrWhichGroupBlocksIt() {
        openDeleteAccount()

        let deadline = Date().addingTimeInterval(25)
        var settled = false
        repeat {
            var labels: [String] = []
            for element in app.staticTexts.allElementsBoundByIndex { labels.append(element.label) }
            let deletable = labels.contains { $0.contains("It cannot be undone") }
            // The blocked variant. A refusal that does not name the group is the dead end this
            // screen exists to avoid, so "you still own a group" alone would not count.
            let blocked = labels.contains { $0.contains("A group must always have an owner") }
            if deletable || blocked { settled = true; break }
            _ = app.wait(for: .runningForeground, timeout: 0.5)
        } while Date() < deadline

        XCTAssertTrue(settled,
                      "the screen should either explain the deletion or name the group blocking it")
    }

    /// The guard. Never press the button after typing — see the note on the class.
    func testTheDestructiveButtonIsDisabledUntilTheWordIsTyped() throws {
        openDeleteAccount()

        let confirm = app.buttons["delete-account-confirm"].firstMatch
        guard confirm.waitForExistence(timeout: 25) else {
            throw XCTSkip("this account owns a group, so the confirm control is not drawn")
        }
        XCTAssertFalse(confirm.isEnabled, "the button must not be armed before the word is typed")

        let field = app.textFields["delete-account-confirmation"].firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 10))
        field.tap()
        field.typeText("delete")   // lower case — the server compares exactly
        XCTAssertFalse(confirm.isEnabled, "the comparison should be case-sensitive")
    }
}
