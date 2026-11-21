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
    /// The room step after publishing (Ben, 11/21/2026): which room of the place this was recorded in.
    @State private var rooms: SessionRoomChoices?
    @State private var roomName = ""
    @State private var roomFailure: String?

    private var coordinates: (latitude: Double, longitude: Double)? {
        dependencies.fieldKit.coordinates(for: localSessionId)
    }

    var body: some View {
        NavigationStack {
            SignalForm {
                if published {
                    Section {
                        Label("Published", systemImage: "checkmark.circle")
                            .foregroundStyle(Theme.success)
                        Text("Your readings are on the location's page now, alongside everyone "
                           + "else who has recorded there. On a paid plan, you can take them back out.")
                            .font(.callout).foregroundStyle(Theme.fog)
                    }
                    roomSection
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
            // It said photos, video and audio stay private. The ones you chose to send are shared with the
            // readings, and anyone can report one (Ben, 11/21/2026).
            Text("Your readings become public on this location's page, with your name and the "
               + "times you recorded, and any photos, video and audio you chose to send. "
               + "Anyone can report one. Next, you can say which room it was.")
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
                TextField("Name people would recognize", text: $newName)
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

    /// Which room it was recorded in. One tap on a room the place already has, or a typed name.
    @ViewBuilder
    private var roomSection: some View {
        if let rooms {
            Section {
                if let current = rooms.roomName, !current.isEmpty {
                    Label("Recorded in \(current)", systemImage: "door.left.hand.open")
                        .foregroundStyle(Theme.success)
                        .accessibilityIdentifier("archive-room-now")
                }
                ForEach(rooms.rooms) { room in
                    Button {
                        Task { await saveRoom(placeRoomId: room.id, name: nil) }
                    } label: {
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(room.name).foregroundStyle(Theme.bone)
                                Text([room.floor, "named by \(room.groupName)"].compactMap { $0 }.joined(separator: " · "))
                                    .font(.caption).foregroundStyle(Theme.fog)
                            }
                            Spacer()
                            if rooms.placeRoomId == room.id {
                                Image(systemName: "checkmark").foregroundStyle(Theme.ecto)
                            }
                        }
                    }
                    .disabled(busy)
                }
                HStack {
                    TextField(rooms.rooms.isEmpty ? "The room's name, such as Room 217" : "Another room — type its name",
                              text: $roomName)
                        .accessibilityIdentifier("archive-room-name")
                    Button("Save") {
                        Task { await saveRoom(placeRoomId: nil, name: roomName.trimmingCharacters(in: .whitespaces)) }
                    }
                    .disabled(busy || roomName.trimmingCharacters(in: .whitespaces).isEmpty)
                    .accessibilityIdentifier("archive-room-save")
                }
                if let roomFailure {
                    Text(roomFailure).foregroundStyle(Theme.danger)
                }
            } header: {
                Text("Which room?")
            } footer: {
                Text("The location's page shows each session with its room, so a room's nights can be read together. "
                   + "You can change it later on the website.")
            }
        }
    }

    // MARK: - Work

    private func saveRoom(placeRoomId: UUID?, name: String?) async {
        busy = true
        roomFailure = nil
        defer { busy = false }
        switch await dependencies.archiveActions.setRoom(
            serverSessionId: serverSessionId, placeRoomId: placeRoomId, name: name) {
        case .success(let saved):
            rooms = saved
            roomName = ""
        case .failure(let error):
            roomFailure = error.message
        }
    }

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
            rooms = await dependencies.archiveActions.roomChoices(serverSessionId: serverSessionId)
        case .failure(let error):
            // The server's sentence, when it wrote one — "Only public locations have an open
            // archive" tells somebody what to do, and "couldn't publish" tells them nothing.
            failure = error.message
        }
    }
}
