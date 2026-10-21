import SwiftUI
import BenKit

/// Question, then listening, then the next question — the shape of an EVP session.
///
/// Ben, 10/20/2026, after a test session: "it was not obvious to me when it was supposed to be
/// recording a response." So the screen is built around the one thing that matters in the dark:
/// which half of the cycle this is. Asking shows red with the question as it is written down;
/// listening shows green with the time the room has had to answer, for the whole wait. One large
/// button moves on to the next step and says what it will do: "Ask a question", "Done asking —
/// listening", "Ask the next question". The magnetic field and the sound are always on screen, so
/// nobody has to leave the wait to see whether something changed.
///
/// The wait is what matters. Marking only the moment somebody spoke would leave a reviewer
/// hunting through hours of tape; bracketing the silence after each question turns the same
/// recording into a list of places to listen.
struct EVPModeView: View {
    @Environment(\.dismiss) private var dismiss

    let session: ActiveFieldSession
    /// Shared with the live page and the iPhone Duo's side column, so all three agree.
    let call: AskAndListen
    let dictation: DictationService?
    let typeQuestions: Bool

    @State private var blackout = false
    @State private var refusedPermission: AppPermission?
    @FocusState private var typing: Bool

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 16) {
                    recordingBanner
                    LiveReadingsStrip(session: session)
                    HStack {
                        MotionPhotoBadge(session: session)
                        Spacer(minLength: 0)
                    }
                    phasePanel
                    controls
                    questionList
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                // A phone's width on an iPad or an opened Duo, centered, rather than stretched.
                .frame(maxWidth: 560)
                .frame(maxWidth: .infinity)
            }
            .scrollDismissesKeyboard(.interactively)
            .background(Theme.ink)
            .navigationTitle("EVP")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Done") { dismiss() }
                        .accessibilityHint("Goes back to the session. Asking or listening carries on.")
                }
                ToolbarItem(placement: .primaryAction) {
                    Button { blackout = true } label: { Image(systemName: "moon.fill") }
                        .accessibilityLabel("Blackout the screen")
                }
            }
            .permissionRefusedAlert($refusedPermission)
            .fullScreenCover(isPresented: $blackout) {
                BlackoutOverlay(session: session) { blackout = false }
            }
        }
    }

    // MARK: - Pieces

    @ViewBuilder
    private var recordingBanner: some View {
        if let recording = session.recording {
            TimelineView(.periodic(from: .now, by: 1)) { context in
                HStack(spacing: 8) {
                    Circle().fill(Theme.danger).frame(width: 8, height: 8)
                    Text("Recording \(SessionClock.elapsed(from: recording.startedAt, to: context.date))")
                        .font(.callout.monospacedDigit())
                    Spacer()
                    Text(recording.relativePath.replacingOccurrences(of: "media/", with: ""))
                        .font(.caption2).foregroundStyle(Theme.fog)
                        .lineLimit(1)
                }
                .foregroundStyle(Theme.bone)
                .padding(10)
                .background(Theme.mist, in: RoundedRectangle(cornerRadius: 10))
            }
        } else {
            // Said plainly. Marks without a recording are still useful — somebody may be
            // recording on a separate device — but nobody should discover the absence later.
            VStack(alignment: .leading, spacing: 8) {
                Label("Not recording", systemImage: "mic.slash")
                    .font(.callout).foregroundStyle(Theme.warning)
                Text("Questions will still be marked, but there'll be no sound here to play back.")
                    .font(.caption).foregroundStyle(Theme.fog)
                // A refused microphone is said here too: this screen had no other way of showing
                // why pressing the button did nothing (2026-09-27).
                if let problem = session.recordingProblem {
                    Label(problem, systemImage: "exclamationmark.triangle")
                        .font(.caption).foregroundStyle(Theme.danger)
                }
                Button("Start recording") {
                    if AppPermission.microphone.isRefused {
                        refusedPermission = .microphone
                    } else {
                        Task { await session.startRecording() }
                    }
                }
                .buttonStyle(.signalSecondary)
                .accessibilityIdentifier("evp-start-recording")
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(10)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 10))
        }
    }

    /// Which half of the cycle this is, big enough to read across a room.
    private var phasePanel: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            VStack(spacing: 10) {
                AskListenIcon(phase: call.phase, size: 48)
                Text(AskListenWords.title(call.phase))
                    .font(.title.bold())
                    .foregroundStyle(AskListenWords.color(call.phase))
                    .accessibilityIdentifier("evp-phase")

                switch call.phase {
                case .ready:
                    Text(session.questions.isEmpty
                         ? "Press Ask a question, then ask it out loud."
                         : "Ready for the next question.")
                        .font(.callout).foregroundStyle(Theme.fog)
                        .multilineTextAlignment(.center)
                case .asking:
                    Text(AskListenWords.clock(since: call.askingSince, now: context.date))
                        .font(.title3.monospacedDigit()).foregroundStyle(Theme.fog)
                    questionField
                    Text("When you've finished asking, press Done asking.")
                        .font(.caption).foregroundStyle(Theme.fog)
                        .multilineTextAlignment(.center)
                case .listening:
                    Text(AskListenWords.clock(since: session.questionOpenedAt, now: context.date))
                        .font(.system(size: 64, weight: .light, design: .rounded))
                        .monospacedDigit()
                        .foregroundStyle(Theme.success)
                        .accessibilityLabel("Listening for \(AskListenWords.clock(since: session.questionOpenedAt, now: context.date))")
                    if !call.heard.isEmpty {
                        Text("“\(call.heard)”")
                            .font(.callout).foregroundStyle(Theme.bone)
                            .multilineTextAlignment(.center)
                    }
                    Text(session.recording != nil
                         ? "Stay quiet. The answer is being recorded."
                         : "Stay quiet. The wait is marked, but no sound is being recorded here.")
                        .font(.caption).foregroundStyle(Theme.fog)
                        .multilineTextAlignment(.center)
                }
            }
            .frame(maxWidth: .infinity)
            .padding(16)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 14))
            .overlay(RoundedRectangle(cornerRadius: 14)
                .strokeBorder(AskListenWords.color(call.phase), lineWidth: call.phase == .ready ? 0 : 3))
            .accessibilityElement(children: .contain)
            .accessibilityIdentifier("evp-state")
        }
    }

    /// What is being asked: written down as it is said where the phone can, typed otherwise.
    @ViewBuilder
    private var questionField: some View {
        if call.isDictating {
            Text(call.heard.isEmpty ? "Writing down what you say…" : call.heard)
                .font(.callout)
                .foregroundStyle(call.heard.isEmpty ? Theme.fog : Theme.bone)
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity)
                .accessibilityLabel(call.heard.isEmpty ? "Writing down what you say" : "Question: \(call.heard)")
                .accessibilityIdentifier("evp-dictated-question")
        } else {
            TextField("Type the question (optional)",
                      text: Binding(get: { call.heard }, set: { call.type($0) }), axis: .vertical)
                .textFieldStyle(.roundedBorder)
                .lineLimit(1...3)
                .focused($typing)
                .submitLabel(.done)
                .accessibilityLabel("Question being asked")
                .accessibilityIdentifier("evp-question-text")
        }
    }

    private var controls: some View {
        VStack(spacing: 12) {
            Button {
                typing = false
                Task { await call.press(session: session, dictation: dictation, typeQuestions: typeQuestions) }
            } label: {
                Label(call.primaryTitle, systemImage: primarySymbol)
                    .font(.title2.bold())
                    .frame(maxWidth: .infinity, minHeight: 84)
            }
            .buttonStyle(.borderedProminent)
            .tint(primaryTint)
            .accessibilityLabel(call.primaryTitle)
            .accessibilityHint(call.primaryHint)
            .accessibilityIdentifier("evp-primary")

            switch call.phase {
            case .ready:
                EmptyView()
            case .asking:
                Button("Cancel this question") {
                    typing = false
                    Task { await call.cancelAsking(dictation: dictation) }
                }
                .buttonStyle(.signalSecondary)
                .accessibilityHint("Drops the question without marking it.")
                .accessibilityIdentifier("evp-cancel-asking")
            case .listening:
                Button {
                    Task { await call.stopListening(session: session) }
                } label: {
                    Label("Stop listening", systemImage: "stop.circle")
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.signalSecondary)
                .accessibilityHint("Ends the wait without asking another question.")
                .accessibilityIdentifier("evp-stop-listening")
            }
        }
    }

    private var primarySymbol: String {
        switch call.phase {
        case .ready: "mic"
        case .asking: "ear"
        case .listening: "mic"
        }
    }

    private var primaryTint: Color {
        switch call.phase {
        case .ready: Theme.ecto
        case .asking: Theme.success
        case .listening: Theme.danger
        }
    }

    @ViewBuilder
    private var questionList: some View {
        if !session.questions.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                ForEach(session.questions) { question in
                    HStack(alignment: .firstTextBaseline, spacing: 10) {
                        Image(systemName: "questionmark.circle")
                            .font(.caption).foregroundStyle(Theme.haunt)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(question.text ?? "Question")
                                .font(.callout).foregroundStyle(Theme.bone)
                            Text(question.at, format: ReaderClock.dateTime.hour().minute().second())
                                .font(.caption2).foregroundStyle(Theme.fog)
                        }
                        Spacer()
                        Text(question.waitedSeconds.map { "listened \(Int($0.rounded()))s" } ?? "listening")
                            .font(.caption.monospacedDigit())
                            .foregroundStyle(question.waitedSeconds == nil
                                             ? Theme.success : Theme.fog)
                    }
                    .accessibilityElement(children: .combine)
                    .accessibilityIdentifier("evp-question-row")
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(10)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 10))
        }
    }
}
