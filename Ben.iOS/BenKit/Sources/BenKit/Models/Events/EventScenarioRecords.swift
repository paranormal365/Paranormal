import Foundation

// What Jim's overnight conference needed on the phone (event-scenario-gaps, 10/21/2026): the event's results,
// and a tour date's room.

// ── the results ──────────────────────────────────────────────────────────────

/// `GET api/public/hosted-events/{id}/results`: the evidence the organizers accepted, ranked, and where the
/// results page is. Ben's scenario: "They could see the event results in the app."
public struct HostedEventResults: Sendable, Codable, Equatable {
    public var hostedEventId: UUID
    public var name: String
    public var collectsEvidence: Bool
    /// The organizer's results page, or the event's own page at its evidence. Absolute.
    public var resultsUrl: String
    public var asksForVotes: Bool
    /// "We're accepting evidence for three days, until …", or nil.
    public var evidenceSentence: String?
    /// When the organizer sent "the results are ready"; nil until then.
    public var resultsSentUtc: Date?
    /// Most positive votes first, then the earliest caught.
    public var items: [HostedEventResultItem]

    /// Whether there is anything to show a guest yet.
    public var hasSomething: Bool { !items.isEmpty || resultsSentUtc != nil }
}

/// One accepted piece of evidence, in rank order.
public struct HostedEventResultItem: Sendable, Codable, Equatable, Identifiable {
    public var submissionId: UUID
    public var uploadFileId: UUID
    public var fileName: String
    public var contentType: String
    public var by: String
    public var note: String?
    public var positiveVotes: Int
    public var caughtUtc: Date
    /// Where it plays from. Absolute, and needs no sign-in.
    public var fileUrl: String

    public var id: UUID { submissionId }

    public var isAudio: Bool { contentType.hasPrefix("audio/") }
    public var isVideo: Bool { contentType.hasPrefix("video/") }
    public var isImage: Bool { contentType.hasPrefix("image/") }
}

public enum EventResultsWording {
    /// "3 confirm", "1 confirms", or nil for none.
    public static func votes(_ count: Int) -> String? {
        switch count {
        case 0: return nil
        case 1: return "1 confirms"
        default: return "\(count) confirm"
        }
    }

    /// What kind of evidence a file is, for its row.
    public static func kind(_ item: HostedEventResultItem) -> String {
        if item.isAudio { return "Recording" }
        if item.isVideo { return "Video" }
        if item.isImage { return "Photo" }
        return "File"
    }
}

// ── who came to the classes ──────────────────────────────────────────────────

/// `GET api/organizations/{org}/events/{id}/sessions/attendance`: every class that takes sign-ups, with its
/// roster, for the door (Ben, 10/21/2026: door helpers get an app screen to mark class attendance).
public struct HostedEventClasses: Sendable, Codable, Equatable {
    public var hostedEventId: UUID
    public var eventName: String
    public var timeZoneId: String
    public var classes: [HostedEventClass]

    public var timeZone: TimeZone { TimeZone(identifier: timeZoneId) ?? .current }
}

public struct HostedEventClass: Sendable, Codable, Equatable, Identifiable {
    public var sessionId: UUID
    public var title: String
    public var startsAtUtc: Date
    public var endsAtUtc: Date
    public var `where`: String?
    public var ledBy: String?
    public var capacity: Int?
    public var roster: ClassRoster

    public var id: UUID { sessionId }
}

/// Who has a place in a class, and who is waiting.
public struct ClassRoster: Sendable, Codable, Equatable {
    public var sessionId: UUID
    public var title: String
    public var `in`: [ClassRosterLine]
    public var waiting: [ClassRosterLine]
    /// People marked there, counting each party.
    public var peopleThere: Int
    public var canMarkAttendance: Bool

    /// Everybody with a place, counting each party.
    public var peopleIn: Int { `in`.reduce(0) { $0 + max(1, $1.people) } }
}

public struct ClassRosterLine: Sendable, Codable, Equatable, Identifiable {
    public var signUpId: UUID
    public var name: String
    public var people: Int
    public var signedUpUtc: Date
    public var isHelping: Bool
    /// When they were marked there; nil when not.
    public var attendedUtc: Date?

    public var id: UUID { signUpId }
    public var isHere: Bool { attendedUtc != nil }
}

// ── a tour date's room ───────────────────────────────────────────────────────

/// `GET api/public/tour-dates/{dateId}/room`: the people checked in on a walk and its guides, staying in touch.
/// The server answers nothing (204) to anybody who isn't in it.
public struct TourRoom: Sendable, Codable, Equatable {
    public var dateId: UUID
    public var title: String
    public var canPost: Bool
    public var whyNotPost: String?
    /// "Guide" or "Organizer" when the reader runs the walk; nil for a guest.
    public var myRole: String?
    /// Newest first.
    public var messages: [TourRoomMessage]
    public var note: String?
}

public struct TourRoomMessage: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var authorId: UUID
    public var authorName: String
    public var body: String
    public var postedUtc: Date
    public var isMine: Bool
    /// "Guide" or "Organizer" for the people running the walk; nil for a guest. Shown so their posts stand out.
    public var authorRole: String?
}
