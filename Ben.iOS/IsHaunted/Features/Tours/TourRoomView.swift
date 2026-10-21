import SwiftUI
import BenKit

/// A tour date's room (event-scenario-gaps, 10/21/2026): the people checked in on the walk and its guides,
/// staying in touch. Ben, 10/20/2026: "You need to be able to stay in touch with people checked in with the
/// event or tour if you both are checked in", with the guide's posts in a different color.
///
/// Words only. The server answers nothing to anybody who isn't in the room, and this screen is only offered
/// to people who are.
struct TourRoomView: View {
    let dateId: UUID

    @Environment(AppDependencies.self) private var dependencies

    @State private var room: TourRoom?
    @State private var loaded = false
    @State private var error: String?
    @State private var draft = ""
    @State private var busy = false

    var body: some View {
        SignalList {
            if !loaded {
                Section { ProgressView("Loading the room…") }
            } else if let room {
                Section {
                    if room.canPost {
                        TextField("Write to everybody on the walk", text: $draft, axis: .vertical)
                            .lineLimit(2...5)
                            .accessibilityIdentifier("tour-room-body")
                        Button {
                            Task { await post() }
                        } label: {
                            Text(busy ? "Posting…" : "Post")
                        }
                        .buttonStyle(.signalPrimary)
                        .disabled(busy || draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                        .accessibilityIdentifier("tour-room-post")
                    } else if let why = room.whyNotPost {
                        Text(why).foregroundStyle(Theme.fog)
                    }
                    if let error { Text(error).foregroundStyle(Theme.danger) }
                } footer: {
                    Text("For the people checked in on this walk and its guides.")
                }

                Section {
                    if room.messages.isEmpty {
                        Text("Nothing here yet.").foregroundStyle(Theme.fog)
                    }
                    ForEach(room.messages) { message in
                        row(message, room)
                    }
                }
            } else {
                Section {
                    Text("This room is for the people checked in on the walk.").foregroundStyle(Theme.fog)
                }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle(room?.title ?? "The walk's room")
        .navigationBarTitleDisplayMode(.inline)
        .refreshable { await load() }
        .task { await load() }
    }

    @ViewBuilder
    private func row(_ message: TourRoomMessage, _ room: TourRoom) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Text(message.isMine ? "You" : message.authorName).font(.subheadline.weight(.semibold))
                if let role = message.authorRole {
                    StatusBadge(text: role, colour: Theme.ecto).accessibilityIdentifier("tour-room-role")
                }
                Spacer()
                Text(message.postedUtc.formatted(.relative(presentation: .named))).font(.caption).foregroundStyle(Theme.fog)
            }
            Text(message.body)
        }
        .padding(.vertical, 2)
        // The people running the walk stand out with a colored edge as well as the badge.
        .overlay(alignment: .leading) {
            if message.authorRole != nil {
                Rectangle().fill(Theme.ecto).frame(width: 3).offset(x: -12)
            }
        }
        .swipeActions {
            if message.isMine || room.myRole != nil {
                Button("Take down", role: .destructive) { Task { await remove(message) } }
            }
        }
    }

    private var store: HostedEventsStore { HostedEventsStore(api: dependencies.api) }

    private func load() async {
        switch await store.loadTourRoom(dateId) {
        case .ok(let value): room = value; error = nil
        case .failed(let reason, _): error = reason ?? "The room couldn't be fetched just now."
        default: error = "The room couldn't be fetched just now."
        }
        loaded = true
    }

    private func post() async {
        let words = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !words.isEmpty else { return }
        busy = true
        defer { busy = false }
        switch await store.postTourRoom(dateId, body: words) {
        case .ok(let value): room = value; draft = ""; error = nil
        case .failed(let reason, _): error = reason ?? "That couldn't be posted just now."
        default: error = "That couldn't be posted just now."
        }
    }

    private func remove(_ message: TourRoomMessage) async {
        switch await store.removeTourRoomMessage(dateId, message: message.id) {
        case .ok(let value): room = value; error = nil
        case .failed(let reason, _): error = reason ?? "That couldn't be taken down just now."
        default: error = "That couldn't be taken down just now."
        }
    }
}
