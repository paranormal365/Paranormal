import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// The door on the phone, with and without a signal (item 235 phase 14c).
@Suite("The door — tonight's list, scanning, and arrivals kept without a signal")
@MainActor
struct DoorStoreTests {

    private func fixture(_ name: String) throws -> Data { try Fixtures.data(name, in: Bundle.module) }

    private static let orgId = UUID(uuidString: "afc81ae7-98fa-44b5-aa04-5ef24fe6c2d5")!
    private static let eventId = UUID(uuidString: "40000002-0000-0000-0000-000000000002")!
    private static let danielBooking = UUID(uuidString: "fb8d257c-09b3-458a-adc8-30bac33e5543")!
    nonisolated private static let cameIn = Date(timeIntervalSince1970: 1_792_195_200) // a fixed evening

    /// A server that can be switched off, like a cellar.
    private final class Line: @unchecked Sendable {
        var up = true
        var answer: @Sendable (URLRequest) throws -> (Data, Int) = { _ in (Data(), 200) }
    }

    private static func cache() -> DoorCache {
        DoorCache(directory: FileManager.default.temporaryDirectory.appendingPathComponent("door-\(UUID().uuidString)", isDirectory: true))
    }

    private static func store(_ line: Line, cache: DoorCache) -> (DoorStore, MockTransport) {
        let transport = MockTransport { request in
            guard line.up else { throw URLError(.notConnectedToInternet) }
            let (body, status) = try line.answer(request)
            return (body, MockTransport.response(for: request, status: status))
        }
        let tokens = TokenSession(
            storage: InMemoryTokenStorage(tokens: StoredTokens(accessToken: "AT", refreshToken: "RT", expiresAt: Date(timeIntervalSinceNow: 600))),
            transport: transport, environment: { .dev })
        return (DoorStore(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens), cache: cache, now: { cameIn }), transport)
    }

    private func door() throws -> HostedEventDoor { try BenJSON.decoder.decode(HostedEventDoor.self, from: fixture("hosted-door")) }

    /// Opens the door once with a signal, so the phone has tonight's list.
    private func openedOnline(_ line: Line, _ cache: DoorCache) async throws -> (DoorStore, MockTransport, HostedEventDoor) {
        let body = try fixture("hosted-door")
        line.answer = { _ in (body, 200) }
        let (store, transport) = Self.store(line, cache: cache)
        guard case .live(let door) = await store.loadDoor(organization: Self.orgId, event: Self.eventId) else {
            throw CocoaError(.featureUnsupported)
        }
        return (store, transport, door)
    }

    // ── the contract, from what the API really answered ─────────────────────

    @Test func theDoorTheDutiesAndBothScanAnswersDecode() throws {
        let door = try door()
        #expect(door.hostedEventId == Self.eventId)
        #expect(door.placesLeftSentence == "Room for 18 more tonight.")
        let daniel = try #require(door.expected.first { $0.leadName == "Daniel Park" })
        #expect(daniel.code == "-TOKEN")
        #expect(!daniel.isIn)

        let duties = try BenJSON.decoder.decode([MyHostedEventDuty].self, from: fixture("hosted-duties"))
        #expect(duties.contains { $0.hostedEventId == Self.eventId && $0.organizationId == Self.orgId })

        let admitted = try BenJSON.decoder.decode(HostedEventScanResult.self, from: fixture("hosted-scan-admitted"))
        #expect(admitted.admitted)
        #expect(admitted.leadName == "Daniel Park")
        let refused = try BenJSON.decoder.decode(HostedEventScanResult.self, from: fixture("hosted-scan-refused"))
        #expect(!refused.admitted)
        #expect(refused.refusal?.contains("look them up by name") == true)
    }

    @Test func aScannedPassAndATypedCodeFindTheSameParty() throws {
        let door = try door()
        // The fixture pass's token, whose last six characters are the door's code.
        #expect(door.party(forScanned: "fixture-pass-token")?.leadName == "Daniel Park")
        #expect(door.party(forCode: "-token")?.leadName == "Daniel Park")
        #expect(door.party(forCode: "- TOKEN")?.leadName == "Daniel Park")
        #expect(door.party(forCode: "TOKEN") == nil)
        #expect(door.party(forScanned: "somebody-elses-pass") == nil)
    }

    // ── tonight's list on the phone ──────────────────────────────────────────

    @Test func theListIsKeptAndShownWhenThereIsNoSignal() async throws {
        let line = Line()
        let cache = Self.cache()
        let (store, _, online) = try await openedOnline(line, cache)

        line.up = false
        guard case .saved(let saved, let at) = await store.loadDoor(organization: Self.orgId, event: Self.eventId) else {
            Issue.record("expected the kept list"); return
        }
        #expect(saved == online)
        #expect(at == Self.cameIn)
    }

    @Test func aDoorThatIsNoLongerTheirsLeavesNothingOnThePhone() async throws {
        let line = Line()
        let cache = Self.cache()
        let (store, _, _) = try await openedOnline(line, cache)

        line.answer = { _ in (Data(), 403) }
        guard case .failed(let why?) = await store.loadDoor(organization: Self.orgId, event: Self.eventId) else { Issue.record("403"); return }
        #expect(why.contains("isn't yours"))

        line.up = false
        guard case .failed = await store.loadDoor(organization: Self.orgId, event: Self.eventId) else {
            Issue.record("the list should have gone with the permission"); return
        }
    }

    // ── letting people in without a signal ───────────────────────────────────

    @Test func anArrivalWithNoSignalIsKeptWithItsTimeAndCountedOnce() async throws {
        let line = Line()
        let cache = Self.cache()
        let (store, _, door) = try await openedOnline(line, cache)
        let daniel = try #require(door.expected.first { $0.hostedEventBookingId == Self.danielBooking })

        line.up = false
        guard case .kept(let kept) = await store.arrive(door, organization: Self.orgId, party: daniel) else { Issue.record("kept"); return }
        #expect(kept.peopleIn == door.peopleIn + daniel.partySize)
        #expect(kept.expected.first { $0.id == daniel.id }?.isIn == true)

        // Pressed again in the dark: still one arrival.
        _ = await store.arrive(kept, organization: Self.orgId, party: daniel)
        let waiting = store.waiting(for: Self.eventId)
        #expect(waiting.count == 1)
        #expect(waiting.first?.arrivedUtc == Self.cameIn)
        #expect(waiting.first?.kind == .arrive)
    }

    @Test func lookingUpAPassShowsWhoTheyAreAndRecordsNothing() async throws {
        let line = Line()
        let cache = Self.cache()
        let (store, transport, door) = try await openedOnline(line, cache)
        let admitted = try fixture("hosted-scan-admitted")
        line.answer = { _ in (admitted, 200) }

        guard case .found(let result, let party) = await store.lookUp(door, organization: Self.orgId, token: "fixture-pass-token") else {
            Issue.record("found"); return
        }
        #expect(result.leadName == "Daniel Park")
        #expect(party?.code == "-TOKEN")
        let sent = String(decoding: transport.requests.last?.httpBody ?? Data(), as: UTF8.self)
        #expect(sent.contains(#""checkIn":false"#))
        #expect(store.waiting(for: Self.eventId).isEmpty)

        let refused = try fixture("hosted-scan-refused")
        line.answer = { _ in (refused, 200) }
        guard case .refused(let why) = await store.lookUp(door, organization: Self.orgId, token: "not-a-pass") else { Issue.record("refused"); return }
        #expect(why.contains("look them up by name"))
    }

    @Test func checkingInSomeOfAPartySendsTheScanThenTheHeadCount() async throws {
        let line = Line()
        let cache = Self.cache()
        let (store, transport, door) = try await openedOnline(line, cache)
        let admitted = try fixture("hosted-scan-admitted")
        let doorBody = try fixture("hosted-door")
        line.answer = { request in request.url!.path.hasSuffix("/door/scan") ? (admitted, 200) : (doorBody, 200) }
        let daniel = try #require(door.expected.first { $0.hostedEventBookingId == Self.danielBooking })

        guard case .checkedIn = await store.checkIn(door, organization: Self.orgId, token: "fixture-pass-token", party: daniel, people: 1) else {
            Issue.record("checked in"); return
        }
        let paths = transport.requests.map { $0.url!.path }
        let scan = try #require(paths.lastIndex { $0.hasSuffix("/door/scan") })
        let arrive = try #require(paths.lastIndex { $0.hasSuffix("/door/arrive") })
        #expect(scan < arrive)
        #expect(String(decoding: transport.requests[scan].httpBody ?? Data(), as: UTF8.self).contains(#""checkIn":true"#))
        #expect(String(decoding: transport.requests[arrive].httpBody ?? Data(), as: UTF8.self).contains(#""people":1"#))
    }

    @Test func aScanWithNoSignalIsFoundOnTheKeptListAndCheckedInWithItsToken() async throws {
        let line = Line()
        let cache = Self.cache()
        let (store, _, door) = try await openedOnline(line, cache)

        line.up = false
        guard case .foundOnThePhone(let party) = await store.lookUp(door, organization: Self.orgId, token: "fixture-pass-token") else {
            Issue.record("found on the phone"); return
        }
        #expect(party.leadName == "Daniel Park")
        #expect(store.waiting(for: Self.eventId).isEmpty)

        guard case .kept(let kept) = await store.checkIn(door, organization: Self.orgId, token: "fixture-pass-token", party: party) else {
            Issue.record("kept"); return
        }
        #expect(kept.expected.first { $0.id == party.id }?.isIn == true)
        #expect(store.waiting(for: Self.eventId).first?.token == "fixture-pass-token")

        guard case .notOnTheSavedList = await store.lookUp(kept, organization: Self.orgId, token: "a-pass-from-another-event") else {
            Issue.record("unknown code"); return
        }
    }

    @Test func takingBackAnArrivalStillOnThePhoneNeedsNoSignal() async throws {
        let line = Line()
        let cache = Self.cache()
        let (store, transport, door) = try await openedOnline(line, cache)
        let daniel = try #require(door.expected.first { $0.hostedEventBookingId == Self.danielBooking })

        line.up = false
        guard case .kept(let kept) = await store.arrive(door, organization: Self.orgId, party: daniel) else { Issue.record("kept"); return }
        let asked = transport.requests.count

        guard case .done(let back) = await store.undo(kept, organization: Self.orgId, party: daniel) else { Issue.record("undo"); return }
        #expect(back.peopleIn == door.peopleIn)
        #expect(store.waiting(for: Self.eventId).isEmpty)
        #expect(transport.requests.count == asked)
    }

    @Test func whatHasToReachTheServerSaysSoWithNoSignal() async throws {
        let line = Line()
        let cache = Self.cache()
        let (store, _, door) = try await openedOnline(line, cache)
        var arrived = door
        arrived.expected[0].arrivedUtc = Self.cameIn

        line.up = false
        guard case .refused(let undo) = await store.undo(arrived, organization: Self.orgId, party: arrived.expected[0]) else {
            Issue.record("undo of a recorded arrival"); return
        }
        #expect(undo.contains("needs a signal"))

        guard case .refused(let walkUp) = await store.walkUp(door, organization: Self.orgId, people: 2, name: "Pub crowd") else {
            Issue.record("walk-up"); return
        }
        #expect(walkUp.contains("needs a signal"))
    }

    // ── sending what was kept ────────────────────────────────────────────────

    @Test func keptArrivalsAreSentWithTheirTimeAndARefusalIsReportedByName() async throws {
        let line = Line()
        let cache = Self.cache()
        let (store, transport, door) = try await openedOnline(line, cache)
        let daniel = try #require(door.expected.first { $0.hostedEventBookingId == Self.danielBooking })
        let other = try #require(door.expected.first { $0.hostedEventBookingId != Self.danielBooking } ?? Optional(daniel))

        line.up = false
        _ = await store.checkIn(door, organization: Self.orgId, token: "fixture-pass-token", party: daniel)
        if other.id != daniel.id { _ = await store.arrive(door, organization: Self.orgId, party: other) }

        let doorBody = try fixture("hosted-door")
        let withdrawn = Data(#"{"admitted":false,"refusal":"This pass was withdrawn by the venue.","hostedEventBookingId":null,"leadName":null,"partySize":null,"kind":null,"nights":null,"guestNames":null,"alreadyCheckedInUtc":null}"#.utf8)
        line.up = true
        line.answer = { request in request.url!.path.hasSuffix("/door/scan") ? (withdrawn, 200) : (doorBody, 200) }

        let report = await store.sendWaiting()
        #expect(report.refused == ["Daniel Park: This pass was withdrawn by the venue."])
        #expect(report.sent == (other.id != daniel.id ? 1 : 0))
        #expect(report.stillWaiting == 0)
        #expect(store.waiting(for: Self.eventId).isEmpty)

        let scan = try #require(transport.requests.last { $0.url!.path.hasSuffix("/door/scan") })
        let body = String(decoding: scan.httpBody ?? Data(), as: UTF8.self)
        #expect(body.contains(#""token":"fixture-pass-token""#))
        #expect(body.contains(#""arrivedUtc":"2026-10-17T00:00:00Z""#))
    }

    @Test func sendingStopsAtTheFirstThatCannotGetThroughAndKeepsTheOrder() async throws {
        let line = Line()
        let cache = Self.cache()
        let (store, _, door) = try await openedOnline(line, cache)
        let daniel = try #require(door.expected.first { $0.hostedEventBookingId == Self.danielBooking })

        line.up = false
        _ = await store.arrive(door, organization: Self.orgId, party: daniel)
        let report = await store.sendWaiting()
        #expect(report.sent == 0)
        #expect(report.stillWaiting == 1)
        #expect(store.waiting(for: Self.eventId).count == 1)
    }

    @Test func aListReadWhileArrivalsAreStillWaitingShowsThemIn() async throws {
        let line = Line()
        let cache = Self.cache()
        let (store, _, door) = try await openedOnline(line, cache)
        let daniel = try #require(door.expected.first { $0.hostedEventBookingId == Self.danielBooking })

        line.up = false
        _ = await store.arrive(door, organization: Self.orgId, party: daniel)

        line.up = true
        guard case .live(let reread) = await store.loadDoor(organization: Self.orgId, event: Self.eventId) else { Issue.record("live"); return }
        #expect(reread.expected.first { $0.id == daniel.id }?.isIn == true)
    }

    @Test func theListOfDoorsIsThereWithNoSignalAndSigningOutForgetsEverything() async throws {
        let line = Line()
        let cache = Self.cache()
        let duties = try fixture("hosted-duties")
        line.answer = { _ in (duties, 200) }
        let (store, _) = Self.store(line, cache: cache)
        guard case .live = await store.loadDuties() else { Issue.record("live"); return }

        line.up = false
        guard case .saved(let saved, _) = await store.loadDuties(), !saved.isEmpty else { Issue.record("saved"); return }

        store.forgetEverything()
        guard case .failed = await store.loadDuties() else { Issue.record("forgotten"); return }
    }
}
