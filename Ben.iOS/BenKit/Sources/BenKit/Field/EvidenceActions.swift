import Foundation

/// The operator's verdict on a submission — their decision about their own event's gallery.
public enum EvidenceStatus: Int, Codable, Sendable {
    case pending = 0
    case accepted = 1
    case rejected = 2

    /// Said from the submitter's side, because that is who reads it here.
    public var summary: String {
        switch self {
        case .pending:  "Waiting"
        case .accepted: "Shown on their event"
        case .rejected: "Not used"
        }
    }
}

/// One thing this account offered at somebody's public event.
///
/// **Two independent decisions live on this row, and conflating them is the mistake to avoid.**
/// `status` is the operator deciding what their EVENT shows. `publishedToPlaceAtUtc` is the
/// photographer deciding what the PLACE's public record shows, under their own name. A picture
/// declined for the gallery is still theirs to contribute; one accepted is not thereby published.
public struct EvidenceSubmission: Codable, Sendable, Equatable, Identifiable {
    public let id: UUID
    public let orgCalendarEventId: UUID
    public let eventTitle: String
    public let fileName: String
    public let contentType: String
    public let note: String?
    public let status: EvidenceStatus
    public let rejectionReason: String?
    public let dateCreated: Date
    public let publishedToPlaceAtUtc: Date?
    /// Whether the event is at a public place, so there is an archive to contribute to at all.
    public let placeAcceptsArchive: Bool

    public init(id: UUID, orgCalendarEventId: UUID, eventTitle: String, fileName: String,
                contentType: String, note: String?, status: EvidenceStatus,
                rejectionReason: String?, dateCreated: Date,
                publishedToPlaceAtUtc: Date?, placeAcceptsArchive: Bool) {
        self.id = id
        self.orgCalendarEventId = orgCalendarEventId
        self.eventTitle = eventTitle
        self.fileName = fileName
        self.contentType = contentType
        self.note = note
        self.status = status
        self.rejectionReason = rejectionReason
        self.dateCreated = dateCreated
        self.publishedToPlaceAtUtc = publishedToPlaceAtUtc
        self.placeAcceptsArchive = placeAcceptsArchive
    }

    public var isInArchive: Bool { publishedToPlaceAtUtc != nil }

    /// What the archive column says, including the reason there is nothing to offer.
    public var archiveSummary: String {
        if isInArchive { return "In the archive" }
        return placeAcceptsArchive ? "Not added" : "This event isn't at a public place"
    }
}

/// A guest's own copy of what they photographed, and contributing it to the place's record.
///
/// **Why this exists at all.** Evidence offered at somebody's public event is stored under THEIR
/// organization, and until 2026-08-31 the only route to it was through the event and only once
/// accepted — so a guest whose submission was declined had handed over the only copy the product
/// would show them. The operator curates their event; they do not come to own what somebody else
/// photographed.
public struct EvidenceActions: Sendable {
    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    /// Everything this account has offered, across every event.
    public func mine() async -> LoadResult<[EvidenceSubmission]> {
        await api.load(Endpoint(.get, "api/my-evidence"), as: [EvidenceSubmission].self)
    }

    /// Contributes one submission to the archive of the place its event was held at.
    ///
    /// A tour walks the same route every week, which makes public events the one activity that
    /// happens repeatedly at fixed locations — so this is where a location's public record fills
    /// fastest.
    public func publishToPlace(eventId: UUID, submissionId: UUID) async -> Result<Void, FeedActionError> {
        outcome(await api.send(Endpoint(.post, path(eventId, submissionId))))
    }

    /// Takes it back off the place's archive.
    ///
    /// The paid half of the archive's bargain, and the server says so: a free account is told that
    /// keeping work private is part of a plan, in a sentence this surfaces unchanged.
    public func retractFromPlace(eventId: UUID, submissionId: UUID) async -> Result<Void, FeedActionError> {
        outcome(await api.send(Endpoint(.delete, path(eventId, submissionId))))
    }

    private func path(_ eventId: UUID, _ submissionId: UUID) -> String {
        "api/events/\(eventId.uuidString.lowercased())"
        + "/evidence/\(submissionId.uuidString.lowercased())/publish-to-place"
    }

    private func outcome(_ result: LoadResult<EmptyBody>) -> Result<Void, FeedActionError> {
        switch result {
        case .ok: .success(())
        // The server's sentence survives. "Keeping your sessions private is part of a paid plan"
        // tells somebody what to do; "couldn't remove that" does not.
        case .failed(let reason, _): .failure(.failed(reason: reason))
        case .sessionEnded: .failure(.sessionEnded)
        case .rateLimited(let after): .failure(.rateLimited(retryAfter: after))
        }
    }
}

