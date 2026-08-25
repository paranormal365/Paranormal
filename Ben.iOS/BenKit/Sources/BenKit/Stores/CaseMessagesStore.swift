import Foundation

/// Messages between a client and the group working their case.
///
/// Reading the list is what marks the group's messages as read — the server does it, so there is
/// no second call and no way for the badge to disagree with what is on screen.
@MainActor
@Observable
public final class CaseMessagesStore {
    public enum State: Equatable, Sendable {
        case loading
        case loaded
        case signedOut
        case failed(reason: String?)
    }

    public private(set) var state: State = .loading
    public private(set) var messages: [MyCaseMessage] = []
    public private(set) var sending = false

    private let caseId: UUID
    private let api: APIClient

    public init(caseId: UUID, api: APIClient) {
        self.caseId = caseId
        self.api = api
    }

    private var path: String { "api/my-cases/\(caseId.uuidString.lowercased())/messages" }

    public func load() async {
        if messages.isEmpty { state = .loading }
        switch await api.load(Endpoint(.get, path), as: [MyCaseMessage].self) {
        case .ok(let items):
            // Oldest first: a conversation reads downwards.
            messages = items.sorted { $0.dateCreated < $1.dateCreated }
            state = .loaded
        case .failed(let reason, _):
            state = .failed(reason: reason)
        case .sessionEnded:
            state = .signedOut
        case .rateLimited:
            state = .failed(reason: "Too many requests just now. Try again in a moment.")
        }
    }

    public func send(_ body: String) async -> Result<MyCaseMessage, FeedActionError> {
        let trimmed = body.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return .failure(.failed(reason: "Write something first.")) }

        struct Body: Encodable { let body: String }
        guard let endpoint = try? Endpoint.json(.post, path, payload: Body(body: trimmed)) else {
            return .failure(.failed(reason: nil))
        }

        sending = true
        defer { sending = false }

        switch await api.load(endpoint, as: MyCaseMessage.self) {
        case .ok(let message):
            // Appended rather than re-fetched: a reload here would scroll the conversation out
            // from under somebody mid-sentence.
            messages.append(message)
            return .success(message)
        case .failed(let reason, _):
            return .failure(.failed(reason: reason))
        case .sessionEnded:
            return .failure(.sessionEnded)
        case .rateLimited(let after):
            return .failure(.rateLimited(retryAfter: after))
        }
    }
}
