import SwiftUI
import BenKit

/// The screen you look at while a session is recording.
///
/// Slice 1 gives it the session's identity and the way to stop; the gauges, capture bar, sentry
/// and EVP modes arrive in the slices that follow. It is deliberately reachable and honest now
/// rather than hidden behind a flag — a screen nobody can open is a screen nobody has tested.
struct LiveSessionView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(Router.self) private var router

    let sessionId: UUID

    @State private var errorMessage: String?

    private var summary: FieldSessionSummary? {
        dependencies.fieldKit.summary(for: sessionId)
    }

    var body: some View {
        List {
            if let summary {
                Section {
                    HStack(spacing: 10) {
                        Image(systemName: "record.circle")
                            .foregroundStyle(Theme.danger)
                            .symbolEffect(.pulse)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(summary.title).font(.headline)
                            Text("Started \(summary.startedAt.formatted(date: .omitted, time: .shortened))")
                                .font(.caption).foregroundStyle(Theme.fog)
                        }
                    }
                }

                Section {
                    Label("Readings and gauges arrive in the next update.",
                          systemImage: "gauge.with.needle")
                        .font(.callout)
                        .foregroundStyle(Theme.fog)
                }

                Section {
                    Button(role: .destructive) {
                        stop()
                    } label: {
                        Label("Stop recording", systemImage: "stop.circle")
                    }
                    .accessibilityIdentifier("stop-field-session")
                }
            } else {
                ContentUnavailableView {
                    Label("That session isn't here", systemImage: "waveform.slash")
                } description: {
                    Text("It may have been deleted.")
                }
            }
        }
        .navigationTitle(summary?.title ?? "Session")
        .navigationBarTitleDisplayMode(.inline)
        .alert("Couldn't stop the session",
               isPresented: Binding(get: { errorMessage != nil },
                                    set: { if !$0 { errorMessage = nil } })) {
            Button("OK", role: .cancel) { errorMessage = nil }
        } message: { Text(errorMessage ?? "") }
        .onAppear { dependencies.fieldKit.load() }
    }

    private func stop() {
        do {
            try dependencies.fieldKit.endSession(sessionId)
            router.push(.fieldSessionReview(sessionId))
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
