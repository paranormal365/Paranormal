import Foundation
import Testing
@testable import BenKit

/// A `.ben` made on one phone becomes a session on another that plays the same way.
///
/// Ben, 2026-09-16: "someone else can share their .ben file with another person on the iphone
/// and the other person can view it like they had recorded it themselves", and "be able to play
/// it back as if they recorded the .ben file on their own device." So the claim under test is
/// sameness: the readings, the marks, the recordings and the base levels come out as the rows and
/// files a session recorded here would have left, and the replay reads them without knowing.
@Suite("Session import")
@MainActor
struct SessionImportTests {

    private let start = Date(timeIntervalSince1970: 1_787_600_000)
    private let sessionId = UUID()
    private let recorder = UUID()

    private func scratch(_ name: String) -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("\(name)-\(UUID().uuidString)", isDirectory: true)
    }

    /// A night recorded on "phone A": four readings, a mark, a recording and a photograph.
    private func recordOnPhoneA() async throws -> URL {
        let files = SessionFileStore(root: scratch("phone-a"))
        try files.createDirectories(for: sessionId)

        let audio = files.fileURL(for: sessionId, relativePath: "media/audio-001.m4a")
        try Data(repeating: 0xAB, count: 12_000).write(to: audio)
        let photo = files.fileURL(for: sessionId, relativePath: "media/photo-001.jpg")
        try Data(repeating: 0xCD, count: 3_000).write(to: photo)

        let log = ReadingLog(fileURL: files.readingLogURL(for: sessionId))
        try await log.append(FieldReading(
            at: start, sequence: 1, triggeredBy: .interval,
            measurements: ["emf": .number(48.2, unit: "uT", baseline: 48.0),
                           "sound_level": .number(-41, unit: "dBFS", baseline: -44)],
            position: .init(latitude: 36.1627, longitude: -86.7816, accuracyMeters: 30),
            motion: .init(headingDegrees: 271.5)))
        try await log.append(FieldReading(
            at: start.addingTimeInterval(20), sequence: 2, triggeredBy: .manual,
            measurements: ["marker": .label("manual_marker"),
                           "emf": .number(53.0, unit: "uT", baseline: 48.0),
                           "room": .label("Cellar")],
            position: .init(latitude: 36.1628, longitude: -86.7817),
            audioRef: .relative("media/audio-001.m4a", mediaType: "audio/mp4", startOffsetSeconds: 15),
            note: "heard a knock"))
        // The recording ended at +30 and ran 25 s, so it began at +5.
        try await log.append(FieldReading(
            at: start.addingTimeInterval(30), sequence: 2, triggeredBy: .manual,
            measurements: ["marker": .label("audio")],
            audioRef: .relative("media/audio-001.m4a", mediaType: "audio/mp4", durationSeconds: 25),
            note: "audio: media/audio-001.m4a"))
        try await log.append(FieldReading(
            at: start.addingTimeInterval(40), sequence: 3, triggeredBy: .manual,
            measurements: ["marker": .label("photo"), "room": .label("Cellar")],
            position: .init(latitude: 36.1629, longitude: -86.7818),
            motion: .init(headingDegrees: 90),
            note: "photo: media/photo-001.jpg"))
        try await log.close()

        let request = DeviceDataExporter.Request(
            sessionId: sessionId, startedAt: start, endedAt: start.addingTimeInterval(60),
            locationLabel: "Back bedroom, north wall", deviceModel: "iPhone17,1",
            timezone: "America/Chicago", batteryPercentAtStart: 81,
            trigger: SamplingPolicy.default.trigger(),
            includedMedia: ["media/audio-001.m4a", "media/photo-001.jpg"],
            recordedByAccountId: recorder, deviceId: "PHONE-A")
        let result = try await DeviceDataExporter(files: files).export(
            request, log: log, to: scratch("export"))
        return result.url
    }

    private func phoneB() throws -> FieldSessionStore {
        FieldSessionStore(database: try .inMemory(), files: SessionFileStore(root: scratch("phone-b")),
                          deviceModel: "iPad16,3")
    }

    @Test("the night arrives whole: rows, readings and files, and it says who recorded it")
    func roundTrip() async throws {
        let bundle = try await recordOnPhoneA()
        let store = try phoneB()

        let imported = try await store.importBundle(at: bundle, thisDeviceId: "PHONE-B")

        #expect(imported.id == sessionId)
        #expect(imported.title == "Back bedroom, north wall")
        #expect(imported.readingCount == 4)
        #expect(imported.captureCount == 2)
        #expect(imported.wasRecordedElsewhere)

        let summary = try #require(store.summary(for: sessionId))
        #expect(summary.outcome == .ended)
        #expect(summary.startedAt == start)
        #expect(summary.endedAt == start.addingTimeInterval(60))
        #expect(summary.markerCount == 1)
        #expect(summary.isImported)
        #expect(summary.sourceDeviceId == "PHONE-A")
        #expect(summary.recordedByAccountId == recorder)
        #expect(summary.wasRecordedElsewhere(thisDeviceId: "PHONE-B"))
        #expect(!summary.wasRecordedElsewhere(thisDeviceId: "PHONE-A"))
        #expect(!summary.isUploaded)

        // The files are where a session recorded here keeps them, byte for byte.
        #expect(store.hasLocalFile("media/audio-001.m4a", in: sessionId))
        #expect(store.hasLocalFile("media/photo-001.jpg", in: sessionId))
        let audio = try Data(contentsOf: store.files.fileURL(for: sessionId, relativePath: "media/audio-001.m4a"))
        #expect(audio == Data(repeating: 0xAB, count: 12_000))

        // The readings log is the same shape the engine writes: one line per reading.
        let log = ReadingLog(fileURL: store.files.readingLogURL(for: sessionId))
        #expect(try await log.lineCount() == 4)
        #expect(try await log.readings().map(\.at) == [0, 20, 30, 40].map { start.addingTimeInterval($0) })
    }

    @Test("the replay reads an imported session without knowing it was imported")
    func replays() async throws {
        let store = try phoneB()
        try await store.importBundle(at: try await recordOnPhoneA(), thisDeviceId: "PHONE-B")

        let source = try #require(store.replayData(for: sessionId))
        #expect(source.baselines == Baselines(magneticMicrotesla: 48.0, soundDbfs: -44))

        let marker = try #require(source.markers.first)
        #expect(marker.kind == .manual)
        #expect(marker.note == "heard a knock")
        #expect(marker.room == "Cellar")
        #expect(marker.magneticMicrotesla == 53.0)
        #expect(marker.audioFilename == "media/audio-001.m4a")
        #expect(marker.audioOffsetSeconds == 15)

        // The recording sits on the timeline from when it began — worked back from its end.
        let segment = try #require(source.media.first)
        #expect(segment.kind == .audio)
        #expect(segment.relativePath == "media/audio-001.m4a")
        #expect(segment.duration == 25)
        #expect(segment.startedAt == start.addingTimeInterval(5))

        // The photograph is a pin at the moment it was taken, with its room and its facing.
        let still = try #require(source.stills.first)
        #expect(still.kind == .photo)
        #expect(still.at == start.addingTimeInterval(40))
        #expect(still.room == "Cellar")
        #expect(still.latitude == 36.1629)

        let captures = store.captures(for: sessionId)
        #expect(captures.map(\.relativePath) == ["media/audio-001.m4a", "media/photo-001.jpg"])
    }

    @Test("pulled from the server, it already knows it is up there")
    func fromTheServer() async throws {
        let store = try phoneB()
        let serverId = UUID()
        try await store.importBundle(at: try await recordOnPhoneA(), thisDeviceId: "PHONE-A",
                                     serverSessionId: serverId)

        let summary = try #require(store.summary(for: sessionId))
        #expect(summary.serverSessionId == serverId)
        #expect(summary.isUploaded)
        #expect(store.isFullyUploaded(sessionId))
        #expect(!summary.wasRecordedElsewhere(thisDeviceId: "PHONE-A"))
    }

    @Test("the same night opened twice is refused, naming the session already here")
    func twice() async throws {
        let store = try phoneB()
        let bundle = try await recordOnPhoneA()
        try await store.importBundle(at: bundle, thisDeviceId: "PHONE-B")

        await #expect(throws: FieldSessionError.alreadyOnThisPhone("Back bedroom, north wall")) {
            try await store.importBundle(at: bundle, thisDeviceId: "PHONE-B")
        }
        #expect(store.sessions.count == 1)
    }

    @Test("a bundle whose seal does not describe its files is refused, and nothing is left behind")
    func tampered() async throws {
        let dir = scratch("tampered")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let media = dir.appendingPathComponent("audio-001.m4a")
        try Data(repeating: 0x11, count: 2_000).write(to: media)

        let envelope = DeviceDataEnvelope(
            device: .init(manufacturer: "Apple", model: "iPhone17,1"),
            session: .init(startedAt: start, endedAt: start.addingTimeInterval(10),
                           locationLabel: "Attic", trigger: SamplingPolicy.default.trigger()))
        let document = try DeviceDataJSON.encoder.encode(envelope)
        let documentDigest = try DeviceDataExporter.digestAndSize(of: .init(path: "data.json", data: document)).0

        // The seal names the right files with the right sizes — and the wrong digest for one.
        let seal = SessionSeal(recordedByAccountId: nil, deviceId: "PHONE-A", sessionId: UUID(),
                               sealedAt: start, entries: [
                                   .init(path: "data.json", sha256: documentDigest, byteCount: Int64(document.count)),
                                   .init(path: "media/audio-001.m4a",
                                         sha256: String(repeating: "0", count: 64), byteCount: 2_000),
                               ])
        let archive = dir.appendingPathComponent("tampered.ben")
        try ZipWriter().write([
            .init(path: "data.json", data: document),
            .init(path: "media/audio-001.m4a", file: media),
            .init(path: SessionSeal.entryPath, data: try DeviceDataJSON.encoder.encode(seal)),
        ], to: archive)

        let store = try phoneB()
        await #expect(throws: FieldSessionError.bundleTampered) {
            try await store.importBundle(at: archive, thisDeviceId: "PHONE-B")
        }
        #expect(store.sessions.isEmpty)
        #expect(store.files.existingSessionIds().isEmpty)
    }

    @Test("a zip with no session in it is refused as not a session")
    func notASession() async throws {
        let dir = scratch("plain")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let archive = dir.appendingPathComponent("photos.ben")
        try ZipWriter().write([.init(path: "holiday.jpg", data: Data(repeating: 1, count: 10))], to: archive)

        let store = try phoneB()
        await #expect(throws: FieldSessionError.notASessionBundle("there is no data.json inside it.")) {
            try await store.importBundle(at: archive, thisDeviceId: "PHONE-B")
        }
    }
}

/// The half that needs no file: what the readings say happened, read back out of them.
@Suite("Session import — reading the night back")
struct SessionImportRebuildTests {

    private let start = Date(timeIntervalSince1970: 1_787_600_000)

    @Test("a marker, a recording and a photograph come back from their readings")
    func rebuild() {
        let readings = [
            FieldReading(at: start, measurements: ["emf": .number(47, unit: "uT", baseline: 46)]),
            FieldReading(at: start.addingTimeInterval(10), triggeredBy: .event,
                         measurements: ["marker": .label("sentry_emf"),
                                        "emf": .number(60, unit: "uT", baseline: 46.5),
                                        "sound_level": .number(-30, unit: "dBFS", baseline: -40)],
                         audioRef: .relative("media/audio-001.m4a", startOffsetSeconds: 8)),
            FieldReading(at: start.addingTimeInterval(20), triggeredBy: .manual,
                         measurements: ["marker": .label("video"), "room": .label("Hall")],
                         motion: .init(headingDegrees: 180),
                         note: "video: media/video-001.mov"),
            FieldReading(at: start.addingTimeInterval(21), triggeredBy: .manual,
                         measurements: ["marker": .label("photo")],
                         note: "photo: media/photo-002.jpg"),
        ]

        let rebuilt = SessionImporter.rebuild(from: readings)

        #expect(rebuilt.baselines == Baselines(magneticMicrotesla: 46.5, soundDbfs: -40))

        let marker = rebuilt.markers[0]
        #expect(rebuilt.markers.count == 1)
        #expect(marker.kind == .sentryEmf)
        #expect(marker.magneticMicrotesla == 60)
        #expect(marker.soundDbfs == -30)
        #expect(marker.audioOffsetSeconds == 8)

        #expect(rebuilt.captures.count == 2)
        // A clip with no length in its reading keeps the moment it was noted; the store reads
        // the length from the file afterwards and moves it back.
        #expect(rebuilt.captures[0].kind == .video)
        #expect(rebuilt.captures[0].relativePath == "media/video-001.mov")
        #expect(rebuilt.captures[0].at == start.addingTimeInterval(20))
        #expect(rebuilt.captures[0].room == "Hall")
        #expect(rebuilt.captures[0].headingDegrees == 180)
        #expect(rebuilt.captures[1].kind == .photo)
        #expect(rebuilt.captures[1].relativePath == "media/photo-002.jpg")
    }

    @Test("a capture note that does not point into media/ is ignored rather than trusted")
    func strayNote() {
        let rebuilt = SessionImporter.rebuild(from: [
            FieldReading(at: start, measurements: ["marker": .label("photo")], note: "photo: /etc/passwd"),
            FieldReading(at: start, measurements: ["marker": .label("photo")], note: "something else"),
        ])
        #expect(rebuilt.captures.isEmpty)
    }
}
