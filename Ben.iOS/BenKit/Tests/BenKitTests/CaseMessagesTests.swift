import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

@Suite("Case messages — the client's side of the conversation")
struct CaseMessagesTests {

    /// Captured verbatim from `GET api/my-cases/{id}/messages` on the dev API.
    private static let realList = """
    [
      {"id":"dd1f5994-0ce3-4c3c-b59b-b1108a764a02",
       "caseId":"a2e42fac-f3ac-4277-9066-706a2155b821",
       "authorAppUserId":"c6b2c5d6-c79e-4b27-7d21-08dee981e41b",
       "authorDisplayName":"Daniel Park",
       "body":"Test message from end-to-end validation pass.",
       "senderSide":0,"isReadByClient":true,"isReadByOrg":false,
       "dateCreated":"2026-08-14T13:04:18.196988"}
    ]
    """

    private func store(_ transport: MockTransport) async -> CaseMessagesStore {
        let tokens = TokenSession(
            storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return await CaseMessagesStore(
            caseId: UUID(), api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    @Test func theRealListShapeDecodes() async {
        let store = await self.store(MockTransport(status: 200, body: Data(Self.realList.utf8)))
        await store.load()

        #expect(await store.state == .loaded)
        #expect(await store.messages.count == 1)
        #expect(await store.messages.first?.authorDisplayName == "Daniel Park")
        // senderSide 0 is the CLIENT — this one is theirs.
        #expect(await store.messages.first?.isMine == true)
    }

    @Test func aConversationReadsOldestFirst() async {
        let body = Data("""
        [
          {"id":"\(UUID().uuidString)","caseId":"\(UUID().uuidString)",
           "authorAppUserId":"\(UUID().uuidString)","authorDisplayName":"Later","body":"second",
           "senderSide":1,"isReadByClient":false,"isReadByOrg":true,
           "dateCreated":"2026-08-20T10:00:00"},
          {"id":"\(UUID().uuidString)","caseId":"\(UUID().uuidString)",
           "authorAppUserId":"\(UUID().uuidString)","authorDisplayName":"Earlier","body":"first",
           "senderSide":0,"isReadByClient":true,"isReadByOrg":false,
           "dateCreated":"2026-08-19T10:00:00"}
        ]
        """.utf8)
        let store = await self.store(MockTransport(status: 200, body: body))
        await store.load()
        #expect(await store.messages.map(\.body) == ["first", "second"])
    }

    @Test func anUnknownSideIsShownAsTheGroupNotAsYou() async {
        // Putting somebody else's words on the reader's own side of the screen is the worse
        // failure of the two, so an enum this build doesn't know defaults away from "mine".
        let body = Data("""
        [{"id":"\(UUID().uuidString)","caseId":"\(UUID().uuidString)",
          "authorAppUserId":"\(UUID().uuidString)","authorDisplayName":"Someone","body":"x",
          "senderSide":99,"isReadByClient":false,"isReadByOrg":false,
          "dateCreated":"2026-08-19T10:00:00"}]
        """.utf8)
        let store = await self.store(MockTransport(status: 200, body: body))
        await store.load()
        #expect(await store.messages.first?.isMine == false)
    }

    @Test func aRefusalIsNotAnEmptyConversation() async {
        let store = await self.store(MockTransport(
            status: 403, body: Data("This case isn't yours.".utf8)))
        await store.load()
        #expect(await store.state == .failed(reason: "This case isn't yours."))
        #expect(await store.messages.isEmpty)
    }

    @Test func sendingAppendsRatherThanRefetching() async {
        let sent = UUID()
        let transport = MockTransport { request in
            if request.httpMethod == "POST" {
                let json = """
                {"id":"\(sent.uuidString.lowercased())","caseId":"\(UUID().uuidString)",
                 "authorAppUserId":"\(UUID().uuidString)","authorDisplayName":"Me",
                 "body":"Any news?","senderSide":0,"isReadByClient":true,"isReadByOrg":false,
                 "dateCreated":"2026-08-24T12:00:00"}
                """
                return (Data(json.utf8), MockTransport.response(for: request, status: 200))
            }
            return (Data(Self.realList.utf8), MockTransport.response(for: request, status: 200))
        }
        let store = await self.store(transport)
        await store.load()

        let result = await store.send("  Any news?  ")
        guard case .success = result else { Issue.record("expected success"); return }

        #expect(await store.messages.count == 2)
        #expect(await store.messages.last?.id == sent)
        // Two calls, not three: no reload, which would scroll the conversation out from under
        // somebody mid-sentence.
        #expect(transport.requests.count == 2)

        let body = try? JSONSerialization.jsonObject(
            with: transport.requests[1].httpBody ?? Data()) as? [String: Any]
        #expect(body?["body"] as? String == "Any news?")   // trimmed
    }

    @Test func anEmptyMessageIsRefusedWithoutTouchingTheServer() async {
        let transport = MockTransport(status: 200, body: Data("[]".utf8))
        let store = await self.store(transport)

        guard case .failure = await store.send("   \n ") else {
            Issue.record("blank messages should not be sent"); return
        }
        #expect(transport.requests.isEmpty)
    }

    @Test func aRefusedSendKeepsTheServersSentence() async {
        let store = await self.store(MockTransport(
            status: 400, body: Data("Message body is required.".utf8)))
        guard case .failure(let error) = await store.send("hello") else {
            Issue.record("expected refusal"); return
        }
        #expect(error.message == "Message body is required.")
    }
}
