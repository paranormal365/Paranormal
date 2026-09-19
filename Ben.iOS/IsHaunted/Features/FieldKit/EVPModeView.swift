import SwiftUI
import BenKit

/// Question, silence, question — the shape of an EVP session.
///
/// Two controls, both large enough to hit without looking, because this is used in the dark
/// while talking to a room. Everything else on the screen is there to be glanced at, not read.
///
/// The wait is what matters. Marking only the moment somebody spoke would leave a reviewer
/// hunting through hours of tape; bracketing the silence after each question turns the same
/// recording into a list of places to listen.
struct EVPModeView: View {
    @Environment(\.dismiss) private var dismiss

    let session: ActiveFieldSession

    @State private var draft = ""
    @State private var showingQuestionField = false
    @State private var blackout = false

    private var isWaiting: Bool { session.questionOpenedAt != nil }

    var body: some View {
        NavigationStack {
            VStack(spacing: 18) {
                recordingBanner

                Spacer(minLength: 0)

                waitReadout

                Spacer(minLength: 0)

                controls

                questionList
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 12)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Theme.ink)
            .navigationTitle("EVP")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Done") { dismiss() }
                }
                ToolbarItem(placement: .primaryAction) {
                    Button { blackout = true } label: { Image(systemName: "moon.fill") }
                        .accessibilityLabel("Blackout the screen")
                }
            }
            .alert("What did you ask?", isPresented: $showingQuestionField) {
                TextField("Is anyone here with us?", text: $draft)
                Button("Asked") {
                    let text = draft
                    draft = ""
                    Task { await session.askQuestion(text.isEmpty ? nil : text) }
                }
                Button("Cancel", role: .cancel) { draft = "" }
            } message: {
                Text("Optional — the mark is made either way.")
            }
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
                Text("Questions will still be marked, but there'll be no audio here to play back.")
                    .font(.caption).foregroundStyle(Theme.fog)
                Button("Start recording") {
                    Task { await session.startRecording() }
                }
                .buttonStyle(.bordered)
                .accessibilityIdentifier("evp-start-recording")
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(10)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 10))
        }
    }

    @ViewBuilder
    private var waitReadout: some View {
        if let openedAt = session.questionOpenedAt {
            TimelineView(.periodic(from: .now, by: 1)) { context in
                VStack(spacing: 6) {
                    Text("waiting")
                        .font(.caption).foregroundStyle(Theme.fog)
                    Text(SessionClock.elapsed(from: openedAt, to: context.date))
                        .font(.system(size: 64, weight: .light, design: .rounded))
                        .monospacedDigit()
                        .foregroundStyle(Theme.ecto)
                }
                .accessibilityElement(children: .combine)
                .accessibilityLabel("Waiting for an answer")
            }
        } else {
            VStack(spacing: 6) {
                Image(systemName: "questionmark.bubble")
                    .font(.system(size: 44))
                    .foregroundStyle(Theme.fog)
                Text(session.questions.isEmpty
                     ? "Ask a question, then wait."
                     : "Ready for the next question.")
                    .font(.callout).foregroundStyle(Theme.fog)
            }
        }
    }

    private var controls: some View {
        VStack(spacing: 12) {
            Button {
                if isWaiting {
                    Task { await session.endWait() }
                } else {
                    Task { await session.askQuestion(nil) }
                }
            } label: {
                Text(isWaiting ? "Stop waiting" : "Ask a question")
                    .font(.title2.bold())
                    .frame(maxWidth: .infinity, minHeight: 84)
            }
            .buttonStyle(.borderedProminent)
            .tint(isWaiting ? Theme.warning : Theme.ecto)
            .accessibilityIdentifier("evp-primary")

            // The secondary path, for when there is time to type what was asked. Deliberately
            // secondary: in the dark, the mark matters more than the wording.
            if !isWaiting {
                Button {
                    showingQuestionField = true
                } label: {
                    Label("Ask, and type it", systemImage: "square.and.pencil")
                        .frame(maxWidth: .infinity, minHeight: 44)
                }
                .buttonStyle(.bordered)
                .accessibilityIdentifier("evp-ask-with-text")
            }
        }
    }

    @ViewBuilder
    private var questionList: some View {
        if !session.questions.isEmpty {
            ScrollView {
                VStack(alignment: .leading, spacing: 8) {
                    ForEach(session.questions) { question in
                        HStack(alignment: .firstTextBaseline, spacing: 10) {
                            Image(systemName: "questionmark.circle")
                                .font(.caption).foregroundStyle(Theme.haunt)
                            VStack(alignment: .leading, spacing: 1) {
                                Text(question.text ?? "Question")
                                    .font(.callout).foregroundStyle(Theme.bone)
                                Text(question.at, format: .dateTime.hour().minute().second())
                                    .font(.caption2).foregroundStyle(Theme.fog)
                            }
                            Spacer()
                            Text(question.waitedSeconds.map { "\(Int($0.rounded()))s" } ?? "waiting")
                                .font(.caption.monospacedDigit())
                                .foregroundStyle(question.waitedSeconds == nil
                                                 ? Theme.ecto : Theme.fog)
                        }
                        .accessibilityIdentifier("evp-question-row")
                    }
                }
            }
            .frame(maxHeight: 180)
            .padding(10)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 10))
        }
    }
}
