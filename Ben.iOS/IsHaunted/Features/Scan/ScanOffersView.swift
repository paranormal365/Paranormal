import SwiftUI
import BenKit

/// The scans other people offered for a room, for whoever looks after the place's scans to accept
/// or decline (LiDAR plan, phase 1).
///
/// The server tells the custodian when an offer arrives; this is where they answer it. Accepting
/// makes the offer the room's scan and keeps the old one as a previous version; declining tells
/// the scanner, with the note.
struct ScanOffersView: View {
    let flow: ScanFlow
    let room: PlaceScanRoom

    @Environment(\.dismiss) private var dismiss
    @State private var offers: [PlaceScanSummary] = []
    @State private var loading = true
    @State private var note = ""
    @State private var problem: String?
    @State private var deciding = false

    var body: some View {
        NavigationStack {
            SignalForm {
                if loading {
                    ProgressView()
                } else if offers.isEmpty {
                    Text("Nothing is waiting for an answer.").foregroundStyle(Theme.fog)
                } else {
                    ForEach(offers) { offer in
                        Section {
                            Text("Offered by \(offer.scannedBy), scanned \(EventClock.dayOnly(offer.scannedUtc, nil, inMyTime: true)).")
                                .foregroundStyle(Theme.bone)
                            Text("\(ScanRetentionSheet.size(offer.fileSize)) · \(offer.keyframeCount) pictures")
                                .font(.caption)
                                .foregroundStyle(Theme.fog)
                            Button("Look at it") { flow.view(offer, room: room.roomName); dismiss() }
                            Button("Accept — it becomes the room's scan") { Task { await decide(offer, accept: true) } }
                                .disabled(deciding)
                                .accessibilityIdentifier("scan-offer-accept")
                            Button("Decline", role: .destructive) { Task { await decide(offer, accept: false) } }
                                .disabled(deciding)
                                .accessibilityIdentifier("scan-offer-decline")
                        } header: {
                            Text("Version \(offer.version)")
                        }
                    }
                    Section {
                        TextField("A note for the scanner (optional)", text: $note, axis: .vertical)
                    } footer: {
                        Text("Whoever made the scan reads this either way.")
                    }
                }
                if let problem {
                    Section { Text(problem).foregroundStyle(Theme.warning) }
                }
            }
            .navigationTitle("Offered for \(room.roomName)")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Close") { dismiss() } }
            }
            .task { await load() }
        }
    }

    private func load() async {
        loading = true
        defer { loading = false }
        switch await flow.versions(of: room) {
        case .ok(let versions): offers = versions.filter { $0.state == .offered }
        case .failed(let reason, _): problem = reason ?? "The offers couldn't be fetched. Check the signal and try again."
        case .sessionEnded: problem = "Your session ended — sign in again to answer."
        case .rateLimited: problem = "Too many requests — try again in a moment."
        }
    }

    private func decide(_ offer: PlaceScanSummary, accept: Bool) async {
        deciding = true
        defer { deciding = false }
        let trimmed = note.trimmingCharacters(in: .whitespacesAndNewlines)
        if let refusal = await flow.decide(offer.id, accept: accept, note: trimmed.isEmpty ? nil : trimmed) {
            problem = refusal
        } else {
            note = ""
            await load()
            if offers.isEmpty { dismiss() }
        }
    }
}
