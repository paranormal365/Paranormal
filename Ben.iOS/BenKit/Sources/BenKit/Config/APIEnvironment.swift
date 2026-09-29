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
    /// 2026-08-19 and became the real production environment on 2026-08-23. Naming it `uat` in a
    /// shipping app would be a lie the day someone acts on it.
    ///
    /// **Its own host since 2026-09-29.** The site moved to MonsterASP, where the website and the API
    /// are separate sites: the website at `ishaunted.com`, the API at the ROOT of `api.ishaunted.com`
    /// (so `https://api.ishaunted.com/api/...`). Until then the API was an IIS application under
    /// `https://ishaunted.com/webapi`, which is what every build up to 1.1.0 (8) calls — see
    /// `retiredProductionBaseURLs`.
    ///
    /// Why not just follow a redirect from the old address: iOS drops the `Authorization` header
    /// when a request is redirected to another host (checked with URLSession, 2026-09-29), so a
    /// redirected app reaches the API signed out on every call.
    public static let production = APIEnvironment(
        name: "IsHaunted", baseURL: URL(string: "https://api.ishaunted.com")!)

    /// Addresses production used to live at. A choice saved while one of these was current means
    /// "production", not "this exact URL": `APIEnvironmentStore.load()` reads it as today's
    /// `.production`, so an update is never left calling the old host.
    public static let retiredProductionBaseURLs: [URL] = [
        URL(string: "https://ishaunted.com/webapi")!,
        URL(string: "https://www.ishaunted.com/webapi")!,
    ]

    /// Whether this is production at any address it has had.
    public var isProduction: Bool {
        let wanted = baseURL.absoluteString.trimmingCharacters(in: CharacterSet(charactersIn: "/")).lowercased()
        return ([Self.production.baseURL] + Self.retiredProductionBaseURLs).contains {
            $0.absoluteString.trimmingCharacters(in: CharacterSet(charactersIn: "/")).lowercased() == wanted
        }
    }

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

    /// The website this API belongs to, for the pages the app sends people to rather than rebuilding
    /// (item 235 phase 14: booking a hosted event's places), and for the Field Kit join QR code —
    /// which MUST be on the website's host, because that is the host the universal links claim.
    ///
    /// Production's API is `api.ishaunted.com` and its website `ishaunted.com`, so an `api.` host
    /// loses that label. An API under `/webapi` on the site's own host (production before
    /// 2026-09-29, and a custom address typed in Settings) loses that segment instead. The local API
    /// on :5252 belongs to the local website on :5078, as `launchSettings` has them.
    public var websiteURL: URL {
        guard var components = URLComponents(url: baseURL, resolvingAgainstBaseURL: true) else { return baseURL }
        if let host = components.host?.lowercased(), host.hasPrefix("api.") {
            components.host = String(host.dropFirst("api.".count))
        }
        if components.path.lowercased().hasSuffix("/webapi") {
            components.path = String(components.path.dropLast("/webapi".count))
        }
        if components.path == "/" { components.path = "" }
        if components.port == 5252 { components.port = 5078 }
        return components.url ?? baseURL
    }

    /// A website page by its path, like `o/paranormal365/events/seance-weekend`.
    public func websiteURL(path: String) -> URL {
        websiteURL.appending(path: path)
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

        // Production saved at an address it has since left (2026-09-29: ishaunted.com/webapi →
        // api.ishaunted.com) is still production. Honouring the stale URL would pin an updated app
        // to a host that only redirects — and a redirect to another host loses the sign-in.
        if saved.isProduction { return .production }

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
