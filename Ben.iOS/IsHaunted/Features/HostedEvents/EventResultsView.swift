import SwiftUI
import BenKit

/// The event's results (event-scenario-gaps, 10/21/2026): the evidence the organizers accepted, most
/// positive votes first and then the earliest caught, and the results page on the website.
///
/// Ben's scenario: "They could see the event results in the app." A piece opens in the browser sheet,
/// which plays recordings and video and shows photos without the app storing anybody's file.
struct EventResultsView: View {
    let hostedEventId: UUID

    @Environment(AppDependencies.self) private var dependencies

    @State private var results: HostedEventResults?
    @State private var loaded = false
    @State private var failed = false
    @State private var open: URL?

    var body: some View {
        SignalList {
            if !loaded {
                Section { ProgressView("Loading the results…") }
            } else if failed {
                Section {
                    Text("The results couldn't be fetched just now. Pull down to try again.")
                        .foregroundStyle(Theme.warning)
                }
            } else if let results {
                if let sentence = results.evidenceSentence {
                    Section { Text(sentence).font(.subheadline) }
                }
                Section {
                    if results.items.isEmpty {
                        Text("No evidence has been accepted yet. The organizers choose what goes here.")
                            .foregroundStyle(Theme.fog)
                            .accessibilityIdentifier("results-empty")
                    }
                    ForEach(Array(results.items.enumerated()), id: \.element.id) { index, item in
                        Button { open = URL(string: item.fileUrl) } label: { row(index + 1, item) }
                            .accessibilityIdentifier("results-item")
                    }
                } header: {
                    Text("What people caught")
                } footer: {
                    Text(results.asksForVotes
                         ? "Ranked by positive votes, then by when it was caught. Vote for the best on the results page."
                         : "Ranked by positive votes, then by when it was caught.")
                }
                Section {
                    Button { open = URL(string: results.resultsUrl) } label: {
                        Label("Open the results page", systemImage: "safari")
                    }
                    .accessibilityIdentifier("results-page")
                }
            } else {
                Section { Text("This event's results aren't on the site.").foregroundStyle(Theme.fog) }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("Results")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await load() }
        .task { await load() }
        .sheet(item: $open) { url in SafariSheet(url: url).ignoresSafeArea() }
    }

    private func row(_ rank: Int, _ item: HostedEventResultItem) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(rank)").font(.headline.monospacedDigit()).foregroundStyle(Theme.ecto).frame(minWidth: 24)
            VStack(alignment: .leading, spacing: 3) {
                Text(item.fileName).foregroundStyle(.primary).lineLimit(2)
                Text("\(EventResultsWording.kind(item)) · \(item.by) · \(item.caughtUtc.formatted(date: .abbreviated, time: .shortened))")
                    .font(.caption).foregroundStyle(Theme.fog)
                if let note = item.note, !note.isEmpty {
                    Text(note).font(.caption).foregroundStyle(.secondary).lineLimit(3)
                }
            }
            Spacer(minLength: 0)
            if let votes = EventResultsWording.votes(item.positiveVotes) {
                StatusBadge(text: votes, colour: Theme.success)
            }
        }
        .padding(.vertical, 2)
    }

    private func load() async {
        switch await HostedEventsStore(api: dependencies.api).loadResults(hostedEventId) {
        case .ok(let value): results = value; failed = false
        default: failed = true
        }
        loaded = true
    }
}
