import Foundation
import Testing
@testable import BenKit

/// What the phone sets for itself once a seat is reserved (item 234).
///
/// The scheduling half needs UNUserNotificationCenter and a real device or simulator, so what is
/// pinned here is everything that decides WHETHER and WHAT — which is where the mistakes are. The
/// two that would matter: a reminder scheduled into the past fires the moment it is set, and a
/// reminder that speaks the phone's clock rather than the walk's tells somebody the wrong hour.
struct SeatReminderTests {

    /// 2026-09-13T20:08:17Z — 3:08 PM in Nashville.
    private static let walkStart = Date(timeIntervalSince1970: 1_789_330_097)

    private static func walk(
        seat: PublicSeat? = PublicSeat(status: .reserved, seats: 2, decidedUtc: nil, acknowledgedUtc: nil),
        address: String? = "1 Printers Alley, Nashville, TN"
    ) -> PublicEventRecord {
        PublicEventRecord(
            id: UUID(), organizationId: UUID(), organizationName: "Printers Alley Walks",
            organizationUrlName: "printers-alley-walks", title: "Saturday walk", description: nil,
            startDateTime: walkStart, endDateTime: walkStart.addingTimeInterval(5400),
            isAllDay: false, meetingUrl: nil,
            location: PublicEventLocation(
                city: "Nashville", state: "TN", approximateLatitude: nil, approximateLongitude: nil,
                exactAddress: address, isExactAddressHidden: false),
            attendingCount: 2, attendeeCapacity: 12, rsvpClosesAt: nil,
            flags: PublicEventFlags(
                canRsvp: false, hasRsvpd: true, isFull: false, rsvpHasClosed: false,
                rsvpBlockedReason: nil),
            tourName: "Printers Alley Ghost Walk", tourUrlName: "printers-alley-ghost-walk",
            guides: nil, tourRating: nil, tourRatingCount: 0, mySeat: seat,
            timeZoneId: "America/Chicago")
    }

    /// The system writes a NARROW NO-BREAK SPACE before the meridiem; every space is flattened
    /// before comparing so a test does not fail against two strings that look identical.
    private static func plain(_ text: String) -> String {
        String(text.map { $0.isWhitespace ? " " : $0 })
    }

    @Test func theNightBeforeCarriesTheMeetingPointAndThePlacesClock() {
        let long = Date(timeIntervalSince1970: 1_789_000_000)   // well before the walk
        let made = SeatReminders.content(for: Self.walk(), lead: .theNightBefore, now: long)

        let reminder = try! #require(made)
        #expect(reminder.title == "Printers Alley Ghost Walk is tomorrow")
        // The walk's clock, not the phone's — the reason EventClock exists.
        #expect(Self.plain(reminder.body).contains("3:08 PM CDT"))
        #expect(reminder.body.contains("1 Printers Alley"))
    }

    @Test func theHourBeforeSaysWhereToHeadFor() {
        let long = Date(timeIntervalSince1970: 1_789_000_000)
        let reminder = try! #require(SeatReminders.content(for: Self.walk(), lead: .anHourBefore, now: long))

        #expect(Self.plain(reminder.title).contains("starts at 3:08 PM CDT"))
        #expect(reminder.body == "Head for 1 Printers Alley, Nashville, TN.")
        #expect(reminder.fireAt == Self.walkStart.addingTimeInterval(-3600))
    }

    @Test func nothingIsScheduledIntoThePast() {
        // A notification set for a moment gone by fires IMMEDIATELY, which would buzz somebody's
        // pocket about a walk they are already standing on.
        let duringTheWalk = Self.walkStart.addingTimeInterval(600)
        #expect(SeatReminders.content(for: Self.walk(), lead: .anHourBefore, now: duringTheWalk) == nil)
        #expect(SeatReminders.content(for: Self.walk(), lead: .theNightBefore, now: duringTheWalk) == nil)
    }

    @Test func aWalkWithNoAddressStillSaysWhen() {
        // The address is withheld on some events by design. A reminder is still worth having.
        let long = Date(timeIntervalSince1970: 1_789_000_000)
        let reminder = try! #require(
            SeatReminders.content(for: Self.walk(address: nil), lead: .anHourBefore, now: long))
        #expect(reminder.body == "Head for Nashville.")
    }

    @Test func remindersAreNamedPerDateSoTheyCanBeTakenBack() {
        // A date that MOVED must not leave a reminder behind pointing at the hour it used to
        // start, which is why the identifier is derived rather than random.
        let id = UUID()
        #expect(SeatReminders.identifier(id, .anHourBefore)
                == "seat-\(id.uuidString.lowercased())-hour-before")
        #expect(SeatReminders.identifier(id, .theNightBefore)
                != SeatReminders.identifier(id, .anHourBefore))
    }

    @Test func anOrdinaryEventFallsBackToItsOwnTitle() {
        var plainEvening = Self.walk()
        plainEvening.tourName = nil
        let long = Date(timeIntervalSince1970: 1_789_000_000)
        let reminder = try! #require(
            SeatReminders.content(for: plainEvening, lead: .theNightBefore, now: long))
        #expect(reminder.title == "Saturday walk is tomorrow")
    }
}
