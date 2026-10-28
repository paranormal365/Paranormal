import SwiftUI
import BenKit

/// Under the room bar on a LiDAR phone: scan the room you are in, or look at its scan (LiDAR plan,
/// phase 1).
///
/// Absent on a phone without LiDAR — the live screen does not draw it at all. What it offers
/// depends on the room's state at the place: no scan yet, a scan, somebody else scanning it right
/// now, or no signal to ask.
struct ScanRoomButton: View {
    let flow: ScanFlow
    let room: String?
    let onChooseRoom: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            content
            if let problem = flow.problem {
                Text(problem)
                    .font(.callout)
                    .foregroundStyle(Theme.warning)
                    .accessibilityIdentifier("scan-problem")
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Theme.mist, in: RoundedRectangle(cornerRadius: Theme.Radius.card))
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("scan-card")
    }

    @ViewBuilder
    private var content: some View {
        if flow.placeId == nil {
            header("Scan rooms here", "A room's scan belongs to its place, so say which place this is first.")
            Button("Choose the place") { flow.choosePlace() }
                .buttonStyle(.signalSecondary)
                .accessibilityIdentifier("scan-choose-place-button")
        } else if room == nil {
            header("Scan this room", "Say which room you're in, and it can be scanned for \(flow.placeName ?? "this place").")
            Button("Choose the room", action: onChooseRoom)
                .buttonStyle(.signalSecondary)
        } else if let room {
            roomContent(room)
        }
    }

    @ViewBuilder
    private func roomContent(_ room: String) -> some View {
        let known = flow.currentRoom
        if let current = known?.current {
            header("\(known?.roomName ?? room) is scanned",
                   "Scanned \(EventClock.dayOnly(current.scannedUtc, nil, inMyTime: true)) by \(current.scannedByYou ? "you" : current.scannedBy).")
            HStack(spacing: 10) {
                if known?.canCompare == true {
                    Button("Compare this room") { flow.compare(room: known?.roomName ?? room, against: current) }
                        .buttonStyle(.signalPrimary)
                        .accessibilityIdentifier("compare-room")
                }
                Button("View the scan") { flow.view(current, room: known?.roomName ?? room) }
                    .buttonStyle(.signalSecondary)
                    .accessibilityIdentifier("view-room-scan")
            }
            if known?.canReplace == true {
                Button("Scan it again, replacing this scan") { Task { await flow.scan(room: room) } }
                    .accessibilityIdentifier("replace-room-scan")
            }
            if known?.canReplace == false, let why = known?.replaceReason {
                Text(why).font(.caption).foregroundStyle(Theme.fog)
            }
            if let known, known.offersWaiting > 0 {
                Button(known.offersWaiting == 1 ? "1 new scan offered — decide" : "\(known.offersWaiting) new scans offered — decide") {
                    flow.decidingOffers = known
                }
                .accessibilityIdentifier("scan-offers")
            }
        } else if let hold = known?.holdBy, !hold.isYou {
            header("\(hold.heldBy) is scanning this room",
                   "Right now, until \(EventClock.timeOnly(hold.until, nil, inMyTime: true)). When their scan is in, you can compare against it.")
            Button("Scan this room") {}
                .buttonStyle(.signalSecondary)
                .disabled(true)
                .accessibilityIdentifier("scan-room-held")
        } else {
            header("Scan this room",
                   flow.isOffline
                   ? "Offline — the phone can't tell whether \(room) is already scanned, so this scan may become a comparison when it's sent."
                   : "\(room) has no scan yet. Walk the room slowly, pointing at every wall.")
            Button("Scan this room") { Task { await flow.scan(room: room) } }
                .buttonStyle(.signalPrimary)
                .accessibilityIdentifier("scan-room")
        }
    }

    private func header(_ title: String, _ detail: String) -> some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: "cube.transparent")
                .font(.title3)
                .foregroundStyle(Theme.ecto)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.headline).foregroundStyle(Theme.bone)
                Text(detail).font(.caption).foregroundStyle(Theme.fog)
            }
        }
    }
}
