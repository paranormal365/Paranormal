import Foundation

/// Which version of the app the App Store is offering — `GET api/public/app-version/ios`.
///
/// The server asks Apple and caches the answer, so every phone gets the same one (10/03/2026).
public struct AppVersionRecord: Codable, Sendable, Equatable {
    public let platform: String
    /// The live marketing version, "1.1.2".
    public let latestVersion: String
    public let releasedUtc: Date?
    /// The "What's New" text, as written for the store.
    public let releaseNotes: String?
    /// The App Store page to update from.
    public let storeUrl: String
    /// The oldest iOS the live version installs on, "18.0".
    public let minimumOsVersion: String?

    public init(platform: String, latestVersion: String, releasedUtc: Date?, releaseNotes: String?,
                storeUrl: String, minimumOsVersion: String?) {
        self.platform = platform
        self.latestVersion = latestVersion
        self.releasedUtc = releasedUtc
        self.releaseNotes = releaseNotes
        self.storeUrl = storeUrl
        self.minimumOsVersion = minimumOsVersion
    }
}

/// Dotted version numbers compared the way people read them: 1.1.10 is newer than 1.1.9.
public enum AppVersionNumber {
    /// Orders two versions by their numeric parts; a missing part counts as 0, so "1.2" equals "1.2.0".
    /// Anything after a part's digits ("3b") is ignored, so a stray suffix cannot reorder releases.
    public static func compare(_ a: String, _ b: String) -> ComparisonResult {
        let left = parts(a), right = parts(b)
        for i in 0..<max(left.count, right.count) {
            let l = i < left.count ? left[i] : 0
            let r = i < right.count ? right[i] : 0
            if l != r { return l < r ? .orderedAscending : .orderedDescending }
        }
        return .orderedSame
    }

    private static func parts(_ version: String) -> [Int] {
        version.split(separator: ".").map { Int($0.prefix(while: \.isNumber)) ?? 0 }
    }
}

/// What the Profile screen says about updates.
public enum AppUpdateStatus: Sendable, Equatable {
    /// This is the version the store is offering, or newer (a build not yet released).
    case upToDate(current: String)
    /// A newer version is out and this phone can install it.
    case available(AppVersionRecord)
    /// A newer version is out, but it needs a newer iOS than this phone has.
    case needsNewerIOS(AppVersionRecord, phoneOS: String)
    /// The check could not be made; says why.
    case failed(reason: String)

    /// The decision, apart from the network, so it can be tested on its own.
    public static func decide(current: String, phoneOS: String, live: AppVersionRecord) -> AppUpdateStatus {
        guard AppVersionNumber.compare(live.latestVersion, current) == .orderedDescending else {
            return .upToDate(current: current)
        }
        if let minimum = live.minimumOsVersion,
           AppVersionNumber.compare(phoneOS, minimum) == .orderedAscending {
            return .needsNewerIOS(live, phoneOS: phoneOS)
        }
        return .available(live)
    }
}

/// Asks the site which version is live and decides what that means for this phone.
public struct AppUpdateChecker: Sendable {
    private let api: APIClient

    public init(api: APIClient) { self.api = api }

    public func check(current: String, phoneOS: String) async -> AppUpdateStatus {
        switch await api.load(Endpoint(.get, "api/public/app-version/ios", requiresAuth: false), as: AppVersionRecord.self) {
        case .ok(let live):
            return AppUpdateStatus.decide(current: current, phoneOS: phoneOS, live: live)
        case .failed(let reason, _):
            return .failed(reason: reason ?? "Couldn't check for a new version. Check your connection and try again.")
        case .rateLimited:
            return .failed(reason: "Too many checks just now. Try again in a minute.")
        case .sessionEnded:
            // The endpoint is anonymous, so this cannot be about the check itself.
            return .failed(reason: "Couldn't check for a new version. Try again.")
        }
    }
}
