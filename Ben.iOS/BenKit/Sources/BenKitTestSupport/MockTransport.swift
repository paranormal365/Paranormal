import Foundation
import BenKit
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Closure-driven `Transport` for tests. Records every request it sees.
public final class MockTransport: Transport, @unchecked Sendable {
    public typealias Handler = @Sendable (URLRequest) async throws -> (Data, HTTPURLResponse)

    private let lock = NSLock()
    private var _requests: [URLRequest] = []
    private let handler: Handler

    public init(handler: @escaping Handler) {
        self.handler = handler
    }

    /// Convenience: always answer with this status/body.
    public convenience init(status: Int, body: Data = Data(), headers: [String: String] = [:]) {
        self.init { request in
            (body, MockTransport.response(for: request, status: status, headers: headers))
        }
    }

    public var requests: [URLRequest] {
        lock.withLock { _requests }
    }

    public func requestCount(pathSuffix: String) -> Int {
        requests.filter { $0.url?.path.hasSuffix(pathSuffix) == true }.count
    }

    public func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        lock.withLock { _requests.append(request) }
        return try await handler(request)
    }

    public func download(_ request: URLRequest) async throws -> (URL, HTTPURLResponse) {
        let (data, response) = try await send(request)
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("mock-download-\(UUID().uuidString)")
        try data.write(to: url)
        return (url, response)
    }

    public func upload(_ request: URLRequest, fromFile file: URL) async throws -> (Data, HTTPURLResponse) {
        var recorded = request
        recorded.httpBody = try? Data(contentsOf: file)
        return try await send(recorded)
    }

    public static func response(
        for request: URLRequest, status: Int, headers: [String: String] = [:]
    ) -> HTTPURLResponse {
        HTTPURLResponse(
            url: request.url ?? URL(string: "http://localhost")!,
            statusCode: status, httpVersion: "HTTP/1.1", headerFields: headers)!
    }
}

/// Loads captured JSON bodies from the test bundle's Fixtures directory.
public enum Fixtures {
    public static func data(_ name: String, in bundle: Bundle) throws -> Data {
        guard let url = bundle.url(forResource: name, withExtension: "json", subdirectory: "Fixtures")
                ?? bundle.url(forResource: name, withExtension: "json")
        else {
            throw CocoaError(.fileNoSuchFile, userInfo: [NSFilePathErrorKey: "Fixtures/\(name).json"])
        }
        return try Data(contentsOf: url)
    }
}
