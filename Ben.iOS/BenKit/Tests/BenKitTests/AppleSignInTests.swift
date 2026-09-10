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
        // No code was held, so none is sent — not an empty one.
        #expect(sent?["authorizationCode"] == nil)
    }

    /// Apple's one-shot authorization code rides along when the sheet gave one, on both doors:
    /// it is what lets the server revoke this person's Apple tokens when they delete their
    /// account (item 229). Absent when nil, never empty.
    @Test func theAuthorizationCodeRidesAlongOnBothDoors() async {
        let transport = MockTransport(status: 401, body: Data("no".utf8))
        let (client, _) = await self.client(transport)

        _ = await client.signIn(identityToken: "a.b.c", authorizationCode: "c-1")
        let signIn = try? JSONSerialization.jsonObject(
            with: transport.requests.last?.httpBody ?? Data()) as? [String: Any]
        #expect(signIn?["authorizationCode"] as? String == "c-1")

        _ = await client.link(identityToken: "a.b.c", email: "a@b.test", password: "pw", authorizationCode: "c-2")
        let link = try? JSONSerialization.jsonObject(
            with: transport.requests.last?.httpBody ?? Data()) as? [String: Any]
        #expect(link?["authorizationCode"] as? String == "c-2")

        _ = await client.link(identityToken: "a.b.c", email: "a@b.test", password: "pw")
        let bare = try? JSONSerialization.jsonObject(
            with: transport.requests.last?.httpBody ?? Data()) as? [String: Any]
        #expect(bare?["authorizationCode"] == nil)
    }

    /// The server may APPEND fields to this body, and a shipped app must keep working.
    ///
    /// `AppleNeedsProfile` is a plain `Decodable` with synthesised keys, so Swift ignores keys it
    /// does not know — but that is a property worth proving rather than assuming, because the
    /// failure mode if it were ever wrong is every Apple sign-up on every installed build breaking
    /// at once, from a server change that looked additive.
    ///
    /// `shouldLinkInstead` and `emailProblem` were added when a taken email address stopped being
    /// reported under the @name field. This app does not act on them yet; item 226 is where it will.
    @Test func aNeedsProfileBodyWithNewerFieldsStillDecodes() async throws {
        let body = """
        {"needsProfile":true,"suggestedDisplayName":"New Person","email":"new@test.com",\
        "isPrivateEmail":false,"handleProblem":null,\
        "shouldLinkInstead":true,"emailProblem":"That email address already has an account here."}
        """

        let decoded = try BenJSON.decoder.decode(
            AppleNeedsProfile.self, from: Data(body.utf8))

        #expect(decoded.needsProfile)
        #expect(decoded.suggestedDisplayName == "New Person")
        #expect(decoded.email == "new@test.com")
        #expect(decoded.handleProblem == nil)
    }

    // MARK: - Claiming an account that already exists

    private var okTokenBody: Data {
        Data(#"{"tokenType":"Bearer","accessToken":"a","expiresIn":3600,"refreshToken":"r"}"#.utf8)
    }

    private func sentFields(_ transport: MockTransport) -> [String: Any] {
        (try? JSONSerialization.jsonObject(
            with: transport.requests.last?.httpBody ?? Data()) as? [String: Any]) ?? [:]
    }

    /// The link call carries the ACCOUNT's address, not Apple's.
    ///
    /// That is the whole point of it. Sign-in only joins an Apple identity to an existing account
    /// when Apple's own VERIFIED address matches one, and a Hide My Email relay never will. Sending
    /// Apple's address here would reproduce exactly the failure this endpoint exists to fix.
    @Test func linkingSendsTheAccountAddressNotApplesAndNoBearer() async {
        let transport = MockTransport(status: 200, body: okTokenBody)
        let (client, _) = await self.client(transport)

        _ = await client.link(identityToken: "apple-token", email: "ben@ishaunted.com", password: "pw")

        let request = transport.requests.last
        #expect(request?.url?.path.hasSuffix("/api/auth/apple/link") == true)
        #expect(request?.value(forHTTPHeaderField: "Authorization") == nil)

        let sent = sentFields(transport)
        #expect(sent["email"] as? String == "ben@ishaunted.com")
        #expect(sent["identityToken"] as? String == "apple-token")
    }

    /// A successful link signs them in outright: an Apple identity token is not a credential the
    /// API accepts on ordinary requests, so there is nothing else to fall back on.
    @Test func aSuccessfulLinkSignsThemIn() async {
        let (client, tokens) = await self.client(MockTransport(status: 200, body: okTokenBody))

        let outcome = await client.link(identityToken: "t", email: "ben@ishaunted.com", password: "pw")

        #expect(outcome == .signedIn)
        #expect(await tokens.isSignedIn)
    }

    /// Being asked for a code is not a failure. The password was right, and reporting it as one
    /// leaves somebody retyping a password that already worked.
    @Test func aTwoFactorAccountIsAskedForACodeRatherThanRefused() async {
        let body = Data(#"{"status":401,"detail":"RequiresTwoFactor"}"#.utf8)
        let (client, tokens) = await self.client(MockTransport(status: 401, body: body))

        let outcome = await client.link(identityToken: "t", email: "ben@ishaunted.com", password: "pw")

        #expect(outcome == .needsTwoFactor)
        #expect(await !tokens.isSignedIn)
    }

    /// No code field is sent until one is asked for. An empty string is an attempt with a WRONG
    /// code, which spends a failure against an account that may have no second factor at all.
    @Test func noCodeFieldIsSentOnTheFirstLinkAttempt() async {
        let transport = MockTransport(status: 200, body: okTokenBody)
        let (client, _) = await self.client(transport)

        _ = await client.link(identityToken: "t", email: "a@b.test", password: "pw")

        let sent = sentFields(transport)
        #expect(sent["twoFactorCode"] == nil)
        #expect(sent["twoFactorRecoveryCode"] == nil)
    }

    /// The code goes in the field matching what they said they typed. Guessing by shape would
    /// spend a single-use recovery code on a mistyped app code.
    @Test func theCodeGoesInTheFieldThatMatchesIt() async {
        let appCode = MockTransport(status: 200, body: okTokenBody)
        let (c1, _) = await self.client(appCode)
        _ = await c1.link(identityToken: "t", email: "a@b.test", password: "pw", twoFactorCode: "123456")
        #expect(sentFields(appCode)["twoFactorCode"] as? String == "123456")
        #expect(sentFields(appCode)["twoFactorRecoveryCode"] == nil)

        let recovery = MockTransport(status: 200, body: okTokenBody)
        let (c2, _) = await self.client(recovery)
        _ = await c2.link(identityToken: "t", email: "a@b.test", password: "pw", recoveryCode: "abcd1234")
        #expect(sentFields(recovery)["twoFactorRecoveryCode"] as? String == "abcd1234")
        #expect(sentFields(recovery)["twoFactorCode"] == nil)
    }

    /// Each refusal is named for what it actually is. Wait, enter your code, confirm your email and
    /// fix your password are four different instructions; three are useless if the fourth is guessed.
    @Test(arguments: [("LockedOut", "locked"),
                      ("NotAllowed", "hasn't been confirmed"),
                      ("Failed", "don't match an account")])
    func eachRefusalIsNamedForWhatItIs(detail: String, fragment: String) async {
        let body = Data(#"{"status":401,"detail":"\#(detail)"}"#.utf8)
        let (client, _) = await self.client(MockTransport(status: 401, body: body))

        let outcome = await client.link(identityToken: "t", email: "a@b.test", password: "pw")

        guard case .failed(let reason) = outcome else {
            Issue.record("expected a refusal for \(detail), got \(outcome)")
            return
        }
        #expect(reason.contains(fragment))
    }

    /// A refusal whose reason cannot be read is UNKNOWN, never "your password is wrong" - that
    /// sends somebody to reset a password that was always right.
    @Test func anUnreadableRefusalDoesNotBlameThePassword() async {
        let body = Data("<html>a proxy page</html>".utf8)
        let (client, _) = await self.client(MockTransport(status: 401, body: body))

        let outcome = await client.link(identityToken: "t", email: "a@b.test", password: "pw")

        guard case .failed(let reason) = outcome else {
            Issue.record("expected a refusal")
            return
        }
        #expect(!reason.contains("don't match an account"))
    }

    /// The server's "that address already has an account" is routed to the link door, not shown
    /// as a handle complaint about a handle that is fine.
    @Test func aTakenAddressIsRoutedToLinkingNotToTheHandleField() async {
        let body = Data("""
        {"needsProfile":true,"suggestedDisplayName":"New Person","email":"ben@ishaunted.com",\
        "isPrivateEmail":false,"handleProblem":null,\
        "shouldLinkInstead":true,"emailProblem":"That email address already has an account here."}
        """.utf8)
        let (client, _) = await self.client(MockTransport(status: 409, body: body))

        let outcome = await client.signIn(identityToken: "t", displayName: "New Person", handle: "np")

        guard case .addressTaken(let reason) = outcome else {
            Issue.record("expected addressTaken, got \(outcome)")
            return
        }
        #expect(reason.contains("already has an account"))
    }
}
