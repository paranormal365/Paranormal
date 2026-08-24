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
}
