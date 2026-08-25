import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Getting an account and looking after it (iOS Slice 8).
@Suite("AccountActions — sign-up, confirmation, two-step")
@MainActor
struct AccountActionsTests {

    private static func actions(_ transport: MockTransport) -> AccountActions {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return AccountActions(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    @Test func registrationIsAnonymousAndSendsEveryField() async {
        let transport = MockTransport(status: 200, body: Data(
            #"{"succeeded":true,"message":"Check your email.","field":null}"#.utf8))
        let result = await Self.actions(transport).register(RegisterRequest(
            email: "a@b.c", password: "pw", displayName: "A Tester", handle: "atester"))

        guard case .success(let response) = result else { Issue.record("expected success"); return }
        #expect(response.succeeded)

        let request = transport.requests.first
        // Signing up cannot require a token — that would be a chicken and an egg.
        #expect(request?.value(forHTTPHeaderField: "Authorization") == nil)
        let body = String(decoding: request?.httpBody ?? Data(), as: UTF8.self)
        #expect(body.contains("\"email\":\"a@b.c\""))
        #expect(body.contains("\"handle\":\"atester\""))
        #expect(body.contains("\"displayName\":\"A Tester\""))
    }

    @Test func aRefusedSignUpKeepsTheServersSentence() async {
        // The server names the field AND writes the sentence; the sentence is what a person
        // reads, and "that name is taken" sends them somewhere "invalid input" does not.
        let transport = MockTransport(status: 400, body: Data(
            #"{"succeeded":false,"message":"That name is already taken.","field":"Handle"}"#.utf8))
        let result = await Self.actions(transport).register(RegisterRequest(
            email: "a@b.c", password: "pw", displayName: "A", handle: "taken"))

        guard case .failure(let error) = result else { Issue.record("expected refusal"); return }
        #expect(error.message == "That name is already taken.")
    }

    @Test func handleAvailabilityAnswersCantTellRatherThanGuessing() async {
        // A failed check must not read as "available" — the server re-checks at submit, and
        // a wrong yes here would send somebody through the whole form for nothing.
        #expect(await Self.actions(MockTransport(status: 500)).handleAvailability("x") == nil)
        // An empty handle isn't worth a round trip.
        #expect(await Self.actions(MockTransport(status: 200)).handleAvailability("   ") == nil)
    }

    @Test func handleAvailabilityCarriesTheReasonItWasRefused() async {
        let transport = MockTransport(status: 200, body: Data(
            #"{"handle":"ben","available":false,"reason":"That name is reserved."}"#.utf8))
        let answer = await Self.actions(transport).handleAvailability("ben")
        #expect(answer?.available == false)
        #expect(answer?.reason == "That name is reserved.")
    }

    @Test func aSpentConfirmationLinkIs200WithSucceededFalse() async {
        // The endpoint answers 200 for a bad or already-used link, so the BODY is the
        // answer. Reading the status alone would report a dead link as a success.
        let transport = MockTransport(status: 200, body: Data(
            #"{"succeeded":false,"message":"That link has expired or has already been used."}"#.utf8))
        let response = await Self.actions(transport).confirmEmail(userId: UUID(), code: "abc")
        #expect(response?.succeeded == false)
        #expect(response?.message.contains("expired") == true)
    }

    @Test func twoFactorCodesAreNormalisedBeforeSending() async {
        // People read a code off a screen in groups; the server wants the digits alone.
        #expect(AccountActions.normalizeCode("123 456") == "123456")
        #expect(AccountActions.normalizeCode("1234-5678") == "12345678")

        let transport = MockTransport(status: 200, body: Data(
            #"{"recoveryCodes":["aaaa-bbbb","cccc-dddd"]}"#.utf8))
        let result = await Self.actions(transport).enableTwoFactor(code: "123 456")

        guard case .success(let enabled) = result else { Issue.record("expected success"); return }
        #expect(enabled.recoveryCodes.count == 2)
        let body = String(decoding: transport.requests.first?.httpBody ?? Data(), as: UTF8.self)
        #expect(body.contains("\"code\":\"123456\""))
    }

    @Test func aWrongTwoFactorCodeKeepsItsSentence() async {
        let transport = MockTransport(status: 400, body: Data("That code didn't work.".utf8))
        let result = await Self.actions(transport).enableTwoFactor(code: "000000")
        guard case .failure(let error) = result else { Issue.record("expected refusal"); return }
        #expect(error.message == "That code didn't work.")
    }

    @Test func changingPasswordCarriesBothHalves() async {
        let transport = MockTransport(status: 204)
        let result = await Self.actions(transport).changePassword(current: "old", new: "new")
        guard case .success = result else { Issue.record("expected success"); return }

        let body = String(decoding: transport.requests.first?.httpBody ?? Data(), as: UTF8.self)
        #expect(body.contains("\"currentPassword\":\"old\""))
        #expect(body.contains("\"newPassword\":\"new\""))
    }

    @Test func aRejectedPasswordChangeSaysWhy() async {
        // Identity's own wording — "Passwords must have at least one digit" is actionable;
        // "Save failed" is not.
        let transport = MockTransport(status: 400, body: Data(
            "Passwords must have at least one digit ('0'-'9').".utf8))
        let result = await Self.actions(transport).changePassword(current: "old", new: "weak")
        guard case .failure(let error) = result else { Issue.record("expected refusal"); return }
        #expect(error.message.contains("at least one digit"))
    }
}
