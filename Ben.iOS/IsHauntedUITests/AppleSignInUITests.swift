import XCTest

/// Sign in with Apple is a door, and a door has to be reachable.
///
/// The Apple sheet itself cannot be driven by a test — it is a system UI in another process, and
/// on a simulator without a provisioned developer account it will not complete at all. What CAN
/// be checked, and is worth checking, is everything on this side of it: that the button exists on
/// the sign-in screen, that it says what it does, and that it is offered to somebody signed OUT.
final class AppleSignInUITests: XCTestCase {

    private var app: XCUIApplication!

    override func setUp() {
        continueAfterFailure = false
        app = XCUIApplication()
        app.launch()
    }

    func testTheAppleButtonIsOnTheSignInScreen() {
        let profile = app.buttons["Profile"]
        XCTAssertTrue(profile.waitForExistence(timeout: 20))
        profile.tap()

        // Earlier tests in the suite may have left a session behind — the Keychain survives an
        // app relaunch by design. Sign out through the real UI rather than adding a test hook.
        let signOut = app.buttons["Sign out"]
        if signOut.waitForExistence(timeout: 5) {
            signOut.tap()
        }

        // "Sign in" also names the sheet's own submit button once it opens.
        let signIn = app.buttons["Sign in"].firstMatch
        XCTAssertTrue(signIn.waitForExistence(timeout: 15), "a signed-out profile must offer a way in")
        signIn.tap()

        let appleButton = app.buttons["sign-in-with-apple"]
        XCTAssertTrue(appleButton.waitForExistence(timeout: 15),
                      "Sign in with Apple must be offered on the sign-in screen")
        XCTAssertTrue(appleButton.isHittable, "the button must be tappable, not just present")

        // The one thing somebody needs to know before tapping it: it will not make a second
        // account behind their back.
        XCTAssertTrue(
            app.staticTexts.containing(
                NSPredicate(format: "label CONTAINS[c] 'rather than making a second one'")
            ).firstMatch.waitForExistence(timeout: 5),
            "the screen should say what happens when the email already has an account")
    }
}
