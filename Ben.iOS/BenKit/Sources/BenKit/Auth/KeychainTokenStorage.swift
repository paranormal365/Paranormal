import Foundation
import Security

/// Tokens live in one generic-password Keychain item: this-device-only,
/// available after first unlock, never synced to iCloud. The Keychain outlives
/// an app deletion, so a stale token on reinstall is expected — the refresh
/// path lands it quietly in signed-out, never an error dialog.
public struct KeychainTokenStorage: TokenStorage {
    private let service: String
    private let account = "tokens"

    public init(service: String = "com.ishaunted.ios") {
        self.service = service
    }

    public func load() -> StoredTokens? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne

        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess,
              let data = item as? Data
        else { return nil }
        return try? JSONDecoder().decode(StoredTokens.self, from: data)
    }

    public func save(_ tokens: StoredTokens) {
        guard let data = try? JSONEncoder().encode(tokens) else { return }
        var attributes = baseQuery
        attributes[kSecValueData as String] = data
        attributes[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly

        let status = SecItemAdd(attributes as CFDictionary, nil)
        if status == errSecDuplicateItem {
            SecItemUpdate(baseQuery as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        }
    }

    public func clear() {
        SecItemDelete(baseQuery as CFDictionary)
    }

    private var baseQuery: [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
    }
}

/// For unit tests and previews.
public final class InMemoryTokenStorage: TokenStorage, @unchecked Sendable {
    private let lock = NSLock()
    private var tokens: StoredTokens?

    public init(tokens: StoredTokens? = nil) {
        self.tokens = tokens
    }

    public func load() -> StoredTokens? {
        lock.withLock { tokens }
    }

    public func save(_ tokens: StoredTokens) {
        lock.withLock { self.tokens = tokens }
    }

    public func clear() {
        lock.withLock { tokens = nil }
    }
}
