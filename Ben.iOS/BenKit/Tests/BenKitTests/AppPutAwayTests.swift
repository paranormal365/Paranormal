import Foundation
import Testing
@testable import BenKit

/// The app being put away in the middle of a session: what carries on, and what the review is told.
///
/// Ben, 2026-09-17: "if they are in a session and leave the app, it should either continue recording
/// waiting on them to come back or pause the recording until they do — with just a blank space for
/// while they were gone and a message saying the recording was paused while not on app." It
/// continues, and it says so: two automatic marks bracket the stretch, the first with the sentence.
@Suite("The app being put away mid-session")
@MainActor
struct AppPutAwayTests {

    private final class StubRecorder: AudioRecording, @unchecked Sendable {
        private(set) var wroteTo: URL?
        var isRecording: Bool { get async { wroteTo != nil } }
        func beginRecording(to url: URL) async throws {
            try Data(count: 4_096).write(to: url)
            wroteTo = url
        }
        @discardableResult
        func endRecording() async -> TimeInterval { 12 }
    }

    private func makeSession(channels: CaptureChannels = [.magnetic, .audio])
        -> (ActiveFieldSession, URL) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("away-\(UUID().uuidString)", isDirectory: true)
        let files = SessionFileStore(root: root)
        let id = UUID()
        try? files.createDirectories(for: id)
        let sensors = SensorSuite(recorder: StubRecorder())
        let log = ReadingLog(fileURL: files.readingLogURL(for: id))
        let engine = FieldSessionEngine(sessionId: id, log: log, sensors: sensors, channels: channels)
        let session = ActiveFieldSession(sessionId: id, startedAt: Date(), engine: engine,
                                         sensors: sensors, files: files,
                                         policy: .default, channels: channels)
        return (session, root)
    }

    @Test func aRunningSessionIsBracketedAndKeepsRecording() async throws {
        let (session, root) = makeSession()
        defer { try? FileManager.default.removeItem(at: root) }
        await session.begin()
        await session.startSession(at: Date())
        let recordingBefore = try #require(session.recording)

        await session.appWentToBackground()
        await session.appReturned()

        // Newest first, as the live screen lists them.
        #expect(session.markers.map(\.kind) == [.appReturned, .appBackgrounded])
        #expect(session.markers.last?.note == "The app was put away here. Sound and readings carried on.")
        #expect(session.markers.first?.note?.hasPrefix("Away for ") == true)
        #expect(session.markers.allSatisfy { $0.kind.isAutomatic })
        // Nothing paused: the same recording is still open, so the sound has no hole in it.
        #expect(session.recording == recordingBefore)
        #expect(session.lastAbsence != nil)
        await session.end()
    }

    @Test func aPendingSessionRecordsNothingBecauseNothingIsRunning() async throws {
        let (session, root) = makeSession()
        defer { try? FileManager.default.removeItem(at: root) }
        await session.begin()

        await session.appWentToBackground()
        await session.appReturned()

        #expect(session.markers.isEmpty)
        #expect(session.lastAbsence == nil)
        await session.end()
    }

    @Test func goingAwayTwiceWithoutComingBackIsOneStretch() async throws {
        let (session, root) = makeSession()
        defer { try? FileManager.default.removeItem(at: root) }
        await session.begin()
        await session.startSession(at: Date())

        await session.appWentToBackground()
        await session.appWentToBackground()
        await session.appReturned()
        await session.appReturned()

        #expect(session.markers.count == 2)
        await session.end()
    }

    @Test func theSentenceSaysWhatCarriedOnAndWhatCouldNot() {
        #expect(ActiveFieldSession.awayNote(for: [.magnetic, .audio, .video])
                == "The app was put away here. Sound and readings carried on; the camera paused until the app came back.")
        #expect(ActiveFieldSession.awayNote(for: [.magnetic, .audio])
                == "The app was put away here. Sound and readings carried on.")
        // Nothing keeps the app awake without sound: the honest sentence says readings paused.
        #expect(ActiveFieldSession.awayNote(for: [.magnetic, .video])
                == "The app was put away here. With no sound recording to keep it awake, readings and the camera paused until the app came back.")
        #expect(ActiveFieldSession.awayNote(for: [.magnetic])
                == "The app was put away here. With no sound recording to keep it awake, readings paused until the app came back.")
    }

    @Test func howLongIsSaidTheWayAPersonSaysIt() {
        #expect(ActiveFieldSession.spell(45) == "45 sec")
        #expect(ActiveFieldSession.spell(200) == "3 min 20 sec")
        #expect(ActiveFieldSession.spell(180) == "3 min")
        #expect(ActiveFieldSession.spell(3720) == "1 hr 2 min")
        #expect(ActiveFieldSession.spell(3600) == "1 hr")
        #expect(ActiveFieldSession.spell(0) == "0 sec")
    }

    /// The wire carries a mark as a label the website humanises; these two must be the labels it knows.
    @Test func theTwoMarksTravelAsTheLabelsTheWebsiteNames() {
        #expect(MarkerKind.appBackgrounded.rawValue == "app_backgrounded")
        #expect(MarkerKind.appReturned.rawValue == "app_returned")
        #expect(MarkerKind.appBackgrounded.trigger == .event)
        #expect(MarkerKind.appBackgrounded.title == "App put away")
        #expect(MarkerKind.appReturned.title == "Back in the app")
    }
}
