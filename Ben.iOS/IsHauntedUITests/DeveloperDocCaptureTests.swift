import XCTest

/// Captures the screenshot set for the developer documentation PDF.
///
/// Separate from `AppStoreScreenshotTests` on purpose: that set is curated for Apple — fixed
/// device sizes, a marketing sequence, no clutter. This one is for a developer joining the
/// project, so it goes wide and deep rather than pretty, and it spends most of its time inside
/// the Field Kit, which is the part of the app with no web equivalent to read about.
///
/// Runs only when asked: `TEST_RUNNER_BEN_DOC_SHOTS=1`. Plain env vars never reach the test
/// runner, hence the prefix.
final class DeveloperDocCaptureTests: XCTestCase {

    private var app: XCUIApplication!

    override func setUpWithError() throws {
        guard ProcessInfo.processInfo.environment["BEN_DOC_SHOTS"] == "1" else {
            throw XCTSkip("documentation capture runs only when asked — TEST_RUNNER_BEN_DOC_SHOTS=1")
        }
        continueAfterFailure = true   // one unreachable screen must not cost the whole set
        app = XCUIApplication()
        app.launchArguments += Self.baseArguments + ["-autoSignIn", Self.credentials]
        app.launch()
    }

    /// A SEEDED account by default.
    ///
    /// These documents state on their first page that every account, case and recording in them is
    /// simulated, so the default has to be somebody the seeders create — not the live site's
    /// administrator, which is what this used to be, paired unworkably with the seeded client's
    /// password.
    private static var captureEmail: String {
        ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "daniel.park@benco.dev"
    }

    private static var credentials: String {
        "\(captureEmail):\(TestSecrets.required("BEN_CLIENT_PASSWORD"))"
    }

    /// The arguments EVERY launch in this capture needs, including the two relaunches below.
    ///
    /// `-apiBaseURL` is the important one and it is deliberately not sticky: `APIEnvironmentStore`
    /// reads it per launch and never saves it, so a relaunch that rebuilds `launchArguments` from
    /// scratch silently falls back to the app's shipped address — **production**. These documents
    /// say on their first page that every account, case and recording in them is simulated, so a
    /// capture that could reach the live site would make that sentence untrue. Set
    /// `TEST_RUNNER_BEN_API_BASE_URL=http://localhost:5252` and point it at the seeded stack.
    private static var baseArguments: [String] {
        var args = ["-fieldKitFakeSensors"]
        if let base = ProcessInfo.processInfo.environment["BEN_API_BASE_URL"], !base.isEmpty {
            args += ["-apiBaseURL", base]
        }
        return args
    }

    private func snap(_ name: String) {
        let a = XCTAttachment(screenshot: app.screenshot())
        a.name = name
        a.lifetime = .keepAlways
        add(a)
    }

    private func settle(_ s: TimeInterval = 2.5) {
        _ = app.wait(for: .runningForeground, timeout: s)
        Thread.sleep(forTimeInterval: s)
    }

    /// Taps by identifier, scrolling it into view first, and says so rather than failing the run.
    ///
    /// The scrolling is the point. A control that exists but sits below the fold reports
    /// `exists == true` and `isHittable == false`, so a plain tap silently does nothing — which is
    /// exactly how the base-level button was missed, leaving the meter screenshotted in its
    /// un-based state and looking as though the needle were broken.
    @discardableResult
    private func tap(_ id: String, timeout: TimeInterval = 8) -> Bool {
        let el = app.descendants(matching: .any).matching(identifier: id).firstMatch
        guard el.waitForExistence(timeout: timeout) else { return false }
        for _ in 0..<4 {
            if el.isHittable { el.tap(); return true }
            app.swipeUp()
            Thread.sleep(forTimeInterval: 0.6)
        }
        return false
    }

    @discardableResult
    private func tapLabel(_ label: String, timeout: TimeInterval = 8) -> Bool {
        for c in [app.buttons[label].firstMatch, app.cells[label].firstMatch,
                  app.staticTexts[label].firstMatch] {
            if c.waitForExistence(timeout: timeout / 3), c.isHittable { c.tap(); return true }
        }
        return false
    }

    /// Waits until the sign-in from `-autoSignIn` has actually landed in the Keychain.
    ///
    /// It used to be `settle(6)`, a guess. On a cold first launch against a locally seeded API the
    /// sign-in took longer than that, the terminate below cut it off, and the whole capture ran
    /// signed out — thirteen screenshots of an app nobody was using, and five sections missing
    /// with nothing to say why. Signed in is the precondition for every screen after this one, so
    /// it is waited for and, if it never comes, said out loud.
    private func waitUntilSignedIn() {
        if confirmSignedIn(as: Self.captureEmail) { return }

        // Whoever the simulator was signed in as LAST is still signed in: the Keychain survives a
        // reinstall, and `SessionStore.signIn` returns immediately unless the state is signedOut,
        // so `-autoSignIn` is a no-op over a restored session. A whole capture once came out as a
        // different person's account without a word about it. Sign that session out and ask again.
        if app.descendants(matching: .any)["Sign out"].firstMatch.exists {
            app.descendants(matching: .any)["Sign out"].firstMatch.tap()
            settle(3)
            app.terminate()
            app.launchArguments = Self.baseArguments + ["-autoSignIn", Self.credentials]
            app.launch()
            settle(4)
            if confirmSignedIn(as: Self.captureEmail) { return }
        }

        XCTFail("Signing in as \(Self.captureEmail) never landed — every signed-in screen below "
                + "would be captured as somebody else, or signed out. Check BEN_CLIENT_EMAIL / "
                + "BEN_CLIENT_PASSWORD and that BEN_API_BASE_URL is serving them.")
    }

    /// Opens Profile and waits for the account row to name the person we asked for.
    ///
    /// By LABEL, not by identifier. `descendants(...)[key]` matches identifiers only, and the
    /// account row is a plain `LabeledContent` with none — so the subscript form could never match
    /// and reported a good sign-in as a failure while the capture went on working perfectly.
    private func confirmSignedIn(as email: String) -> Bool {
        guard AppNavigator.openSection("Profile", in: app, timeout: 20) else { return false }
        let row = app.staticTexts
            .matching(NSPredicate(format: "label CONTAINS[c] %@", email))
            .firstMatch
        return row.waitForExistence(timeout: 45)
    }

    func testCaptureTheDocumentationSet() {
        waitUntilSignedIn()

        // Relaunch signed in FROM THE KEYCHAIN, the way a real session starts. Without it the
        // stores fetch while sign-in is still in flight and cache their anonymous answers —
        // every signed-in surface then screenshots as empty.
        app.terminate()
        app.launchArguments = Self.baseArguments
        app.launch()
        settle(5)

        // ── The shell ────────────────────────────────────────────────────────
        snap("10-feed")

        if tap("open-composer") || tapLabel("Compose") { settle(); snap("11-composer")
                                                          tapLabel("Cancel"); settle(1) }

        if tapLabel("Notifications") { settle(); snap("12-notifications") }

        // ── Cases ────────────────────────────────────────────────────────────
        if AppNavigator.openSection("My Cases", in: app) {
            settle(); snap("20-cases")
            let row = app.descendants(matching: .any).matching(identifier: "case-row").firstMatch
            if row.waitForExistence(timeout: 12), row.isHittable {
                row.tap(); settle(3); snap("21-case-detail")
                if tapLabel("Messages") { settle(); snap("22-case-messages") }
                if tapLabel("Reports")  { settle(); snap("23-case-reports") }
            }
        }

        // ── Investigations ───────────────────────────────────────────────────
        if AppNavigator.openSection("Investigations", in: app) {
            settle(); snap("30-investigations")
        }

        // ── Field Kit: the long one ──────────────────────────────────────────
        if AppNavigator.openSection("Field Kit", in: app) {
            settle(3)
            snap("40-fieldkit-home")

            if tap("start-field-session") || tapLabel("Start a session") {
                settle(2)
                snap("41-name-the-session")

                let label = app.textFields.matching(identifier: "session-label").firstMatch
                if label.waitForExistence(timeout: 8) { label.tap(); label.typeText("Cellar stairs") }
                settle(1)

                if tap("confirm-start-session") || tapLabel("Open the session") {
                    // Item 215: the session opens pending; Start begins the log.
                    _ = tap("start-recording", timeout: 15) || tapLabel("Start")
                    settle(4)
                    snap("42-live-session")               // gauges running on fake sensors

                    if tap("set-base-level") {
                        settle(2)
                        snap("43-controls-with-base-set")
                        // Back up to the dial. Tapping the button scrolls the page down to reach
                        // it, which left the meter itself off-screen — and the meter with a base
                        // set is the whole point: the needle only appears once there is a base to
                        // measure departure FROM.
                        app.swipeDown(); app.swipeDown()
                        settle(2)
                        snap("42b-meter-with-needle")
                    }
                    if tap("mark-now")       { settle(2); snap("44-marked") }

                    if tap("blackout") {
                        settle(2)
                        snap("52-blackout")
                        // Dismissed by tapping the overlay AND again by identifier, because a
                        // blackout left on covers every control below it, including Stop.
                        tap("blackout-overlay", timeout: 3)
                        settle(1)
                        if !tap("mark-now", timeout: 3) { app.tap() }
                        settle(1)
                    }

                    if tap("open-note") {
                        settle(2); snap("45-note-composer")
                        // A TEXT FIELD, not a text view: `note-text` is a TextField with a vertical
                        // axis, which XCUITest reports as a textField. Asking for textViews found
                        // nothing, so nothing was typed, Save stayed disabled, and the composer
                        // stayed open over everything after it — six sections of this document
                        // went missing that way, silently.
                        let note = app.descendants(matching: .any)
                            .matching(identifier: "note-text").firstMatch
                        if note.waitForExistence(timeout: 8) {
                            note.tap(); note.typeText("Three knocks, low on the wall.")
                            settle(1); snap("46-note-typed")
                        }
                        if !tap("save-note") { _ = tapLabel("Save the note") }
                        settle(2)
                        // Whatever happened, leave the sheet. A composer left up hides the Stop
                        // button, and Stop is the door to the session review.
                        if app.descendants(matching: .any)["save-note"].firstMatch.exists {
                            _ = tapLabel("Cancel")
                            settle(1)
                        }
                    }

                    if tap("open-evp") {
                        settle(2); snap("47-evp-mode")
                        if tap("evp-start-recording") { settle(3); snap("48-evp-recording") }
                        tapLabel("Done"); settle(1)
                    }

                    if tap("room-bar") || tap("room-name-field") {
                        settle(2); snap("49-room-bar")
                        tapLabel("Cancel"); settle(1)
                    }

                    if tap("arm-sentry") {
                        settle(2); snap("50-sentry-panel")
                        if tap("confirm-arm") { settle(3); snap("51-sentry-armed") }
                        tap("disarm-sentry"); settle(1)
                    }

                    if tap("stop-field-session") || tapLabel("Stop") {
                        settle(4)
                        snap("53-session-review")
                        if tap("build-export") { settle(3); snap("54-export") ; tapLabel("Cancel") }
                        settle(1)
                        if tap("add-to-archive") { settle(3); snap("55-publish-to-archive"); tapLabel("Cancel") }
                    }
                }
            }
        }

        // ── Back to a known place ────────────────────────────────────────────
        // The Field Kit leaves the app deep inside a live session, and any step above that did
        // not land leaves it deeper still. Relaunching is the cheapest reliable reset: the
        // session is already persisted, and the remaining screens are reached from the root.
        // Without this, five sections of the document came out with no picture at all.
        app.terminate()
        app.launchArguments = Self.baseArguments
        app.launch()
        settle(5)

        // ── Events, evidence, profile ────────────────────────────────────────
        if AppNavigator.openSection("Events", in: app, timeout: 6) {
            settle(); snap("60-events")
        } else if AppNavigator.openSection("Profile", in: app) {
            if let row = AppNavigator.section("Public events", in: app, timeout: 8) {
                row.tap(); settle(); snap("60-events")
            }
        }

        if AppNavigator.openSection("Profile", in: app) {
            settle(2); snap("70-profile")
            if tapLabel("My evidence") { settle(); snap("71-my-evidence"); tapLabel("Profile") }
            if tapLabel("Security")    { settle(); snap("72-security");    tapLabel("Profile") }
            if tapLabel("About")       { settle(); snap("73-about") }
        }
    }
}
