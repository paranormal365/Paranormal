import Foundation
import Testing
@testable import BenKit

@Suite("URL building — base paths survive (the ApiBasePathHandler.cs lesson)")
struct URLBuildingTests {

    @Test func plainBase() {
        let env = APIEnvironment(name: "t", baseURL: URL(string: "http://localhost:5252")!)
        let url = env.url(for: Endpoint(.get, "api/feed"))
        #expect(url?.absoluteString == "http://localhost:5252/api/feed")
    }

    @Test func baseWithPathIsPreserved() {
        let env = APIEnvironment(name: "t", baseURL: URL(string: "https://ishaunted.com/webapi")!)
        let url = env.url(for: Endpoint(.get, "api/feed/posts"))
        #expect(url?.absoluteString == "https://ishaunted.com/webapi/api/feed/posts")
    }

    @Test func baseWithTrailingSlashDoesNotDouble() {
        let env = APIEnvironment(name: "t", baseURL: URL(string: "https://ishaunted.com/webapi/")!)
        let url = env.url(for: Endpoint(.get, "api/feed"))
        #expect(url?.absoluteString == "https://ishaunted.com/webapi/api/feed")
    }

    @Test func rootLevelIdentityEndpoints() {
        let env = APIEnvironment(name: "t", baseURL: URL(string: "https://ishaunted.com/webapi")!)
        let url = env.url(for: Endpoint(.post, "login", requiresAuth: false))
        #expect(url?.absoluteString == "https://ishaunted.com/webapi/login")
    }

    @Test func queryItemsAttach() {
        let env = APIEnvironment.dev
        let url = env.url(for: Endpoint(.get, "api/feed", query: [
            URLQueryItem(name: "mode", value: "foryou"),
            URLQueryItem(name: "cursor", value: "abc=="),
        ]))
        #expect(url?.absoluteString == "http://localhost:5252/api/feed?mode=foryou&cursor=abc%3D%3D")
    }
}

@Suite("Login request encoding — nil 2FA fields must vanish")
struct LoginEncodingTests {

    @Test func nilTwoFactorFieldsAreAbsent() throws {
        let data = try BenJSON.encoder.encode(LoginRequest(email: "a@b.c", password: "p"))
        let json = String(decoding: data, as: UTF8.self)
        #expect(!json.contains("twoFactorCode"))
        #expect(!json.contains("twoFactorRecoveryCode"))
    }

    @Test func providedTwoFactorCodeIsPresent() throws {
        let data = try BenJSON.encoder.encode(
            LoginRequest(email: "a@b.c", password: "p", twoFactorCode: "123456"))
        let json = String(decoding: data, as: UTF8.self)
        #expect(json.contains("\"twoFactorCode\":\"123456\""))
        #expect(!json.contains("twoFactorRecoveryCode"))
    }

    @Test func requiresTwoFactorIsDetectedFromProblemDetails() throws {
        let body = Data(#"{"type":"x","title":"Unauthorized","status":401,"detail":"RequiresTwoFactor"}"#.utf8)
        let problem = try BenJSON.decoder.decode(ProblemDetailsBody.self, from: body)
        #expect(problem.requiresTwoFactor)
    }

    @Test func badPasswordProblemDetailsIsNotTwoFactor() throws {
        let body = Data(#"{"title":"Unauthorized","status":401,"detail":"Failed"}"#.utf8)
        let problem = try BenJSON.decoder.decode(ProblemDetailsBody.self, from: body)
        #expect(!problem.requiresTwoFactor)
    }
}

@Suite("Date decoding — the three shapes C# DateTime arrives in")
struct DateDecodingTests {
    private struct Box: Decodable { let d: Date }

    private func decode(_ raw: String) throws -> Date {
        try BenJSON.decoder.decode(Box.self, from: Data(#"{"d":"\#(raw)"}"#.utf8)).d
    }

    @Test func isoWithFractionalSeconds() throws {
        let date = try decode("2026-08-24T16:54:47.123Z")
        #expect(abs(date.timeIntervalSince1970 - 1787590487.123) < 0.01)
    }

    @Test func isoWithoutFraction() throws {
        let date = try decode("2026-08-24T16:54:47Z")
        #expect(date.timeIntervalSince1970 == 1787590487)
    }

    @Test func nakedUTCDateTimeWithSevenDigits() throws {
        let date = try decode("2026-08-24T16:54:47.1234567")
        #expect(abs(date.timeIntervalSince1970 - 1787590487.1234567) < 0.01)
    }

    @Test func nakedUTCDateTimeWithoutFraction() throws {
        let date = try decode("2026-08-24T16:54:47")
        #expect(date.timeIntervalSince1970 == 1787590487)
    }

    @Test func emptyGuidSentinel() {
        #expect(UUID.emptyGuid.isEmptyGuid)
        #expect(!UUID().isEmptyGuid)
    }
}

@Suite("What a shipped build is allowed to talk to")
struct ShippingEnvironmentTests {

    /// The constant the release build falls back to. If this drifts back to a bare
    /// `https://ishaunted.com`, every call 404s: the API is an IIS application under `/webapi`
    /// and its own routes already begin with `api/`.
    @Test func theLiveEnvironmentCarriesTheWebapiBasePath() {
        #expect(APIEnvironment.production.baseURL.absoluteString == "https://ishaunted.com/webapi")
        let url = APIEnvironment.production.url(for: Endpoint(.get, "api/public/events"))
        #expect(url?.absoluteString == "https://ishaunted.com/webapi/api/public/events")
    }

    /// Addresses only a developer's own machine can answer. A release build must refuse to
    /// honour a saved one — a shipped app pointed at localhost reaches the customer's own
    /// phone, fails every call, and reads as broken rather than as misconfigured.
    @Test(arguments: [
        "http://localhost:5252",
        "https://localhost:5252",
        "http://127.0.0.1:5252",
        "https://192.168.1.50:5252",
        "https://10.0.0.4",
        "https://172.16.0.9",
        "https://172.31.255.254",
        "https://benmac.local",
        "http://ishaunted.com/webapi",   // plain http is not shippable either
    ])
    func unreachableAddressesAreRejected(_ raw: String) {
        let env = APIEnvironment(name: "t", baseURL: URL(string: raw)!)
        #expect(env.isReachableOffDevelopmentMachine == false, "\(raw) should be rejected")
    }

    /// The mirror image: real public hosts must pass, including the 172 addresses that sit
    /// OUTSIDE the private 172.16–172.31 range and would be caught by a sloppier prefix test.
    @Test(arguments: [
        "https://ishaunted.com/webapi",
        "https://www.ishaunted.com/webapi",
        "https://172.32.0.1",
        "https://172.15.0.1",
    ])
    func realHostsArePermitted(_ raw: String) {
        let env = APIEnvironment(name: "t", baseURL: URL(string: raw)!)
        #expect(env.isReachableOffDevelopmentMachine == true, "\(raw) should be permitted")
    }
}
