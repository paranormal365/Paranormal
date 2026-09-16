import Foundation
import Testing
@testable import BenKit

/// Sound carries on after something takes the microphone.
///
/// Ben, 2026-09-16, testing the approved build: "When running the field kit, if you switch to video then go back to
/// the app, audio is no longer being displayed or recorded. It is like it doesn't know to continue recording audio
/// whether it is recording video or not." The camera takes the audio session; what was recorded up to that point is
/// a real clip and is kept, and a new clip starts when the microphone comes back.
@MainActor
struct MicrophoneHandoverTests {

    /// A recorder that writes a real file and can be told the microphone was taken and handed back.
    private final class InterruptibleRecorder: AudioRecording, @unchecked Sendable {
        private(set) var files: [URL] = []
        private let feed: AsyncStream<AudioRecordingEvent>
        private let sink: AsyncStream<AudioRecordingEvent>.Continuation

        init() {
            (feed, sink) = AsyncStream.makeStream(of: AudioRecordingEvent.self)
        }

        var events: AsyncStream<AudioRecordingEvent> { feed }
        var isRecording: Bool { get async { !files.isEmpty } }

        func beginRecording(to url: URL) async throws {
            try Data(count: 8_192).write(to: url)
            files.append(url)
        }

        @discardableResult
        func endRecording() async -> TimeInterval { 9 }

        func microphoneTaken() { sink.yield(.interrupted) }
        func microphoneReturned() { sink.yield(.resumed) }
    }

    private func makeSession(_ recorder: AudioRecording)
        -> (ActiveFieldSession, UUID, URL) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("mic-\(UUID().uuidString)", isDirectory: true)
        let files = SessionFileStore(root: root)
        let id = UUID()
        try? files.createDirectories(for: id)

        let channels: CaptureChannels = [.magnetic, .audio]
        let sensors = SensorSuite(recorder: recorder)
        let log = ReadingLog(fileURL: files.readingLogURL(for: id))
        let engine = FieldSessionEngine(sessionId: id, log: log, sensors: sensors, channels: channels)
        let session = ActiveFieldSession(sessionId: id, startedAt: Date(), engine: engine,
                                         sensors: sensors, files: files,
                                         policy: .default, channels: channels)
        return (session, id, root)
    }

    /// Waits for the session to notice, since the microphone's events arrive on their own task.
    private func settle() async {
        for _ in 0..<40 {
            await Task.yield()
            try? await Task.sleep(nanoseconds: 5_000_000)
        }
    }

    @Test func theClipIsKeptWhenTheCameraTakesTheMicrophone() async throws {
        let recorder = InterruptibleRecorder()
        let (session, _, root) = makeSession(recorder)
        defer { try? FileManager.default.removeItem(at: root) }

        await session.begin()
        await session.startSession(at: Date())
        #expect(session.recording != nil)

        recorder.microphoneTaken()
        await settle()

        // Nothing is running, the clip that was running is listed, and the screen has words for why.
        #expect(session.recording == nil)
        #expect(session.captures.contains { $0.kind == .audio })
        #expect(session.recordingProblem?.contains("camera") == true)
    }

    @Test func soundCarriesOnAsANewClipWhenTheMicrophoneComesBack() async throws {
        let recorder = InterruptibleRecorder()
        let (session, _, root) = makeSession(recorder)
        defer { try? FileManager.default.removeItem(at: root) }

        await session.begin()
        await session.startSession(at: Date())
        recorder.microphoneTaken()
        await settle()
        recorder.microphoneReturned()
        await settle()

        #expect(session.recording != nil, "the session did not start recording again")
        #expect(recorder.files.count == 2, "the second clip went to a file of its own")
        #expect(recorder.files[0] != recorder.files[1])
    }

    @Test func nothingResumesWhenTheSessionWasNotRecording() async throws {
        let recorder = InterruptibleRecorder()
        let (session, _, root) = makeSession(recorder)
        defer { try? FileManager.default.removeItem(at: root) }

        await session.begin()          // pending: Start has not been pressed
        recorder.microphoneTaken()
        recorder.microphoneReturned()
        await settle()

        #expect(session.recording == nil)
        #expect(recorder.files.isEmpty)
    }
}
