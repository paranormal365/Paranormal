import Foundation
import Testing
@testable import BenKit

@Suite("LoadResult mapping — the WebApiClient.cs contract")
struct ResponseMappingTests {

    // MARK: Prose extraction (SendListAsync's looksLikeProse, byte for byte)

    @Test func proseSentenceSurvives() {
        #expect(ResponseMapping.prose(fromBody: "\"You are not a member of this group.\"\n")
                == "You are not a member of this group.")
    }

    @Test func problemDetailsBlobIsDropped() {
        #expect(ResponseMapping.prose(fromBody: #"{"title":"Unauthorized","status":401}"#) == nil)
    }

    @Test func htmlErrorPageIsDropped() {
        #expect(ResponseMapping.prose(fromBody: "<html><body>502 Bad Gateway</body></html>") == nil)
    }

    @Test func leadingWhitespaceBeforeBraceStillDropped() {
        #expect(ResponseMapping.prose(fromBody: "   {\"title\":\"x\"}") == nil)
    }

    @Test func longBodiesAreDropped() {
        #expect(ResponseMapping.prose(fromBody: String(repeating: "a", count: 400)) == nil)
        #expect(ResponseMapping.prose(fromBody: String(repeating: "a", count: 399)) != nil)
    }

    @Test func blankIsDropped() {
        #expect(ResponseMapping.prose(fromBody: "   \n") == nil)
        #expect(ResponseMapping.prose(fromBody: nil) == nil)
    }

    // MARK: Status mapping

    @Test func status401IsSessionEndedNotFailure() {
        let result: LoadResult<[String]> = ResponseMapping.failure(
            statusCode: 401, data: Data(), headers: [:])
        #expect(result == .sessionEnded)
    }

    @Test func status403IsFailureNotSessionEnded() {
        let result: LoadResult<[String]> = ResponseMapping.failure(
            statusCode: 403, data: Data("You may not see this.".utf8), headers: [:])
        #expect(result == .failed(reason: "You may not see this.", statusCode: 403))
    }

    @Test func status403WithProblemDetailsFallsBackToStatusText() {
        let result: LoadResult<[String]> = ResponseMapping.failure(
            statusCode: 403, data: Data(#"{"title":"Forbidden"}"#.utf8), headers: [:])
        guard case .failed(let reason, _) = result else {
            Issue.record("expected .failed"); return
        }
        #expect(reason?.contains("403") == true)
    }

    @Test func status429WithDeltaSeconds() {
        let result: LoadResult<[String]> = ResponseMapping.failure(
            statusCode: 429, data: Data(), headers: ["Retry-After": "42"])
        #expect(result == .rateLimited(retryAfter: 42))
    }

    @Test func status429WithHttpDate() {
        let result: LoadResult<[String]> = ResponseMapping.failure(
            statusCode: 429, data: Data(), headers: ["Retry-After": "Wed, 21 Oct 2015 07:28:00 GMT"])
        // A past date clamps to zero rather than going negative.
        #expect(result == .rateLimited(retryAfter: 0))
    }

    // MARK: Decode

    @Test func successWithListDecodes() {
        let result = ResponseMapping.decode(
            [String].self, statusCode: 200, data: Data(#"["a","b"]"#.utf8), headers: [:])
        #expect(result == .ok(["a", "b"]))
    }

    @Test func successWithEmptyListIsOkNotFailed() {
        let result = ResponseMapping.decode(
            [String].self, statusCode: 200, data: Data("[]".utf8), headers: [:])
        #expect(result == .ok([]))
    }

    @Test func emptyBodySuccessIsOkForEmptyBodyType() {
        let result = ResponseMapping.decode(
            EmptyBody.self, statusCode: 204, data: Data(), headers: [:])
        #expect(result == .ok(EmptyBody()))
    }

    @Test func emptyBodyWhenValueExpectedIsFailureNotCrash() {
        let result = ResponseMapping.decode(
            [String].self, statusCode: 204, data: Data(), headers: [:])
        guard case .failed = result else {
            Issue.record("expected .failed, got \(result)"); return
        }
    }

    @Test func undecodableSuccessBodyIsFailure() {
        let result = ResponseMapping.decode(
            [String].self, statusCode: 200, data: Data("not json".utf8), headers: [:])
        guard case .failed = result else {
            Issue.record("expected .failed"); return
        }
    }
}
