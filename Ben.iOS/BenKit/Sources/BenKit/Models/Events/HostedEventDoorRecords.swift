import Foundation

// Ports of the door's records (Ben.Service.Models/Entities/HostedEventPassRecords.cs, item 235 phases 7 and 14c):
// the events a person may run the door at, one night's door, and what a scan answers.

/// An event this person may run the door at (`GET api/me/hosted-event-duties`).
public struct MyHostedEventDuty: Sendable, Codable, Equatable, Identifiable {
    public var hostedEventId: UUID
    /// The organizing group; the door's routes live under it.
    public var organizationId: UUID
    public var eventName: String
    public var organizationName: String
    public var venueName: String?
    public var startsOn: Date
    public var endsOn: Date
    public var timeZoneId: String
    /// What the organizers called their part ("Front door"), when they were added as a helper.
    public var roleLabel: String?
    /// One of the event's nights is today at the venue.
    public var tonight: Bool

    public var id: UUID { hostedEventId }
}

public struct HostedEventNightInfo: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var date: Date
    public var label: String
    public var title: String?
    public var sortOrder: Int
}

/// The colour a party wears, and how the venue decides it.
public struct HostedEventDoorBand: Sendable, Codable, Equatable {
    public var id: UUID
    public var colour: String
    public var meaning: String
    public var hex: String?
    public var sortOrder: Int
}

/// One party expected at the door tonight. Names and dietary FLAGS only — never an address or who has the allergy.
public struct HostedEventDoorParty: Sendable, Codable, Equatable, Identifiable {
    public var hostedEventBookingId: UUID
    public var leadName: String
    public var partySize: Int
    public var kind: HostedEventBookingKind
    public var `where`: String?
    /// The last six characters of the live pass, upper-cased — what a door types when the camera gives up, and
    /// what the phone matches a scanned code against when it has no signal.
    public var code: String?
    public var arrivedUtc: Date?
    public var leftUtc: Date?
    /// How many actually came in, when it was not the whole party.
    public var peopleIn: Int?
    public var dietary: [String]
    public var band: HostedEventDoorBand?

    public var id: UUID { hostedEventBookingId }
    public var isIn: Bool { arrivedUtc != nil && leftUtc == nil }
}

public struct HostedEventWalkUp: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var people: Int
    public var name: String?
    public var note: String?
    public var arrivedUtc: Date
}

/// One night's door (`GET api/organizations/{org}/events/{event}/door?night=`), and the answer to every move on it.
public struct HostedEventDoor: Sendable, Codable, Equatable {
    public var hostedEventId: UUID
    public var eventName: String
    public var nightId: UUID
    public var nightDate: Date
    public var nights: [HostedEventNightInfo]
    public var expected: [HostedEventDoorParty]
    public var peopleExpected: Int
    public var peopleIn: Int
    public var walkUps: [HostedEventWalkUp]
    /// Null when the event has no ceiling — the honest answer, not a large number.
    public var placesLeft: Int?
    public var placesLeftSentence: String?

    /// The party whose pass ends in these characters. A pass code is six characters of the token, upper-cased.
    public func party(forScanned token: String) -> HostedEventDoorParty? {
        let trimmed = token.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmed.count >= 6 else { return party(forCode: trimmed) }
        return party(forCode: String(trimmed.suffix(6)))
    }

    /// The party with this typed pass code, ignoring case and spaces.
    public func party(forCode code: String) -> HostedEventDoorParty? {
        let wanted = code.replacingOccurrences(of: " ", with: "").uppercased()
        guard wanted.count == 6 else { return nil }
        return expected.first { $0.code?.uppercased() == wanted }
    }
}

/// What a scan answers. A refusal is a sentence somebody can say aloud to the person in front of them.
public struct HostedEventScanResult: Sendable, Codable, Equatable {
    public var admitted: Bool
    public var refusal: String?
    public var hostedEventBookingId: UUID?
    public var leadName: String?
    public var partySize: Int?
    public var kind: HostedEventBookingKind?
    public var nights: [HostedEventBookingNight]?
    public var guestNames: [String]?
    /// They were already in — the first arrival is kept, and the door decides.
    public var alreadyCheckedInUtc: Date?
}
