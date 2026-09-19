import SwiftUI
import BenKit

/// Putting a finished session into a public location's archive.
///
/// The screen is built around one conviction: **the picker is the feature, not the form.** A
/// location's archive is only worth anything if everybody who records there lands on the same
/// page, and the first three-person test of this feature produced two pages because one person
/// wrote "Keysburg Road" where another wrote "Rd". Matching on text will always lose that fight
/// eventually; showing somebody "Bell Witch Cave · 3 sessions · 300 ft away" wins it outright.
///
/// So nearby places are offered first and naming a new one is deliberately the second option,
/// reachable but not the default.
struct PublishToArchiveView: View {
    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.dismiss) private var dismiss

    /// The server's id for the session — publishing is a thing that happens to an uploaded row.
    let serverSessionId: UUID
    /// The local session, for its coordinates.
    let localSessionId: UUID

    @State private var candidates: [ArchivePlaceCandidate] = []
    @State private var loading = true
    @State private var naming = false
    @State private var newName = ""
    @State private var newCity = ""
    @State private var newState = ""
    @State private var busy = false
    @State private var failure: String?
    @State private var published = false

    private var coordinates: (latitude: Double, longitude: Double)? {
        dependencies.fieldKit.coordinates(for: localSessionId)
    }

    var body: some View {
        NavigationStack {
            Form {
                if published {
                    Section {
                        Label("Published", systemImage: "checkmark.circle")
                            .foregroundStyle(Theme.success)
                        Text("Your readings are on the location's page now, alongside everyone "
                           + "else who has recorded there. You can take them back out at any time.")
                            .font(.callout).foregroundStyle(Theme.fog)
                    }
                } else {
                    explainer
                    if loading {
                        Section { HStack { ProgressView(); Text("Looking for places nearby…").padding(.leading, 6) } }
                    } else {
                        nearbySection
                        namingSection
                    }
                    if let failure {
                        Section { Text(failure).foregroundStyle(Theme.danger) }
                    }
                }
            }
            .navigationTitle("Add to the archive")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button(published ? "Done" : "Cancel") { dismiss() }.disabled(busy)
                }
            }
            .task { await loadCandidates() }
        }
    }

    // MARK: - Sections

    @ViewBuilder
    private var explainer: some View {
        Section {
            Text("Your readings become public on this location's page, with your name and the "
               + "times you recorded. Photos, video and audio stay private — only the numbers "
               + "are shared.")
            Text("One person's readings are a story. A location recorded by ten people is "
               + "something anyone can check.")
                .font(.callout).foregroundStyle(Theme.fog)
        } header: {
            Text("What happens")
        }
    }

    @ViewBuilder
    private var nearbySection: some View {
        if !candidates.isEmpty {
            Section {
                ForEach(candidates) { candidate in
                    Button {
                        Task { await publish { await dependencies.archiveActions.publish(
                            sessionId: serverSessionId, toExisting: candidate.id) } }
                    } label: {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(candidate.name ?? "Unnamed place")
                                .foregroundStyle(Theme.bone)
                            HStack(spacing: 6) {
                                Text(candidate.distanceText)
                                if !candidate.where_.isEmpty { Text("· \(candidate.where_)") }
                                // The reason to pick this instead of starting a new page.
                                if candidate.publishedSessions > 0 {
                                    Text("· \(candidate.publishedSessions) session\(candidate.publishedSessions == 1 ? "" : "s") already")
                                        .foregroundStyle(Theme.ecto)
                                }
                            }
                            .font(.caption).foregroundStyle(Theme.fog)
                        }
                    }
                    .disabled(busy)
                }
            } header: {
                Text("Where were you?")
            } footer: {
                Text("Picking the place others used is what keeps one location on one page.")
            }
        } else if coordinates == nil {
            Section {
                Text("This session has no location — either it was declined or there was no "
                   + "signal. Name where you were and it will be matched to any existing record.")
                    .font(.callout).foregroundStyle(Theme.fog)
            }
        } else {
            Section {
                Text("Nothing has been recorded near here yet. Name it and you'll be the first.")
                    .font(.callout).foregroundStyle(Theme.fog)
            }
        }
    }

    @ViewBuilder
    private var namingSection: some View {
        Section {
            if naming {
                TextField("Name people would recognise", text: $newName)
                TextField("Town", text: $newCity)
                TextField("State", text: $newState).textInputAutocapitalization(.characters)
                Button {
                    Task { await publish { await dependencies.archiveActions.publish(
                        sessionId: serverSessionId,
                        naming: NewArchivePlace(
                            name: newName.trimmingCharacters(in: .whitespaces),
                            city: newCity.isEmpty ? nil : newCity,
                            state: newState.isEmpty ? nil : newState,
                            latitude: coordinates?.latitude,
                            longitude: coordinates?.longitude)) } }
                } label: {
                    if busy { ProgressView() } else { Text("Publish here") }
                }
                .disabled(busy || newName.trimmingCharacters(in: .whitespaces).isEmpty)
            } else {
                Button("Somewhere else — name it") { naming = true }
            }
        } footer: {
            // Said plainly and early, because it is the one refusal people will meet.
            Text("Public locations only. A session recorded at somebody's home can't be archived — "
               + "that work stays with you and your group.")
        }
    }

    // MARK: - Work

    private func loadCandidates() async {
        let here = coordinates
        candidates = await dependencies.archiveActions.candidates(
            latitude: here?.latitude, longitude: here?.longitude)
        // Nothing nearby means naming is the only path, so open it rather than making somebody
        // press a button to discover there was no choice.
        if candidates.isEmpty { naming = true }
        loading = false
    }

    private func publish(_ action: @Sendable () async -> Result<Void, FeedActionError>) async {
        busy = true
        failure = nil
        defer { busy = false }

        switch await action() {
        case .success:
            published = true
        case .failure(let error):
            // The server's sentence, when it wrote one — "Only public locations have an open
            // archive" tells somebody what to do, and "couldn't publish" tells them nothing.
            failure = error.message
        }
    }
}
