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

    @Test func loggingAnOccurrenceCreatesTheEntryThenAttachesFiles() async throws {
        let file = FileManager.default.temporaryDirectory
            .appendingPathComponent("occ-\(UUID().uuidString).jpg")
        try Data([0xFF, 0xD8, 0xFF, 0xE0]).write(to: file)
        defer { try? FileManager.default.removeItem(at: file) }

        let entryId = UUID()
        // Captured verbatim from the dev API — an invented shape here is exactly what let a
        // decode bug reach a live run. Only the ids are swapped for the test's own.
        let entryJSON = """
        {"id":"\(entryId.uuidString.lowercased())",
         "caseId":"a2e42fac-f3ac-4277-9066-706a2155b821",
         "authorAppUserId":"036c7175-ef91-4dfe-7d1d-08dee981e41b",
         "authorDisplayName":"AverageBen","entryType":0,
         "eventDateTime":"2026-08-24T02:00:00","title":"Footsteps upstairs","body":"Again.",
         "visibility":0,"investigationId":null,"experienceTypeIds":[],"files":[],
         "dateCreated":"2026-08-25T00:01:22.992675","dateUpdated":null,
         "createdByAppUserId":"036c7175-ef91-4dfe-7d1d-08dee981e41b",
         "updatedByAppUserId":null}
        """
        let transport = MockTransport { request in
            let path = request.url?.path ?? ""
            if path.hasSuffix("/files") {
                // The attach answers OccurrenceFileItem — `fileId`, not `id`.
                return (Data(#"""
                    {"fileId":"\#(UUID().uuidString.lowercased())","fileName":"shot.jpg",
                     "contentType":"image/jpeg","fileSize":4}
                    """#.utf8),
                        MockTransport.response(for: request, status: 200))
            }
            return (Data(entryJSON.utf8), MockTransport.response(for: request, status: 200))
        }
        let store = Self.detailStore(transport)

        let result = await store.logOccurrence(
            eventDateTime: Date(timeIntervalSince1970: 1_800_000_000),
            title: "Footsteps upstairs", body: "Again.",
            media: [MediaUpload(fileURL: file, filename: "shot.jpg",
                                contentType: "image/jpeg", byteCount: 4)])

        guard case .success(let entry) = result else { Issue.record("expected success"); return }
        #expect(entry.id == entryId)
        #expect(store.failedAttachments == 0)

        // The ENTRY first, then the file against it — the other order would leave orphaned
        // files belonging to nothing.
        #expect(transport.requests.count == 2)
        #expect(transport.requests[0].url?.path.hasSuffix("/occurrences") == true)
        #expect(transport.requests[1].url?.path.contains(entryId.uuidString.lowercased()) == true)
        // The file part is named exactly `file` — the controller's IFormFile parameter.
        let attachBody = String(decoding: transport.requests[1].httpBody ?? Data(), as: UTF8.self)
        #expect(attachBody.contains("name=\"file\"; filename=\"shot.jpg\""))
    }

    @Test func aFailedAttachmentDoesNotLoseTheEntry() async throws {
        // The entry EXISTS the moment the first call succeeds. Reporting the whole thing as
        // failed would tell somebody to write it again, creating a duplicate.
        let file = FileManager.default.temporaryDirectory
            .appendingPathComponent("occ-\(UUID().uuidString).jpg")
        try Data([0xFF, 0xD8]).write(to: file)
        defer { try? FileManager.default.removeItem(at: file) }

        let entryJSON = """
        {"id":"\(UUID().uuidString.lowercased())",
         "caseId":"a2e42fac-f3ac-4277-9066-706a2155b821",
         "authorAppUserId":"036c7175-ef91-4dfe-7d1d-08dee981e41b",
         "authorDisplayName":"AverageBen","entryType":0,"eventDateTime":null,
         "title":"T","body":null,"visibility":0,"investigationId":null,
         "experienceTypeIds":[],"files":[],
         "dateCreated":"2026-08-25T00:01:22.992675","dateUpdated":null,
         "createdByAppUserId":"036c7175-ef91-4dfe-7d1d-08dee981e41b",
         "updatedByAppUserId":null}
        """
        let transport = MockTransport { request in
            let path = request.url?.path ?? ""
            return path.hasSuffix("/files")
                ? (Data(), MockTransport.response(for: request, status: 500))
                : (Data(entryJSON.utf8), MockTransport.response(for: request, status: 200))
        }
        let store = Self.detailStore(transport)

        let result = await store.logOccurrence(
            eventDateTime: nil, title: "T", body: nil,
            media: [MediaUpload(fileURL: file, filename: "a.jpg",
                                contentType: "image/jpeg", byteCount: 2)])

        guard case .success = result else { Issue.record("the entry should still be saved"); return }
        #expect(store.failedAttachments == 1)
    }

    @Test func aRefusedOccurrenceKeepsTheServersSentence() async {
        let store = Self.detailStore(MockTransport(status: 400, body: Data("Say what happened.".utf8)))
        let result = await store.logOccurrence(eventDateTime: nil, title: nil, body: nil)
        guard case .failure(let error) = result else { Issue.record("expected refusal"); return }
        #expect(error.message == "Say what happened.")
    }

    @Test func caseFilesGoThroughTheAuthenticatedSharedRoute() {
        // AsyncImage would 401 on this; the loader carries the token instead.
        let fileId = UUID()
        let endpoint = CaseDetailStore.fileEndpoint(fileId)
        #expect(endpoint.path == "api/upload-files/\(fileId.uuidString.lowercased())/download")
        #expect(endpoint.requiresAuth)
    }

    @Test func aFileOnACaseDecodesFromTheServersOwnKey() throws {
        // The server names it `fileId`; decoding it as `id` silently failed the WHOLE case,
        // so a case with one photo read as "the server's answer couldn't be read". Live-caught.
        let json = Data("""
        {"fileId":"11111111-1111-1111-1111-111111111111","fileName":"a.jpg",
         "contentType":"image/jpeg","fileSize":812}
        """.utf8)
        let file = try BenJSON.decoder.decode(MyCaseFile.self, from: json)
        #expect(file.id.uuidString.lowercased().hasPrefix("11111111"))
        #expect(file.isImage)
    }

    @Test func theWriteResponseIsADifferentShapeFromTheReadOne() throws {
        // POST answers CaseTimelineEntryRecord (authorAppUserId, no fromInvestigators);
        // GET answers ClientCaseOccurrence. Two shapes for one row — keep them apart.
        let mine = UUID()
        let json = Data("""
        {"id":"22222222-2222-2222-2222-222222222222",
         "caseId":"33333333-3333-3333-3333-333333333333",
         "authorAppUserId":"\(mine.uuidString)","authorDisplayName":"A Client",
         "entryType":0,"eventDateTime":"2026-08-24T02:15:00","title":"Footsteps",
         "body":null,"experienceTypeIds":[],
         "files":[{"fileId":"44444444-4444-4444-4444-444444444444","fileName":"a.jpg",
                   "contentType":"image/jpeg","fileSize":812}],
         "dateCreated":"2026-08-24T09:00:00"}
        """.utf8)
        let record = try BenJSON.decoder.decode(CaseTimelineEntryRecord.self, from: json)
        #expect(record.files.count == 1)

        // The side is decided by comparing, not assumed: my own entry is not "them".
        #expect(record.asOccurrence(readerId: mine).fromInvestigators == false)
        #expect(record.asOccurrence(readerId: UUID()).fromInvestigators == true)
    }
}
