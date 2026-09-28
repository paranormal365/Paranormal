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
