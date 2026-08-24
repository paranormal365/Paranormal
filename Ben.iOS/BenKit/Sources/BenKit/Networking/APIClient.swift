import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// The one place requests are built, sent, and mapped into `LoadResult`.
public actor APIClient {
    private let environment: @Sendable () -> APIEnvironment
    private let transport: Transport
    private let tokens: TokenSession

    public init(
        environment: @escaping @Sendable () -> APIEnvironment,
        transport: Transport,
        tokens: TokenSession
    ) {
        self.environment = environment
        self.transport = transport
        self.tokens = tokens
    }

    /// Fetch and decode a value.
    public func load<T: Decodable & Sendable>(_ endpoint: Endpoint, as type: T.Type) async -> LoadResult<T> {
        guard let request = await buildRequest(endpoint) else {
            return .failed(reason: nil)
        }
        do {
            let (data, response) = try await transport.send(request)
            if response.statusCode == 401 { await tokens.handleUnauthorized() }
            return ResponseMapping.decode(
                T.self, statusCode: response.statusCode,
                data: data, headers: response.allHeaderFields)
        } catch {
            // Unreachable API. Emphatically NOT "there is nothing here".
            return .failed(reason: nil)
        }
    }

    /// Fire an operation whose success needs no payload (204s, empty 200s).
    public func send(_ endpoint: Endpoint) async -> LoadResult<EmptyBody> {
        await load(endpoint, as: EmptyBody.self)
    }

    /// Download to a caller-owned file (PDFs, case videos — the no-Range routes
    /// where download-then-open is the reliable path). The temp file is moved to
    /// `destination` before returning.
    public func download(_ endpoint: Endpoint, to destination: URL) async -> LoadResult<URL> {
        guard let request = await buildRequest(endpoint) else {
            return .failed(reason: nil)
        }
        do {
            let (tempURL, response) = try await transport.download(request)
            if response.statusCode == 401 { await tokens.handleUnauthorized() }
            guard (200..<300).contains(response.statusCode) else {
                let data = (try? Data(contentsOf: tempURL, options: .mappedIfSafe)) ?? Data()
                try? FileManager.default.removeItem(at: tempURL)
                return ResponseMapping.failure(
                    statusCode: response.statusCode, data: data,
                    headers: response.allHeaderFields)
            }
            try? FileManager.default.removeItem(at: destination)
            try FileManager.default.moveItem(at: tempURL, to: destination)
            return .ok(destination)
        } catch {
            return .failed(reason: nil)
        }
    }

    /// Multipart upload composed to a scratch file so memory stays flat even
    /// for video. Decodes the response like `load`.
    public func upload<T: Decodable & Sendable>(
        _ endpoint: Endpoint, as type: T.Type
    ) async -> LoadResult<T> {
        guard case .multipart(let multipart) = endpoint.body else {
            assertionFailure("upload() requires a multipart endpoint body")
            return .failed(reason: nil)
        }
        guard var request = await buildRequest(endpoint, omitBody: true) else {
            return .failed(reason: nil)
        }
        request.setValue(multipart.contentTypeHeader, forHTTPHeaderField: "Content-Type")

        let scratch = FileManager.default.temporaryDirectory
            .appendingPathComponent("upload-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: scratch) }
        do {
            try multipart.write(to: scratch)
            let (data, response) = try await transport.upload(request, fromFile: scratch)
            if response.statusCode == 401 { await tokens.handleUnauthorized() }
            return ResponseMapping.decode(
                T.self, statusCode: response.statusCode,
                data: data, headers: response.allHeaderFields)
        } catch {
            return .failed(reason: nil)
        }
    }

    /// The absolute URL an endpoint resolves to — for handing anonymous media
    /// routes straight to AVPlayer/image loaders.
    public nonisolated func absoluteURL(for endpoint: Endpoint, in environment: APIEnvironment) -> URL? {
        environment.url(for: endpoint)
    }

    private func buildRequest(_ endpoint: Endpoint, omitBody: Bool = false) async -> URLRequest? {
        guard let url = environment().url(for: endpoint) else { return nil }
        var request = URLRequest(url: url)
        request.httpMethod = endpoint.method.rawValue
        request.setValue("application/json", forHTTPHeaderField: "Accept")

        if endpoint.requiresAuth, let token = await tokens.validAccessToken() {
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }

        if !omitBody, case .json(let data) = endpoint.body {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = data
        }
        return request
    }
}
