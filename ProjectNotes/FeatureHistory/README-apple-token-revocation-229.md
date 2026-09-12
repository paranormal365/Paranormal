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
| A | **Done.** `AppleSigningOptions` (`Apple:TeamId/KeyId/PrivateKeyPath` on the API, path never key), `AppleClientSecret` (ten-minute ES256 JWT, raw r‖s), `IAppleTokenClient` (`ExchangeCodeAsync`, `RevokeAsync`; never throws; `invalid_client` logged as the configuration mistake it is) | 5 secret tests, 8 client tests with a stub handler; **the live opt-in test posted a bogus code with the real key and Apple answered `invalid_grant`** — it accepted the secret and refused only the code |
| B | **Done.** `AppleCredential` (one per person per client, refresh token data-protected under `Ben.Apple.RefreshToken.v1`) + migration `20260910174206_AppleCredentials`; `AppleCredentialService.RememberAsync` (best-effort exchange and upsert); `AppleIdentity` carries the token's audience; both requests take an optional `authorizationCode`; `IssueTokenAsync` remembers before it signs in | 6 service tests; controller: the code is exchanged for the token's own audience after success, a failing exchange still signs in, no code means no exchange, a refused sign-in exchanges nothing |
| C | **Done.** `RevokeAllAsync` before the anonymising transaction in `AccountClosureService.CloseAsync` and `AppUserPurge`; accepted tokens deleted, refused ones kept with `DateRevocationFailed` stamped | Closure tests: revoke called with the unprotected token and its client id, row gone; a refused revoke still closes and keeps the stamped row |
| D | **Done.** BenKit `signIn`/`link` take `authorizationCode` (omitted when nil); the app keeps `credential.authorizationCode` from the sheet and sends it on both doors; the client library's contracts, `AppleSignInClient`, `ReadCallback` and the website's hand-off carry Apple's `code` to the API | BenKit 17 tests (the new one checks both doors and the absent case); client library 55; hand-off and handshake tests; the app compiles for the simulator |
| E | **Done.** Merged to develop `d14f6f58` and master `429f6685`, pushed; `AppleCredentials` applied to the testing copy and, on Ben's say-so, to production `IsHauntedDb` by an explicit `--connection` — the listing afterwards shows nothing pending. **Still to do at deploy time:** `Apple:TeamId`, `Apple:KeyId`, `Apple:PrivateKeyPath` and the `.p8` on the server; until then production exchanges no codes and revokes nothing, and logs nothing about it | Suite 7,898 green; `migrations list` before and after on production |

## Deliberately unchanged

- Sign-in itself still proves identity with the identity token alone. The code is optional on
  the wire, so a client that predates this keeps working; it simply leaves nothing to revoke.
- The Microsoft flow. Microsoft has no equivalent requirement.

## Not verifiable here

A real exchange needs a real authorization code, which only a real Apple sign-in produces; the
live test proves the secret and the endpoint, the unit tests prove the plumbing. The first real
round trip is on UAT, with the item 227 portal work.
