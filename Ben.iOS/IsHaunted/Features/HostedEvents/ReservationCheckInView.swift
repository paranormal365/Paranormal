import SwiftUI
import BenKit

/// One reservation at the door, and checking it in as arrived (item 235 phase 14c).
///
/// Ben, 2026-09-13: the scanner *"looks up and clicking the reservation allows the doorman to check them in as
/// having arrived to event."* So a scan never admits anybody by itself: it finds the reservation, the door taps it,
/// sees who it is — the lead name, how many, the nights and the room or seat, the colour they wear, anything the
/// kitchen needs to know — and checks them in. Tapping a party on tonight's list opens the same screen.
struct ReservationCheckInView: View {
    let door: HostedEventDoor
    let organizationId: UUID
    let store: DoorStore
    let party: HostedEventDoorParty
    /// What the server said about the scanned pass, when it was scanned with a signal: guest names and every night.
    var scan: HostedEventScanResult?
    /// The scanned pass itself, so the check-in is recorded as a scan and the pass is checked again.
    var token: String?
    var onCheckedIn: (HostedEventDoor, String) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var people: Int
    @State private var busy = false
    @State private var refusal: String?

    init(door: HostedEventDoor, organizationId: UUID, store: DoorStore, party: HostedEventDoorParty,
         scan: HostedEventScanResult? = nil, token: String? = nil,
         onCheckedIn: @escaping (HostedEventDoor, String) -> Void) {
        self.door = door
        self.organizationId = organizationId
        self.store = store
        self.party = party
        self.scan = scan
        self.token = token
        self.onCheckedIn = onCheckedIn
        _people = State(initialValue: party.partySize)
    }

    var body: some View {
        NavigationStack {
            List {
                Section {
                    VStack(alignment: .leading, spacing: 6) {
                        HStack(alignment: .firstTextBaseline) {
                            Text(party.leadName).font(.title2.weight(.bold))
                            Spacer()
                            if let band = party.band {
                                Text(band.colour)
                                    .font(.subheadline.weight(.semibold))
                                    .padding(.horizontal, 10).padding(.vertical, 4)
                                    .background((Color(hex: band.hex) ?? Theme.ecto).opacity(0.3), in: Capsule())
                            }
                        }
                        Text(party.partySize == 1 ? "1 person" : "Party of \(party.partySize)").font(.headline)
                        if let place = party.where { Text(place).foregroundStyle(Theme.fog) }
                        if let code = party.code { Text("Pass code \(code)").font(.subheadline.monospaced()).foregroundStyle(Theme.fog) }
                    }
                    .padding(.vertical, 4)
                }

                if let arrived = party.arrivedUtc, party.leftUtc == nil {
                    Section {
                        Label("Already checked in tonight at \(arrived.formatted(date: .omitted, time: .shortened))"
                              + (party.peopleIn.map { " — \($0) of \(party.partySize)" } ?? ""),
                              systemImage: "checkmark.circle.fill")
                            .foregroundStyle(Theme.success)
                    }
                }

                if !party.dietary.isEmpty {
                    Section("For the kitchen") {
                        ForEach(party.dietary, id: \.self) { Label($0, systemImage: "fork.knife").foregroundStyle(Theme.warning) }
                    }
                }

                if let guests = scan?.guestNames, !guests.isEmpty {
                    Section("With them") {
                        ForEach(guests, id: \.self) { Text($0) }
                    }
                }

                if let nights = scan?.nights, !nights.isEmpty {
                    Section("Nights") {
                        ForEach(nights, id: \.hostedEventNightId) { night in
                            HStack {
                                Text(night.date.formatted(Date.FormatStyle(timeZone: .gmt).weekday(.abbreviated).month(.twoDigits).day(.twoDigits)))
                                    .fontWeight(night.hostedEventNightId == door.nightId ? .semibold : .regular)
                                Spacer()
                                Text(night.unitName).foregroundStyle(Theme.fog)
                            }
                        }
                    }
                }

                if !party.isIn {
                    Section {
                        if party.partySize > 1 {
                            Stepper(people == party.partySize ? "All \(party.partySize) are here" : "\(people) of \(party.partySize) are here",
                                    value: $people, in: 1...party.partySize)
                        }
                        Button {
                            Task { await checkIn() }
                        } label: {
                            if busy {
                                ProgressView().frame(maxWidth: .infinity, minHeight: 56)
                            } else {
                                Text("Check in as arrived").font(.title3.weight(.semibold)).frame(maxWidth: .infinity, minHeight: 56)
                            }
                        }
                        .buttonStyle(.borderedProminent)
                        .disabled(busy)
                        .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 8, trailing: 16))
                        .accessibilityIdentifier("reservation-check-in")
                    } footer: {
                        if let refusal {
                            Text(refusal).foregroundStyle(Theme.danger)
                        }
                    }
                }
            }
            .listStyle(.insetGrouped)
            .navigationTitle("Reservation")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Close") { dismiss() }.disabled(busy) }
            }
        }
    }

    private func checkIn() async {
        busy = true
        defer { busy = false }
        let count = people < party.partySize ? people : nil
        let who = "\(party.leadName) — \(count.map { "\($0) of \(party.partySize)" } ?? (party.partySize == 1 ? "1 person" : "all \(party.partySize)"))"

        if let token {
            switch await store.checkIn(door, organization: organizationId, token: token, party: party, people: count) {
            case .checkedIn(let updated):
                onCheckedIn(updated, "Checked in: \(who).")
                dismiss()
            case .kept(let updated):
                onCheckedIn(updated, "Checked in on this phone: \(who). It's sent when there's signal.")
                dismiss()
            case .refused(let sentence):
                refusal = sentence
            }
        } else {
            switch await store.arrive(door, organization: organizationId, party: party, people: count) {
            case .done(let updated):
                onCheckedIn(updated, "Checked in: \(who).")
                dismiss()
            case .kept(let updated):
                onCheckedIn(updated, "Checked in on this phone: \(who). It's sent when there's signal.")
                dismiss()
            case .refused(let sentence):
                refusal = sentence
            }
        }
    }
}
