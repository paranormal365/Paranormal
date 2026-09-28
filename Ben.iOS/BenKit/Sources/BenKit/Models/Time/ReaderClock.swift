import Foundation

/// The clock the app shows times on: **where the phone is** (Ben, 2026-09-28: "ensure the time
/// displayed in the app is local time for their current location").
///
/// That is the phone's own time zone, which iOS sets from the phone's location (Settings → General →
/// Date & Time → Set Automatically, on by default), so it follows the person as they travel. A zone
/// saved on the account is the WEBSITE's clock and does not move this one: somebody who chose
/// Central on the site and flies to Denver reads the app on Denver time.
///
/// **Passed, not set globally.** Foundation no longer lets an app change the zone `Date.formatted()`
/// uses — `NSTimeZone.default` is ignored by the modern formatters (checked 2026-09-28: set to Tokyo,
/// a Chicago phone still formatted Chicago). So every time a person reads goes through
/// `readerFormatted` / `dateTime`, which name the zone explicitly — and which a test can pin.
public enum ReaderClock {
    private static let lock = NSLock()
    nonisolated(unsafe) private static var pinned: TimeZone?

    /// The zone to show times in: the phone's, following it as it travels.
    public static var zone: TimeZone {
        lock.lock(); defer { lock.unlock() }
        return pinned ?? .autoupdatingCurrent
    }

    /// `.dateTime` on the reader's clock, for `Text(date, format: ReaderClock.dateTime.hour().minute())`.
    public static var dateTime: Date.FormatStyle { Date.FormatStyle(timeZone: zone) }

    /// Tests only: reads as though the phone were in `ianaId` (nil goes back to the phone). Nothing
    /// in the app calls this — the app's clock is the phone's.
    static func pinForTesting(_ ianaId: String?) {
        let zone = ianaId.flatMap { TimeZone(identifier: $0) }
        lock.lock(); pinned = zone; lock.unlock()
    }
}

public extension Date {
    /// `formatted(date:time:)` on the reader's clock rather than whatever the device happens to be on.
    func readerFormatted(date: Date.FormatStyle.DateStyle, time: Date.FormatStyle.TimeStyle) -> String {
        formatted(Date.FormatStyle(date: date, time: time, timeZone: ReaderClock.zone))
    }
}

/// Which clock a case, investigation, event or tour is read on: the reader's ("My time" — where the
/// phone is, the default) or the place's ("Local time"). Remembered on this device.
public enum TimeView {
    /// The `UserDefaults` key; views observe it with `@AppStorage(TimeView.storageKey)`.
    public static let storageKey = "time.inMyTime"

    /// What the switch starts on before anybody touches it: the phone's clock.
    public static let defaultInMyTime = true

    public static var inMyTime: Bool {
        get { UserDefaults.standard.object(forKey: storageKey) as? Bool ?? defaultInMyTime }
        set { UserDefaults.standard.set(newValue, forKey: storageKey) }
    }
}
