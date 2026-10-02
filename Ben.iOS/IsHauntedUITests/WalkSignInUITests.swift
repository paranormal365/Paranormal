import XCTest

/// Signs one person in and stops — the opening move of a hand-driven walk of the app.
///
/// A person walking the app by hand (or an assistant driving the simulator) needs to be signed in
/// as a seeded persona without the password ever being typed or written into a command. This test
/// does exactly that and nothing else: it launches against the stack named by
/// `TEST_RUNNER_BEN_API_BASE_URL`, signs in through `-autoSignIn` with the password read from the
/// environment variable named by `TEST_RUNNER_BEN_WALK_PASSWORD_VAR`, waits for the session to
/// land in the Keychain, and leaves the app there. The next launch — by hand — is signed in.
///
/// Skipped unless `TEST_RUNNER_BEN_WALK=1`.
final class WalkSignInUITests: XCTestCase {

    func testSignInAndStop() throws {
        let env = ProcessInfo.processInfo.environment
        guard env["BEN_WALK"] == "1", let base = env["BEN_API_BASE_URL"], !base.isEmpty,
              let email = env["BEN_WALK_EMAIL"], let passwordVariable = env["BEN_WALK_PASSWORD_VAR"] else {
            throw XCTSkip("run by the walk script (TEST_RUNNER_BEN_WALK=1 with the API, email and password variable)")
        }
        let app = XCUIApplication()
        app.launchArguments += ["-apiBaseURL", base,
                                "-autoSignIn", "\(email):\(TestSecrets.required(passwordVariable))"]
        app.launch()

        // Signed in when the Profile tab offers Sign out rather than Sign in.
        Thread.sleep(forTimeInterval: 6)
        XCTAssertTrue(AppNavigator.openSection("Profile", in: app, timeout: 20), "Profile should open")
        let signOut = app.descendants(matching: .any)["Sign out"].firstMatch
        for _ in 0..<4 where !signOut.exists { app.swipeUp() }
        XCTAssertTrue(signOut.exists, "\(email) should be signed in")
    }
}
