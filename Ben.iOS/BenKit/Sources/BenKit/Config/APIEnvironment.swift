import Foundation

/// Which API host the app talks to. The base URL may carry a path
/// (`https://ishaunted.com/webapi/`) — every request URL is built by *appending*
/// to that path, never by resolving a root-relative string against it, which is
/// exactly the mistake `ApiBasePathHandler.cs` exists to prevent on the web side.
public struct APIEnvironment: Sendable, Equatable, Codable, Hashable {
    public var name: String
    public var baseURL: URL

    public init(name: String, baseURL: URL) {
        self.name = name
        self.baseURL = baseURL
    }

    /// Local dev API — `dotnet run` in Ben.Data.WebApi (launchSettings `http` profile).
    public static let dev = APIEnvironment(name: "Dev", baseURL: URL(string: "http://localhost:5252")!)
    /// UAT — ishaunted.com is UAT, not production.
    public static let uat = APIEnvironment(name: "UAT", baseURL: URL(string: "https://ishaunted.com")!)

    public static let presets: [APIEnvironment] = [.dev, .uat]

    /// Builds the absolute URL for an endpoint, preserving any base path.
    public func url(for endpoint: Endpoint) -> URL? {
        guard var components = URLComponents(url: baseURL, resolvingAgainstBaseURL: true) else { return nil }
        var basePath = components.path
        if basePath.hasSuffix("/") { basePath.removeLast() }
        components.path = basePath + "/" + endpoint.path
        components.queryItems = endpoint.query.isEmpty ? nil : endpoint.query
        return components.url
    }
}

/// Persists the chosen environment. Switching environments must be followed by
/// clearing the session and caches — the store only remembers the choice.
public struct APIEnvironmentStore: Sendable {
    private static let key = "com.ishaunted.ios.apiEnvironment"

    public init() {}

    public func load() -> APIEnvironment {
        guard let data = UserDefaults.standard.data(forKey: Self.key),
              let saved = try? JSONDecoder().decode(APIEnvironment.self, from: data)
        else { return .dev }
        return saved
    }

    public func save(_ environment: APIEnvironment) {
        if let data = try? JSONEncoder().encode(environment) {
            UserDefaults.standard.set(data, forKey: Self.key)
        }
    }
}
