import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Sessions on the device: what they hold, what survives a crash, and what a broken database
/// looks like to a person.
@Suite("Field sessions — the store")
@MainActor
struct FieldSessionStoreTests {

    private func makeStore(now: @escaping @Sendable () -> Date = Date.init)
        throws -> (FieldSessionStore, URL) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("field-\(UUID().uuidString)", isDirectory: true)
        let store = FieldSessionStore(
            database: try .inMemory(),
            files: SessionFileStore(root: root),
            deviceModel: "iPhone17,1",
            now: now)
        return (store, root)
    }

    @Test func startingASessionCreatesItsDirectoryBeforeItsRow() throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }

        let id = try store.startSession(locationLabel: "  Back bedroom  ")
        store.load()

        // The directory exists before anything can appear in the list, so a session in the list
        // always has somewhere to write.
        #expect(FileManager.default.fileExists(atPath: store.files.directory(for: id).path))
        #expect(FileManager.default.fileExists(atPath: store.files.mediaDirectory(for: id).path))

        let session = try #require(store.summary(for: id))
        #expect(session.locationLabel == "Back bedroom")   // trimmed
        #expect(session.isRecording)
        #expect(store.activeSessionId == id)
    }

    @Test func aSessionWithNoLabelStillHasSomethingToCallItself() throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }

        let id = try store.startSession(locationLabel: nil)
        store.load()
        let session = try #require(store.summary(for: id))
        // Never "Session 4" — a date somebody can recognise.
        #expect(!session.title.isEmpty)
        #expect(session.locationLabel == nil)

        try store.link(id, investigationId: UUID(), investigationTitle: "The Old Mill")
        #expect(try #require(store.summary(for: id)).title == "The Old Mill")
    }

    @Test func endingASessionRecordsWhenItStopped() async throws {
        let clock = ManualClock()
        let (store, root) = try makeStore(now: clock.nowProvider)
        defer { try? FileManager.default.removeItem(at: root) }

        let id = try store.startSession(locationLabel: "Cellar")
        clock.advance(by: 3_600)
        try await store.endSession(id)

        let session = try #require(store.summary(for: id))
        #expect(session.outcome == .ended)
        #expect(session.duration == 3_600)
        #expect(store.activeSessionId == nil)
    }

    @Test func aSessionTheAppDiedDuringIsClosedAsInterruptedWithNoInventedEndTime() async throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }

        let id = try store.startSession(locationLabel: "Attic")
        // Five readings landed before the phone died.
        let log = ReadingLog(fileURL: store.files.readingLogURL(for: id))
        for index in 0..<5 {
            try await log.append(FieldReading(at: Date(), sequence: index + 1,
                                              triggeredBy: .interval))
        }
        try await log.close()

        // Relaunch.
        await store.recoverInterruptedSessions()

        let session = try #require(store.summary(for: id))
        #expect(session.outcome == .interrupted)
        // The honest answer to "when did it stop" is that nobody knows. Stamping the relaunch
        // time would invent a fact a reviewer would then reason from.
        #expect(session.endedAt == nil)
        #expect(session.readingCount == 5)
        #expect(store.activeSessionId == nil)
    }

    @Test func deletingASessionTakesItsFilesWithIt() throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }

        let id = try store.startSession(locationLabel: "Hall")
        let directory = store.files.directory(for: id)
        #expect(FileManager.default.fileExists(atPath: directory.path))

        try store.delete(id)
        store.load()

        #expect(store.summary(for: id) == nil)
        // A session that vanished from the list while its recordings stayed would quietly fill
        // the phone.
        #expect(!FileManager.default.fileExists(atPath: directory.path))
    }

    @Test func sessionsAreListedNewestFirst() throws {
        let clock = ManualClock()
        let (store, root) = try makeStore(now: clock.nowProvider)
        defer { try? FileManager.default.removeItem(at: root) }

        _ = try store.startSession(locationLabel: "First")
        clock.advance(by: 600)
        _ = try store.startSession(locationLabel: "Second")
        store.load()

        #expect(store.sessions.map(\.locationLabel) == ["Second", "First"])
    }

    @Test func aStoreWithNoDatabaseSaysSoInsteadOfLookingEmpty() throws {
        // An empty list would tell somebody their sessions were gone. A refusal is a state.
        let store = FieldSessionStore(
            database: nil,
            files: SessionFileStore(root: FileManager.default.temporaryDirectory),
            deviceModel: "iPhone17,1")

        guard case .unavailable(let reason) = store.state else {
            Issue.record("expected an explained refusal"); return
        }
        #expect(!reason.isEmpty)
        #expect(throws: FieldSessionError.self) { try store.startSession(locationLabel: "x") }
    }

    @Test func theDeviceModelIsTheHardwareIdentifierNotAFriendlyName() {
        // `device.model` in an exported bundle: "iPhone" would not let anyone assess a reading
        // for known quirks.
        let identifier = DeviceModel.identifier()
        #expect(!identifier.isEmpty)
        #expect(identifier != "iPhone")
    }

    @Test func mediaPathsAreRelativeAndDoNotCollide() throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let id = try store.startSession(locationLabel: "Kitchen")

        let first = try store.files.nextMediaPath(for: id, kind: .photo, fileExtension: "jpg")
        #expect(first.relative == "media/photo-001.jpg")
        FileManager.default.createFile(atPath: first.url.path, contents: Data("x".utf8))

        let second = try store.files.nextMediaPath(for: id, kind: .photo, fileExtension: "jpg")
        #expect(second.relative == "media/photo-002.jpg")

        // These are the paths that end up in an exported bundle, where absolute paths are a
        // security boundary — so they must survive the spec's own filename rule.
        #expect(FieldReading.FileRef.relative(second.relative) != nil)
    }
}
