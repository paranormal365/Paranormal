import Foundation

/// Where the signed-in person's identity is kept between launches (item 235 phase 14c).
///
/// **Why it is kept at all.** A cold start used to be anonymous until `api/me` answered. With no signal that meant
/// the app came up signed out while still holding a perfectly good session — and everything a person needs most in
/// a building with no reception (tonight's door, a saved pass, the programme) sits behind being somebody. So the
/// last identity the server confirmed is kept beside the tokens, and a cold start with no signal is that person
/// until the server says otherwise.
///
/// It holds only what `api/me` returned — an id, an address and two flags — and it goes whenever the session does.
public protocol IdentityStorage: Sendable {
    func load() -> MeResponse?
    func save(_ me: MeResponse)
    func clear()
}

/// In memory, for tests and previews.
public final class InMemoryIdentityStorage: IdentityStorage, @unchecked Sendable {
    private let lock = NSLock()
    private var me: MeResponse?

    public init(me: MeResponse? = nil) { self.me = me }

    public func load() -> MeResponse? { lock.withLock { me } }
    public func save(_ me: MeResponse) { lock.withLock { self.me = me } }
    public func clear() { lock.withLock { me = nil } }
}

/// On the phone, in Application Support, protected until it is first unlocked after a restart — the same protection
/// as the saved passes, so a phone woken in a pocket can still show them.
public struct FileIdentityStorage: IdentityStorage {
    private let file: URL

    public init(file: URL) { self.file = file }

    public static func applicationSupport() -> FileIdentityStorage {
        let base = (try? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true))
            ?? FileManager.default.temporaryDirectory
        return FileIdentityStorage(file: base.appendingPathComponent("Session/me.json"))
    }

    public func load() -> MeResponse? {
        guard let data = try? Data(contentsOf: file) else { return nil }
        return try? JSONDecoder().decode(MeResponse.self, from: data)
    }

    public func save(_ me: MeResponse) {
        do {
            try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            let data = try JSONEncoder().encode(me)
            #if os(iOS)
            try data.write(to: file, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
            #else
            try data.write(to: file, options: .atomic)
            #endif
        } catch {
            // Not kept means an offline cold start is anonymous, as it always was.
        }
    }

    public func clear() { try? FileManager.default.removeItem(at: file) }
}
