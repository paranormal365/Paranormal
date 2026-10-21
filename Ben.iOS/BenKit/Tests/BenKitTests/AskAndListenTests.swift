import Foundation
import Testing
@testable import BenKit

/// Asking a question and listening for the answer, one button (Ben, 10/20/2026): "a button to say
/// that you are done asking and should be recording audio for a response. Then the button to say
/// you are asking the next question and press again to say you are done and listening."
@Suite("Ask and listen")
@MainActor
struct AskAndListenTests {

    private final class StubDictation: DictationService, @unchecked Sendable {
        let words: String
        private(set) var stops = 0
        init(words: String) { self.words = words }
        var isAvailableOffline: Bool { get async { true } }
        func requestPermission() async -> Bool { true }
        func start() async throws -> AsyncStream<DictationUpdate> {
            AsyncStream { continuation in
                continuation.yield(DictationUpdate(text: words, isFinal: false))
            }
        }
        @discardableResult
        func stop() async -> String { stops += 1; return words }
    }

    /// A clock the test moves by hand.
    private final class Clock: @unchecked Sendable {
        private let lock = NSLock()
        private var value = Date(timeIntervalSince1970: 2_000_000)
        func now() -> Date { lock.lock(); defer { lock.unlock() }; return value }
        func advance(_ seconds: TimeInterval) {
            lock.lock(); value = value.addingTimeInterval(seconds); lock.unlock()
        }
    }

    private func makeSession(clock: Clock) -> (ActiveFieldSession, URL) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("ask-\(UUID().uuidString)", isDirectory: true)
        let files = SessionFileStore(root: root)
        let id = UUID()
        _ = try? files.createDirectories(for: id)
        let sensors = SensorSuite()
        let log = ReadingLog(fileURL: files.readingLogURL(for: id))
        let now: @Sendable () -> Date = { clock.now() }
        let engine = FieldSessionEngine(sessionId: id, log: log, sensors: sensors,
                                        channels: [.magnetic], now: now)
        let session = ActiveFieldSession(sessionId: id, startedAt: clock.now(), engine: engine,
                                         sensors: sensors, files: files,
                                         policy: .default, channels: [.magnetic], now: now)
        return (session, root)
    }

    @Test func theButtonSaysWhatItWillDoAtEachStep() async {
        let clock = Clock()
        let (session, root) = makeSession(clock: clock)
        defer { try? FileManager.default.removeItem(at: root) }
        await session.begin()
        await session.startSession(at: clock.now())
        let call = AskAndListen(now: { clock.now() })

        #expect(call.phase == .ready)
        #expect(call.primaryTitle == "Ask a question")

        await call.press(session: session, dictation: nil, typeQuestions: false)
        #expect(call.phase == .asking)
        #expect(call.primaryTitle == "Done asking — listening")
        // Nothing is marked while the question is still being asked.
        #expect(session.questions.isEmpty)
        #expect(session.questionOpenedAt == nil)

        call.type("Is anyone here with us?")
        await call.press(session: session, dictation: nil, typeQuestions: false)
        #expect(call.phase == .listening)
        #expect(call.primaryTitle == "Ask the next question")
        #expect(session.questions.first?.text == "Is anyone here with us?")
        #expect(session.questionOpenedAt != nil)
        await session.end()
    }

    /// The answer window is the listening stretch only: it closes when the next question STARTS,
    /// not when that question is finished. It used to stay open through the next question's asking.
    @Test func theNextQuestionClosesTheAnswerWindowTheMomentItStarts() async throws {
        let clock = Clock()
        let (session, root) = makeSession(clock: clock)
        defer { try? FileManager.default.removeItem(at: root) }
        await session.begin()
        await session.startSession(at: clock.now())
        let call = AskAndListen(now: { clock.now() })

        await call.press(session: session, dictation: nil, typeQuestions: false)   // asking
        clock.advance(4)
        await call.press(session: session, dictation: nil, typeQuestions: false)   // listening
        clock.advance(30)
        await call.press(session: session, dictation: nil, typeQuestions: false)   // next question
        #expect(call.phase == .asking)
        // Closed now, at 30 s — before the next question is asked.
        #expect(session.questionOpenedAt == nil)
        let first = try #require(session.questions.first)
        #expect(first.waitedSeconds == 30)

        clock.advance(6)
        await call.press(session: session, dictation: nil, typeQuestions: false)   // listening again
        #expect(session.questions.count == 2)
        // The first wait was not stretched by the 6 seconds of asking.
        #expect(session.questions.last?.waitedSeconds == 30)
        #expect(session.markers.filter { $0.kind == .evpWaitEnd }.count == 1)
        await session.end()
    }

    @Test func stopListeningEndsTheWaitWithoutAnotherQuestion() async {
        let clock = Clock()
        let (session, root) = makeSession(clock: clock)
        defer { try? FileManager.default.removeItem(at: root) }
        await session.begin()
        await session.startSession(at: clock.now())
        let call = AskAndListen(now: { clock.now() })

        await call.press(session: session, dictation: nil, typeQuestions: false)
        await call.press(session: session, dictation: nil, typeQuestions: false)
        clock.advance(12)
        await call.stopListening(session: session)
        #expect(call.phase == .ready)
        #expect(session.questionOpenedAt == nil)
        #expect(session.questions.first?.waitedSeconds == 12)
        await session.end()
    }

    @Test func aCancelledQuestionMarksNothing() async {
        let clock = Clock()
        let (session, root) = makeSession(clock: clock)
        defer { try? FileManager.default.removeItem(at: root) }
        await session.begin()
        await session.startSession(at: clock.now())
        let call = AskAndListen(now: { clock.now() })

        await call.press(session: session, dictation: nil, typeQuestions: false)
        call.type("Never mind")
        await call.cancelAsking(dictation: nil)
        #expect(call.phase == .ready)
        #expect(call.heard.isEmpty)
        #expect(session.questions.isEmpty)
        #expect(session.markers.isEmpty)
        await session.end()
    }

    @Test func dictatedWordsBecomeTheQuestionAndCannotBeOvertyped() async throws {
        let clock = Clock()
        let (session, root) = makeSession(clock: clock)
        defer { try? FileManager.default.removeItem(at: root) }
        await session.begin()
        await session.startSession(at: clock.now())
        let call = AskAndListen(now: { clock.now() })
        let dictation = StubDictation(words: "Can you tell us your name")

        await call.press(session: session, dictation: dictation, typeQuestions: true)
        #expect(call.isDictating)
        call.type("typed over it")
        #expect(call.heard != "typed over it")

        await call.press(session: session, dictation: dictation, typeQuestions: true)
        #expect(dictation.stops == 1)
        #expect(!call.isDictating)
        #expect(session.questions.first?.text == "Can you tell us your name")
        await session.end()
    }

    @Test func typingIsOfferedWhenDictationIsOff() async {
        let clock = Clock()
        let (session, root) = makeSession(clock: clock)
        defer { try? FileManager.default.removeItem(at: root) }
        await session.begin()
        await session.startSession(at: clock.now())
        let call = AskAndListen(now: { clock.now() })

        await call.press(session: session, dictation: StubDictation(words: "x"), typeQuestions: false)
        #expect(!call.isDictating)
        call.type("Who is here?")
        #expect(call.heard == "Who is here?")
        await call.reset(dictation: nil)
        #expect(call.phase == .ready)
        await session.end()
    }

    // MARK: - Motion photos

    @Test func motionPhotosAreCountedAndTheirMomentKept() async {
        let clock = Clock()
        let (session, root) = makeSession(clock: clock)
        defer { try? FileManager.default.removeItem(at: root) }
        await session.begin()
        await session.startSession(at: clock.now())

        await session.noteCapture(kind: .photo, relativePath: "media/a.jpg", byteCount: 10)
        #expect(session.motionPhotoCount == 0)
        #expect(session.lastMotionPhotoAt == nil)

        clock.advance(5)
        await session.noteCapture(kind: .photo, relativePath: "media/b.jpg", byteCount: 10, byMotion: true)
        #expect(session.motionPhotoCount == 1)
        #expect(session.lastMotionPhotoAt == clock.now())
        await session.end()
    }
}
