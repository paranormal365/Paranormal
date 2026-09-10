# External sign-in: what is right, what was learned, and the path from here

Written 2026-09-10 at Ben's request, after a day on items 225, 226 and 227. Plan of record for the
rest of `feature/apple-signin-website-227` and for what follows it.

## The right answer, in one paragraph

An external identity is the provider's **subject**, stored as an external login. The address is
decoration. A first-time external sign-in either finds that subject, or has to be told who this is:
by **creating** an account (display name and handle, nothing more), or by **claiming** one that
exists, which is a real sign-in — email, password, second factor, lockout, confirmed account — and
must be offered **always**, because the server can only spot a collision in the one case that was
never broken. Every door that mints a session must ask whether the account may sign in at all. And
those rules must live in **one place**, because today they live in two and the second one had every
bug the first one had.

## Where 227 stands: built, correct, unverified through Apple

Sign in with Apple on the website is on this branch and ships hidden until a Services ID is set.

- No client secret: `response_type=code id_token` returns the identity token in the form post.
- Token crosses from the form-post endpoint to a Blazor page as a single-use two-minute code.
- Create or link, link always offered, forgot-password pointed at for a lost password.
- Domain-verification file served at `/.well-known/apple-developer-domain-association.txt`.
- Verified by running the site both unconfigured and configured, including a simulated Apple form
  post, a forged post, and a cancellation.

**Not verified:** the round trip through Apple. Apple refuses localhost; this needs the https host
and the portal work listed in `README-apple-signin-website-227.md`. The single setting everything
rests on: **the Services ID must be grouped under `com.ishaunted.ios` as its primary App ID**, or
`sub` differs between web and phone and the site sees two people.

## Where 226 stands: server done, iPhone half-drafted

The server half shipped with 225: `POST api/auth/apple/link`, now with lockout, confirmed-account
and second-factor checks.

**Done, later the same day.** `link(...)` in BenKit with 16 tests written against the harness the
file actually has; `AppleProfileSheet` gained the second door, offered always, with the create form
put away when the server says the address is taken and a code field that appears only when asked
for. The footer's pinned promise — "rather than making a second one" — is now true for a mismatched
address too, so the sentence changed and the UI test did not. Verified on both the iPhone 17 Pro and
iPad Pro 13-inch simulators. Not verifiable here: the door against a real Apple identity.

## What this session found, in order of consequence

1. **Linking bypassed the second factor** — first in the Apple link I wrote (caught by Ben's
   question), then found pre-existing in Entra's. Permanent, not momentary: once linked, the
   external identity signs in on its own and the code is never asked for again.
2. **Entra's link had no rate limit and no lockout.** 600 guesses a minute, none counted, from anyone
   with a free Microsoft account.
3. **Neither external door honoured an administrator's refusal.** Apple minted a session with the
   unconditional `SignInAsync`; the Entra claims transformation resolved a closed or locked account
   and attached its roles. Our own `RecordingSignInManager` comment asserted the opposite.
4. **Duplicate accounts** whenever the provider's address did not match — Hide My Email, or simply a
   different address. Fixed on website and desktop; iPhone is 226.
5. **An address collision was reported under the @name field** with Identity's raw wording.
6. **Apple's `is_private_email` was thrown away.** Now `AppUser.EmailKind`, with a migration.
7. **Two response shapes for the same refusal.** I hardened Entra's link but kept its `{message}`
   body while Apple's uses `/login`'s problem-detail. Two clients now map the same concept twice.
   I created this divergence today.

## Decided with Ben and built (Phase C, 2026-09-10)

- **Entra's email fallback is gone.** It auto-linked an unlinked object id to whatever account
  held its `email` / `preferred_username` / `upn` claim, with no proof of ownership; Microsoft does
  not vouch for a personal account's `email`, and this authority accepts personal accounts.
  Removed, not narrowed: a rotated object id uses the link door once, with a password and second
  factor. Production holds one Microsoft login and it is linked by object id, so nobody is
  stranded. Pinned by `AnUnlinkedIdentityIsNotResolvedByItsAddressClaim` for all three claims.
- **Only a verified address is confirmed at creation.** `ExternalSignInService.RegisterAsync` sets
  `EmailConfirmed = withheld || EmailVerified`: an unverified real address (Microsoft's always;
  Apple's rarely) starts unconfirmed and is sent the same confirmation a website sign-up gets,
  through `IConfirmationSender` (implemented by `AccountCreationService`). A withheld Apple address
  keeps its placeholder and stays usable — its account has nothing to confirm and no way to.
- **The external gate is closure and lockout, not `CanSignInAsync`.** That method folds in the
  confirmed-account rule, which would refuse the very account just created. Confirmation proves
  the address; the provider's token proves the person. What confirmation still gates is what an
  address is for: a password reset (`/forgotPassword` already refuses an unconfirmed address) and
  being written to (`MeResponse.CanBeEmailed` is now false until confirmed). The Entra claims
  transformation asks the same `MaySignInAsync`, so the two Microsoft doors cannot disagree.
- **The UI path.** `MyProfile` shows the unconfirmed address with a **Send the link again**
  button; `ActionNeededBanners` carries a once-per-sign-in reminder pointing there; help pages
  `your-profile` and `getting-started` say why a reset link may not arrive. `MeResponse` and
  `MyProfileRecord` gained `EmailConfirmed` (default true, so an older server reads as before).

## Still open — decisions, not oversights

- **Withheld-address Apple accounts have no recovery path.** Apple is their only key. The profile
  now says so and offers to add an address; nothing forces it.
- **Relay mail is dropped** until `ishaunted.com` is registered under Apple's email-communication
  configuration. Not code.
- **Field Kit ownership** is the bearer token's user id, never the provider. Ben's concern is
  really the duplicate-account one: uploads made from a duplicate land on the duplicate.

## The structural lesson, and the correction

Every finding above was found twice, once per provider, because each provider is a separate door
with its own hand-rolled checks. A third door — Android, or another provider — would inherit none
of today's fixes. **The correction is one server-side `ExternalSignInService`** that both
controllers call and that a future provider would call too:

```
Resolve(provider, subject)      → found → MaySignIn(lockout, closed, confirmed) → issue tokens
                                → not found → address collision? → ShouldLink : NeedsProfile
Link(provider, subject, email, password, code) → FULL sign-in gate → attach → issue tokens
Register(provider, subject, address, kind, name, handle) → collision → create → attach → issue tokens
```

One refusal vocabulary (`/login`'s problem-detail: `Failed`, `NotAllowed`, `LockedOut`,
`RequiresTwoFactor`), so `LoginFailureMapping` serves every door on every client. **Done:** the
controller tests were the regression net and held. Two Entra tests asserted the old result *type*
on the link path and were changed to assert the unified shape, which is what they were for.

Where the Phase C decisions now live: `EntraAuthController` passes its address claim to the
service as `EmailVerified: false`, so the service's one rule — a verified address auto-links, an
unverified one routes to the link door — already governs Microsoft. Deciding to trust a Microsoft
claim (work accounts via `upn`, or `xms_edov`) is a one-line change in one place.

Its natural companion was to be **`api/auth/entra/token`**, exchanging a validated Microsoft token
for our own session so that one session type served everywhere. The desktop app — the client that
needed that most — was shelved on 2026-09-10, so this is now optional: the website's Entra bridge
works as it always has. Worth doing only if a second native client appears.

## The path

| Phase | Work | Verified by |
| --- | --- | --- |
| A | **Done.** 226 on the iPhone: BenKit `link()`, the sheet's second door, committed | 339 BenKit tests; Apple UI regression green on iPhone 17 Pro **and** iPad Pro 13-inch |
| B | **Done.** `ExternalSignInService` (`ba74afca`, `81602335`); both controllers thin; one refusal shape; `EntraLinkRefusal` deleted; both clients read `/login`'s problem-detail through the one `LoginFailureMapping` | 20-case service matrix; all 45 Apple + 26 Entra controller tests unchanged in intent; suite 7,954 green |
| C | **Done.** Entra email fallback removed; unverified addresses start unconfirmed and are sent a confirmation; external gate is closure + lockout; profile notice, banner and help text. (`api/auth/entra/token` dropped out with the desktop app.) | 3 transformation cases + 5 service cases + 1 rewritten Entra controller case, each seen failing first; suite 7,866 green |
| D | Portal work and UAT: Services ID grouped under the iOS App ID, domain file, `Apple:ClientIds`, email-communication registration; Entra redirect URI | A real Apple round trip on UAT; a real Microsoft one |
| E | **Merged** to develop `b0d4f9f1` and pushed; `EmailKind` migration applied to the testing copy `IsHauntedDb_player` with an explicit `--connection`. **Production (`IsHauntedDb`) still lacks exactly one migration, `20260910143508_AppUserEmailKind`** — additive (an int column defaulting to 0, so the deployed build keeps working) — to be applied only by an explicit `--connection` naming `IsHauntedDb`, when Ben says so | Suite green on the merged tip; `migrations list` against production read-only |

Phase A before B because it is small and closes the last live duplicate-account path. B before C
because the Entra decisions should be made once, inside the shared service, not patched into the
old controller a third time.

## What I got wrong today, so it is not repeated

- Shipped a two-factor bypass in new security code and only caught it when asked. Rule: **any
  endpoint that takes a password and grants anything runs the full sign-in gate.**
  `CheckPasswordSignInAsync` is not that gate — it never returns `RequiresTwoFactor`.
- Named the wrong database as production. The correct fact was in my own notes, below a stale
  summary line. Rule: check the deploy script, not the settings file.
- A concurrency test passed with its guard deleted, because the stub slept on the thread.
- Two scripted edits wrote wrong code (`EmailKindKind`; a `None` that silently wrote nothing).
- `git mv` staged a phase into an unrelated commit.
- Wrote iOS tests against a harness I had not read.
- Left two doors answering the same refusal in two shapes.
