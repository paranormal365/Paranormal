import SwiftUI
import VisionKit
import BenKit

/// One night's door on the phone (item 235 phase 14c): who is expected, who is in, and letting a party in by
/// name, by scanning their pass, or by the pass code they read out.
///
/// **Made for a thumb in the dark.** The buttons are tall, the count is at the top, and the screen does not go
/// to sleep while it is open. A steward reads names and dietary flags; nobody's address is here.
///
/// **No signal is an ordinary evening.** The last list read is shown with when it was kept; arrivals are kept on
/// the phone with the time they happened and sent when there is signal, and anything the server then refuses is
/// listed by name so somebody can sort it out.
struct DoorView: View {
    let organizationId: UUID
    let hostedEventId: UUID

    @Environment(AppDependencies.self) private var dependencies
    @Environment(\.scenePhase) private var scenePhase

    @State private var store: DoorStore?
    @State private var door: HostedEventDoor?
    @State private var savedAt: Date?
    @State private var loaded = false
    @State private var failure: String?
    @State private var message: String?
    @State private var search = ""
    @State private var busyParty: UUID?
    @State private var waiting = 0
    @State private var refusedLater: [String] = []
    @State private var scanning = false
    @State private var capturedCode: String?
    @State private var lookingUp = false
    @State private var scanned: Reservation?
    @State private var opened: Reservation?
    @State private var note: String?
    @State private var addingWalkUp = false

    /// A reservation on screen: the party, and — when it came from the camera — the pass and what the server said.
    struct Reservation: Identifiable {
        let id = UUID()
        var party: HostedEventDoorParty
        var scan: HostedEventScanResult?
        var token: String?
    }

    var body: some View {
        Group {
            if let door {
                list(door)
            } else if !loaded {
                ProgressView("Opening the door…").frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ContentUnavailableView {
                    Label("Couldn't open the door", systemImage: "exclamationmark.triangle").foregroundStyle(Theme.warning)
                } description: {
                    Text(failure ?? "Check your connection and try again.")
                } actions: {
                    Button("Try again") { Task { await refresh() } }.buttonStyle(.borderedProminent)
                }
            }
        }
        .navigationTitle(door?.eventName ?? "The door")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if let door, door.nights.count > 1 {
                ToolbarItem(placement: .primaryAction) {
                    Menu {
                        ForEach(door.nights) { night in
                            Button { Task { await load(night: night.id) } } label: {
                                if night.id == door.nightId { Label(nightName(night), systemImage: "checkmark") } else { Text(nightName(night)) }
                            }
                        }
                    } label: {
                        Label("Night", systemImage: "calendar")
                    }
                    .accessibilityIdentifier("door-night")
                }
            }
        }
        .searchable(text: $search, placement: .navigationBarDrawer(displayMode: .always), prompt: "Name or pass code")
        .refreshable { await refresh() }
        .task {
            if store == nil { store = DoorStore(api: dependencies.api) }
            await refresh()
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active { Task { await refresh() } }
        }
        .onAppear { UIApplication.shared.isIdleTimerDisabled = true }
        .onDisappear { UIApplication.shared.isIdleTimerDisabled = false }
        .fullScreenCover(isPresented: $scanning, onDismiss: {
            if let code = capturedCode {
                capturedCode = nil
                Task { await lookUp(code) }
            }
        }) {
            DoorScannerView { code in capturedCode = code }
        }
        .sheet(item: $opened) { reservation in
            if let door, let store {
                ReservationCheckInView(door: door, organizationId: organizationId, store: store, party: reservation.party,
                                       scan: reservation.scan, token: reservation.token) { updated, sentence in
                    self.door = updated
                    note = sentence
                    message = nil
                    scanned = nil
                    search = ""
                    waiting = store.waiting(for: hostedEventId).count
                }
            }
        }
        .sheet(isPresented: $addingWalkUp) {
            if let door, let store {
                WalkUpSheet(door: door, organizationId: organizationId, store: store) { updated in
                    self.door = updated
                }
                .presentationDetents([.medium])
            }
        }
    }

    // ── the list ─────────────────────────────────────────────────────────────

    @ViewBuilder
    private func list(_ door: HostedEventDoor) -> some View {
        let matching = door.expected.filter(matches)
        let coming = matching.filter { !$0.isIn }
        let inside = matching.filter(\.isIn)

        List {
            Section {
                VStack(alignment: .leading, spacing: 4) {
                    Text("\(door.peopleIn) in · \(door.peopleExpected) expected")
                        .font(.title2.weight(.semibold).monospacedDigit())
                        .accessibilityIdentifier("door-count")
                    Text(nightTitle(door)).font(.subheadline).foregroundStyle(Theme.fog)
                    if let places = door.placesLeftSentence {
                        Text(places).font(.subheadline).foregroundStyle(door.placesLeft == 0 ? Theme.warning : Theme.fog)
                    }
                }
                .padding(.vertical, 4)

                Button {
                    #if DEBUG
                    // Automation hook, like `-autoSignIn`: `-doorScanCode <token>` stands in for the camera on a
                    // simulator, which has none, so the scan → reservation → check-in path can be walked.
                    if let code = UserDefaults.standard.string(forKey: "doorScanCode"), !DataScannerViewController.isSupported {
                        Task { await lookUp(code) }
                        return
                    }
                    #endif
                    if DataScannerViewController.isSupported && DataScannerViewController.isAvailable {
                        note = nil
                        message = nil
                        scanned = nil
                        scanning = true
                    } else {
                        message = DataScannerViewController.isSupported
                            ? "The camera isn't available. Allow it in Settings, or type the pass code in the search box."
                            : "This device can't scan codes. Type the pass code in the search box instead."
                    }
                } label: {
                    if lookingUp {
                        ProgressView().frame(maxWidth: .infinity, minHeight: 56)
                    } else {
                        Label("Scan a pass", systemImage: "qrcode.viewfinder")
                            .font(.title3.weight(.semibold))
                            .frame(maxWidth: .infinity, minHeight: 56)
                    }
                }
                .buttonStyle(.borderedProminent)
                .disabled(lookingUp)
                .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 8, trailing: 16))
                .accessibilityIdentifier("door-scan")
            }

            if let scanned {
                Section {
                    Button {
                        opened = scanned
                    } label: {
                        HStack {
                            partyDetails(scanned.party)
                            Spacer()
                            if scanned.party.isIn {
                                Text("Already in").font(.subheadline).foregroundStyle(Theme.success)
                            }
                            Image(systemName: "chevron.right").foregroundStyle(Theme.fog)
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityIdentifier("door-scanned-reservation")
                } header: {
                    Text("Scanned pass")
                } footer: {
                    Text(scanned.party.isIn ? "Tap to see the reservation." : "Tap the reservation to check them in.")
                }
            }

            if let note {
                Section { Label(note, systemImage: "checkmark.circle.fill").font(.subheadline).foregroundStyle(Theme.success) }
            }

            if let savedAt {
                Section {
                    Label("No signal — this is the list kept on this phone at \(savedAt.formatted(date: .omitted, time: .shortened)). Everyone you check in is kept and sent when there's signal.",
                          systemImage: "wifi.slash")
                        .font(.footnote).foregroundStyle(Theme.warning)
                }
            }
            if waiting > 0 {
                Section {
                    HStack {
                        Label(waiting == 1 ? "1 arrival waiting to send" : "\(waiting) arrivals waiting to send", systemImage: "tray.and.arrow.up")
                        Spacer()
                        Button("Send now") { Task { await refresh() } }.buttonStyle(.bordered)
                    }
                    .font(.subheadline)
                    .accessibilityIdentifier("door-waiting")
                }
            }
            if !refusedLater.isEmpty {
                Section {
                    ForEach(refusedLater, id: \.self) { line in
                        Label(line, systemImage: "exclamationmark.octagon").font(.footnote).foregroundStyle(Theme.danger)
                    }
                    Button("Clear these") { refusedLater = [] }.font(.footnote)
                } header: {
                    Text("Checked in with no signal, then refused")
                } footer: {
                    Text("They're in the building but not on the list. Find them, or tell the organizers.")
                }
            }
            if let message {
                Section { Text(message).font(.footnote).foregroundStyle(Theme.warning) }
            }

            Section(coming.isEmpty ? "Still to come" : "Still to come (\(coming.count))") {
                if coming.isEmpty {
                    Text(search.isEmpty ? "Everybody expected is in." : "Nobody still to come matches that.").foregroundStyle(Theme.fog)
                }
                ForEach(coming) { party in comingRow(party, door) }
            }

            if !inside.isEmpty {
                Section("In (\(inside.count))") {
                    ForEach(inside) { party in insideRow(party, door) }
                }
            }

            Section {
                ForEach(door.walkUps) { walkUp in
                    HStack {
                        Text(walkUp.name ?? "Somebody")
                        Spacer()
                        Text(walkUp.people == 1 ? "1 person" : "\(walkUp.people) people").foregroundStyle(Theme.fog)
                    }
                    .swipeActions {
                        Button("Take back", role: .destructive) { Task { await move { await $0.undoWalkUp(door, organization: organizationId, walkUp: walkUp) } } }
                    }
                }
                Button {
                    addingWalkUp = true
                } label: {
                    Label("Somebody without a booking", systemImage: "person.badge.plus").frame(minHeight: 44)
                }
                .accessibilityIdentifier("door-walk-up")
            } header: {
                Text("Without a booking")
            } footer: {
                Text("Swipe one to take it back.")
            }
        }
        .listStyle(.insetGrouped)
    }

    private func comingRow(_ party: HostedEventDoorParty, _ door: HostedEventDoor) -> some View {
        HStack(alignment: .center, spacing: 12) {
            Button { opened = Reservation(party: party) } label: {
                partyDetails(party).frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("door-reservation-\(party.id.uuidString.lowercased())")
            Spacer(minLength: 8)
            if busyParty == party.id {
                ProgressView().frame(minWidth: 96, minHeight: 56)
            } else {
                Menu {
                    if party.partySize > 1 {
                        ForEach((1..<party.partySize).reversed(), id: \.self) { count in
                            Button("Only \(count) of them") { Task { await arrive(party, door, people: count) } }
                        }
                    }
                } label: {
                    Text("Check in").font(.headline).frame(minWidth: 96, minHeight: 56)
                } primaryAction: {
                    Task { await arrive(party, door, people: nil) }
                }
                .buttonStyle(.borderedProminent)
                .disabled(busyParty != nil)
                .accessibilityIdentifier("door-check-in-\(party.id.uuidString.lowercased())")
            }
        }
        .padding(.vertical, 4)
    }

    private func insideRow(_ party: HostedEventDoorParty, _ door: HostedEventDoor) -> some View {
        HStack(alignment: .center, spacing: 12) {
            Button { opened = Reservation(party: party) } label: {
            VStack(alignment: .leading, spacing: 2) {
                partyDetails(party)
                if let arrived = party.arrivedUtc {
                    Text("In since \(arrived.formatted(date: .omitted, time: .shortened))"
                         + (party.peopleIn.map { " · \($0) of \(party.partySize)" } ?? ""))
                        .font(.caption).foregroundStyle(Theme.success)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading).contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            Spacer(minLength: 8)
            Button("Undo") { Task { await move { await $0.undo(door, organization: organizationId, party: party) } } }
                .buttonStyle(.bordered)
                .frame(minHeight: 44)
                .disabled(busyParty != nil)
        }
        .padding(.vertical, 2)
    }

    private func partyDetails(_ party: HostedEventDoorParty) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 6) {
                Text(party.leadName).font(.headline)
                if let band = party.band {
                    Text(band.colour)
                        .font(.caption.weight(.semibold))
                        .padding(.horizontal, 6).padding(.vertical, 2)
                        .background((Color(hex: band.hex) ?? Theme.ecto).opacity(0.3), in: Capsule())
                }
            }
            Text([party.partySize == 1 ? "1 person" : "Party of \(party.partySize)", party.where, party.code.map { "Code \($0)" }]
                    .compactMap { $0 }.joined(separator: " · "))
                .font(.subheadline).foregroundStyle(Theme.fog)
            if !party.dietary.isEmpty {
                Label(party.dietary.joined(separator: " · "), systemImage: "fork.knife")
                    .font(.caption).foregroundStyle(Theme.warning)
            }
        }
    }

    // ── reads and writes ─────────────────────────────────────────────────────

    /// Sends what was kept first, so the list read afterwards already has it.
    private func refresh() async {
        guard let store else { return }
        if !store.waiting(for: hostedEventId).isEmpty {
            let report = await store.sendWaiting()
            refusedLater.append(contentsOf: report.refused)
        }
        await load(night: door?.nightId)
    }

    private func load(night: UUID?) async {
        guard let store else { return }
        switch await store.loadDoor(organization: organizationId, event: hostedEventId, night: night) {
        case .live(let value):
            door = value
            savedAt = nil
            failure = nil
        case .saved(let value, let at):
            door = value
            savedAt = at
        case .failed(let reason):
            failure = reason
            if reason?.contains("isn't yours") == true { door = nil }
        }
        waiting = store.waiting(for: hostedEventId).count
        loaded = true
    }

    private func arrive(_ party: HostedEventDoorParty, _ door: HostedEventDoor, people: Int?) async {
        busyParty = party.id
        defer { busyParty = nil }
        await move { await $0.arrive(door, organization: organizationId, party: party, people: people) }
        if message == nil {
            search = ""
            note = "Checked in: \(party.leadName)."
        }
    }

    /// What a captured code means: the reservation it belongs to, shown for the door to tap — or why not, in words.
    private func lookUp(_ code: String) async {
        guard let store, let door else { return }
        lookingUp = true
        defer { lookingUp = false }
        note = nil
        message = nil
        scanned = nil

        switch await store.lookUp(door, organization: organizationId, token: code) {
        case .found(let result, let party?):
            scanned = Reservation(party: party, scan: result, token: code)
        case .found(let result, nil):
            message = "\(result.leadName ?? "That reservation") isn't expected tonight"
                + (result.nights?.isEmpty == false ? " — check the nights on their pass." : ".")
        case .foundOnThePhone(let party):
            scanned = Reservation(party: party, scan: nil, token: code)
        case .refused(let sentence):
            message = sentence
        case .notOnTheSavedList:
            message = "That pass isn't on the list kept on this phone. With no signal only tonight's reservations can be checked — look them up by name, or ask the organizers."
        case .failed(let sentence):
            message = sentence
        }
    }

    private func move(_ action: (DoorStore) async -> DoorStore.MoveOutcome) async {
        guard let store else { return }
        switch await action(store) {
        case .done(let updated):
            door = updated
            message = nil
            note = nil
        case .kept(let updated):
            door = updated
            message = nil
            if savedAt == nil { savedAt = Date() }
        case .refused(let sentence):
            message = sentence
        }
        waiting = store.waiting(for: hostedEventId).count
    }

    private func matches(_ party: HostedEventDoorParty) -> Bool {
        let typed = search.trimmingCharacters(in: .whitespaces)
        guard !typed.isEmpty else { return true }
        return party.leadName.localizedCaseInsensitiveContains(typed)
            || (party.code?.localizedCaseInsensitiveContains(typed.replacingOccurrences(of: " ", with: "")) ?? false)
    }

    private func nightTitle(_ door: HostedEventDoor) -> String {
        if let night = door.nights.first(where: { $0.id == door.nightId }) { return nightName(night) }
        return door.nightDate.formatted(Date.FormatStyle(timeZone: .gmt).weekday(.wide).month(.twoDigits).day(.twoDigits))
    }

    private func nightName(_ night: HostedEventNightInfo) -> String {
        let date = night.date.formatted(Date.FormatStyle(timeZone: .gmt).weekday(.abbreviated).month(.twoDigits).day(.twoDigits))
        guard let title = night.title, !title.isEmpty else { return date }
        return "\(date) · \(title)"
    }
}

/// Somebody who turned up without a booking: how many, and a name if they gave one.
private struct WalkUpSheet: View {
    let door: HostedEventDoor
    let organizationId: UUID
    let store: DoorStore
    var onDone: (HostedEventDoor) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var people = 1
    @State private var name = ""
    @State private var busy = false
    @State private var refusal: String?

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Stepper(people == 1 ? "1 person" : "\(people) people", value: $people, in: 1...40)
                    TextField("Name (optional)", text: $name)
                } footer: {
                    Text(door.placesLeftSentence ?? "They're counted in tonight's number. No account is made for them.")
                }
                if let refusal {
                    Section { Label(refusal, systemImage: "exclamationmark.triangle").foregroundStyle(Theme.danger).font(.callout) }
                }
            }
            .navigationTitle("Without a booking")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() }.disabled(busy) }
                ToolbarItem(placement: .confirmationAction) {
                    if busy { ProgressView() } else { Button("Let them in") { Task { await save() } } }
                }
            }
        }
    }

    private func save() async {
        busy = true
        defer { busy = false }
        switch await store.walkUp(door, organization: organizationId, people: people, name: name) {
        case .done(let updated), .kept(let updated):
            onDone(updated)
            dismiss()
        case .refused(let sentence):
            refusal = sentence
        }
    }
}
