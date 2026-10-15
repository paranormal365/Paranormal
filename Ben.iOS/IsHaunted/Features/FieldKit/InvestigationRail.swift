import SwiftUI
import AVFoundation
import BenKit

// The investigation's own controls, down the side of the screen (Ben, 10/15/2026).
//
// On the iPhone Duo opened, iOS puts a screen's toolbar buttons in a column at the right edge. The ☰
// at the top of that column gets around the app; while a session runs, the buttons below it run the
// investigation: mark what was noticed, log an experience, ask and listen, note, change room, reset
// the base, the flashlight. The page itself keeps the gauge, the clock and the readings.

extension EnvironmentValues {
    /// True where the investigation's controls go in the side column rather than on the page: a
    /// regular-width window too narrow for a standing sidebar, which is the iPhone Duo opened.
    @Entry var investigationRail = false
}

/// Asking and listening, one button (Ben, 10/15/2026): "it would be the mic to say they are talking
/// and pressing the mic again for waiting for the response. If they have enabled being able to
/// voice-to-text then while they are talking we could try to type their question as they ask it
/// then when they start the listening, we stop trying to type what is being said."
///
/// Press: asking. Press again: the question is filed (with what was typed of it) and the wait for an
/// answer begins. Press again: the next question, which closes that wait. Holding the button while
/// listening ends the wait without asking again.
@MainActor
@Observable
final class CallAndResponse {
    enum Phase: Equatable { case idle, asking, listening }

    private(set) var phase: Phase = .idle
    /// What has been typed of the question so far.
    private(set) var heard = ""
    private(set) var askingSince: Date?

    private var listener: Task<Void, Never>?

    func press(session: ActiveFieldSession, dictation: DictationService?, typeQuestions: Bool) async {
        switch phase {
        case .idle, .listening:
            heard = ""
            askingSince = .now
            phase = .asking
            if typeQuestions, let dictation, await dictation.isAvailableOffline {
                listener = Task { [weak self] in
                    guard let stream = try? await dictation.start() else { return }
                    for await update in stream { self?.heard = update.text }
                }
            }
        case .asking:
            var text = heard
            if listener != nil, let dictation {
                let final = await dictation.stop()
                if !final.isEmpty { text = final }
            }
            listener?.cancel()
            listener = nil
            let question = text.trimmingCharacters(in: .whitespacesAndNewlines)
            // Asking closes any wait still open, so the list of questions stays right.
            await session.askQuestion(question.isEmpty ? nil : question)
            heard = question
            askingSince = nil
            phase = .listening
        }
    }

    /// Ends the wait without a new question.
    func stopListening(session: ActiveFieldSession) async {
        guard phase == .listening else { return }
        await session.endWait()
        phase = .idle
    }

    /// The session ended or the screen went: nothing keeps typing.
    func reset(dictation: DictationService?) async {
        if listener != nil { await dictation?.stop() }
        listener?.cancel()
        listener = nil
        phase = .idle
        heard = ""
    }
}

/// The phone's light. Only the back camera has one; where there is none the button is not shown.
@MainActor
@Observable
final class Flashlight {
    private(set) var isOn = false

    var isAvailable: Bool { AVCaptureDevice.default(for: .video)?.hasTorch == true }

    func toggle() {
        guard let device = AVCaptureDevice.default(for: .video), device.hasTorch else { return }
        do {
            try device.lockForConfiguration()
            defer { device.unlockForConfiguration() }
            if isOn {
                device.torchMode = .off
            } else {
                try device.setTorchModeOn(level: AVCaptureDevice.maxAvailableTorchLevel)
            }
            isOn.toggle()
        } catch {
            isOn = false
        }
    }

    func off() {
        guard isOn, let device = AVCaptureDevice.default(for: .video) else { return }
        try? device.lockForConfiguration()
        device.torchMode = .off
        device.unlockForConfiguration()
        isOn = false
    }
}

/// What somebody felt, saw or heard, logged at the moment as a mark with these words.
enum Experience: String, CaseIterable, Identifiable {
    case coldSpot = "Cold spot"
    case touched = "Touched"
    case heardVoice = "Heard a voice"
    case heardSound = "Heard a sound"
    case sawSomething = "Saw something"
    case smell = "Smelled something"
    case uneasy = "Felt uneasy"

    var id: String { rawValue }
}

/// The line across the top of the page while asking or listening: what is being typed, or how long
/// the room has had to answer.
struct CallAndResponseBanner: View {
    let call: CallAndResponse
    let session: ActiveFieldSession

    var body: some View {
        if call.phase != .idle {
            TimelineView(.periodic(from: .now, by: 1)) { context in
                HStack(spacing: 10) {
                    Image(systemName: call.phase == .asking ? "mic.fill" : "ear")
                        .foregroundStyle(call.phase == .asking ? Theme.danger : Theme.ecto)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(call.phase == .asking ? "Asking" : "Listening for an answer")
                            .font(.caption.weight(.semibold)).foregroundStyle(Theme.fog)
                        Text(line(at: context.date))
                            .font(.callout).foregroundStyle(Theme.bone)
                            .lineLimit(2)
                    }
                    Spacer(minLength: 0)
                }
                .padding(12)
                .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
            }
            .accessibilityIdentifier("call-and-response")
        }
    }

    private func line(at now: Date) -> String {
        switch call.phase {
        case .asking:
            return call.heard.isEmpty ? "Ask your question, then press the microphone again." : call.heard
        case .listening:
            let waited = session.questionOpenedAt.map { Int(now.timeIntervalSince($0)) } ?? 0
            let clock = String(format: "%d:%02d", waited / 60, waited % 60)
            return call.heard.isEmpty ? clock : "\(clock) · \(call.heard)"
        case .idle:
            return ""
        }
    }
}
