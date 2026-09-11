import Foundation

// Ports of Ben.Service.Models/Entities/PublicEventRecords.cs — a visitor's view
// of an organization's public events.

/// Where an event is, as far as this reader is entitled to know. When the exact
/// address is withheld, absence is structural: the payload has no slot for it.
public struct PublicEventLocation: Sendable, Codable, Equatable {
    public var city: String?
    public var state: String?
    public var approximateLatitude: Decimal?
    public var approximateLongitude: Decimal?
    /// Set only for a reader entitled to it: the organizer, or somebody attending.
    public var exactAddress: String?
    /// True when an exact address exists but is being withheld, so the UI can say so.
    public var isExactAddressHidden: Bool
}

/// What a visitor may do about this event right now, decided SERVER-side.
/// Render RSVP buttons from these flags only — no client-side date math.
public struct PublicEventFlags: Sendable, Codable, Equatable {
    public var canRsvp: Bool
    public var hasRsvpd: Bool
    public var isFull: Bool
    public var rsvpHasClosed: Bool
    /// Why they cannot come, written to be shown to a person.
    public var rsvpBlockedReason: String?
}

/// Somebody leading a tour date, as a visitor may know them (item 233).
///
/// The photo is OPTIONAL and deliberately so — Ben, 2026-09-10: "The photo of tour guide is
/// optional … if it is led by more than one person, it would not be the same picture for each
/// tour." A guide with no published photo has no id here, and the row shows their name alone.
public struct PublicGuide: Sendable, Codable, Equatable, Hashable {
    public var displayName: String
    public var handle: String?
    /// The file to ask `api/public/users/photos/{id}` for. Nil when they have published none.
    public var photoUploadFileId: UUID?
}

/// One public event (`GET api/public/events/{eventId}`).
public struct PublicEventRecord: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var organizationId: UUID
    public var organizationName: String
    public var organizationUrlName: String
    public var title: String
    public var description: String?
    public var startDateTime: Date
    public var endDateTime: Date
    public var isAllDay: Bool
    public var meetingUrl: String?
    public var location: PublicEventLocation
    public var attendingCount: Int
    public var attendeeCapacity: Int?
    public var rsvpClosesAt: Date?
    public var flags: PublicEventFlags
    // ── The tour this night belongs to (item 233) ───────────────────────────
    // Nil on an ordinary event, which is most of them: a group's open evening
    // belongs to nobody's walking tour.
    public var tourName: String?
    public var tourUrlName: String?
    /// Who is leading THIS date, which need not be the tour's usual guides.
    public var guides: [PublicGuide]?
    /// The average a guest gave the tour, to one decimal, with the count behind it.
    public var tourRating: Decimal?
    public var tourRatingCount: Int
    /// The IANA zone the night happens in. Nil means nobody said, and the time is UTC.
    public var timeZoneId: String?
}

/// One public event as it appears in a list (`GET api/public/events`).
public struct PublicEventListItem: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    /// The readable slug this event is reached by; without it a card has nowhere to link.
    public var urlName: String?
    public var organizationId: UUID
    public var organizationName: String
    public var organizationUrlName: String
    public var title: String
    public var startDateTime: Date
    public var endDateTime: Date
    public var isAllDay: Bool
    public var city: String?
    public var state: String?
    public var approximateLatitude: Decimal?
    public var approximateLongitude: Decimal?
    public var attendingCount: Int
    public var attendeeCapacity: Int?
    public var isOnline: Bool
    // ── The tour this night belongs to (item 233) ───────────────────────────
    // The LIST carries the tour's name but not its guides: a guide is a person, and a list of
    // twenty nights is not the place to name sixty of them. The detail record has them.
    public var tourName: String?
    public var tourUrlName: String?
    /// The IANA zone the night happens in. Nil means nobody said, and the time is UTC.
    public var timeZoneId: String?
}

extension PublicEventListItem {
    public var placeLabel: String? {
        let parts = [city, state].compactMap { $0?.isEmpty == false ? $0 : nil }
        return parts.isEmpty ? nil : parts.joined(separator: ", ")
    }

    public var hasCoordinates: Bool { approximateLatitude != nil && approximateLongitude != nil }

    /// Coordinates as a map wants them. APPROXIMATE by design — the server snaps a public
    /// event's position to a grid so a listing cannot become an address lookup. Never label
    /// a pin drawn from these as the venue.
    public var mapCoordinate: (latitude: Double, longitude: Double)? {
        guard let lat = approximateLatitude, let lon = approximateLongitude else { return nil }
        return (Double(truncating: lat as NSNumber), Double(truncating: lon as NSNumber))
    }

    /// A null capacity is UNLIMITED, not a cap of zero — treating it as zero would show
    /// every uncapped event as full.
    public var isFull: Bool {
        guard let capacity = attendeeCapacity else { return false }
        return attendingCount >= capacity
    }

    /// Never negative: a cap lowered after sign-ups leaves an overbooked event at zero left.
    public var spacesLeft: Int? {
        attendeeCapacity.map { max(0, $0 - attendingCount) }
    }
}
