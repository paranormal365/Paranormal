import XCTest

/// Navigating the app without caring which shell it drew.
///
/// The same build is a TabView on iPhone and a NavigationSplitView on iPad, and the two expose
/// their sections as different element types — a tab is a button, a sidebar row is a cell. Four
/// UI tests written against the tab bar therefore failed on iPad at their FIRST tap, which said
/// nothing about the app and everything about the tests. This is the seam that keeps one suite
/// honest on both devices.
enum AppNavigator {

    /// Opens a top-level section by its visible name.
    @discardableResult
    static func openSection(_ name: String, in app: XCUIApplication,
                            timeout: TimeInterval = 25) -> Bool {
        guard let element = section(name, in: app, timeout: timeout) else { return false }
        element.tap()
        return true
    }

    /// The section's control, whichever kind this shell used for it.
    static func section(_ name: String, in app: XCUIApplication,
                        timeout: TimeInterval = 25) -> XCUIElement? {
        let deadline = Date().addingTimeInterval(timeout)
        repeat {
            // Order matters: on iPad a sidebar row is a cell that also CONTAINS a static text,
            // and tapping the inner label is less reliable than tapping the row.
            for candidate in [app.buttons[name].firstMatch,
                              app.cells[name].firstMatch,
                              app.staticTexts[name].firstMatch] {
                if candidate.exists && candidate.isHittable { return candidate }
            }
            _ = app.wait(for: .runningForeground, timeout: 0.5)
        } while Date() < deadline
        return nil
    }

    /// Opens the first case, or skips when this account has none to open.
    static func openFirstCase(in app: XCUIApplication) throws {
        XCTAssertTrue(openSection("My Cases", in: app), "the Cases section should be reachable")

        let row = app.buttons.matching(identifier: "case-row").firstMatch
        let cell = app.cells.matching(identifier: "case-row").firstMatch
        if row.waitForExistence(timeout: 20) {
            row.tap()
        } else if cell.exists {
            cell.tap()
        } else {
            throw XCTSkip("no case on this account — nothing to open")
        }
    }
}

/// Credentials for the seeded test accounts, read from the environment with no fallback.
///
/// These used to be literals in each fixture. The development database is also the one
/// ishaunted.com uses, so those literals were working production credentials in a public
/// repository. They are gone; a run without the variables stops with a message naming the one it
/// wanted instead of signing in as somebody real or failing on an empty password.
///
/// Plain environment variables never reach an XCUITest — use the `TEST_RUNNER_` prefix:
/// `TEST_RUNNER_BEN_CLIENT_PASSWORD=… xcodebuild test …`
enum TestSecrets {
    static func required(_ variable: String, file: StaticString = #filePath, line: UInt = #line) -> String {
        guard let value = ProcessInfo.processInfo.environment[variable], !value.isEmpty else {
            XCTFail("\(variable) is not set. Export it (with the TEST_RUNNER_ prefix for UI tests) "
                    + "— the seeded passwords are no longer compiled into this target.",
                    file: file, line: line)
            return ""
        }
        return value
    }
}
