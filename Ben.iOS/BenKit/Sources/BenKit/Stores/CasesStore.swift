import Foundation
import Observation

/// The client's cases (iOS Slice 6). Reading only, for now: logging a new occurrence needs
/// the media pipeline the composer already proved, and arrives next.
@Observable
@MainActor
public final class CasesStore {
    public enum State: Equatable {
        case loading
        case loaded
        /// Cases are about YOUR case — there is nothing here without an account, and saying
        /// so is not an apology.
        case signedOut
        case failed(reason: String?)
    }

    public private(set) var state: State = .loading
    public private(set) var cases: [MyCaseSummary] = []

    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    public func load() async {
        if case .loaded = state {} else { state = .loading }

        switch await api.load(Endpoint(.get, "api/my-cases"), as: [MyCaseSummary].self) {
        case .ok(let cases):
            self.cases = cases
            state = .loaded
        case .sessionEnded:
            cases = []
            state = .signedOut
        case .failed(_, let statusCode) where statusCode == 401:
            cases = []
            state = .signedOut
        case .failed(let reason, _):
            state = .failed(reason: reason)
        case .rateLimited:
            state = .failed(reason: "Too many requests — try again shortly.")
        }
    }

    public func clear() {
        cases = []
        state = .loading
    }
}

/// One case, in full.
@Observable
@MainActor
public final class CaseDetailStore {
    public private(set) var state: CasesStore.State = .loading
    public private(set) var detail: MyCaseDetail?

    private let api: APIClient
    private let caseId: UUID

    public init(caseId: UUID, api: APIClient) {
        self.caseId = caseId
        self.api = api
    }

    public func load() async {
        if case .loaded = state {} else { state = .loading }

        let endpoint = Endpoint(.get, "api/my-cases/\(caseId.uuidString.lowercased())")
        switch await api.load(endpoint, as: MyCaseDetail.self) {
        case .ok(let detail):
            self.detail = detail
            state = .loaded
        case .sessionEnded:
            state = .signedOut
        case .failed(_, let statusCode) where statusCode == 401:
            state = .signedOut
        case .failed(let reason, let statusCode) where statusCode == 404:
            // Not theirs, or gone. One answer for both, exactly as the server intends —
            // "this case isn't yours" would confirm the case exists.
            _ = reason
            state = .failed(reason: "That case isn't available.")
        case .failed(let reason, _):
            state = .failed(reason: reason)
        case .rateLimited:
            state = .failed(reason: "Too many requests — try again shortly.")
        }
    }

    /// Logs something that happened, from the phone — the thing a phone is genuinely best at,
    /// because the person is usually standing in the room when they remember it.
    ///
    /// Two steps, because the API has two: the entry is created first, then each file is
    /// attached to it. That order matters — a half-uploaded photo leaves a real entry with
    /// fewer pictures, which is recoverable, whereas the other way round would leave orphaned
    /// files belonging to nothing.
    public func logOccurrence(
        eventDateTime: Date?, title: String?, body: String?,
        experienceTypeIds: [UUID] = [], media: [MediaUpload] = []
    ) async -> Result<MyCaseOccurrence, FeedActionError> {
        struct Body: Encodable {
            let eventDateTime: Date?
            let title: String?
            let body: String?
            let experienceTypeIds: [UUID]
        }
        guard let endpoint = try? Endpoint.json(
            .post, "api/my-cases/\(caseId.uuidString.lowercased())/occurrences",
            payload: Body(eventDateTime: eventDateTime, title: title,
                          body: body, experienceTypeIds: experienceTypeIds))
        else { return .failure(.failed(reason: nil)) }

        let created: MyCaseOccurrence
        switch await api.load(endpoint, as: CaseTimelineEntryRecord.self) {
        case .ok(let entry): created = entry.asOccurrence(readerId: entry.authorAppUserId)
        case .failed(let reason, _): return .failure(.failed(reason: reason))
        case .sessionEnded: return .failure(.sessionEnded)
        case .rateLimited(let after): return .failure(.rateLimited(retryAfter: after))
        }

        // Attachments are best-effort by design: the entry EXISTS now, and a failed upload
        // must not report the whole thing as lost. `failedAttachments` tells the caller how
        // many to mention rather than swallowing it.
        var failures = 0
        for upload in media {
            let attach = Endpoint(
                .post,
                "api/my-cases/\(caseId.uuidString.lowercased())/occurrences/\(created.id.uuidString.lowercased())/files",
                body: .multipart(MultipartBody(parts: [
                    .file("file", filename: upload.filename,
                          contentType: upload.contentType, url: upload.fileURL)
                ])))
            if !(await api.upload(attach, as: MyCaseFile.self)).isOk { failures += 1 }
        }
        failedAttachments = failures

        return .success(created)
    }

    /// How many attachments failed on the last log. The entry still saved; the screen says so.
    public private(set) var failedAttachments = 0

    /// Where an attached file's bytes come from — the same shared, AUTHENTICATED route the
    /// website uses (`GetFileDownloadUrl`). There is no public URL for case media, which is
    /// the whole point: a case belongs to its client and its group. An `AsyncImage` pointed
    /// at this would get a 401, so images go through `AuthenticatedImageLoader`.
    public nonisolated static func fileEndpoint(_ fileId: UUID) -> Endpoint {
        Endpoint(.get, "api/upload-files/\(fileId.uuidString.lowercased())/download")
    }
}
