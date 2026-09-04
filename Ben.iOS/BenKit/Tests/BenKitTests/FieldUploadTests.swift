import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Sending a session up, and what may safely be deleted afterwards.
@Suite("Field session upload")
@MainActor
struct FieldUploadTests {

    private func makeStore() throws -> (FieldSessionStore, URL) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("upload-\(UUID().uuidString)", isDirectory: true)
        let store = FieldSessionStore(
            database: try .inMemory(),
            files: SessionFileStore(root: root),
            deviceModel: "iPhone17,1",
            sensors: { SensorSuite() })
        return (store, root)
    }

    /// A session with one capture whose bytes really exist on disk.
    private func sessionWithACapture(_ store: FieldSessionStore) async throws -> UUID {
        let id = try store.startSession(locationLabel: "Cellar")
        await store.activate(id, channels: [.magnetic])
        try await store.beginRecording(id)   // item 215: Start opens the log
        let path = try store.files.nextMediaPath(for: id, kind: .photo, fileExtension: "jpg")
        try Data(repeating: 0xAA, count: 2_048).write(to: path.url)
        await store.active?.noteCapture(kind: .photo, relativePath: path.relative,
                                        byteCount: 2_048)
        try await store.endSession(id)
        return id
    }

    // MARK: - The request

    @Test func theDocumentGoesUpWithEverythingTheServerNeedsToAttributeIt() async {
        let transport = MockTransport(status: 200, body: Data("""
        {"id":"\(UUID().uuidString.lowercased())","investigationId":null,
         "deviceSessionId":"\(UUID().uuidString.lowercased())","readingCount":12,
         "markerCount":2,"recordedByName":"A Member","files":[]}
        """.utf8))
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport,
                                  environment: { .dev })
        let client = FieldUploadClient(
            api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))

        let deviceSessionId = UUID()
        let recordedBy = UUID()
        _ = await client.submitDocument(Data("{}".utf8), deviceSessionId: deviceSessionId,
                                        investigationId: nil,
                                        recordedByAppUserId: recordedBy,
                                        recordedByName: "A Member")

        let body = String(decoding: transport.requests.first?.httpBody ?? Data(), as: UTF8.self)
        #expect(body.contains("name=\"deviceSessionId\""))
        #expect(body.contains(deviceSessionId.uuidString))
        #expect(body.contains(recordedBy.uuidString))
        #expect(body.contains("A Member"))
        // No investigation means the field is ABSENT, not empty — an empty value would read as
        // "an investigation I could not name" rather than "there isn't one".
        #expect(!body.contains("name=\"investigationId\""))
    }

    @Test func choosingAnInvestigationSendsIt() async {
        let transport = MockTransport(status: 200, body: Data("""
        {"id":"\(UUID().uuidString.lowercased())","investigationId":null,
         "deviceSessionId":"\(UUID().uuidString.lowercased())","readingCount":0,
         "markerCount":0,"recordedByName":null,"files":[]}
        """.utf8))
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport,
                                  environment: { .dev })
        let client = FieldUploadClient(
            api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))

        let investigation = UUID()
        _ = await client.submitDocument(Data("{}".utf8), deviceSessionId: UUID(),
                                        investigationId: investigation,
                                        recordedByAppUserId: nil, recordedByName: nil)

        let body = String(decoding: transport.requests.first?.httpBody ?? Data(), as: UTF8.self)
        #expect(body.contains(investigation.uuidString))
    }

    @Test func aRefusedUploadKeepsTheServersOwnSentence() async {
        let transport = MockTransport(
            status: 400, body: Data("That doesn't look like a session document.".utf8))
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport,
                                  environment: { .dev })
        let client = FieldUploadClient(
            api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))

        let result = await client.submitDocument(Data("{}".utf8), deviceSessionId: UUID(),
                                                 investigationId: nil,
                                                 recordedByAppUserId: nil, recordedByName: nil)
        guard case .failure(let error) = result else { Issue.record("expected a refusal"); return }
        #expect(error.message == "That doesn't look like a session document.")
    }

    // MARK: - What may be deleted

    @Test func aSessionIsOnlySafeToClearOnceEveryFileIsUp() async throws {
        // The whole point of the mark: "safe to delete" must mean the bytes exist somewhere
        // else, not that the document arrived.
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let id = try await sessionWithACapture(store)

        #expect(store.isFullyUploaded(id) == false)

        store.markUploaded(id, serverSessionId: UUID())
        // Document up, file not — still not safe.
        #expect(store.isFullyUploaded(id) == false)

        let capture = try #require(store.captures(for: id).first)
        store.markFileUploaded(capture.id, in: id)
        #expect(store.isFullyUploaded(id))
    }

    @Test func aFailedFileRecordsWhyRatherThanLookingUploaded() async throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let id = try await sessionWithACapture(store)
        let capture = try #require(store.captures(for: id).first)

        store.markUploaded(id, serverSessionId: UUID())
        store.markFileUploaded(capture.id, in: id, problem: "The connection dropped.")

        #expect(store.isFullyUploaded(id) == false)
    }

    @Test func clearingTheFilesKeepsTheSessionAndItsReadings() async throws {
        // Deleting recordings to free the phone must not cost the trace of the night. The
        // readings are small; the video is not.
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let id = try await sessionWithACapture(store)
        let capture = try #require(store.captures(for: id).first)

        #expect(store.hasLocalFile(capture.relativePath, in: id))

        try store.deleteLocalMedia(for: id)

        #expect(store.hasLocalFile(capture.relativePath, in: id) == false)
        // The session, its counts and the record that the capture EXISTED all survive.
        #expect(store.summary(for: id) != nil)
        #expect(store.captures(for: id).count == 1)
        #expect(FileManager.default.fileExists(
            atPath: store.files.readingLogURL(for: id).path))
    }

    @Test func deletingOneCaptureTakesItsBytesAndItsRow() async throws {
        // For the ones somebody simply does not want to keep — a blurred photo, a clip of
        // nothing.
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let id = try await sessionWithACapture(store)
        let capture = try #require(store.captures(for: id).first)

        try store.deleteCapture(capture.id, in: id)

        #expect(store.captures(for: id).isEmpty)
        #expect(store.hasLocalFile(capture.relativePath, in: id) == false)
        #expect(store.summary(for: id)?.captureCount == 0)
    }

    @Test func deletingTheWholeSessionStillTakesEverythingWithIt() async throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let id = try await sessionWithACapture(store)

        try store.delete(id)

        #expect(store.summary(for: id) == nil)
        #expect(!FileManager.default.fileExists(atPath: store.files.directory(for: id).path))
    }
}
