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
| 4 | `Ben.Desktop.App.Library` — the `Kit/` controls and view models | blocked, see below |
| 5 | `Ben.Desktop.App.UI` — shell and the Identity sign-in screens | blocked |
| 6 | Microsoft Entra (MSAL) and Sign in with Apple | blocked |
| 7 | Solution filter for CI, a macOS build job, docs | partly done |

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
- **Two tests were checked against the bug they exist to catch.** Reintroducing an unreadable
  refusal being reported as a wrong password fails three cases of `LoginFailureMappingTests`.
  Deleting the single-flight guard fails `Concurrent_callers_share_one_refresh` — but only after
  the test was fixed: its first draft slept on the thread inside the caller's own lock, so the
  callers never overlapped and it passed with the guard gone.

## Blocked on one command

Phases 4 to 6 need the .NET MAUI workload, and `/usr/local/share/dotnet` is root-owned:

```bash
sudo dotnet workload install maui-maccatalyst
```

Not `maui`, which also pulls Android, iOS and Tizen. Not `maui-windows`, which is Windows-only and
will refuse here. On the Windows machine the counterpart is `dotnet workload install maui-windows`.

## Needs Windows

Nothing below can be checked on this Mac, and none of it should be claimed until it has been:

- `net10.0-windows10.0.19041.0` restores and builds at all.
- The WAM broker sign-in flow and its `ms-appx-web://` redirect URI.
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

1. **There is no endpoint that exchanges an Entra token for an Identity session.** The website gets
   away with it through its OIDC cookie. A desktop client has no cookie, so it either keeps using
   the Entra JWT directly or the server grows an `api/auth/entra/token`.
2. **There is no sign-out or revoke endpoint, and no device registry, for any client.** Sign-out is
   local everywhere. "Sign out my other machine" is not currently possible for anyone.
3. **Central package management.** Two Telerik product lines (Blazor 14.1.0 and MAUI) now have to
   move together by hand, and there is no `Directory.Packages.props`.
4. **`Apple:ClientIds` needs the desktop bundle id** before Sign in with Apple can work there.
