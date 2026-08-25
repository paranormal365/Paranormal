import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Recording sound into a session, and what happens when it does not work.
@Suite("Field capture — recording into a session")
@MainActor
struct FieldCaptureTests {

    /// A recorder that writes a real file of a chosen size, or refuses.
    private final class StubRecorder: AudioRecording, @unchecked Sendable {
        let bytes: Int
        let failure: AudioRecordingError?
        let duration: TimeInterval
        private(set) var wroteTo: URL?

        init(bytes: Int = 4_096, duration: TimeInterval = 12,
             failure: AudioRecordingError? = nil) {
            self.bytes = bytes
            self.duration = duration
            self.failure = failure
        }

        var isRecording: Bool { get async { wroteTo != nil } }

        func beginRecording(to url: URL) async throws {
            if let failure { throw failure }
            try Data(count: bytes).write(to: url)
            wroteTo = url
        }

        @discardableResult
        func endRecording() async -> TimeInterval { duration }
    }

    private func makeSession(recorder: AudioRecording,
                             channels: CaptureChannels = [.magnetic])
        -> (ActiveFieldSession, SessionFileStore, UUID, URL) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("capture-\(UUID().uuidString)", isDirectory: true)
        let files = SessionFileStore(root: root)
        let id = UUID()
        try? files.createDirectories(for: id)

        let sensors = SensorSuite(recorder: recorder)
        let log = ReadingLog(fileURL: files.readingLogURL(for: id))
        let engine = FieldSessionEngine(sessionId: id, log: log, sensors: sensors,
                                        channels: channels)
        let session = ActiveFieldSession(sessionId: id, startedAt: Date(), engine: engine,
                                         sensors: sensors, files: files,
                                         policy: .default, channels: channels)
        return (session, files, id, root)
    }

    @Test func recordingWritesIntoTheSessionsOwnDirectory() async throws {
        let recorder = StubRecorder()
        let (session, files, id, root) = makeSession(recorder: recorder)
        defer { try? FileManager.default.removeItem(at: root) }

        await session.startRecording()

        let state = try #require(session.recording)
        #expect(state.relativePath == "media/audio-001.m4a")
        // Application Support, not tmp: a field recording may sit for a week before anyone
        // reviews it, and the system empties tmp whenever it likes.
        #expect(recorder.wroteTo?.path.contains(id.uuidString.lowercased()) == true)

        await session.stopRecording()

        #expect(session.recording == nil)
        #expect(session.captures.count == 1)
        #expect(session.captures.first?.kind == .audio)
        #expect(session.captures.first?.durationSeconds == 12)
    }

    @Test func aRecordingThatProducedNothingIsReportedNotListed() async throws {
        // The worst outcome this feature has is somebody believing they recorded something.
        // An empty file listed as a capture is exactly that, discovered a week later.
        let (session, _, _, root) = makeSession(recorder: StubRecorder(bytes: 12, duration: 0.1))
        defer { try? FileManager.default.removeItem(at: root) }

        await session.startRecording()
        await session.stopRecording()

        #expect(session.captures.isEmpty)
        #expect(session.recordingProblem?.isEmpty == false)
    }

    @Test func aRecorderThatRefusesSaysWhyRatherThanFailingQuietly() async throws {
        let (session, _, _, root) = makeSession(
            recorder: StubRecorder(failure: .microphoneUnavailable))
        defer { try? FileManager.default.removeItem(at: root) }

        await session.startRecording()

        #expect(session.recording == nil)
        #expect(session.recordingProblem?.contains("microphone") == true)
    }

    @Test func switchingTheAudioChannelOnStartsRecordingAndOffStopsIt() async throws {
        // The switch means "record sound", not "show me a meter" — so it has to actually
        // start and stop the recording.
        let (session, _, _, root) = makeSession(recorder: StubRecorder())
        defer { try? FileManager.default.removeItem(at: root) }

        await session.setChannels([.magnetic, .audio])
        #expect(session.recording != nil)

        await session.setChannels([.magnetic])
        #expect(session.recording == nil)
        #expect(session.captures.count == 1)
    }

    @Test func endingASessionClosesAnOpenRecordingFirst() async throws {
        // An m4a whose container was never finalised is not a short recording, it is an
        // unplayable file.
        let (session, _, _, root) = makeSession(recorder: StubRecorder())
        defer { try? FileManager.default.removeItem(at: root) }

        await session.startRecording()
        await session.end()

        #expect(session.recording == nil)
        #expect(session.captures.count == 1)
    }

    @Test func markersMadeWhileRecordingPointIntoTheFile() async throws {
        let (session, _, _, root) = makeSession(recorder: StubRecorder())
        defer { try? FileManager.default.removeItem(at: root) }

        await session.startRecording()
        let marker = await session.mark(kind: .manual, note: "knock")

        #expect(marker.audioFilename == "media/audio-001.m4a")
        #expect(marker.audioOffsetSeconds != nil)
    }

    @Test func mediaNamesNeverCollideAcrossKinds() throws {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("names-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let files = SessionFileStore(root: root)
        let id = UUID()

        let photo = try files.nextMediaPath(for: id, kind: .photo, fileExtension: "jpg")
        FileManager.default.createFile(atPath: photo.url.path, contents: Data("x".utf8))
        let video = try files.nextMediaPath(for: id, kind: .video, fileExtension: "mov")
        let audio = try files.nextMediaPath(for: id, kind: .audio, fileExtension: "m4a")

        #expect(photo.relative == "media/photo-001.jpg")
        #expect(video.relative == "media/video-001.mov")
        #expect(audio.relative == "media/audio-001.m4a")
        // Every one of them has to survive the bundle's path rules, since these are the strings
        // that end up in an exported file.
        for path in [photo.relative, video.relative, audio.relative] {
            #expect(FieldReading.FileRef.relative(path) != nil)
        }
    }
}
