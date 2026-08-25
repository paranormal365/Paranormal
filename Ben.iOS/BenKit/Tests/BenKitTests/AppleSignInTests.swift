import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

@Suite("Sign in with Apple")
struct AppleSignInTests {

    private func client(_ transport: MockTransport) async -> (AppleSignInClient, TokenSession) {
        let tokens = TokenSession(
            storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        let api = APIClient(environment: { .dev }, transport: transport, tokens: tokens)
        return (AppleSignInClient(api: api, tokens: tokens), tokens)
    }

    @Test func aSuccessfulSignInAdoptsTheTokensLikeAnyOtherLogin() async {
        // The server deliberately answers with the SAME body /login writes, so nothing about
        // Apple is special once the token is in hand.
        let body = Data("""
        {"tokenType":"Bearer","accessToken":"apple-access","expiresIn":3600,
         "refreshToken":"apple-refresh"}
        """.utf8)
        let (client, tokens) = await self.client(MockTransport(status: 200, body: body))

        let outcome = await client.signIn(identityToken: "a.b.c")

        #expect(outcome == .signedIn)
        #expect(await tokens.isSignedIn)
    }

    @Test func aNewIdentityIsSentToCollectANameAndHandle() async {
        // This literal is pinned on the SERVER side by
        // AppleAuthControllerTests.TheNeedsProfileBodyIsExactlyWhatTheAppDecodes — the two
        // languages have nothing else holding them together.
        let body = Data("""
        {"needsProfile":true,"suggestedDisplayName":"New Person","email":"new@test.com",\
        "isPrivateEmail":false,"handleProblem":null}
        """.utf8)
        let (client, tokens) = await self.client(MockTransport(status: 409, body: body))

        let outcome = await client.signIn(identityToken: "a.b.c")

        #expect(outcome == .needsProfile(
            suggestedName: "New Person", email: "new@test.com", handleProblem: nil))
        // Nothing was adopted: there is no session yet, and pretending otherwise would show a
        // signed-in shell over an account that does not exist.
        #expect(await tokens.isSignedIn == false)
    }

    @Test func aHideMyEmailAddressIsNotOfferedBackAsTheirEmail() async {
        // It works, but it is a relay Apple invented — showing it as "your email" in a form
        // invites somebody to correct it, or to believe they will read mail there.
        let body = Data("""
        {"needsProfile":true,"suggestedDisplayName":null,"email":"abc123@privaterelay.appleid.com",\
        "isPrivateEmail":true,"handleProblem":null}
        """.utf8)
        let (client, _) = await self.client(MockTransport(status: 409, body: body))

        guard case .needsProfile(_, let email, _) = await client.signIn(identityToken: "a.b.c") else {
            Issue.record("expected needsProfile"); return
        }
        #expect(email == nil)
    }

    @Test func aTakenHandleComesBackAsTheServersOwnSentence() async {
        let body = Data("""
        {"needsProfile":true,"suggestedDisplayName":"New Person","email":null,\
        "isPrivateEmail":false,"handleProblem":"That name was taken a moment ago. Try another."}
        """.utf8)
        let (client, _) = await self.client(MockTransport(status: 409, body: body))

        guard case .needsProfile(_, _, let problem) = await client.signIn(
            identityToken: "a.b.c", displayName: "New Person", handle: "taken")
        else { Issue.record("expected needsProfile"); return }
        #expect(problem == "That name was taken a moment ago. Try another.")
    }

    @Test func aRefusalKeepsTheServersSentenceRatherThanAStatusCode() async {
        // Captured from the running dev API: POST api/auth/apple with a junk token.
        let (client, _) = await self.client(MockTransport(
            status: 401, body: Data("That Apple sign-in couldn't be verified. Try again.".utf8)))

        #expect(await client.signIn(identityToken: "junk")
                == .failed(reason: "That Apple sign-in couldn't be verified. Try again."))
    }

    @Test func anUnconfiguredServerSaysSoInsteadOfBlamingTheSignIn() async {
        let (client, _) = await self.client(MockTransport(
            status: 503, body: Data("Signing in with Apple isn't set up on this server yet.".utf8)))

        #expect(await client.signIn(identityToken: "a.b.c")
                == .failed(reason: "Signing in with Apple isn't set up on this server yet."))
    }

    @Test func theRequestCarriesTheTokenAndGoesToTheRightDoorWithoutABearer() async {
        let transport = MockTransport(status: 401, body: Data("no".utf8))
        let (client, _) = await self.client(transport)
        _ = await client.signIn(identityToken: "a.b.c", displayName: "N", handle: "h")

        let request = transport.requests.first
        #expect(request?.url?.path.hasSuffix("/api/auth/apple") == true)
        // Signing IN cannot require being signed in.
        #expect(request?.value(forHTTPHeaderField: "Authorization") == nil)

        let sent = try? JSONSerialization.jsonObject(
            with: request?.httpBody ?? Data()) as? [String: Any]
        #expect(sent?["identityToken"] as? String == "a.b.c")
        #expect(sent?["handle"] as? String == "h")
    }
}
