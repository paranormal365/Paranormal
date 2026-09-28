import XCTest

/// A lead's launch reaching a phone as a real notification, and its tap joining the session
/// (item 252) — the whole path: permission, the phone registering with the API, the API asking
/// Apple's sandbox, Apple delivering, the tap, and the live session waiting for Start.
///
/// Possible in the simulator because an Apple-silicon Mac's simulator receives real sandbox pushes.
/// Opt-in, because it needs the local API configured with the APNs key and a live event the client
/// is registered for: `TEST_RUNNER_BEN_PUSH_E2E=1`, `TEST_RUNNER_BEN_API_BASE_URL`,
/// `TEST_RUNNER_BEN_CLIENT_PASSWORD`, `TEST_RUNNER_BEN_SUPERADMIN_PASSWORD` (who launches) and
/// `TEST_RUNNER_BEN_LAUNCH_EVENT_ID` (the calendar event to launch).
final class PushEndToEndUITests: XCTestCase {

    private struct Outcome: Decodable { let people: Int; let peopleWithTheApp: Int; let phonesReached: Int }

    func testALaunchReachesThePhoneAndTappingItJoins() throws {
        let environment = ProcessInfo.processInfo.environment
        guard environment["BEN_PUSH_E2E"] == "1", let base = environment["BEN_API_BASE_URL"],
              let eventId = environment["BEN_LAUNCH_EVENT_ID"] else {
            throw XCTSkip("set TEST_RUNNER_BEN_PUSH_E2E=1 with the API, passwords and TEST_RUNNER_BEN_LAUNCH_EVENT_ID")
        }
        continueAfterFailure = false
        let app = XCUIApplication()
        let email = environment["BEN_CLIENT_EMAIL"] ?? "daniel.park@benco.dev"
        let password = TestSecrets.required("BEN_CLIENT_PASSWORD")
        app.launchArguments += ["-fieldKitFakeSensors", "-apiBaseURL", base, "-autoSignIn", "\(email):\(password)"]
        let springboard = XCUIApplication(bundleIdentifier: "com.apple.springboard")

        app.launch()
        sleep(5)
        XCTAssertTrue(AppNavigator.openSection("Field Kit", in: app))
        // "Happening now" asks for notifications — the moment one plainly helps.
        let allow = springboard.buttons["Allow"]
        if allow.waitForExistence(timeout: 15) { allow.tap() }
        sleep(10)   // Apple hands the phone its token; the app gives it to the API

        // Out of the app first, so the notification arrives as it would for anybody not looking at
        // it — Apple delivers within a second, and a banner shown inside the app is gone before a
        // test could go home to look for it.
        XCUIDevice.shared.press(.home)

        // The lead presses Launch.
        let outcome = try launch(eventId: eventId, base: base,
                                 password: TestSecrets.required("BEN_SUPERADMIN_PASSWORD"))
        XCTAssertGreaterThan(outcome.peopleWithTheApp, 0, "the phone should have registered with the API")
        XCTAssertGreaterThan(outcome.phonesReached, 0, "Apple should have accepted the push for it")
        let banner = springboard.descendants(matching: .any)
            .matching(NSPredicate(format: "label CONTAINS[c] %@", "is starting")).firstMatch
        XCTAssertTrue(banner.waitForExistence(timeout: 45), "the notification should arrive")
        let shot = XCTAttachment(screenshot: XCUIScreen.main.screenshot())
        shot.name = "push-arrived"
        shot.lifetime = .keepAlways
        add(shot)
        banner.tap()

        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 25),
                      "tapping it should open the live session, waiting for Start")
        XCTAssertFalse(app.buttons["stop-field-session"].exists, "nothing should record before Start")
        let joined = XCTAttachment(screenshot: app.screenshot())
        joined.name = "push-joined"
        joined.lifetime = .keepAlways
        add(joined)
    }

    /// Signs in as the lead and launches, as the lead's phone would.
    private func launch(eventId: String, base: String, password: String) throws -> Outcome {
        struct Token: Decodable { let accessToken: String }
        let token: Token = try call("\(base)/login", body: ["email": "haveben@msn.com", "password": password], bearer: nil)
        return try call("\(base)/api/field-launches", body: ["target": "event", "id": eventId], bearer: token.accessToken)
    }

    private func call<T: Decodable>(_ url: String, body: [String: String], bearer: String?) throws -> T {
        var request = URLRequest(url: URL(string: url)!)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if let bearer { request.setValue("Bearer \(bearer)", forHTTPHeaderField: "Authorization") }
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        let done = expectation(description: url)
        var result: Result<Data, Error> = .failure(URLError(.unknown))
        URLSession.shared.dataTask(with: request) { data, response, error in
            if let error { result = .failure(error) }
            else if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                result = .failure(NSError(domain: "http", code: http.statusCode,
                                          userInfo: [NSLocalizedDescriptionKey: String(data: data ?? Data(), encoding: .utf8) ?? ""]))
            } else { result = .success(data ?? Data()) }
            done.fulfill()
        }.resume()
        wait(for: [done], timeout: 30)
        return try JSONDecoder().decode(T.self, from: try result.get())
    }
}
