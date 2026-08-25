import SwiftUI
import BenKit

/// Says which room the operator is standing in, and lets them change it in one tap.
///
/// **Why a person has to say this.** Indoors a fix is 20–50 m wide — the width of the whole
/// house — so nothing the instruments produce can tell the cellar from the front bedroom. The
/// operator is the only reliable source of that fact, so the app asks for it once and then
/// stamps it onto every reading, mark and capture until they say otherwise.
struct RoomBar: View {
    let room: String?
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                Image(systemName: "door.left.hand.open")
                    .font(.title3)
                    .foregroundStyle(Theme.ecto)
                VStack(alignment: .leading, spacing: 1) {
                    Text(room ?? "Set the room")
                        .font(.headline)
                        .foregroundStyle(room == nil ? Theme.fog : Theme.bone)
                    Text(room == nil
                         ? "Everything you record will say where it came from."
                         : "Everything from here on is marked as this room.")
                        .font(.caption)
                        .foregroundStyle(Theme.fog)
                }
                Spacer(minLength: 8)
                Image(systemName: "chevron.right")
                    .font(.footnote.weight(.semibold))
                    .foregroundStyle(Theme.fog)
            }
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Theme.mist, in: RoundedRectangle(cornerRadius: 12))
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("room-bar")
        .accessibilityLabel(room.map { "Room: \($0)" } ?? "Set the room")
    }
}

/// Picking the room. Rooms already used this session come first — walking a loop through a
/// house means naming the same four rooms over and over, and typing in the dark is miserable.
struct RoomSheet: View {
    let current: String?
    let visited: [String]
    let onChoose: (String?) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var draft: String = ""
    @FocusState private var typing: Bool

    /// The rooms almost every property has. Offered only as a starting point — the operator's
    /// own words ("back bedroom, north wall") are always better and always allowed.
    private static let common = ["Living room", "Kitchen", "Hallway", "Bedroom", "Bathroom",
                                 "Basement", "Attic", "Stairs", "Garage", "Outside"]

    private var suggestions: [String] {
        var seen = Set(visited.map { $0.lowercased() })
        var list = visited
        for name in Self.common where !seen.contains(name.lowercased()) {
            seen.insert(name.lowercased())
            list.append(name)
        }
        return list
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Room", text: $draft)
                        .focused($typing)
                        .submitLabel(.done)
                        .onSubmit { choose(draft) }
                        .accessibilityIdentifier("room-name-field")
                    Button("Use this room") { choose(draft) }
                        .disabled(draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                        .accessibilityIdentifier("room-save")
                } header: {
                    Text("Where are you?")
                } footer: {
                    Text("A fix indoors covers the whole building, so this is the only thing "
                         + "that tells one room from another later. Changing it also drops a "
                         + "mark, so the timeline shows when you moved.")
                }

                Section("Quick pick") {
                    ForEach(suggestions, id: \.self) { name in
                        Button { choose(name) } label: {
                            HStack {
                                Text(name)
                                Spacer()
                                if name.caseInsensitiveCompare(current ?? "") == .orderedSame {
                                    Image(systemName: "checkmark")
                                        .foregroundStyle(Theme.ecto)
                                }
                            }
                        }
                        .accessibilityIdentifier("room-suggestion")
                    }
                }

                if current != nil {
                    Section {
                        Button("Stop naming a room", role: .destructive) { onChoose(nil); dismiss() }
                            .accessibilityIdentifier("room-clear")
                    } footer: {
                        Text("Readings keep their position, they just stop saying which room.")
                    }
                }
            }
            .navigationTitle("Room")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
            }
            .onAppear {
                draft = current ?? ""
                // Not auto-focused: most of the time the room is already in the quick list, and
                // a keyboard covering it costs a tap rather than saving one.
            }
        }
    }

    private func choose(_ name: String) {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        onChoose(trimmed)
        dismiss()
    }
}
