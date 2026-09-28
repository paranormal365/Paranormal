import SwiftUI
import BenKit

/// A case, investigation, event or tour time, on the place's clock or the reader's (Ben, 2026-09-28:
/// "You can see the event and tour time in the event or tour timezone or the user's timezone").
///
/// Always names its zone ("8:00 PM EDT"), so nobody has to guess whose eight o'clock it is. Which
/// clock follows `TimeZoneSwitch`, remembered on this device — every PlaceTime on screen redraws
/// when it flips, because each one watches the same stored setting. "My time" is where the phone
/// is (the default), and redraws when the phone crosses into another zone.
struct PlaceTime: View {
    enum Shape { case dayAndTime, time, day }

    let utc: Date
    let zoneId: String?
    var shape: Shape = .dayAndTime

    @AppStorage(TimeView.storageKey) private var inMyTime = TimeView.defaultInMyTime

    @State private var zoneChanges = 0

    var body: some View {
        let _ = zoneChanges
        Text(text)
            .onReceive(NotificationCenter.default.publisher(for: .NSSystemTimeZoneDidChange)) { _ in zoneChanges += 1 }
    }

    private var text: String {
        switch shape {
        case .dayAndTime: EventClock.dayAndTime(utc, zoneId, inMyTime: inMyTime)
        case .time: EventClock.timeOnly(utc, zoneId, inMyTime: inMyTime)
        case .day: EventClock.dayOnly(utc, zoneId, inMyTime: inMyTime)
        }
    }
}

/// "Local time · My time": which clock the times on this screen read on — the place's, or where the
/// phone is. Says so plainly instead
/// when the place's clock and the reader's are the same.
struct TimeZoneSwitch: View {
    /// The place's zone; nil for a list whose rows keep different clocks.
    let zoneId: String?

    @AppStorage(TimeView.storageKey) private var inMyTime = TimeView.defaultInMyTime

    @State private var zoneChanges = 0

    var body: some View {
        let _ = zoneChanges
        content
            .onReceive(NotificationCenter.default.publisher(for: .NSSystemTimeZoneDidChange)) { _ in zoneChanges += 1 }
    }

    @ViewBuilder private var content: some View {
        if let zoneId, EventClock.zone(zoneId).identifier == ReaderClock.zone.identifier {
            Label("Times are \(letters(EventClock.zone(zoneId))), your time too", systemImage: "clock")
                .font(.caption).foregroundStyle(Theme.fog)
                .accessibilityIdentifier("time-zone-same")
        } else {
            Picker("Show times in", selection: $inMyTime) {
                Text(zoneId.map { "Local time (\(letters(EventClock.zone($0))))" } ?? "Local time").tag(false)
                Text("My time (\(letters(ReaderClock.zone)))").tag(true)
            }
            .pickerStyle(.segmented)
            .accessibilityIdentifier("time-zone-switch")
        }
    }

    private func letters(_ zone: TimeZone) -> String { zone.abbreviation(for: Date()) ?? zone.identifier }
}
