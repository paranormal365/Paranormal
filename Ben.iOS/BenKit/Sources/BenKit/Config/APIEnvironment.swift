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
    /// The live site. There is exactly one deployment: `ishaunted.com` was stood up as UAT on
    /// 2026-08-19 and became the real production environment on 2026-08-23
    /// (`feature/production-deploy-ishaunted`). No separate production host exists — the repo
    /// references no other hostname, and the publish scripts and `docs/deploy-production.md` all
    /// target this one. Naming it `uat` in a shipping app would be a lie the day someone acts on it.
    ///
    /// The `/webapi` path is not decoration: the API is deployed as an IIS application under it,
    /// so `https://ishaunted.com/api/...` is a 404 and `https://ishaunted.com/webapi/api/...` is
    /// the real route. Verified against the live host 2026-08-28. This is the same trap
    /// `ApiBasePathHandler.cs` exists to prevent on the web side — the base path must be
    /// APPENDED to, never resolved against, which is what `url(for:)` does.
    public static let production = APIEnvironment(
        name: "IsHaunted", baseURL: URL(string: "https://ishaunted.com/webapi")!)

    public static let presets: [APIEnvironment] = [.dev, .production]

    /// The environment a build falls back to when the user has chosen nothing.
    ///
    /// A release build must never fall back to `dev`. `dev` is `http://localhost:5252`, which on
    /// somebody else's phone is their own device answering nothing: the app would launch, fail
    /// every call, and read as simply broken rather than as misconfigured. DEBUG keeps `dev` so
    /// the simulator workflow is unchanged.
    public static var fallback: APIEnvironment {
        #if DEBUG
        return .dev
        #else
        return .production
        #endif
    }

    /// Whether this base URL could be answered by a host that is not the developer's own machine.
    ///
    /// Loopback and private LAN addresses are reachable only from where they were typed. A release
    /// build treats a saved one as no choice at all, rather than honouring it and shipping an app
    /// that cannot reach anything.
    public var isReachableOffDevelopmentMachine: Bool {
        guard baseURL.scheme?.lowercased() == "https" else { return false }
        guard let host = baseURL.host?.lowercased() else { return false }
        if host == "localhost" || host == "::1" || host.hasSuffix(".local") { return false }
        if host.hasPrefix("127.") || host.hasPrefix("10.") || host.hasPrefix("192.168.") {
            return false
        }
        // The private range 172.16.0.0 - 172.31.255.255; 172.32.x is public and must pass.
        if host.hasPrefix("172.") {
            let secondOctet = host.dropFirst(4).prefix { $0 != "." }
            if let value = Int(secondOctet), (16...31).contains(value) { return false }
        }
        return true
    }

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
        #if DEBUG
        // Automation hook, matching `-autoSignIn` in IsHauntedApp: `-apiBaseURL <url>` points the
        // whole app at one API for the life of the launch, without writing to the saved choice.
        // A UI test that needs an API built from the working tree — a new endpoint, a migration
        // the shared dev database has not had — can stand one up on a scratch port and aim at it,
        // instead of silently exercising whatever host happened to be running.
        //
        // DEBUG only, and it deliberately does NOT call save(): nothing a test does should change
        // which environment the next ordinary launch uses.
        if let raw = UserDefaults.standard.string(forKey: "apiBaseURL"),
           let url = URL(string: raw), url.scheme != nil {
            return APIEnvironment(name: "Test", baseURL: url)
        }
        #endif

        guard let data = UserDefaults.standard.data(forKey: Self.key),
              let saved = try? JSONDecoder().decode(APIEnvironment.self, from: data)
        else { return .fallback }

        #if DEBUG
        return saved
        #else
        // A shipped build never honours a saved developer address. Nothing a tester left behind on
        // a TestFlight device should be able to point the app at a host only a Mac can answer.
        return saved.isReachableOffDevelopmentMachine ? saved : .fallback
        #endif
    }

    public func save(_ environment: APIEnvironment) {
        if let data = try? JSONEncoder().encode(environment) {
            UserDefaults.standard.set(data, forKey: Self.key)
        }
    }
}
