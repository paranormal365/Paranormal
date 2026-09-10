# Feature: a desktop client, and the client library both front ends share (item 225)

Branched from `develop` at `481abb91` (2026-09-10).

> "I would like to create a plan for building two new projects inside my Ben solution called
> Ben.Desktop.App.UI and Ben.Desktop.App.Library. These are going to be C# projects. The
> Ben.Desktop.App.UI project will be a Telerik .NET MAUI project and Ben.Desktop.App.Library will
> be a library of re-usable components. They will be able to communicate with our database using
> Ben.Data.WebApi. The end user will have to validate their account and follow the rules for
> accounts as currently exists through Identity."

Plan of record: `~/.claude/plans/i-would-like-to-jolly-scott.md`.

## The shape of it

Three projects, not two. The desktop app needs the same HTTP and sign-in plumbing the website
already has, and that plumbing lived somewhere a desktop app cannot reach — so it moved.

```
Ben.Service.Models          DTOs, no packages at all
        ↑
Ben.Data.WebApi.Client      HTTP + sign-in. No UI, no ASP.NET, no packages.
        ↑                   Referenced by BOTH front ends.
        ├── Ben.Web.Services            (the website, unchanged in behaviour)
        └── Ben.Desktop.App.Library     (Telerik MAUI controls + view models)
                    ↑
            Ben.Desktop.App.UI          (the app: Windows + Mac Catalyst)
```

`Ben.Data.WebApi.Client` sits beside `Ben.Data.WebApi` in the solution because it is that API's
client half. It never references the server: a client that references its server compiles the
server into every app that ships it.

## Phases

| Phase | What | State |
| --- | --- | --- |
| 1 | Ben.Data.Common stops declaring EF Core it never used | **done** — `118dbbe5` |
| 2 | Extract `Ben.Data.WebApi.Client` from `Ben.Web.Services` | **done** — `346753e9` |
| 3 | Session core: tokens, refresh, the sign-in state machine | **done** — `acb6509b` |
| 4 | `Ben.Desktop.App.Library` — the `Kit/` controls and view models | **done** — `e003cd99` |
| 5 | `Ben.Desktop.App.UI` — shell and the Identity sign-in screens | **done** — `e003cd99` |
| 6 | Microsoft and Apple sign-in, and the account-merge door | **done** — `d071d993` |
| 7 | Solution filter for CI, a macOS build job, docs | **done** — `e003cd99` |

## Verified, not assumed

- **The whole suite passes at every phase.** 7,727 before this branch, 7,785 after phase 3, zero
  failures. When the three moved tests left `Ben.Web.Tests` its count fell by exactly 40 and the
  new project's count was exactly 40.
- **`Ben.Service.Models` no longer names a single EF or Identity library** in its dependency graph.
  That is the thing phase 1 was for, and it is checked in the graph rather than assumed from the
  csproj.
- **Ten fixtures captured from a real API**, pointed at a scratch database (`IsHauntedDb_desktop225`)
  and never the shared one. See `Ben.Data.WebApi.Client.Tests/Fixtures/README.md` for exactly how
  each was produced. Capturing them settled three things that had been assumed:
  - The three `/login` refusals really are one status separated only by a string in the body.
  - The 429 really does carry `Retry-After: 60`. Every client had been discarding it.
  - Registration answers a refusal as **JSON**, not prose — so the ordinary refusal handling would
    have thrown away "That name is taken." and shown a paraphrase of the status code instead.
  - Two-factor was turned on for real to capture its challenge, and the second step was walked
    through to a token.
- **The app builds, launches and stays running**, which means its whole graph resolved: both HTTP
  pipelines, the token session, the session store, secure storage, Telerik and both pages.
- **The same code path was driven end to end against a real API** by `LiveApiTests` — sign in,
  roles from `api/me`, the bearer handler reaching an authenticated endpoint, a real refresh, and a
  wrong password still mapping to invalid credentials. 103 tests pass against the live server with
  nothing skipped. They skip, rather than fail, when no server is listening.
- **Two tests were checked against the bug they exist to catch.** Reintroducing an unreadable
  refusal being reported as a wrong password fails three cases of `LoginFailureMappingTests`.
  Deleting the single-flight guard fails `Concurrent_callers_share_one_refresh` — but only after
  the test was fixed: its first draft slept on the thread inside the caller's own lock, so the
  callers never overlapped and it passed with the guard gone.

## Building the desktop projects on this Mac

The MAUI workload cannot go into `/usr/local/share/dotnet` without `sudo`, so it went into a
user-local SDK instead. Nothing about the system install changed, and `dotnet` on the PATH is still
the same one it always was.

```bash
# once
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version 10.0.301 --install-dir ~/.dotnet-maui
DOTNET_ROOT=~/.dotnet-maui ~/.dotnet-maui/dotnet workload install maui-maccatalyst

# thereafter
DOTNET_ROOT=~/.dotnet-maui ~/.dotnet-maui/dotnet build Ben.Desktop.App.UI -f net10.0-maccatalyst
```

`maui-maccatalyst`, not `maui` — the latter also pulls Android, iOS and Tizen for no benefit here.
`sudo dotnet workload install maui-maccatalyst` against the system SDK works too, if you would
rather have one install than two.

**Everything else still uses the ordinary `dotnet`.** `Ben.Server.slnf` is the whole solution minus
the two MAUI projects, and it is what CI and an everyday build should use:

```bash
dotnet build Ben.Server.slnf && dotnet test Ben.Server.slnf
```

`dotnet build Ben.slnx` now fails on any machine without the MAUI workload (NETSDK1147). That is
checked, not assumed, and it is why the filter exists.

## External sign-in

**Microsoft is not one of our sessions. Apple is.** A Microsoft sign-in leaves the client holding a
token Microsoft issued, which the API validates under its second scheme, so it renews at Microsoft
rather than at our own endpoint. Apple's endpoint signs the person in under our scheme and answers
a body identical to what a password sign-in returns.

**MSAL was considered and dropped.** It has no Mac Catalyst asset — Catalyst resolves its plain
desktop build — so it would mean a loopback listener and a network-server sandbox entitlement, and
would still only serve Windows properly. One authorization-code flow with proof key, behind a small
browser seam, covers both. Everything either side of the browser is ordinary testable code.

### The duplicate-account gap Ben spotted

Sign-in only joins an external identity to an existing account when the provider's verified email
happens to equal one. Two ordinary situations defeat that: Apple's Hide My Email relay, whose
address never matches anything here, and an Apple ID or work Microsoft account simply being at a
different address from the one somebody signed up with. Both ended at "create an account" and
quietly produced a **second** account holding none of their cases, groups or history.

- Microsoft already had a link endpoint taking an arbitrary email and password.
- **Apple had no such door**, so `api/auth/apple/link` is new. It must issue a session, because an
  Apple identity token is not a credential the API accepts on ordinary requests. It honours lockout,
  since it takes a password from an unauthenticated caller.
- The client now offers "I already have an account" for the **whole** of that screen, not only when
  the server spots a matching address — the server can only spot one in the case that was never
  broken.

Where else this gap lives is recorded as items 226 and 227: the iPhone app has it, the website's
Microsoft flow does not, and the website has no Apple button at all yet.

## Found only by running it

Four things that a clean build said nothing about:

1. An `x:Name` in XAML generates a field on the same partial class, so an element named `Hint` and
   a bindable property named `Hint` collide at compile time. Elements carry a `Label` suffix now.
2. The app compiled and then died inside `UseTelerik`: .NET 10 stopped including
   `Microsoft.Maui.Controls.Compatibility` implicitly, and Telerik still needs it.
3. Before that it would not launch at all — "Launchd job spawn failed", which names nothing. An
   ad-hoc signature cannot carry `keychain-access-groups`, because `$(AppIdentifierPrefix)` expands
   only from a provisioning profile. That entitlement is now in a separate plist used only when
   there is a real signing identity.
4. `Telerik.UI.for.Maui` 3.2.1 brings SkiaSharp 2.88.1 and `System.Security.Cryptography.Pkcs`
   6.0.0 transitively, both with known HIGH severity advisories. Pinned forward; the macOS CI job
   re-checks so a Telerik upgrade cannot quietly reintroduce them.

**Neither interactive sign-in has been run.** Nothing in this branch should be read as claiming
otherwise. What was checked is that both Microsoft endpoints refuse an unauthenticated caller, that
both Apple doors refuse a token Apple did not sign, and that a forged redirect is rejected — the
last proved by deleting the state comparison and watching the test fail. The round trip itself needs
a tenant, a browser and an Apple App ID.

**Still unverified on the Mac:** whether `SecureStorage` actually persists across a relaunch on an
ad-hoc-signed Catalyst build. The iOS app hit exactly this — an unsigned build cannot use the
simulator Keychain and persistence fails silently — so assume it needs a signing identity until
somebody has watched a session survive a restart.

## Needs Windows

Nothing below can be checked on this Mac, and none of it should be claimed until it has been:

- `net10.0-windows10.0.19041.0` restores and builds at all.
- Whether MAUI's WebAuthenticator can receive the callback at all in an **unpackaged** Windows app.
  It wants the operating system to route a registered protocol back to the app, which normally means
  packaging. Expect to either package the Windows head or use a loopback redirect there instead.
- `SecureStorage` on an unpackaged Windows app.
- Telerik UI for .NET MAUI rendering on WinUI.
- Whether `WindowsPackageType=None` is the right call, or whether it should ship as MSIX.

## Deliberately unchanged

- **Every moved type kept its `Ben.Web.Services.WebApi` namespace**, the way `Ben.Video.Core` kept
  the editor's. Namespace is independent of assembly in C#, so roughly 150 Razor files and both
  `_Imports.razor` are untouched. The only change is where they compile.
- **The website's behaviour.** `IWebApiTokenStore` still carries impersonation, the Entra bridge and
  the `AuthReady` gate; it just extends `ITokenStore` now.
- **`JwtClaimsParser` stayed in the web project.** It is nearly useless against opaque tokens, but
  it is still called and still tested, and removing it is not this branch's business.

`WebApiBearerTokenHandler` was deleted, and that is not a behaviour change: nothing registered it.
`IHttpClientFactory` resolves handlers from the root scope, so the token store injected into it was
never the Blazor circuit's and was always empty. Phase 3 rebuilds it where that objection does not
apply — one container, one signed-in person.

## Follow-ups this work surfaced

1. **There is no endpoint that exchanges a Microsoft token for an Identity session.** The desktop
   client uses the Microsoft token directly, which works because the API accepts both schemes. An
   `api/auth/entra/token` would let a Microsoft sign-in become an ordinary session and is worth
   considering, but nothing needs it today.
2. **`EntraAuthController.Link` does not honour lockout.** It uses a bare password check, so guesses
   against an unauthenticated door are free. The new Apple link uses the lockout-honouring form; the
   Microsoft one should match. Recorded in item 226.
3. **Portal work, before either provider can actually be used.** Microsoft: a "Mobile and desktop
   applications" platform carrying `msauth.com.ishaunted.desktop://auth`, with public client flows
   enabled. Apple: an App ID for `com.ishaunted.desktop` with the Sign in with Apple capability.
4. **There is no sign-out or revoke endpoint, and no device registry, for any client.** Sign-out is
   local everywhere. "Sign out my other machine" is not currently possible for anyone.
5. **Central package management.** Two Telerik product lines (Blazor 14.1.0 and MAUI) now have to
   move together by hand, and there is no `Directory.Packages.props`.
6. **A Services ID** will be needed in `Apple:ClientIds` if the website ever offers the button —
   item 227. The desktop bundle id is already there.
