import Foundation
import Testing
@testable import BenKit

/// The clock a night is read on (item 233).
///
/// The property worth pinning is the one that was wrong before the rule existed: the same instant
/// must read the same way on every phone. A test that used the device's zone would pass in
/// Nashville and fail in Tokyo, which is the bug rather than the check — so the zone is named.
struct EventClockTests {

    /// 2026-09-13T20:08:17Z — the instant the seeded Saturday walk starts.
    private static let walkStart = Date(timeIntervalSince1970: 1_789_330_097)

    /// The system writes "3:08 PM" with a NARROW NO-BREAK SPACE before the meridiem, which is
    /// right on screen and invisible in a test that would otherwise fail against two strings that
    /// look identical. Every kind of space becomes an ordinary one before comparing.
    private static func plain(_ text: String) -> String {
        String(text.map { $0.isWhitespace ? " " : $0 })
    }

    @Test func aWalkReadsOnTheClockOfThePlaceItHappens() {
        #expect(Self.plain(EventClock.dayAndTime(Self.walkStart, "America/Chicago")) == "9/13/26, 3:08 PM CDT")
    }

    @Test func thePhonesOwnZoneNeverEntersIntoIt() {
        // The same instant, asked for in three zones. One answer per PLACE, and the place is the
        // walk's, not the reader's.
        #expect(Self.plain(EventClock.dayAndTime(Self.walkStart, "America/Chicago")) == "9/13/26, 3:08 PM CDT")
        #expect(Self.plain(EventClock.label(Self.walkStart, "Asia/Tokyo")) == "GMT+9")
        #expect(Self.plain(EventClock.label(Self.walkStart, "Europe/London")) == "GMT+1")
    }

    @Test func nobodySaidMeansUtcAndSaysSo() {
        // Most events carry no zone, and a silent guess at the reader's own is how a night ends up
        // advertised at the wrong hour. UTC, named.
        #expect(Self.plain(EventClock.label(Self.walkStart, nil)) == "GMT")
        #expect(Self.plain(EventClock.dayAndTime(Self.walkStart, nil)) == "9/13/26, 8:08 PM GMT")
        #expect(Self.plain(EventClock.dayAndTime(Self.walkStart, "")) == "9/13/26, 8:08 PM GMT")
    }

    @Test func anUnknownZoneFallsBackRatherThanFailing() {
        // An id this phone's database does not know is a reason to say UTC, not to leave a row
        // blank. The server is free to record a zone a two-year-old iOS release has never heard of.
        // Foundation normalises the UTC zone's identifier to "GMT"; what matters is the offset.
        #expect(EventClock.zone("Mars/Olympus_Mons").secondsFromGMT() == 0)
    }

    @Test func theAbbreviationFollowsTheSeason() {
        // Central Daylight in September, Central Standard in January — the walk's own clock
        // changes under it, and a fixed "CST" would be wrong for half the year.
        let january = Date(timeIntervalSince1970: 1_768_000_000)   // 2026-01-09T23:06:40Z
        #expect(Self.plain(EventClock.label(Self.walkStart, "America/Chicago")) == "CDT")
        #expect(Self.plain(EventClock.label(january, "America/Chicago")) == "CST")
    }
}
