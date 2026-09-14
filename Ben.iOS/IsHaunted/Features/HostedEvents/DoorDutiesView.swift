import SwiftUI
import BenKit

/// The events this person may let people into (item 235 phase 14c) — as one of the group, as a helper the
/// organizers added, or as one of the venue's own people when the venue lent them.
///
/// Kept on the phone like the lists behind it, so the way to tonight's door is still there with no signal.
struct DoorDutiesView: View {
    @Environment(AppDependencies.self) private var dependencies

    @State private var duties: [MyHostedEventDuty] = []
    @State private var savedAt: Date?
    @State private var loaded = false
    @State private var failure: String?

    var body: some View {
        Group {
            if !duties.isEmpty {
                List {
                    if let savedAt {
                        Section {
                            Label("No signal — this is the list kept on this phone at \(savedAt.formatted(date: .omitted, time: .shortened)).",
                                  systemImage: "wifi.slash")
                                .font(.footnote).foregroundStyle(Theme.warning)
                        }
                    }
                    Section {
                        ForEach(duties) { duty in
                            NavigationLink(value: AppRoute.door(organizationId: duty.organizationId, hostedEventId: duty.hostedEventId)) {
                                row(duty)
                            }
                            .accessibilityIdentifier("door-duty-\(duty.hostedEventId.uuidString.lowercased())")
                        }
                    } footer: {
                        Text("Tonight's list is kept on this phone once you've opened it, so the door still works with no signal.")
                    }
                }
                .listStyle(.insetGrouped)
            } else if !loaded {
                ProgressView("Loading your doors…").frame(maxWidth: .infinity, maxHeight: .infinity)
            } else if let failure {
                ContentUnavailableView {
                    Label("Couldn't load your doors", systemImage: "exclamationmark.triangle").foregroundStyle(Theme.warning)
                } description: {
                    Text(failure)
                } actions: {
                    Button("Try again") { Task { await load() } }.buttonStyle(.borderedProminent)
                }
            } else {
                ContentUnavailableView("No doors to run", systemImage: "door.left.hand.open",
                                       description: Text("When organizers ask you to help at the door of an event, it shows here."))
            }
        }
        .navigationTitle("Doors I'm running")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await load() }
        .task { await load() }
    }

    private func row(_ duty: MyHostedEventDuty) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(alignment: .firstTextBaseline) {
                Text(duty.eventName).font(.headline)
                Spacer()
                if duty.tonight { StatusBadge(text: "Tonight", colour: Theme.success) }
            }
            Text([duty.organizationName, duty.venueName].compactMap { $0 }.joined(separator: " · "))
                .font(.subheadline).foregroundStyle(Theme.fog)
            Text([HostedEventWords.dates(duty.startsOn, duty.endsOn), duty.roleLabel].compactMap { $0 }.joined(separator: " · "))
                .font(.caption).foregroundStyle(Theme.fog)
        }
        .padding(.vertical, 2)
    }

    private func load() async {
        switch await DoorStore(api: dependencies.api).loadDuties() {
        case .live(let value):
            duties = value
            savedAt = nil
            failure = nil
        case .saved(let value, let at):
            duties = value
            savedAt = at
        case .failed(let reason):
            failure = reason ?? "Check your connection and try again."
        }
        loaded = true
    }
}
