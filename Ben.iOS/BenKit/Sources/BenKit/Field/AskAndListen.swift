import Foundation

/// Asking a question, then listening for the answer: one button, three states.
///
/// Ben, 10/15/2026: "it would be the mic to say they are talking and pressing the mic again for
/// waiting for the response." And 10/20/2026, after a test session: "it was not obvious to me
/// when it was supposed to be recording a response ... there should be a button to say that you
/// are done asking and should be recording audio for a response. Then the button to say you are
/// asking the next question and press again to say you are done and listening for a response."
///
/// - **Ready** — "Ask a question" starts asking.
/// - **Asking** — the question is being spoken (and typed, if the phone can write down what is
///   said, or by hand). "Done asking — listening" files the question and opens the answer window.
/// - **Listening** — the room has the floor. "Ask the next question" closes the answer window at
///   that moment and starts asking again. "Stop listening" closes it without another question.
///
/// The answer window is exactly the listening stretch: it opens when the question is filed and
/// closes the moment the next question starts, not when the next one is finished. Before
/// 10/21/2026 the next question's own asking time was counted as listening.
///
/// Used by the EVP screen and by the iPhone Duo's side column, which share one of these so the
/// two can never disagree about which state the session is in.
@MainActor
@Observable
public final class AskAndListen {
    public enum Phase: Equatable, Sendable { case ready, asking, listening }

    public private(set) var phase: Phase = .ready
    /// What has been written of the question so far: dictated, or typed by hand.
    public private(set) var heard = ""
    public private(set) var askingSince: Date?
    /// True while the phone is writing down what is said. Typing by hand is offered otherwise.
    public private(set) var isDictating = false

    private var listener: Task<Void, Never>?
    private let now: @Sendable () -> Date

    /// Set while a press is being handled, so a second press can't land half-way through the first.
    private var handling = false

    public init(now: @escaping @Sendable () -> Date = Date.init) {
        self.now = now
    }

    // MARK: - What the button says

    /// The main button's words, which are also its accessibility label.
    public var primaryTitle: String {
        switch phase {
        case .ready: "Ask a question"
        case .asking: "Done asking — listening"
        case .listening: "Ask the next question"
        }
    }

    /// What VoiceOver says the main button does.
    public var primaryHint: String {
        switch phase {
        case .ready: "Starts asking. Ask out loud, then press again to listen for an answer."
        case .asking: "Files the question and starts listening for an answer."
        case .listening: "Stops listening and starts asking the next question."
        }
    }

    // MARK: - Pressing

    public func press(session: ActiveFieldSession, dictation: DictationService?,
                      typeQuestions: Bool) async {
        guard !handling else { return }
        handling = true
        defer { handling = false }
        switch phase {
        case .ready, .listening:
            // The answer window ends the moment the next question starts.
            if phase == .listening { await session.endWait() }
            await beginAsking(dictation: dictation, typeQuestions: typeQuestions)
        case .asking:
            await finishAsking(session: session, dictation: dictation)
        }
    }

    /// Typing the question by hand. Ignored while the phone is writing down what is said, so the
    /// two never fight over the same words.
    public func type(_ text: String) {
        guard phase == .asking, !isDictating else { return }
        heard = text
    }

    /// Ends the answer window without asking again.
    public func stopListening(session: ActiveFieldSession) async {
        guard phase == .listening, !handling else { return }
        handling = true
        defer { handling = false }
        await session.endWait()
        phase = .ready
        heard = ""
    }

    /// Drops a question that was started and not asked after all. Nothing is marked.
    public func cancelAsking(dictation: DictationService?) async {
        guard phase == .asking, !handling else { return }
        handling = true
        defer { handling = false }
        await stopDictating(dictation)
        heard = ""
        askingSince = nil
        phase = .ready
    }

    /// The session ended or the screen went: nothing keeps typing.
    public func reset(dictation: DictationService?) async {
        await stopDictating(dictation)
        phase = .ready
        heard = ""
        askingSince = nil
    }

    // MARK: - Steps

    private func beginAsking(dictation: DictationService?, typeQuestions: Bool) async {
        heard = ""
        askingSince = now()
        phase = .asking
        isDictating = false
        guard typeQuestions, let dictation, await dictation.isAvailableOffline else { return }
        isDictating = true
        listener = Task { [weak self] in
            guard let stream = try? await dictation.start() else {
                self?.isDictating = false
                return
            }
            for await update in stream { self?.heard = update.text }
        }
    }

    private func finishAsking(session: ActiveFieldSession, dictation: DictationService?) async {
        var text = heard
        if isDictating, let dictation {
            let final = await dictation.stop()
            if !final.isEmpty { text = final }
        }
        listener?.cancel()
        listener = nil
        isDictating = false
        let question = text.trimmingCharacters(in: .whitespacesAndNewlines)
        await session.askQuestion(question.isEmpty ? nil : question)
        heard = question
        askingSince = nil
        phase = .listening
    }

    private func stopDictating(_ dictation: DictationService?) async {
        if isDictating { await dictation?.stop() }
        listener?.cancel()
        listener = nil
        isDictating = false
    }
}
