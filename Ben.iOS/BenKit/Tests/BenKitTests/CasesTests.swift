import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// The client's cases (iOS Slice 6): the live shapes, the timeline's ordering, and the
/// refusals that must not read as emptiness.
@Suite("Cases — the client's own view")
@MainActor
struct CasesTests {

    private static func store(_ transport: MockTransport) -> CasesStore {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return CasesStore(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    private static func detailStore(_ transport: MockTransport, id: UUID = UUID()) -> CaseDetailStore {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return CaseDetailStore(caseId: id, api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    @Test func theLiveListFixtureDecodes() async throws {
        let data = try Fixtures.data("my-cases", in: Bundle.module)
        let store = Self.store(MockTransport(status: 200, body: data))
        await store.load()

        #expect(store.state == .loaded)
        let first = try #require(store.cases.first)
        #expect(first.caseReference.hasPrefix("#"))
        #expect(!first.title.isEmpty)
    }

    @Test func theLiveDetailFixtureDecodesWithItsTimeline() async throws {
        let data = try Fixtures.data("my-case-detail", in: Bundle.module)
        let store = Self.detailStore(MockTransport(status: 200, body: data))
        await store.load()

        let detail = try #require(store.detail)
        #expect(detail.caseReference.hasPrefix("#"))
        #expect(detail.isPrimaryClient)
        #expect(!detail.occurrences.isEmpty)
        // A closed case carries its closing date; the fixture is one.
        #expect(detail.dateCaseClosed != nil)
    }

    @Test func theTimelineIsOrderedByWhenThingsHappened() throws {
        // Not by when they were typed: somebody logging three months of experiences in one
        // sitting must not have them read as all happening that evening.
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        func entry(event: Date?, created: Date) -> MyCaseOccurrence {
            MyCaseOccurrence(
                id: UUID(), entryType: .occurrence, eventDateTime: event,
                title: nil, body: nil, fromInvestigators: false,
                dateCreated: created, files: [], experienceTypeIds: [])
        }
        let old = entry(event: now.addingTimeInterval(-86_400 * 30), created: now)
        let recent = entry(event: now.addingTimeInterval(-3600), created: now.addingTimeInterval(-86_400))

        let detail = MyCaseDetail(
            caseId: UUID(), caseReference: "#2026-001", title: "T", city: nil, state: nil,
            status: .active, description: nil, caseManagerDisplayName: nil,
            dateCaseOpened: now, dateCaseClosed: nil,
            occurrences: [old, recent], investigations: [],
            unreadMessageCount: 0, isPrimaryClient: true, contacts: [])

        #expect(detail.timeline.map(\.id) == [recent.id, old.id])
    }

    @Test func anEntryWithNoEventDateFallsBackToWhenItWasWritten() throws {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        let undated = MyCaseOccurrence(
            id: UUID(), entryType: .note, eventDateTime: nil, title: "note",
            body: nil, fromInvestigators: true, dateCreated: now,
            files: [], experienceTypeIds: [])
        let older = MyCaseOccurrence(
            id: UUID(), entryType: .occurrence, eventDateTime: now.addingTimeInterval(-7200),
            title: nil, body: nil, fromInvestigators: false,
            dateCreated: now.addingTimeInterval(-7200), files: [], experienceTypeIds: [])

        let detail = MyCaseDetail(
            caseId: UUID(), caseReference: "#1", title: "T", city: nil, state: nil,
            status: .active, description: nil, caseManagerDisplayName: nil,
            dateCaseOpened: now, dateCaseClosed: nil,
            occurrences: [older, undated], investigations: [],
            unreadMessageCount: 0, isPrimaryClient: true, contacts: [])

        #expect(detail.timeline.first?.id == undated.id)
    }

    @Test func anUnknownStatusDecodesRatherThanThrowing() throws {
        struct Box: Decodable { let status: CaseStatus }
        #expect(try BenJSON.decoder.decode(Box.self, from: Data(#"{"status":99}"#.utf8)).status == .unknown)
        #expect(try BenJSON.decoder.decode(Box.self, from: Data(#"{"status":1}"#.utf8)).status == .active)
    }

    @Test func signedOutIsAFactNotAnError() async {
        let store = Self.store(MockTransport(status: 401))
        await store.load()
        #expect(store.state == .signedOut)
        #expect(store.cases.isEmpty)
    }

    @Test func aRefusalIsNotAnEmptyList() async {
        // The distinction this codebase exists to keep: "you may not see this" and "there is
        // nothing here" are different answers and must render differently.
        let store = Self.store(MockTransport(status: 403, body: Data("Not yours.".utf8)))
        await store.load()
        #expect(store.state == .failed(reason: "Not yours."))
        #expect(store.cases.isEmpty)
    }

    @Test func aMissingCaseSaysUnavailableWithoutConfirmingItExists() async {
        // 404 covers both "gone" and "not yours". Saying "that case isn't yours" would
        // confirm the case exists to somebody guessing ids.
        let store = Self.detailStore(MockTransport(status: 404))
        await store.load()
        #expect(store.state == .failed(reason: "That case isn't available."))
    }

    @Test func caseFilesGoThroughTheAuthenticatedSharedRoute() {
        // AsyncImage would 401 on this; the loader carries the token instead.
        let fileId = UUID()
        let endpoint = CaseDetailStore.fileEndpoint(fileId)
        #expect(endpoint.path == "api/upload-files/\(fileId.uuidString.lowercased())/download")
        #expect(endpoint.requiresAuth)
    }
}
