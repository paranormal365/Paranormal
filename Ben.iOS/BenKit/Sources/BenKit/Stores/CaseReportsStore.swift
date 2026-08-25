import Foundation

/// The reports a group has published on a client's case, and getting one onto the phone.
@MainActor
@Observable
public final class CaseReportsStore {
    /// The same four-way shape the other case stores use: a refusal must never render as
    /// "nothing here".
    public enum State: Equatable {
        case loading
        case loaded
        case signedOut
        case failed(reason: String?)
    }

    public private(set) var state: State = .loading
    public private(set) var reports: [MyCaseReport] = []

    private let caseId: UUID
    private let api: APIClient

    public init(caseId: UUID, api: APIClient) {
        self.caseId = caseId
        self.api = api
    }

    public func load() async {
        if reports.isEmpty { state = .loading }
        switch await api.load(
            Endpoint(.get, "api/my-cases/\(caseId.uuidString.lowercased())/reports"),
            as: [MyCaseReport].self) {
        case .ok(let items):
            // Newest first: the report somebody opens the screen for is the one just published.
            reports = items.sorted { $0.readerDate > $1.readerDate }
            state = .loaded
        case .failed(let reason, _):
            state = .failed(reason: reason)
        case .sessionEnded:
            state = .signedOut
        case .rateLimited:
            state = .failed(reason: "Too many requests just now. Try again in a moment.")
        }
    }

    /// Fetches the PDF to a file and hands back its location.
    ///
    /// A download, not a stream: this route carries a bearer token and has no Range support, so
    /// pointing a viewer straight at the URL would get an unauthorized empty document.
    public func downloadPDF(_ report: MyCaseReport) async -> Result<URL, FeedActionError> {
        let destination = FileManager.default.temporaryDirectory
            .appendingPathComponent("case-report-\(report.id.uuidString.lowercased()).pdf")

        switch await api.download(
            Endpoint(.get,
                     "api/my-cases/\(caseId.uuidString.lowercased())/reports/\(report.id.uuidString.lowercased())/pdf"),
            to: destination) {
        case .ok(let url):
            return .success(url)
        case .failed(let reason, _):
            return .failure(.failed(reason: reason))
        case .sessionEnded:
            return .failure(.sessionEnded)
        case .rateLimited(let after):
            return .failure(.rateLimited(retryAfter: after))
        }
    }
}
