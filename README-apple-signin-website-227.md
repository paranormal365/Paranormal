# Feature: Sign in with Apple on the website, and joining accounts up (item 227)

Branched from `develop` at `bb7a3701` (2026-09-10).

> "when we add the fixes for the gaps in the WebApp, I would like to add a 'Sign in with Apple'
> button which they can use to create a new account or link an existing account."

Follows item 225, which built the same two doors for the desktop client and added the server half
both front ends need.

**Plan of record for the rest of this branch and what follows it:**
`ProjectNotes/External-SignIn-Plan-2026-09-10.md` — what the right answer is, what the day found in
order of consequence, what is still open for Ben's decision, and the phased path: finish 226 on the
iPhone, then consolidate both providers into one `ExternalSignInService`, then the Entra decisions,
then UAT through the real providers.

**Item 226 is built on this branch too** (the iPhone's link door), verified on both simulators; see
the plan's Phase A. Only the Apple round trip itself remains unverifiable without a real Apple ID.

**Phase B is done too:** every external door's decisions live once in `ExternalSignInService`, both
controllers are thin over it, and a refused link answers in the same shape from Apple and from
Microsoft. Phase C — the Microsoft decisions — is waiting on Ben.

## Why this closes the iPhone gap without touching the iPhone app

The Apple sign-in endpoint has three outcomes, and the first is **this Apple identity is already
known, sign straight in**. That path needs no client change at all.

So an Apple ID linked *anywhere*, once, makes the iPhone work unchanged. No release, no App Review.
Building the button here is therefore the cheapest way to close item 226 as well, and it is why
this is being done before the iOS work rather than after it.

What it does **not** fix is a duplicate account somebody already has. See the open question below.

## The web flow is not the native flow

This is the part most likely to cost a day if it is assumed rather than checked.

1. **A Services ID, not a bundle id.** Apple's web sign-in identifies the caller by a Services ID
   configured against the App ID, and that value becomes the token's audience. It has to be added
   to `Apple:ClientIds` in `Ben.Data.WebApi/appsettings.json`, which already carries
   `com.ishaunted.ios` and `com.ishaunted.desktop`. Neither of those will do.

2. **No native sheet.** The browser is redirected to Apple, and Apple answers with a `form_post` to
   a registered `https` URL. That needs a real endpoint on `Ben.Web.Website` that accepts a POST,
   beside the existing `/auth/entra-signin` and `/auth/entra-signout` in `Program.cs`. It is not a
   Blazor page callback.

3. **VERIFY whether a client secret is needed, before designing around one.** Apple can return the
   identity token directly in the form post when `id_token` is part of the requested response type,
   and the identity token is all our API needs. Exchanging the authorization code at Apple's token
   endpoint instead requires a client secret that is itself a JWT signed with a downloaded `.p8`
   key and expires within six months. Read Apple's current documentation rather than trusting
   either reading of this paragraph.

4. **No localhost.** Apple refuses non-`https` redirect URLs, so this cannot be exercised on
   `127.0.0.1:5078` the way everything else here is. Budget for UAT being the only place it can be
   tested end to end, and keep everything either side of the redirect testable without it.

5. **The name arrives once.** Apple includes it in a `user` field on the FIRST authorization only
   and never again. Same rule as the native flow, same consequence for dropping it: the account
   ends up named whatever we invented.

## The two doors

Both already exist on the server. Nothing new is needed there for the button itself.

| Door | Endpoint | Proves what |
| --- | --- | --- |
| Create an account | `POST api/auth/apple` | Apple token, plus a chosen display name and handle |
| Claim one that exists | `POST api/auth/apple/link` | Apple token, plus that account's **full sign-in**: email, password, and a second factor when the account has one |

### Linking is a real sign-in, not a password check

Ben, 2026-09-10: "we make them follow the normal login process with verified e-mail and password
(and 2fa if configured)". Right, and the first version of this endpoint did not do it.

`CheckPasswordSignInAsync` verifies the password and the lockout and **nothing else**. It never
returns `RequiresTwoFactor` — that is `PasswordSignInAsync`'s job, and this endpoint cannot use that
one without creating a cookie sign-in it has no business creating. So linking would have joined a
two-factor account on a password alone: a way around the exact protection its owner turned on.

The second factor is now checked explicitly, an authenticator code verified and a recovery code
**redeemed** rather than merely checked. Refusals answer in the same problem-detail shape `/login`
uses, carrying Identity's own word, so a client maps them with the same code rather than a second
copy that would eventually disagree. An unconfirmed address now says so instead of being reported as
a wrong password.

One thing a test caught while this was being written: an unknown address and a wrong password have
to answer in the same **shape**, not merely the same words. Answering one with prose and the other
with a problem-detail tells them apart just as loudly, which would make this a way to ask whether
any given address has an account here.

**Offer the second one for the whole of that screen**, not only when Apple's address happens to
match an existing account. The server can only detect a collision when the addresses already match,
and that is precisely the case that was never broken. A Hide My Email relay address matches nothing;
the person knows they have an account, and the server cannot. Item 225 learned this the hard way.

`Entra/CompleteProfile.razor` is the shape to follow. It already gets this right for Microsoft.

## What was built, and what was actually verified

- `GET /auth/apple-signin` builds Apple's authorize URL, stashes a state and nonce in a
  `Secure; SameSite=None; HttpOnly` cookie, and redirects. `SameSite=None` is required: Apple posts
  the answer back cross-site, and without it the browser drops the cookie and every sign-in fails
  the state check for a reason nothing on screen explains.
- `POST /auth/apple-callback` checks the state, reads the identity token and Apple's one-shot name,
  and hands them to a Blazor page through `AppleSignInHandoff` as a single-use code that expires in
  two minutes. The token itself never appears in a URL, a log, or browser history.
- `/apple/complete` finishes it: signed in, or the create-or-link form.
- The button on `Login.razor`, hidden until configured.
- `/.well-known/apple-developer-domain-association.txt`, for step 3 above.

**Verified by running the site**, first unconfigured and then configured:

| Checked | Result |
| --- | --- |
| Unconfigured: button, endpoint, association file | hidden, redirects to `/login`, 404 |
| Configured: the redirect Apple receives | correct `client_id`, `response_type=code id_token`, `response_mode=form_post`, `scope=name email`, state and nonce |
| A simulated Apple form post with a matching state | redirects to `/apple/complete` with a one-time code |
| A forged post with no cookie from this browser | refused, back to `/login?appleError=1` |
| Somebody cancelling on Apple's page | back to `/login`, and NOT dressed as an error |

**Not verified:** the round trip through Apple itself. That needs a Services ID and an https host,
and Apple refuses localhost, so it cannot be done from here. Everything either side of the redirect
is pure and covered by tests, precisely because that half can never be exercised locally.

## Duplicates that already exist: none, measured 2026-09-10

Counted read-only against both databases on the server. Worth having done both: the first reading
of which one was production was **wrong**, and Ben corrected it.

| Database | Role | Accounts | External logins | Apple-shaped accounts |
| --- | --- | --- | --- | --- |
| `IsHauntedDb` | **production** | 3 | 1, Microsoft | 0 |
| `IsHauntedDb_player` | testing | 20 | 0 | 0 |

`IsHauntedDb_player` is the newer name and is what `appsettings.Development.json` points at, which
is what made it look like the live one. It is not. **The `_player` suffix means testing.**

**Nobody has ever signed in with Apple, on either.** Production carries a single external login, a
Microsoft one, on `averageben`. So there is no mess to clean up, and the re-point door below is
insurance rather than remediation — worth building alongside the button, because the day the button
ships is the day people start hitting the case it exists for.

Production does hold two accounts sharing the display name "AverageBen", `averageben` and
`averageben2` at `admin@ishaunted.com`, created within the hour on 2026-08-27. Neither has an Apple
login or an Apple-shaped address, so that pair is deliberate rather than a symptom of this defect.
Noted only so a later reader does not mistake it for one.

### Re-point, decided with Ben

Take the Apple identity off a duplicate, attach it to the real account, close the duplicate. It must
**refuse** when the duplicate holds anything worth keeping. `AccountClosureService` already closes an
account safely and already refuses when the person owns a group, so that guard exists and should be
reused rather than rewritten.

**A real merge is deliberately not in scope.** `367` columns across `153` entity files point at an
`AppUser`, which is why account closure anonymises rather than deletes. That is a project of its own,
and nothing today needs it.

## Also in scope, small

`EntraAuthController.Link` uses a bare `CheckPasswordAsync`, which does not count failed attempts.
It is an unauthenticated door that takes a password, so guesses against it are free. The Apple link
added in item 225 uses `CheckPasswordSignInAsync` with lockout; this should match.

## What Ben has to do in Apple's portal

The code ships **disabled** and turns itself on when the two settings below are filled in. Nothing
here is a secret; they are all public identifiers.

**No webhook is needed.** Apple has an optional server-to-server notification endpoint that reports
account deletions and consent revocations. Sign in with Apple works without it, and it is not a
prerequisite for anything here.

**No `.p8` key and no client secret is needed either**, which is the part every tutorial makes look
frightening. A key is only required to redeem the authorization code at Apple's token endpoint. This
asks for `response_type=code id_token`, so Apple returns the signed identity token in the form post
itself, and that token is the only thing the API wants. The API already validates Apple tokens
against Apple's published keys for the iPhone app.

1. **Create a Services ID.** Identifiers, then Services IDs. Something like `com.ishaunted.web`. It
   is not a bundle id and the existing app ids will not work.
2. **Enable Sign in with Apple on it, and Configure. Group it with `com.ishaunted.ios` as the
   primary App ID — do NOT let it become a primary App ID of its own.** Team `5778H75249`.
   - Domains and Subdomains: `ishaunted.com`, plus `www.ishaunted.com` if that is used.
   - Return URLs: `https://ishaunted.com/auth/apple-callback`, exactly.
3. **Verify the domain.** Apple issues a verification file. Paste its contents into
   `Apple:DomainAssociation` and the site serves it at
   `/.well-known/apple-developer-domain-association.txt`. Serve it **verbatim** — a newline an
   editor adds is enough to fail verification, and the portal reports that as a mismatch rather than
   as a formatting problem.
4. **Add the Services ID to `Apple:ClientIds`** in `Ben.Data.WebApi/appsettings.json`, beside
   `com.ishaunted.ios` and `com.ishaunted.desktop`. It becomes the token's audience, so without it a
   perfectly valid token is refused with a 401 that names nothing.
5. **Fill in `Apple:ServicesId` and `Apple:RedirectUri`** in `Ben.Web.Website/appsettings.json`.

### The one setting that decides whether this works at all

**Group every identifier under one primary App ID.** `com.ishaunted.ios`, `com.ishaunted.desktop`
and the new Services ID must all name the same primary App ID in their Sign in with Apple
configuration.

Here is why it matters more than anything else on this page. **Nothing identifies an Apple user by
their email address.** The identity is the `sub` claim in Apple's token, and the server looks a
returning person up by exactly that, never by address — which is what makes Hide My Email
survivable at all, and is pinned by
`AppleAuthControllerTests.AReturningIdentityIsFoundBySubjectWhateverTheEmailSays`.

But **Apple issues `sub` per app group, not per person.** Two identifiers in the same group see the
same `sub` for the same person; identifiers in different groups see different ones. So a Services ID
set up as its own primary App ID would give somebody a different `sub` on the website than on their
phone, and the site would see two unrelated people. The relay address cannot rescue that either,
because Apple issues a different relay address per group too.

The symptom would be somebody signing in on the web and finding a brand new empty account, having
used the app for months. **The link door on the completion page is what makes that recoverable** —
they can claim their real account with its password — but it is a cure for something that need not
happen.

### What Apple actually gives us to build an account from

Very little, and it is worth knowing before designing a form around it.

| From Apple | When |
| --- | --- |
| `sub`, the identity | Always. This is what identifies somebody, forever. |
| Email address | First authorization only. May be a relay. May be withheld entirely. |
| `email_verified`, `is_private_email` | With the token |
| The person's name | First authorization only, in a separate field, never in the token |

Nothing else. No handle, and a handle is permanent — which is why the server refuses to invent one
and asks instead.

**Apple-created accounts are active immediately; there is no email to confirm.** Apple has already
verified the address, so `EmailConfirmed` is set at creation. That is deliberate and it has to stay
that way: somebody who withheld their address gets `{sub}@appleid.invalid`, which is not deliverable,
so a confirmation requirement would lock them out permanently with no way to ask for another link.
A relay address is only deliverable once the sending domain is registered with Apple, so it would
lock those people out too until that is done.

### One more thing, if the site emails these people

Apple's Hide My Email relay only forwards mail from sender domains registered under **Configure
Sign in with Apple for Email Communication**. Until `ishaunted.com` is registered there, anything the
site sends to an `@privaterelay.appleid.com` address is dropped, silently. Account confirmation is
not affected, because an Apple-created account is already confirmed, but notifications are.

## Build note carried over from item 225

`dotnet build Ben.slnx` now needs the .NET MAUI workload. Use `Ben.Server.slnf` for everyday builds
and tests, which is what CI does.
