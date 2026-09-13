import Foundation

/// Fetches bytes from an AUTHENTICATED route, for images SwiftUI's `AsyncImage` cannot load.
///
/// `AsyncImage` issues its own unauthenticated request, so anything behind a bearer token —
/// case files, private photos — comes back 401 and renders as a broken image. This goes
/// through `APIClient`, so the token (and its refresh) apply exactly as they do everywhere
/// else, and a refusal is a refusal rather than a mysteriously empty frame.
public actor AuthenticatedImageLoader {
    private let api: APIClient
    /// Small, bounded: a case timeline shows a handful of photos, and holding decoded bytes
    /// for every file a person has ever scrolled past is how an app gets killed for memory.
    private var cache: [UUID: Data] = [:]
    private var order: [UUID] = []
    private let limit: Int

    public init(api: APIClient, limit: Int = 24) {
        self.api = api
        self.limit = limit
    }

    public func data(for fileId: UUID) async -> Data? {
        await data(for: fileId, from: CaseDetailStore.fileEndpoint(fileId))
    }

    /// The same bounded cache for any authenticated media route — an event room's photos, keyed by the post.
    public func data(for key: UUID, from endpoint: Endpoint) async -> Data? {
        if let cached = cache[key] { return cached }

        guard case .ok(let data) = await api.loadData(endpoint) else {
            return nil
        }

        cache[key] = data
        order.append(key)
        if order.count > limit, let evicted = order.first {
            order.removeFirst()
            cache[evicted] = nil
        }
        return data
    }
}
