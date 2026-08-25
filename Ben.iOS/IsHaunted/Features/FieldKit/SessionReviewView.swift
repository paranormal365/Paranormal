import SwiftUI
import BenKit

/// A finished session, read back.
///
/// Slice 1 shows what the session IS — when, where, how long, what it holds, and what it is
/// linked to. The timeline, chart, movement map and export arrive in later slices.
struct SessionReviewView: View {
    @Environment(AppDependencies.self) private var dependencies

    let sessionId: UUID

    @State private var linking = false

    private var summary: FieldSessionSummary? {
        dependencies.fieldKit.summary(for: sessionId)
    }

    var body: some View {
        List {
            if let summary {
                Section {
                    LabeledContent("Started",
                                   value: summary.startedAt.formatted(date: .abbreviated,
                                                                      time: .shortened))
                    if let duration = summary.duration {
                        LabeledContent("Ran for", value: Self.durationText(duration))
                    } else if summary.outcome == .interrupted {
                        // Said plainly: the app went away mid-session, so its end time is
                        // genuinely unknown rather than quietly guessed.
                        LabeledContent("Ended", value: "Interrupted — end time unknown")
                    }
                    LabeledContent("Readings", value: "\(summary.readingCount)")
                    if summary.markerCount > 0 {
                        LabeledContent("Marked", value: "\(summary.markerCount)")
                    }
                    if summary.captureCount > 0 {
                        LabeledContent("Captured", value: "\(summary.captureCount)")
                    }
                }

                Section {
                    if let investigation = summary.investigationTitle, !investigation.isEmpty {
                        LabeledContent("Investigation", value: investigation)
                    } else {
                        Text("Not linked to an investigation.")
                            .font(.callout).foregroundStyle(Theme.fog)
                    }
                } header: {
                    Text("Linked to")
                }

                Section {
                    Label("The timeline, chart, movement map and export arrive in the next updates.",
                          systemImage: "chart.xyaxis.line")
                        .font(.callout).foregroundStyle(Theme.fog)
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
        .onAppear { dependencies.fieldKit.load() }
    }

    static func durationText(_ seconds: TimeInterval) -> String {
        let total = Int(seconds.rounded())
        let hours = total / 3600, minutes = (total % 3600) / 60, secs = total % 60
        if hours > 0 { return "\(hours)h \(minutes)m" }
        if minutes > 0 { return "\(minutes)m \(secs)s" }
        return "\(secs)s"
    }
}
