import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Joining the group's session a lead launched (item 252). Ben, 2026-09-28: "instead of forcing
/// them to join" — the card or the push opens Field Kit already set to the thing, and nothing
/// records until Start. The fixtures are the real API's answers for a launch on a client's visit.
@Suite("Field launches — joining the group's session")
@MainActor
struct FieldLaunchTests {

    @Test func aLaunchDecodesAsTheServerSendsIt() throws {
        let launch = try BenJSON.decoder.decode(FieldLaunchRecord.self,
                                                from: try Fixtures.data("field-launch", in: Bundle.module))
        #expect(launch.target == "investigation")
        #expect(launch.investigationId != nil)
        #expect(launch.orgCalendarEventId == nil)
        #expect(launch.title == "Evening visit")
        #expect(launch.locationLabel == "The front parlour")
        #expect(launch.isPublic == false)
        #expect(launch.expiresUtc.timeIntervalSince(launch.endsUtc) == 6 * 3600)
        #expect(DeepLinkParser.parse(try #require(URL(string: launch.appLink))) == .fieldLaunch(launch.id))
    }

    @Test func theFeedCardCarriesItsLaunchAndAnOrdinaryPostDoesNot() throws {
        let page = try BenJSON.decoder.decode(FeedPageRecord.self,
                                              from: try Fixtures.data("feed-page-with-launch-card", in: Bundle.module))
        let card = try #require(page.posts.first?.launch)
        #expect(card.title == "Evening visit")
        #expect(card.appLink.hasPrefix("ishaunted://field-kit/launch/"))

        let ordinary = try BenJSON.decoder.decode(FeedPageRecord.self,
                                                  from: try Fixtures.data("feed-page", in: Bundle.module))
        #expect(ordinary.posts.allSatisfy { $0.launch == nil })
    }

    @Test func theLinkIsParsedFromTheSchemeAndTheWebsiteAlike() throws {
        let id = UUID()
        #expect(DeepLinkParser.parse(URL(string: "ishaunted://field-kit/launch/\(id.uuidString)")!) == .fieldLaunch(id))
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/field-kit/launch/\(id.uuidString)")!) == .fieldLaunch(id))
        #expect(DeepLinkParser.parse(URL(string: "ishaunted://field-kit/launch/not-an-id")!) == nil)
        #expect(DeepLinkParser.parse(URL(string: "ishaunted://field-kit")!) == nil)
    }

    @Test func theLeadsCodeAndTheLeadsNotificationHaveLinksOfTheirOwn() throws {
        let id = UUID()
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/field-kit/join/Ab3_-x9")!) == .fieldJoin(token: "Ab3_-x9"))
        #expect(DeepLinkParser.parse(URL(string: "ishaunted://field-kit/join/Ab3_-x9")!) == .fieldJoin(token: "Ab3_-x9"))
        #expect(DeepLinkParser.parse(URL(string: "ishaunted://field-kit/launch/\(id.uuidString)/requests")!) == .fieldLaunchRequests(id))
        // The plain launch link still joins; it is not mistaken for the requests list.
        #expect(DeepLinkParser.parse(URL(string: "ishaunted://field-kit/launch/\(id.uuidString)")!) == .fieldLaunch(id))
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/field-kit/somewhere/else")!) == nil)
    }

    @Test func whereAScannerStandsDecodesAsTheServerSendsIt() throws {
        let asking = try BenJSON.decoder.decode(JoinStandingRecord.self, from: try Fixtures.data("field-launch-join-ask", in: Bundle.module))
        #expect(asking.standing == "ask")
        #expect(!asking.isIn)
        #expect(asking.launch == nil)                        // not in: nothing to open yet
        #expect(asking.title == "Séance at the Union Station Hotel")

        let inside = try BenJSON.decoder.decode(JoinStandingRecord.self, from: try Fixtures.data("field-launch-join-in", in: Bundle.module))
        #expect(inside.isIn)
        let launch = try #require(inside.launch)
        #expect(launch.id == inside.launchId)
        #expect(launch.joinToken == nil)                      // a guest is never handed the lead's code

        let asked = try BenJSON.decoder.decode([JoinRequestRecord].self, from: try Fixtures.data("field-launch-join-requests", in: Bundle.module))
        #expect(asked.map(\.status) == ["pending"])
        #expect(asked.first?.displayName.isEmpty == false)
    }

    @Test func aTokenIsEscapedForItsPath() {
        #expect(FieldLaunchActions.escaped("Ab3_-x9") == "Ab3_-x9")
        #expect(FieldLaunchActions.escaped("a/b c") == "a%2Fb%20c")
    }

    private func makeStore() throws -> (FieldSessionStore, URL) {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("launch-\(UUID().uuidString)", isDirectory: true)
        return (FieldSessionStore(database: try .inMemory(), files: SessionFileStore(root: root), deviceModel: "iPhone17,1"), root)
    }

    private static func launch(target: String, investigationId: UUID? = nil, orgCalendarEventId: UUID? = nil,
                               hostedEventId: UUID? = nil, location: String? = "Printers Alley") -> FieldLaunchRecord {
        FieldLaunchRecord(id: UUID(), target: target, investigationId: investigationId,
                          orgCalendarEventId: orgCalendarEventId, hostedEventId: hostedEventId,
                          title: "Saturday walk", locationLabel: location, organizationName: "Nashville Ghost Walks",
                          launchedByName: "Gideon", launchedUtc: Date(), endsUtc: Date().addingTimeInterval(7200),
                          expiresUtc: Date().addingTimeInterval(7200 + 6 * 3600), isPublic: true,
                          appLink: "ishaunted://field-kit/launch/x")
    }

    @Test func joiningATourDateOpensASessionAlreadySetToItAndRecordsNothingYet() throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let tourDate = UUID()
        let launch = Self.launch(target: "event", orgCalendarEventId: tourDate)

        let id = try store.startSession(joining: launch)
        store.load()

        let session = try #require(store.summary(for: id))
        #expect(session.isPending)                       // nothing records until Start
        #expect(session.locationLabel == "Printers Alley")
        #expect(session.orgCalendarEventId == tourDate)
        #expect(session.eventTitle == "Saturday walk")
        #expect(session.investigationId == nil)
        #expect(session.fieldLaunchId == launch.id)
    }

    @Test func joiningAnInvestigationFilesTheSessionUnderIt() throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let visit = UUID()

        let id = try store.startSession(joining: Self.launch(target: "investigation", investigationId: visit))
        store.load()

        let session = try #require(store.summary(for: id))
        #expect(session.investigationId == visit)
        #expect(session.investigationTitle == "Saturday walk")
        #expect(session.eventTitle == nil)
    }

    @Test func aWithheldPlaceIsLabelledWithWhatItIs() throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }

        let id = try store.startSession(joining: Self.launch(target: "hosted-event", hostedEventId: UUID(), location: nil))
        store.load()

        #expect(try #require(store.summary(for: id)).title == "Saturday walk")
    }
}

/// Sending a joined session, and a long one in parts (item 252).
@Suite("Field launches — sending what was recorded")
struct FieldLaunchSendingTests {

    @Test func eachPartOfASessionHasItsOwnIdAndTheSamePartKeepsIt() {
        let session = UUID()
        let start = Date(timeIntervalSince1970: 1_790_000_000)
        let first = SessionWindow(start: start, end: start.addingTimeInterval(600))
        let second = SessionWindow(start: start.addingTimeInterval(600), end: start.addingTimeInterval(1200))

        #expect(first.uploadId(for: session) != second.uploadId(for: session))      // the next ten minutes is its own
        #expect(first.uploadId(for: session) != session)
        #expect(first.uploadId(for: session) == first.uploadId(for: session))       // a retry replaces only itself
        // A handle nudged by a fraction of a second is the same part, not a new one.
        let nudged = SessionWindow(start: start.addingTimeInterval(0.2), end: start.addingTimeInterval(600.3))
        #expect(nudged.uploadId(for: session) == first.uploadId(for: session))
        #expect(first.uploadId(for: UUID()) != first.uploadId(for: session))         // another session's is its own
    }

    @Test func aJoinedSessionIsSentWithItsLaunchAndNoInvestigation() async throws {
        let transport = MockTransport(status: 200, body: Data("""
        {"id":"\(UUID().uuidString.lowercased())","investigationId":null,
         "deviceSessionId":"\(UUID().uuidString.lowercased())","readingCount":3,
         "markerCount":0,"recordedByName":null,"files":[]}
        """.utf8))
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        let client = FieldUploadClient(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
        let bundle = FileManager.default.temporaryDirectory.appendingPathComponent("launch-\(UUID().uuidString).ben")
        try Data("PK".utf8).write(to: bundle)
        defer { try? FileManager.default.removeItem(at: bundle) }
        let launch = UUID()

        _ = await client.submitBundle(at: bundle, deviceSessionId: UUID(), investigationId: nil,
                                      recordedByAppUserId: nil, recordedByName: nil, fieldLaunchId: launch)

        let body = String(decoding: transport.requests.first?.httpBody ?? Data(), as: UTF8.self)
        #expect(body.contains("name=\"fieldLaunchId\""))
        #expect(body.contains(launch.uuidString))
        #expect(!body.contains("name=\"investigationId\""))
        #expect(!body.contains("name=\"orgCalendarEventId\""))
    }
}
