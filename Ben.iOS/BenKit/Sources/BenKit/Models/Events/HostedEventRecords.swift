import Foundation

// Ports of the guest's side of item 235's records (Ben.Service.Models/Entities/HostedEvent*Records.cs):
// a booking at a hosted event, its pass, and the event itself as a visitor reads it.

/// Where a booking at a hosted event stands. Raw values are the C# enum's — append-only on both sides.
public enum HostedEventBookingStatus: Int, Sendable, Codable, Equatable {
    /// Asked for in words; holds nothing yet.
    case requested = 0
    /// The venue said yes. The places are theirs, and there may be a pass.
    case confirmed = 1
    case turnedDown = 2
    /// Released — by the venue, or at the guest's request.
    case cancelled = 3
    /// Picked on the plan and held until the venue answers, or the hold runs out.
    case held = 4
    /// The hold ran out before the venue answered.
    case expired = 5

    /// Waiting on the venue, or holding places — a booking that is still going somewhere.
    public var isLive: Bool { self == .requested || self == .held || self == .confirmed }
}

public enum HostedEventBookingKind: Int, Sendable, Codable, Equatable {
    case overnight = 0
    case dayPass = 1
}

/// One night of a booking, and the room or seat on it.
public struct HostedEventBookingNight: Sendable, Codable, Equatable, Hashable {
    public var hostedEventNightId: UUID
    public var date: Date
    public var hostedEventLayoutUnitId: UUID?
    public var unitName: String
}

public struct HostedEventBookingGuest: Sendable, Codable, Equatable, Hashable {
    public var id: UUID
    public var displayName: String
    public var appUserId: UUID?
    public var dietaryNotes: String?
    public var sortOrder: Int
}

/// A session of the programme this guest has a place in, or is waiting for (phase 12).
public struct MySessionLine: Sendable, Codable, Equatable, Hashable {
    public var sessionId: UUID
    public var title: String
    public var startsAtUtc: Date
    public var endsAtUtc: Date
    public var `where`: String?
    public var waiting: Bool
    public var calledOff: Bool
}

/// A guest's own booking (`GET api/public/hosted-events/mine`, `…/{id}/my-booking`).
public struct MyHostedEventBooking: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var hostedEventId: UUID
    public var eventName: String
    public var eventUrlName: String?
    public var organizationName: String?
    public var organizationUrlName: String?
    public var venueName: String?
    /// Calendar dates, midnight, as the server stores them.
    public var startsOn: Date
    public var endsOn: Date
    public var partySize: Int
    public var kind: HostedEventBookingKind
    public var status: HostedEventBookingStatus
    public var decisionNote: String?
    public var guestAcknowledgedUtc: Date?
    public var cancellationRequestedUtc: Date?
    public var note: String?
    public var nights: [HostedEventBookingNight]
    public var guests: [HostedEventBookingGuest]
    public var holdExpiresUtc: Date?
    /// Filled on the list only.
    public var sessions: [MySessionLine]?
    public var mayReview: Bool?
    public var hasReviewed: Bool?
}

/// The ticket. The token is what the QR code carries; `imageUrl` draws the same code on the server.
public struct HostedEventPass: Sendable, Codable, Equatable {
    public var id: UUID
    public var hostedEventBookingId: UUID
    public var token: String
    public var imageUrl: String
    public var issuedUtc: Date
    public var revokedUtc: Date?
    public var revokedReason: String?
    public var emailedUtc: Date?
    public var checkedInUtc: Date?
    public var checkedInByName: String?
    public var replacedAnEarlierOne: Bool

    public var isRevoked: Bool { revokedUtc != nil }

    /// What a door can read out when the code won't scan: the token's last six characters, as the website shows.
    public var shortCode: String {
        token.count <= 6 ? token.uppercased() : String(token.suffix(6)).uppercased()
    }
}

/// The colour to collect at the desk, when the venue uses bands.
public struct HostedEventBand: Sendable, Codable, Equatable {
    public var id: UUID
    public var colour: String
    public var meaning: String
    public var hex: String?
    public var sortOrder: Int
}

/// A guest's pass and what it admits (`GET api/public/hosted-events/{id}/my-booking/pass`).
public struct MyHostedEventPass: Sendable, Codable, Equatable {
    public var pass: HostedEventPass
    public var eventName: String
    public var venueName: String?
    public var leadName: String
    public var partySize: Int
    public var kind: HostedEventBookingKind
    public var nights: [HostedEventBookingNight]
    public var band: HostedEventBand?
    /// "Sat 10/31 · Dinner · Table 4" lines, when the venue has seated them (phase 13).
    public var seating: [String]?
}

public enum HostedEventBookingMode: Int, Sendable, Codable, Equatable {
    /// Guests ask in words and the venue places them.
    case ask = 0
    /// Guests pick their own places on the plan.
    case pick = 1
}

/// A hosted event as a visitor reads it (`GET api/public/hosted-events/{id}`). Only what the phone uses.
public struct PublicHostedEvent: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var umbrellaEventId: UUID
    public var organizationName: String
    public var organizationUrlName: String
    public var name: String
    public var urlName: String
    public var tagline: String?
    public var timeZoneId: String
    public var startsOn: Date
    public var endsOn: Date
    public var dateNoun: String
    public var venueName: String?
    public var city: String?
    public var state: String?
    public var exactAddress: String?
    public var contactLine: String?
    /// "Stairs only to the ballroom", "hearing loop in the bar" — what to know about getting in and around (phase 17a).
    public var accessNotes: String?
    public var isCancelled: Bool
    public var bookingMode: HostedEventBookingMode
    public var isTakingBookings: Bool
    public var notTakingBookingsSentence: String?
    public var dayPassPrice: Decimal?

    /// The public page, where places are asked for or picked — the phone sends people there rather than
    /// rebuilding the seat picker.
    public var pagePath: String { "o/\(organizationUrlName)/events/\(urlName)" }
}
