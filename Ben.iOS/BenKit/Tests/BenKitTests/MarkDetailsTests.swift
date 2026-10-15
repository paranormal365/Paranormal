import Foundation
import Testing
@testable import BenKit

/// Details added to a mark after the session, before it is sent (Ben, 10/15/2026): "They could just be
/// able to add more information about them before submitting them to the web app as a package." Only
/// text, only on marks made during the session, and the mark itself stays exactly as recorded.
@Suite("Marks — details added after the session")
@MainActor
struct MarkDetailsTests {

    private func makeStore() throws -> (FieldSessionStore, URL) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("details-\(UUID().uuidString)", isDirectory: true)
        let store = FieldSessionStore(database: try .inMemory(), files: SessionFileStore(root: root),
                                      deviceModel: "iPhone17,1")
        return (store, root)
    }

    /// A finished session with one cold spot marked in it.
    private func recorded(_ store: FieldSessionStore) async throws -> (UUID, FieldMarkerRecord) {
        let id = try store.startSession(locationLabel: "Cellar")
        await store.activate(id, channels: [.magnetic])
        try await store.beginRecording(id)
        await store.active?.mark(kind: .manual, note: "Cold spot")
        try await store.endSession(id)
        let mark = try #require(store.replayData(for: id)?.markers.first)
        return (id, mark)
    }

    @Test func detailsAreKeptBesideTheMarkWhichStaysAsRecorded() async throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let (id, mark) = try await recorded(store)

        try store.addDetails(id, toMarker: mark.id, "  Left side of the stairs, about ten seconds.  ")

        let after = try #require(store.replayData(for: id)?.markers.first)
        #expect(after.details == "Left side of the stairs, about ten seconds.")
        #expect(after.detailsAddedAt != nil)
        // The moment itself is untouched.
        #expect(after.note == "Cold spot")
        #expect(after.at == mark.at)
        #expect(after.kind == mark.kind)

        // Empty text takes the details away again.
        try store.addDetails(id, toMarker: mark.id, "   ")
        #expect(store.replayData(for: id)?.markers.first?.details == nil)
    }

    @Test func aSentSessionsMarksCannotBeChanged() async throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let (id, mark) = try await recorded(store)
        store.markUploaded(id, serverSessionId: UUID())

        #expect(throws: FieldSessionError.alreadySent) {
            try store.addDetails(id, toMarker: mark.id, "too late")
        }
        #expect(store.replayData(for: id)?.markers.first?.details == nil)
    }

    @Test func thePackageCarriesTheDetailsAndTheReadingsAreUnchanged() async throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let (id, mark) = try await recorded(store)

        let before = try await ReadingLog(fileURL: store.files.readingLogURL(for: id)).rawLines()
        try store.addDetails(id, toMarker: mark.id, "Sarah felt it too.")
        let after = try await ReadingLog(fileURL: store.files.readingLogURL(for: id)).rawLines()
        #expect(before == after, "the recorded readings are never rewritten")

        let bundle = try await store.export(id, includedMedia: [])
        let opened = try SessionImporter.open(bundle.url)
        let details = try #require(opened.envelope.markDetails)
        #expect(details.count == 1)
        #expect(details[0].details == "Sarah felt it too.")
        #expect(details[0].marker == MarkerKind.manual.rawValue)
        #expect(abs(details[0].at.timeIntervalSince(mark.at)) < 0.001)
        // The mark's own reading still says only what was said at the time.
        let reading = try #require(opened.envelope.readings.first { $0.measurements?["marker"] != nil })
        #expect(reading.note == "Cold spot")
    }

    @Test func aPackageOpenedOnAnotherPhoneShowsTheDetailsAndCannotChangeThem() async throws {
        let (phoneA, rootA) = try makeStore()
        let (phoneB, rootB) = try makeStore()
        defer { try? FileManager.default.removeItem(at: rootA); try? FileManager.default.removeItem(at: rootB) }
        let (id, mark) = try await recorded(phoneA)
        try phoneA.addDetails(id, toMarker: mark.id, "Heard it from the landing.")

        let bundle = try await phoneA.export(id, includedMedia: [])
        _ = try await phoneB.importBundle(at: bundle.url, thisDeviceId: "PHONE-B")

        let opened = try #require(phoneB.replayData(for: id)?.markers.first)
        #expect(opened.details == "Heard it from the landing.")
        #expect(throws: FieldSessionError.notYours) {
            try phoneB.addDetails(id, toMarker: opened.id, "mine now")
        }
    }

    @Test func aSessionWithNothingAddedHasNoDetailsSection() async throws {
        let (store, root) = try makeStore()
        defer { try? FileManager.default.removeItem(at: root) }
        let (id, _) = try await recorded(store)

        let document = try await DeviceDataExporter(files: store.files).buildDocument(
            .init(sessionId: id, startedAt: .now, endedAt: nil, locationLabel: nil, deviceModel: "iPhone17,1",
                  timezone: nil, batteryPercentAtStart: nil, trigger: SamplingPolicy.default.trigger(),
                  includedMedia: []),
            log: ReadingLog(fileURL: store.files.readingLogURL(for: id)))
        #expect(!String(decoding: document, as: UTF8.self).contains("mark_details"))
    }
}
