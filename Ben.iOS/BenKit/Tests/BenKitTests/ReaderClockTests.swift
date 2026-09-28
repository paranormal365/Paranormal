import Foundation
import Testing
@testable import BenKit

/// Ben, 2026-09-28: "saves in utc displays in current time zone", and "ensure the time displayed in
/// the app is local time for their current location" — the phone's clock, which iOS sets from where
/// the phone is. Serialized: `ReaderClock` is one clock for the whole app.
@Suite(.serialized)
struct ReaderClockTests {
    private static let instant = Date(timeIntervalSince1970: 1_789_330_097)   // 2026-09-13T20:08:17Z

    private static func plain(_ text: String) -> String { String(text.map { $0.isWhitespace ? " " : $0 }) }

    @Test func theAppReadsOnThePhonesOwnClock() {
        // Not a snapshot of the zone at launch: the auto-updating one, so a phone that crosses into
        // another zone reads the new one without restarting the app.
        #expect(ReaderClock.zone == TimeZone.autoupdatingCurrent)
        #expect(Self.instant.readerFormatted(date: .omitted, time: .shortened)
                == Self.instant.formatted(date: .omitted, time: .shortened))
    }

    @Test func whereverThePhoneIsIsWhereTimesRead() {
        // Standing in for a phone that has flown to Los Angeles.
        ReaderClock.pinForTesting("America/Los_Angeles")
        defer { ReaderClock.pinForTesting(nil) }
        #expect(Self.plain(Self.instant.readerFormatted(date: .omitted, time: .shortened)) == "1:08 PM")
        #expect(Self.instant.formatted(ReaderClock.dateTime.hour(.twoDigits(amPM: .omitted))) == "01")
    }

    @Test func theSwitchStartsOnThePhonesClock() {
        let defaults = UserDefaults.standard
        let kept = defaults.object(forKey: TimeView.storageKey)
        defer { defaults.set(kept, forKey: TimeView.storageKey) }
        defaults.removeObject(forKey: TimeView.storageKey)
        #expect(TimeView.inMyTime)
        TimeView.inMyTime = false
        #expect(!TimeView.inMyTime)
    }

    @Test func aSessionFileStillSavesUTC() throws {
        // The .ben side of the rule: wherever the phone is, what is written is UTC.
        ReaderClock.pinForTesting("Asia/Tokyo")
        defer { ReaderClock.pinForTesting(nil) }
        let written = Self.instant.formatted(DeviceDataJSON.iso8601)
        #expect(written.hasSuffix("Z"))
        #expect(written.hasPrefix("2026-09-13T20:08:17"))
    }

    // Here rather than in EventClockTests: the suites run in parallel, and ReaderClock is one clock.
    @Test func myTimeReadsTheSameInstantWhereThePhoneIs() {
        // "Local time · My time": the walk's 3:08 PM CDT, for somebody whose phone is in Tokyo.
        ReaderClock.pinForTesting("Asia/Tokyo")
        defer { ReaderClock.pinForTesting(nil) }
        #expect(Self.plain(EventClock.dayAndTime(Self.instant, "America/Chicago", inMyTime: true)) == "9/14/26, 5:08 AM GMT+9")
        #expect(Self.plain(EventClock.timeOnly(Self.instant, "America/Chicago", inMyTime: false)) == "3:08 PM CDT")
    }
}
