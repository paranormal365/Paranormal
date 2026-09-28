import XCTest

/// The lead's QR code (item 252). Ben: "would the qr code scan also register someone to join...
/// maybe it sends a request to the leader and the leader has to confirm."
///
/// Somebody registered scans and is straight in. Somebody who is not asks; the lead lets them in;
/// they are then in. The lead's side is played through the API, as the lead's phone would.
///
/// Needs a reachable API and a public event the client is registered for that nobody has launched
/// in the last ten minutes: `TEST_RUNNER_BEN_API_BASE_URL`, `TEST_RUNNER_BEN_JOIN_EVENT_ID`, and the
/// client, member and SuperAdmin (the lead) passwords.
final class JoinByCodeUITests: XCTestCase {

    private struct Launched: Decodable {
        struct Launch: Decodable { let id: UUID; let joinToken: String? }
        let launch: Launch
    }
    private struct Request: Decodable { let id: UUID; let status: String; let displayName: String }

    nonisolated(unsafe) private static var launched: Launched.Launch?

    private func environment() throws -> (base: String, eventId: String) {
        let env = ProcessInfo.processInfo.environment
        guard let base = env["BEN_API_BASE_URL"], let event = env["BEN_JOIN_EVENT_ID"] else {
            throw XCTSkip("set TEST_RUNNER_BEN_API_BASE_URL and TEST_RUNNER_BEN_JOIN_EVENT_ID")
        }
        return (base, event)
    }

    /// The lead launches once for the whole class; both tests scan the same code.
    private func theLaunch(_ api: LocalAPI, lead: String, eventId: String) throws -> Launched.Launch {
        if let launched = Self.launched { return launched }
        let outcome: Launched = try api.decode(api.post("/api/field-launches", ["target": "event", "id": eventId], bearer: lead))
        Self.launched = outcome.launch
        return outcome.launch
    }

    private func app(email: String, passwordVariable: String, scanning token: String) -> XCUIApplication {
        let app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors",
                                "-apiBaseURL", ProcessInfo.processInfo.environment["BEN_API_BASE_URL"]!,
                                "-autoSignIn", "\(email):\(TestSecrets.required(passwordVariable))",
                                "-openLink", "https://ishaunted.com/field-kit/join/\(token)"]
        return app
    }

    func testSomebodyRegisteredIsStraightIn() throws {
        let (base, eventId) = try environment()
        let api = LocalAPI(base: base, test: self)
        let launch = try theLaunch(api, lead: try api.signIn("haveben@msn.com", passwordVariable: "BEN_SUPERADMIN_PASSWORD"), eventId: eventId)
        let token = try XCTUnwrap(launch.joinToken, "the lead should be given the code")

        let app = app(email: ProcessInfo.processInfo.environment["BEN_CLIENT_EMAIL"] ?? "daniel.park@benco.dev",
                      passwordVariable: "BEN_CLIENT_PASSWORD", scanning: token)
        app.launch()

        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 30),
                      "somebody registered should be straight into the session")
        XCTAssertFalse(app.buttons["join-code-ask"].exists)
    }

    func testSomebodyNotRegisteredAsksAndIsLetIn() throws {
        let (base, eventId) = try environment()
        let api = LocalAPI(base: base, test: self)
        let lead = try api.signIn("haveben@msn.com", passwordVariable: "BEN_SUPERADMIN_PASSWORD")
        let launch = try theLaunch(api, lead: lead, eventId: eventId)
        let token = try XCTUnwrap(launch.joinToken)

        let app = app(email: ProcessInfo.processInfo.environment["BEN_MEMBER_EMAIL"] ?? "james.thornton@benco.dev",
                      passwordVariable: "BEN_MEMBER_PASSWORD", scanning: token)
        app.launch()

        let ask = app.buttons["join-code-ask"].firstMatch
        XCTAssertTrue(ask.waitForExistence(timeout: 30), "somebody not registered should be offered Ask to join")
        attach(app, "join-code-ask")
        ask.tap()
        // Asking is when a notification plainly helps ("you're in"), so the app asks for them.
        let allow = XCUIApplication(bundleIdentifier: "com.apple.springboard").buttons["Allow"]
        if allow.waitForExistence(timeout: 8) { allow.tap() }
        XCTAssertTrue(app.descendants(matching: .any)["join-code-pending"].waitForExistence(timeout: 15),
                      "asking should say they are waiting for the lead")
        XCTAssertTrue(app.buttons["join-code-record"].exists, "they should be able to record while they wait")
        attach(app, "join-code-pending")

        // The lead's phone: the request is there, and they let the person in.
        let requests: [Request] = try api.decode(api.get("/api/field-launches/\(launch.id.uuidString.lowercased())/requests", bearer: lead))
        let asked = try XCTUnwrap(requests.first { $0.status == "pending" }, "the lead should see the request")
        _ = try api.post("/api/field-launches/\(launch.id.uuidString.lowercased())/requests/\(asked.id.uuidString.lowercased())/approve",
                         bearer: lead)

        // Back on their phone, untouched: the waiting screen looks again and takes them straight in.
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 30),
                      "once let in, they should be straight into the session")
    }

    private func attach(_ app: XCUIApplication, _ name: String) {
        let shot = XCTAttachment(screenshot: app.screenshot())
        shot.name = name
        shot.lifetime = .keepAlways
        add(shot)
    }
}
