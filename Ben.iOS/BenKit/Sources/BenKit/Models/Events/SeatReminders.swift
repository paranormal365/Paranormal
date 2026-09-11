import Foundation
#if canImport(UserNotifications)
import UserNotifications
#endif

/// Reminders the phone sets for itself once a seat is reserved (item 234, Ben 2026-09-10).
///
/// Ben asked that somebody with a place "can get notified where the tour is, when to get there and
/// other stuff that would be available in the e-mail like a link to add to the calendar and a link
/// to get directions to the start point."
///
/// **These are LOCAL notifications, not push.** Ben chose that on 2026-09-11, and it is the honest
/// first answer: nothing here needs an Apple push key, a device-token table or a sender on the
/// server, and a reminder set this way still arrives with the phone in a cellar with no signal —
/// which is where a ghost walk tends to be. Real push is the separate piece, and the one thing it
/// is genuinely needed for is an approval landing while the app is closed.
///
/// **Only a RESERVED seat is reminded about.** A request nobody has approved is not a walk anybody
/// is going on, and a phone that buzzed the night before one would be telling its owner something
/// untrue.
///
/// Nothing here asks for permission on its own. Permission is asked at the moment somebody has a
/// seat to be reminded about, which is the only moment the request makes sense — see
/// `requestPermissionIfNeeded`.
public enum SeatReminders {

    /// How far ahead each reminder lands.
    ///
    /// The night before is for packing a coat and telling somebody where you are going; the hour
    /// before is for leaving the house. Both carry the meeting point, because the meeting point is
    /// the thing somebody actually needs and it is the thing they will not have to hand.
    public enum Lead: Sendable, CaseIterable {
        case theNightBefore
        case anHourBefore

        /// Seconds before the walk starts.
        var secondsBefore: TimeInterval {
            switch self {
            case .theNightBefore: 15 * 60 * 60   // 5pm the previous day for an 8pm walk
            case .anHourBefore:   60 * 60
            }
        }

        var suffix: String {
            switch self {
            case .theNightBefore: "night-before"
            case .anHourBefore:   "hour-before"
            }
        }
    }

    /// The id every reminder for one date carries, so they can all be taken back together.
    static func identifier(_ eventId: UUID, _ lead: Lead) -> String {
        "seat-\(eventId.uuidString.lowercased())-\(lead.suffix)"
    }

    /// What one reminder says.
    ///
    /// Returns nil when the moment has already passed — a notification scheduled into the past
    /// fires immediately, which would buzz somebody's pocket for a walk they are already on.
    static func content(
        for event: PublicEventRecord, lead: Lead, now: Date = Date()
    ) -> (fireAt: Date, title: String, body: String)? {
        let fireAt = event.startDateTime.addingTimeInterval(-lead.secondsBefore)
        guard fireAt > now else { return nil }

        let walk = event.tourName ?? event.title
        // The clock of the PLACE, never the phone's — the same rule the pages follow. A reminder
        // that says 8pm to somebody in another zone is worse than no reminder.
        let when = EventClock.timeOnly(event.startDateTime, event.timeZoneId)
        let day  = EventClock.dayAndTime(event.startDateTime, event.timeZoneId)

        let meetingPoint = event.location.exactAddress ?? event.location.city

        switch lead {
        case .theNightBefore:
            return (fireAt,
                    "\(walk) is tomorrow",
                    meetingPoint.map { "\(day). You're meeting at \($0)." }
                        ?? "\(day).")
        case .anHourBefore:
            return (fireAt,
                    "\(walk) starts at \(when)",
                    meetingPoint.map { "Head for \($0)." }
                        ?? "Time to set off.")
        }
    }
}

#if canImport(UserNotifications)
extension SeatReminders {

    /// Asks for permission, once, at the moment there is something to be reminded about.
    ///
    /// Asked here rather than at launch on purpose: a permission sheet in front of somebody who
    /// has not booked anything is a sheet they decline, and iOS only ever asks once.
    @discardableResult
    public static func requestPermissionIfNeeded(
        _ center: UNUserNotificationCenter = .current()
    ) async -> Bool {
        let settings = await center.notificationSettings()
        switch settings.authorizationStatus {
        case .authorized, .provisional, .ephemeral:
            return true
        case .denied:
            // Never re-ask. iOS would not show it, and the app must go on working without it —
            // the seat is reserved whether or not the phone is allowed to mention it.
            return false
        default:
            return (try? await center.requestAuthorization(options: [.alert, .sound])) ?? false
        }
    }

    /// Sets the reminders for a reserved seat, replacing whatever was set for that date before.
    ///
    /// Idempotent: the old ones are always removed first, so a date that MOVED does not leave a
    /// reminder behind pointing at the hour it used to start.
    public static func schedule(
        for event: PublicEventRecord, center: UNUserNotificationCenter = .current(), now: Date = Date()
    ) async {
        cancel(for: event.id, center: center)

        guard event.mySeat?.status == .reserved else { return }
        guard await requestPermissionIfNeeded(center) else { return }

        for lead in Lead.allCases {
            guard let (fireAt, title, body) = content(for: event, lead: lead, now: now) else { continue }

            let content = UNMutableNotificationContent()
            content.title = title
            content.body = body
            content.sound = .default
            // So a tap can open the walk rather than just the app.
            content.userInfo = ["eventId": event.id.uuidString.lowercased()]

            let parts = Calendar.current.dateComponents(
                [.year, .month, .day, .hour, .minute], from: fireAt)
            let request = UNNotificationRequest(
                identifier: identifier(event.id, lead),
                content: content,
                trigger: UNCalendarNotificationTrigger(dateMatching: parts, repeats: false))

            try? await center.add(request)
        }
    }

    /// Takes back every reminder for a date — a seat turned down, or a guest who cancelled.
    public static func cancel(for eventId: UUID, center: UNUserNotificationCenter = .current()) {
        center.removePendingNotificationRequests(
            withIdentifiers: Lead.allCases.map { identifier(eventId, $0) })
    }
}
#endif
