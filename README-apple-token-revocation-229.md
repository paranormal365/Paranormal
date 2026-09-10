# Item 229: revoke a person's Apple tokens when their account is deleted

Branched from `develop` at `f828b049` (2026-09-10). Ben, after items 227 and 228: *"lets continue
with Signin with Apple or Maps work."* This is the Sign in with Apple piece his new key exists for.

## Why

App Review guideline 5.1.1(v): an app that offers Sign in with Apple and lets people delete their
account must **revoke their Sign in with Apple tokens** as part of that deletion. Ours deletes the
account (item: account deletion, anonymise-and-lock) and drops the external login row, so nobody
can get back in — but Apple is never told, and the person's Apple ID still lists IsHaunted as an
app they use. Review checks for the revocation call.

## How Apple's revocation works, and what it forces on us

Revocation takes a **refresh token** (or access token) obtained by exchanging the
**authorization code** Apple hands over at sign-in. Neither of our flows ever exchanged that
code: the iPhone app sends only the identity token, and the web flow asked for `id_token` in the
form post precisely to avoid needing a client secret. So revocation needs three new things:

1. **A client secret**: an ES256 JWT signed with the Sign in with Apple key (`5VY456C8RR`,
   the `.p8` at `~/.ishaunted/AuthKey_5VY456C8RR.p8`), claims `iss` = team, `sub` = the client
   id the code was minted for, `aud` = `https://appleid.apple.com`. Same hand-rolled signer shape
   as item 228's MapKit token; the API gets its own copy because it is a different project.
2. **The code, exchanged at sign-in**: the iPhone app and the website both already receive the
   authorization code and both throw it away. They send it alongside the identity token; the
   API exchanges it at `https://appleid.apple.com/auth/token` and keeps the **refresh token**,
   data-protected, on a new `AppleCredentials` row (user, client id, Apple subject). Best-effort:
   a failed exchange is logged and never fails a sign-in — the token is the proof, the code is
   for later.
3. **Revocation on closure**: `AccountClosureService.CloseAsync` (owner) and `AppUserPurge`
   (SuperAdmin) both revoke every stored refresh token at `https://appleid.apple.com/auth/revoke`
   before the login rows go. A failed revoke is logged at Error and does not stop the closure —
   a deletion that cannot complete because Apple is down is worse for the person than one Apple
   hears about late — and the row is kept so a later sweep can retry.

The client id is whichever audience the code was minted for: `com.ishaunted.ios` from the app,
the Services ID from the website. Stored with the credential, because the secret must name it.

## Phases

| Phase | Work | Verified by |
| --- | --- | --- |
| A | `AppleSigningOptions` (`Apple:TeamId/KeyId/PrivateKeyPath` on the API, path never key), `AppleClientSecret` (the JWT), `IAppleTokenClient` (`ExchangeCodeAsync`, `RevokeAsync`; never throws) | Unit tests on the secret and the client with a stub handler; **a live opt-in test posts a bogus code with a real secret and expects `invalid_grant`, not `invalid_client`** — proof Apple accepts the signature |
| B | `AppleCredential` entity + migration; `AppleCredentialStore` (data-protected refresh token); `AppleSignInRequest`/`AppleLinkRequest` gain an optional `authorizationCode`; the controller stores after any success, best-effort | Controller tests: code stored on sign-in, register and link; a failing exchange still signs in; no code, no call |
| C | Revocation on closure and on purge; the credential rows go with the login rows | Closure tests: revoke called per credential with the right client id; a failed revoke still closes; nothing to revoke, nothing called |
| D | Clients: BenKit sends `authorizationCode` from the credential (`signIn` and `link`); the client library's contracts and `AppleSignInClient` carry it; the website's callback keeps Apple's `code` through the hand-off | BenKit tests on the wire body; client library tests; website handoff test |
| E | Docs: this README, backlog item 229, the App Store notes; migration to the testing copy, and to production with an explicit `--connection` naming `IsHauntedDb` when Ben says | Suite green; `migrations list` on both |

## Deliberately unchanged

- Sign-in itself still proves identity with the identity token alone. The code is optional on
  the wire, so a client that predates this keeps working; it simply leaves nothing to revoke.
- The Microsoft flow. Microsoft has no equivalent requirement.

## Not verifiable here

A real exchange needs a real authorization code, which only a real Apple sign-in produces; the
live test proves the secret and the endpoint, the unit tests prove the plumbing. The first real
round trip is on UAT, with the item 227 portal work.
