import SwiftUI
import BenKit

/// How a hosted booking's state is said and coloured — one place, so the list, the event screen and the pass agree.
enum HostedEventWords {
    static func status(_ booking: MyHostedEventBooking) -> (text: String, colour: Color) {
        switch booking.status {
        case .requested: ("Asked for", Theme.warning)
        case .held: ("Held for you", Theme.warning)
        case .confirmed: ("Confirmed", Theme.success)
        case .turnedDown: ("Not this time", Theme.fog)
        case .cancelled: ("Released", Theme.fog)
        case .expired: ("Hold ran out", Theme.fog)
        }
    }

    /// "Fri 10/16 – Sat 10/17", from calendar dates the server stores at midnight.
    static func dates(_ startsOn: Date, _ endsOn: Date) -> String {
        let style = Date.FormatStyle(timeZone: TimeZone(identifier: "UTC")!).weekday(.abbreviated).month(.twoDigits).day(.twoDigits)
        let first = startsOn.formatted(style)
        let last = endsOn.formatted(style)
        return first == last ? first : "\(first) – \(last)"
    }

    /// What the booking is: "Day pass · 2 people", or the nights and rooms.
    static func what(_ booking: MyHostedEventBooking) -> String {
        let people = "\(booking.partySize) \(booking.partySize == 1 ? "person" : "people")"
        if booking.kind == .dayPass && booking.nights.isEmpty { return "Day pass · \(people)" }
        let places = Set(booking.nights.map(\.unitName)).filter { !$0.isEmpty }.sorted()
        return places.isEmpty ? people : "\(people) · \(places.joined(separator: ", "))"
    }
}

/// A small capsule with a word in it.
struct StatusBadge: View {
    let text: String
    let colour: Color

    var body: some View {
        Text(text)
            .font(.caption.weight(.semibold))
            .padding(.horizontal, 8).padding(.vertical, 4)
            .background(colour.opacity(0.2), in: Capsule())
            .foregroundStyle(colour)
    }
}
