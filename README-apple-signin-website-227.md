# Feature: Sign in with Apple on the website, and joining accounts up (item 227)

Branched from `develop` at `bb7a3701` (2026-09-10).

> "when we add the fixes for the gaps in the WebApp, I would like to add a 'Sign in with Apple'
> button which they can use to create a new account or link an existing account."

Follows item 225, which built the same two doors for the desktop client and added the server half
both front ends need.

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
| Claim one that exists | `POST api/auth/apple/link` | Apple token, plus that account's email and password |

**Offer the second one for the whole of that screen**, not only when Apple's address happens to
match an existing account. The server can only detect a collision when the addresses already match,
and that is precisely the case that was never broken. A Hide My Email relay address matches nothing;
the person knows they have an account, and the server cannot. Item 225 learned this the hard way.

`Entra/CompleteProfile.razor` is the shape to follow. It already gets this right for Microsoft.

## Where the button goes

`Ben.Web.Website/Components/Pages/Login.razor`, alongside whatever Microsoft offers today.

## Open question, to settle before building

**What happens to a duplicate account somebody already has.** Linking later does not merge anything,
and it is worse than it sounds: the Apple identity is attached to the wrong account, so a later link
attempt hits the "already linked to a different account" refusal, and they are stuck. The duplicate
has also taken a permanent handle.

Two very different sizes of answer:

- **Re-point.** Take the Apple identity off the duplicate, attach it to the real account, close the
  duplicate. Small, and it covers the common case where the duplicate is empty because the person
  noticed straight away. `AccountClosureService` already closes an account safely and already
  refuses when the person owns a group, so the guard exists.
- **A real merge**, moving data across. **367 columns across 153 entity files** point at an
  `AppUser`. Account closure anonymises rather than deletes for exactly this reason. This is a
  project, not a task.

**Measure before choosing.** Apple-created accounts with no usable address carry
`{sub}@appleid.invalid`, and relay ones carry `@privaterelay.appleid.com`. Counting those on
production says whether any duplicates exist yet.

## Also in scope, small

`EntraAuthController.Link` uses a bare `CheckPasswordAsync`, which does not count failed attempts.
It is an unauthenticated door that takes a password, so guesses against it are free. The Apple link
added in item 225 uses `CheckPasswordSignInAsync` with lockout; this should match.

## Build note carried over from item 225

`dotnet build Ben.slnx` now needs the .NET MAUI workload. Use `Ben.Server.slnf` for everyday builds
and tests, which is what CI does.
