import Foundation

// Ports of what a guest reads DURING a hosted event (item 235 phase 14b): the programme, the menus, the
// downloads and the room. Sources: Ben.Service.Models/Entities/HostedEventRecords.cs and
// HostedEventMenuRecords.cs. Raw enum values are the C# ones — append-only on both sides.

// ── the programme ────────────────────────────────────────────────────────────

/// `GET api/public/hosted-events/{id}/programme`. A visitor sees places taken; never who took them.
public struct HostedEventProgramme: Sendable, Codable, Equatable {
    public var hostedEventId: UUID
    /// The venue's clock. Sessions are grouped and shown on it, named beside the time.
    public var timeZoneId: String
    public var nights: [Date]
    public var sessions: [HostedEventSession]
    public var canSignUp: Bool
    /// Why this reader cannot sign up, in the server's words (null for a visitor).
    public var whyNotSignUp: String?
    /// How many of the party one sign-up may cover.
    public var maxPeople: Int
    /// Something changed since this guest last looked.
    public var changedSinceSeen: Bool

    public var timeZone: TimeZone { TimeZone(identifier: timeZoneId) ?? .gmt }
}

public struct HostedEventSession: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var title: String
    public var description: String?
    public var startsAtUtc: Date
    public var endsAtUtc: Date
    public var `where`: String?
    public var ledBy: String?
    /// Null is "no limit".
    public var capacity: Int?
    public var requiresSignUp: Bool
    public var placesTaken: Int
    public var isCancelled: Bool
    public var cancelledReason: String?
    /// Changed since this guest last looked at the programme.
    public var changed: Bool
    public var mine: MySessionPlace?

    /// "Full" means nobody new gets a place — they join the waiting list instead.
    public var isFull: Bool {
        guard let capacity else { return false }
        return placesTaken >= capacity
    }
}

/// This guest's place in one session: signed up, or waiting and where in the queue.
public struct MySessionPlace: Sendable, Codable, Equatable {
    public var signUpId: UUID
    public var people: Int
    public var waiting: Bool
    public var position: Int?
}

// ── downloads ────────────────────────────────────────────────────────────────

public enum EventFileAudience: Int, Sendable, Codable, Equatable {
    case staff = 0
    case attendees = 1
    case `public` = 2
}

/// `GET api/public/hosted-events/{id}/files` — only what reaches this reader.
public struct HostedEventFile: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var uploadFileId: UUID
    public var fileName: String
    public var contentType: String
    public var fileSize: Int64
    public var folder: String?
    public var description: String?
    public var audience: EventFileAudience
    public var sortOrder: Int
    public var dateCreated: Date

    public var displaySize: String {
        ByteCountFormatter.string(fromByteCount: fileSize, countStyle: .file)
    }
}

// ── the room ─────────────────────────────────────────────────────────────────

public enum EventPhotoPosting: Int, Sendable, Codable, Equatable {
    case teamAndGuests = 0
    case teamOnly = 1
}

/// `GET api/public/hosted-events/{id}/room`, and the answer to every write in it.
public struct EventRoom: Sendable, Codable, Equatable {
    public var canPost: Bool
    public var whyNotPost: String?
    public var canModerate: Bool
    /// Who "send to the hosts" reaches — the organizers and, when there is one, the venue.
    public var hostNames: [String]
    /// Newest first, fifty at a time.
    public var messages: [EventRoomMessage]
    /// What the last write did, in words ("Posted, and your photo was sent to …").
    public var note: String?
    public var photoPosting: EventPhotoPosting
    public var canAddPhotos: Bool
    public var canSeeWall: Bool
    /// This guest has not yet agreed to their photos being shown at this event.
    public var needsPhotoConsent: Bool
    public var photoNotice: String?
}

public struct EventRoomMessage: Sendable, Codable, Equatable, Identifiable {
    public var id: UUID
    public var authorId: UUID
    public var authorName: String
    public var body: String
    public var postedUtc: Date
    public var hasMedia: Bool
    public var mediaContentType: String?
    /// Held by the screener: seen by its author and the event's moderators only.
    public var mediaWaiting: Bool
    public var isMine: Bool
    public var isHidden: Bool
    public var sentToHosts: Bool
    public var reports: Int

    public var isVideo: Bool { mediaContentType?.hasPrefix("video/") == true }
}
