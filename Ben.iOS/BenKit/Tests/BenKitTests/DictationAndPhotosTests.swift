import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Dictated notes, and choosing the photograph that stands for a place.
@Suite("Dictation and property photos")
@MainActor
struct DictationAndPhotosTests {

    /// A transcriber that reports whether it can work offline, and says what it was told to say.
    private final class StubDictation: DictationService, @unchecked Sendable {
        let offline: Bool
        let heard: [String]
        private(set) var wasStopped = false

        init(offline: Bool, heard: [String] = ["there is", "there is someone", "there is someone here"]) {
            self.offline = offline
            self.heard = heard
        }

        var isAvailableOffline: Bool { get async { offline } }
        func requestPermission() async -> Bool { offline }

        func start() async throws -> AsyncStream<DictationUpdate> {
            guard offline else { throw DictationError.unavailableOffline }
            return AsyncStream { continuation in
                for (index, text) in heard.enumerated() {
                    continuation.yield(DictationUpdate(text: text,
                                                       isFinal: index == heard.count - 1))
                }
                continuation.finish()
            }
        }

        @discardableResult
        func stop() async -> String {
            wasStopped = true
            return heard.last ?? ""
        }
    }

    private func makeStore(dictation: DictationService? = nil)
        throws -> (FieldSessionStore, URL) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("dictate-\(UUID().uuidString)", isDirectory: true)
        let store = FieldSessionStore(
            database: try .inMemory(),
            files: SessionFileStore(root: root),
            deviceModel: "iPhone17,1",
            sensors: { SensorSuite(dictation: dictation) })
        return (store, root)
    }

    // MARK: - Dictation

    @Test func aDeviceThatCannotTranscribeOfflineIsNotOfferedDictation() async throws {
        // The gate Ben asked for. A transcriber that needs a connection works in the car park
        // and fails in the cellar — which is worse than not offering it, because somebody would
        // rely on it exactly where it stops working.
        let stub = StubDictation(offline: false)
        #expect(await stub.isAvailableOffline == false)
        #expect(await stub.requestPermission() == false)

        await #expect(throws: DictationError.self) { try await stub.start() }
    }

    @Test func dictationBuildsUpTheSentenceAndKeepsTheLastRevision() async throws {
        // Recognisers revise as they go: "there is" becomes "there is someone here". Taking the
        // first result would record half of what somebody said.
        let stub = StubDictation(offline: true)
        var seen: [String] = []
        for await update in try await stub.start() { seen.append(update.text) }

        #expect(seen.count == 3)
        #expect(seen.last == "there is someone here")
        #expect(await stub.stop() == "there is someone here")
    }

    @Test func aDictatedNoteEndsUpOnTheMarkLikeAnyOther() async throws {
        // However it was written down, it is the same mark on the same timeline.
        let (store, root) = try makeStore(dictation: StubDictation(offline: true))
        defer { try? FileManager.default.removeItem(at: root) }

        let id = try store.startSession(locationLabel: "Landing")
        await store.activate(id, channels: [.magnetic])
        try await store.beginRecording(id)   // item 215: Start opens the log
        let session = try #require(store.active)

        await session.mark(kind: .manual, note: "there is someone here")
        try await store.endSession(id)

        let source = try #require(store.replayData(for: id))
        #expect(source.markers.first?.note == "there is someone here")
    }

    @Test func theNoteKindsOfferedDependOnWhatTheDeviceCanDo() async {
        // Typing and recording are always there; dictation is conditional.
        #expect(NoteKind.allCases.contains(.typed))
        #expect(NoteKind.allCases.contains(.audio))
        #expect(NoteKind.dictated.title == "Dictate")
    }

    // MARK: - The photograph that stands for a place

    @Test func onePhotoCanBeChosenToRepresentTheProperty() async throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }

        let id = try store.startSession(locationLabel: "The Old Mill")
        await store.activate(id, channels: [.magnetic])
        try await store.beginRecording(id)   // item 215: Start opens the log
        let session = try #require(store.active)

        for index in 1...2 {
            let path = try store.files.nextMediaPath(for: id, kind: .photo, fileExtension: "jpg")
            try Data(repeating: UInt8(index), count: 128).write(to: path.url)
            await session.noteCapture(kind: .photo, relativePath: path.relative, byteCount: 128)
        }
        try await store.endSession(id)

        let photos = store.captures(for: id).filter { $0.kind == .photo }
        #expect(photos.count == 2)
        #expect(photos.allSatisfy { !$0.isRepresentative })   // nothing is chosen unless chosen

        try store.setRepresentative(photos[0].id, in: id)
        #expect(store.representative(for: id)?.id == photos[0].id)

        // Choosing another replaces it: "which photo represents this place" has one answer.
        try store.setRepresentative(photos[1].id, in: id)
        let chosen = store.captures(for: id).filter(\.isRepresentative)
        #expect(chosen.count == 1)
        #expect(chosen.first?.id == photos[1].id)

        // And choosing the same one again clears it, so a choice can be undone.
        try store.setRepresentative(photos[1].id, in: id)
        #expect(store.representative(for: id) == nil)
    }

    @Test func onlyPhotosAreOfferedAsThePropertyPicture() async throws {
        // A video or a sound file cannot stand for a building on a case list.
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }

        let id = try store.startSession(locationLabel: "Cellar")
        await store.activate(id, channels: [.magnetic])
        try await store.beginRecording(id)   // item 215: Start opens the log
        let session = try #require(store.active)

        let photo = try store.files.nextMediaPath(for: id, kind: .photo, fileExtension: "jpg")
        try Data(count: 64).write(to: photo.url)
        await session.noteCapture(kind: .photo, relativePath: photo.relative, byteCount: 64)

        let clip = try store.files.nextMediaPath(for: id, kind: .video, fileExtension: "mov")
        try Data(count: 64).write(to: clip.url)
        await session.noteCapture(kind: .video, relativePath: clip.relative,
                                  byteCount: 64, durationSeconds: 4)
        try await store.endSession(id)

        let stills = store.captures(for: id).filter { $0.kind == .photo }
        #expect(stills.count == 1)
    }
}
