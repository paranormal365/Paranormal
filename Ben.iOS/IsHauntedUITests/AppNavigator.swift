import XCTest

/// Navigating the app without caring which shell it drew.
///
/// The same build is a TabView on iPhone and a NavigationSplitView on iPad, and the two expose
/// their sections as different element types — a tab is a button, a sidebar row is a cell. Four
/// UI tests written against the tab bar therefore failed on iPad at their FIRST tap, which said
/// nothing about the app and everything about the tests. This is the seam that keeps one suite
/// honest on both devices.
enum AppNavigator {

    /// Presses Start a session, and answers the question it asks when another session is still
    /// open.
    ///
    /// Since 2026-09-27 (Ben: "If they have recorded a session, just save it and ask if they want to
    /// create a new one"), Start with a session still open asks first: save the recording one, or
    /// replace the one never started. A suite that leaves a session open — any test that stops
    /// half-way — used to walk straight into the next test's sheet; now every later test met the
    /// question instead and failed at its first step. The helper takes the answer a person
    /// starting over would take.
    static func startNewSession(in app: XCUIApplication, file: StaticString = #filePath, line: UInt = #line) {
        app.buttons["start-field-session"].tap()
        // firstMatch: a confirmation dialog draws its buttons twice in the element tree (its
        // popover and its sheet form), and a bare query then refuses to tap an ambiguous match.
        let saveAndStart = app.buttons["save-and-start-new"].firstMatch
        if saveAndStart.waitForExistence(timeout: 3) {
            saveAndStart.tap()
        }
    }

    /// Types into a field after clearing what is already there.
    ///
    /// The new-session sheet fills the name in from where the phone is ("224 High St, Red Boiling
    /// Springs"), so typing a name after a tap ran the two together (10/15/2026, once the capture's
    /// simulator had a location).
    static func replaceText(in field: XCUIElement, with text: String) {
        // At the far right, so the cursor lands after the filled-in text: a tap in the middle left the
        // end of the address behind ("…north wallSprings").
        field.coordinate(withNormalizedOffset: CGVector(dx: 0.97, dy: 0.5)).tap()
        let current = field.value as? String ?? ""
        if !current.isEmpty, current != field.placeholderValue {
            field.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: current.count + 2))
        }
        field.typeText(text)
    }

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
            // On a narrow regular-width window (the iPhone Duo opened) the sections are behind
            // the ☰ button rather than in a standing sidebar.
            let menu = app.buttons["shell-menu"].firstMatch
            if menu.exists && menu.isHittable && !app.cells[name].firstMatch.isHittable {
                menu.tap()
                _ = app.wait(for: .runningForeground, timeout: 0.8)
            }
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
