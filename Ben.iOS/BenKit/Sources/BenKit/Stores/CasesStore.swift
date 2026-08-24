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

    /// Where an attached file's bytes come from — the same shared, AUTHENTICATED route the
    /// website uses (`GetFileDownloadUrl`). There is no public URL for case media, which is
    /// the whole point: a case belongs to its client and its group. An `AsyncImage` pointed
    /// at this would get a 401, so images go through `AuthenticatedImageLoader`.
    public nonisolated static func fileEndpoint(_ fileId: UUID) -> Endpoint {
        Endpoint(.get, "api/upload-files/\(fileId.uuidString.lowercased())/download")
    }
}
