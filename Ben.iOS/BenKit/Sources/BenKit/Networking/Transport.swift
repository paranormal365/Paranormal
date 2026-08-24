import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// The seam between `APIClient`/`TokenSession` and the network. `MockTransport`
/// (BenKitTestSupport) implements this so every unit test runs without a socket.
public protocol Transport: Sendable {
    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse)
    /// Downloads to a temporary file the caller must move or consume promptly.
    func download(_ request: URLRequest) async throws -> (URL, HTTPURLResponse)
    func upload(_ request: URLRequest, fromFile file: URL) async throws -> (Data, HTTPURLResponse)
}

public struct URLSessionTransport: Transport {
    private let session: URLSession

    public init(session: URLSession = .benShared) {
        self.session = session
    }

    public func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let (data, response) = try await session.data(for: request)
        return (data, try Self.http(response))
    }

    public func download(_ request: URLRequest) async throws -> (URL, HTTPURLResponse) {
        let (url, response) = try await session.download(for: request)
        return (url, try Self.http(response))
    }

    public func upload(_ request: URLRequest, fromFile file: URL) async throws -> (Data, HTTPURLResponse) {
        let (data, response) = try await session.upload(for: request, fromFile: file)
        return (data, try Self.http(response))
    }

    private static func http(_ response: URLResponse) throws -> HTTPURLResponse {
        guard let http = response as? HTTPURLResponse else {
            throw URLError(.badServerResponse)
        }
        return http
    }
}

extension URLSession {
    /// The app-wide session: a dedicated URLCache (20 MB memory / 200 MB disk)
    /// honoring server cache headers — feed images and event lists benefit with
    /// zero schema work (the Phase-1 offline posture).
    public static let benShared: URLSession = {
        let configuration = URLSessionConfiguration.default
        configuration.urlCache = URLCache(
            memoryCapacity: 20 * 1024 * 1024,
            diskCapacity: 200 * 1024 * 1024)
        configuration.requestCachePolicy = .useProtocolCachePolicy
        return URLSession(configuration: configuration)
    }()
}
