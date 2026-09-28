import Foundation

/// Telling the server which phone to push for the signed-in person (item 252).
public struct PushDeviceActions: Sendable {
    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    /// Apple's token as the server stores it: lowercase hex.
    public static func hex(_ token: Data) -> String {
        token.map { String(format: "%02x", $0) }.joined()
    }

    /// Registers (or refreshes) this phone. `sandbox` for a build run from Xcode, whose token
    /// belongs to Apple's sandbox service; TestFlight and the App Store use production.
    @discardableResult
    public func register(token: String, sandbox: Bool, appVersion: String?) async -> Bool {
        struct Body: Encodable { let token: String; let environment: String; let appVersion: String? }
        guard let endpoint = try? Endpoint.json(.post, "api/me/push-devices", payload: Body(
            token: token, environment: sandbox ? "sandbox" : "production", appVersion: appVersion))
        else { return false }
        if case .ok = await api.send(endpoint) { return true }
        return false
    }

    /// Stops pushes to this phone for the signed-in person — at sign-out.
    public func remove(token: String) async {
        _ = await api.send(Endpoint(.delete, "api/me/push-devices/\(token)"))
    }
}
