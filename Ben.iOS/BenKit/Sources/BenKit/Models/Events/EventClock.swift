import Foundation

/// The clock a recorded moment is read on (item 233, Ben 2026-09-11).
///
/// Ben's rule, in his words: *"The dates and times may be recorded in UTC, but should render at
/// either UTC or at the time of the location where the evidence was collected or photo take,
/// etc."*
///
/// The server stores UTC and says, separately, which IANA zone the thing happened in. Rendering
/// with the phone's own zone would tell somebody in Tokyo that a Nashville walk starts at 10am —
/// true of their morning, useless for the walk. So a time is formatted in the **place's** zone,
/// with the zone named beside it, and falls back to UTC (said out loud) when nobody recorded one.
///
/// This is the phone's half of `Ben.Web.Services/EventClock.cs`; the two must agree, because the
/// same night is read on both.
public enum EventClock {
    /// The zone for an IANA id, or UTC. Never throws: an id this phone's database does not know
    /// is a reason to say UTC, not to fail rendering a page.
    public static func zone(_ ianaId: String?) -> TimeZone {
        guard let ianaId, !ianaId.isEmpty, let zone = TimeZone(identifier: ianaId) else {
            return TimeZone(identifier: "UTC") ?? .gmt
        }
        return zone
    }

    /// A short name for the zone as it stands on that date — "CDT" in summer, "CST" in winter.
    public static func label(_ utc: Date, _ ianaId: String?) -> String {
        let tz = zone(ianaId)
        return tz.abbreviation(for: utc) ?? tz.identifier
    }

    /// "09/13/2026 3:08 PM CDT" — the US order Ben uses everywhere on this site.
    public static func dayAndTime(_ utc: Date, _ ianaId: String?) -> String {
        format(utc, ianaId, dateStyle: .short, timeStyle: .short)
    }

    /// "3:08 PM CDT", for a row that already says which day it is.
    public static func timeOnly(_ utc: Date, _ ianaId: String?) -> String {
        format(utc, ianaId, dateStyle: .none, timeStyle: .short)
    }

    private static func format(
        _ utc: Date, _ ianaId: String?, dateStyle: DateFormatter.Style, timeStyle: DateFormatter.Style
    ) -> String {
        let formatter = DateFormatter()
        // en_US_POSIX, so the order is MM/dd/yyyy whatever the phone is set to. The reader's own
        // locale decides how THEY write dates; this is the site's format, the same one the web
        // pages and the guest email use, and a walk that says 09/13 in one place and 13/09 in
        // another is two different answers to one question.
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = zone(ianaId)
        formatter.dateStyle = dateStyle
        formatter.timeStyle = timeStyle
        return "\(formatter.string(from: utc)) \(label(utc, ianaId))"
    }
}
