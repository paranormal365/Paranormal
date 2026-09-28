import XCTest

/// A session joined from a lead's launch goes to the group's night when it is sent (item 252):
/// joined, recorded, stopped, sent — and the lead finds it filed under the event, with nothing
/// chosen along the way.
///
/// Needs a reachable API, an open launch of a calendar event the client is registered for, and the
/// event's id: `TEST_RUNNER_BEN_API_BASE_URL`, `TEST_RUNNER_BEN_LAUNCH_ID`, `TEST_RUNNER_BEN_LAUNCH_EVENT_OF`,
/// and the client and SuperAdmin (the lead, a member of the group) passwords.
final class JoinedSessionSendUITests: XCTestCase {

    private struct Session: Decodable { let id: UUID; let orgCalendarEventId: UUID? }

    func testAJoinedSessionIsSentToTheGroupsNight() throws {
        let env = ProcessInfo.processInfo.environment
        guard let base = env["BEN_API_BASE_URL"], let launchId = env["BEN_LAUNCH_ID"], let eventId = env["BEN_LAUNCH_EVENT_OF"] else {
            throw XCTSkip("set TEST_RUNNER_BEN_API_BASE_URL, TEST_RUNNER_BEN_LAUNCH_ID and TEST_RUNNER_BEN_LAUNCH_EVENT_OF")
        }
        continueAfterFailure = false
        let api = LocalAPI(base: base, test: self)
        let lead = try api.signIn("haveben@msn.com", passwordVariable: "BEN_SUPERADMIN_PASSWORD")
        let before: [Session] = try api.decode(api.get("/api/field-sessions/for-event/\(eventId)", bearer: lead))

        let app = XCUIApplication()
        app.launchArguments += ["-fieldKitFakeSensors", "-apiBaseURL", base,
                                "-autoSignIn", "\(env["BEN_CLIENT_EMAIL"] ?? "daniel.park@benco.dev"):\(TestSecrets.required("BEN_CLIENT_PASSWORD"))",
                                "-openLink", "ishaunted://field-kit/launch/\(launchId)"]
        app.launch()

        // Joined: straight to the session. Record a little, and stop.
        XCTAssertTrue(app.buttons["start-recording"].waitForExistence(timeout: 30))
        app.buttons["start-recording"].tap()
        XCTAssertTrue(app.buttons["stop-field-session"].waitForExistence(timeout: 20))
        sleep(5)
        app.buttons["stop-field-session"].tap()

        let share = app.buttons["open-share-menu"]
        XCTAssertTrue(share.waitForExistence(timeout: 25))
        share.tap()
        let toServer = app.buttons["Send to the server"].firstMatch
        XCTAssertTrue(toServer.waitForExistence(timeout: 10))
        toServer.tap()

        // It says where it is going, and nothing had to be chosen.
        let destination = app.switches["upload-send-to-event"].firstMatch
        XCTAssertTrue(destination.waitForExistence(timeout: 20), "the send screen should say it goes to the group's night")
        XCTAssertEqual(destination.value as? String, "1", "sending it there should be the starting choice")
        XCTAssertFalse(app.buttons["upload-investigation"].exists, "there is no investigation to pick")
        let shot = XCTAttachment(screenshot: app.screenshot())
        shot.name = "send-to-event"
        shot.lifetime = .keepAlways
        add(shot)

        let send = app.buttons["send-session"].firstMatch
        for _ in 0..<5 where !(send.exists && send.isHittable) { app.swipeUp() }
        send.tap()

        // The lead's view: it arrived, filed under the event.
        var arrived = false
        for _ in 0..<30 where !arrived {
            sleep(2)
            let after: [Session] = try api.decode(api.get("/api/field-sessions/for-event/\(eventId)", bearer: lead))
            arrived = after.count > before.count && after.allSatisfy { $0.orgCalendarEventId?.uuidString.lowercased() == eventId.lowercased() }
        }
        XCTAssertTrue(arrived, "the session should be on the server, filed under the event")
    }
}
