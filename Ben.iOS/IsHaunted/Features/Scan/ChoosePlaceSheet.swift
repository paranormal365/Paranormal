import SwiftUI
import BenKit

/// Which place this session is at, asked before its first room scan (LiDAR plan, phase 1).
///
/// A room's scan belongs to its place, so the phone has to know the place before it can say
/// whether a room is scanned. Places near the phone come first — picking one is what stops one
/// building becoming two pages — and a place nobody has named yet can be named here.
struct ChoosePlaceSheet: View {
    let latitude: Double?
    let longitude: Double?
    let onChosen: (UUID, String?) async -> Void

    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    @State private var candidates: [ArchivePlaceCandidate] = []
    @State private var loading = true
    @State private var name = ""
    @State private var city = ""
    @State private var state = ""
    @State private var saving = false
    @State private var problem: String?

    var body: some View {
        NavigationStack {
            SignalForm {
                Section {
                    if loading {
                        ProgressView()
                    } else if candidates.isEmpty {
                        Text(latitude == nil
                             ? "The phone doesn't know where it is, so it can't suggest places. Name the place below."
                             : "No public places are recorded near here yet. Name this one below.")
                            .font(.callout)
                            .foregroundStyle(Theme.fog)
                    } else {
                        ForEach(candidates) { place in
                            Button {
                                Task { await choose(place.id, place.name) }
                            } label: {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(place.name ?? "A place with no name").foregroundStyle(Theme.bone)
                                    Text([place.where_, place.distanceText].filter { !$0.isEmpty }.joined(separator: " · "))
                                        .font(.caption)
                                        .foregroundStyle(Theme.fog)
                                }
                            }
                            .accessibilityIdentifier("scan-place-candidate")
                        }
                    }
                } header: {
                    Text("Near you")
                } footer: {
                    Text("A room's scan belongs to its place, and everybody who comes after you compares against it.")
                }

                Section {
                    TextField("What people call it", text: $name)
                        .textInputAutocapitalization(.words)
                        .accessibilityIdentifier("scan-place-name")
                    TextField("Town", text: $city)
                        .textInputAutocapitalization(.words)
                        .accessibilityIdentifier("scan-place-city")
                    TextField("State", text: $state)
                        .textInputAutocapitalization(.characters)
                        .accessibilityIdentifier("scan-place-state")
                    Button(saving ? "Adding…" : "Use this place") { Task { await namePlace() } }
                        .disabled(saving || [name, city, state].contains { $0.trimmingCharacters(in: .whitespaces).isEmpty })
                        .accessibilityIdentifier("scan-place-save")
                } header: {
                    Text("Name a place")
                } footer: {
                    Text("For a public place — a landmark, a business, a cemetery. A home is scanned through the group working there.")
                }

                if let problem {
                    Section {
                        Text(problem).foregroundStyle(Theme.warning)
                    }
                }
            }
            .accessibilityElement(children: .contain)
            .accessibilityIdentifier("scan-choose-place")
            .navigationTitle("Where is this?")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
            }
            .task {
                candidates = await dependencies.archiveActions.candidates(latitude: latitude, longitude: longitude)
                loading = false
            }
        }
    }

    private func choose(_ id: UUID, _ placeName: String?) async {
        await onChosen(id, placeName)
        dismiss()
    }

    private func namePlace() async {
        saving = true
        defer { saving = false }
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        switch await dependencies.placeScans.namePlace(
            name: trimmed, city: city.trimmingCharacters(in: .whitespacesAndNewlines),
            state: state.trimmingCharacters(in: .whitespacesAndNewlines),
            latitude: latitude, longitude: longitude) {
        case .success(let place):
            await choose(place.id, trimmed)
        case .failure(let error):
            problem = error.message
        }
    }
}
