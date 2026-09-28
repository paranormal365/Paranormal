import Foundation

/// The four calls behind a lead's Launch and a guest's Join (item 252).
public struct FieldLaunchActions: Sendable {
    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    /// What the signed-in person may launch right now.
    public func launchable() async -> LoadResult<[LaunchableRecord]> {
        await api.load(Endpoint(.get, "api/field-launches/launchable"), as: [LaunchableRecord].self)
    }

    /// Launch: the card goes up for everybody registered, and their phones are told.
    public func launch(target: String, id: UUID) async -> LoadResult<LaunchOutcomeRecord> {
        struct Body: Encodable { let target: String; let id: UUID }
        guard let endpoint = try? Endpoint.json(.post, "api/field-launches", payload: Body(target: target, id: id)) else {
            return .failed(reason: nil)
        }
        return await api.load(endpoint, as: LaunchOutcomeRecord.self)
    }

    /// The launches still open for the signed-in person — "Happening now".
    public func mine() async -> LoadResult<[FieldLaunchRecord]> {
        await api.load(Endpoint(.get, "api/field-launches/mine"), as: [FieldLaunchRecord].self)
    }

    /// One launch, to join it. Signed in, it carries who is asking (a private launch opens only
    /// for its people); signed out, a public one still opens — Field Kit needs no account.
    public func launch(_ id: UUID) async -> LoadResult<FieldLaunchRecord> {
        await api.load(Endpoint(.get, "api/field-launches/\(id.uuidString.lowercased())"), as: FieldLaunchRecord.self)
    }
}
