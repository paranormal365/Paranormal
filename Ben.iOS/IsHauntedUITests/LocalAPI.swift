import XCTest

/// The other side of a conversation a UI test is watching — the lead launching, approving — spoken
/// to the same local API the app is pointed at. Credentials come from the TEST_RUNNER_ environment.
struct LocalAPI {
    let base: String
    let test: XCTestCase

    func signIn(_ email: String, passwordVariable: String) throws -> String {
        struct Token: Decodable { let accessToken: String }
        let body = try JSONSerialization.data(withJSONObject: ["email": email, "password": TestSecrets.required(passwordVariable)])
        let token: Token = try decode(send("POST", "/login", body: body, bearer: nil))
        return token.accessToken
    }

    func post(_ path: String, _ json: [String: String] = [:], bearer: String) throws -> Data {
        try send("POST", path, body: try JSONSerialization.data(withJSONObject: json), bearer: bearer)
    }

    func get(_ path: String, bearer: String) throws -> Data {
        try send("GET", path, body: nil, bearer: bearer)
    }

    func decode<T: Decodable>(_ data: Data) throws -> T {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { _ in Date() }   // tests read ids and words, never dates
        return try decoder.decode(T.self, from: data)
    }

    private func send(_ method: String, _ path: String, body: Data?, bearer: String?) throws -> Data {
        var request = URLRequest(url: URL(string: base + path)!)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if let bearer { request.setValue("Bearer \(bearer)", forHTTPHeaderField: "Authorization") }
        request.httpBody = body
        let done = test.expectation(description: path)
        nonisolated(unsafe) var result: Result<Data, Error> = .failure(URLError(.unknown))
        URLSession.shared.dataTask(with: request) { data, response, error in
            if let error { result = .failure(error) }
            else if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                result = .failure(NSError(domain: "http", code: http.statusCode, userInfo: [
                    NSLocalizedDescriptionKey: "\(method) \(path): \(http.statusCode) \(String(data: data ?? Data(), encoding: .utf8) ?? "")"]))
            } else { result = .success(data ?? Data()) }
            done.fulfill()
        }.resume()
        test.wait(for: [done], timeout: 30)
        return try result.get()
    }
}
