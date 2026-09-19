# Site evaluation 2026-09-06 — Phase 5: server hygiene

Branch: `feature/site-eval-phase-5-server-hygiene`. Plan of record:
`ProjectNotes/Site-Evaluation-2026-09-06.md`, *Phase 5 — Server hygiene*.

## The problem

Five findings about what the server does when nobody is looking: a log filling with warnings about
requests that succeeded, an error printed twice and read as two failures, an allocator with no
callers, a suspicion about circuits, and a click that did nothing.

Four of the five turned out to be different from their description. Two were worse, one was
already fixed, and one was not real at all.

## What each one actually was

### W-A16 — 188,000 warnings about requests that worked

The default authorization policy accepts either the local Identity bearer scheme or the Entra JWT
scheme, and **a policy that lists two schemes authenticates with both**. So every ordinary
sign-in's Identity token — a data-protection blob with no dots in it — was handed to the Entra
handler, which correctly reported that it is not a JWT.

Measured on an isolated stack: **four log lines per request**, including
`IDX14100: JWT is not well formed`, at Warning, with a stack trace. Three requests produced twelve
lines. A log in that state cannot show a real fault.

The handler now declines a token it could not own before trying to validate it —
`NoResult()`, not `Fail()`, because "this is not mine" is the truth and the Identity handler is
about to accept it. The shape test accepts both JWT serialisations, five segments as well as
three, because the permissive direction costs a log line and the strict direction would break
somebody's sign-in.

### W-S7 — one send, two consoles, and a configuration trap

The same "No confirmation message" error printed twice per sign-up. It was one send.

`Serilog:WriteTo` is a JSON array, .NET configuration merges arrays **by index**, and it merges the
object at each index **key by key** rather than replacing it. `appsettings.json` held
`[MSSqlServer]`; the gitignored `appsettings.Development.json` holds `[Console, MSSqlServer]`. So
index 0 resolved to a hybrid: `Name` of Console, `Args` of the SQL sink — including its
`restrictedToMinimumLevel: Error`. A console that printed errors and nothing else, on top of the
console the host added in code. Errors doubled. Warnings did not, which is why the log looked like
two failures and one recovery.

Neither file was wrong on its own. `appsettings.json` now lists Console at index 0 and MSSqlServer
at index 1, matching the Development file, and the host adds nothing.

**The first attempt at this was wrong and shipped nothing.** It skipped the code sink whenever
configuration mentioned Console — which left only the Error-restricted hybrid, and the console
fell silent below Error. Caught by starting a host and reading its log rather than the two files.

### C1 — the allocator with no callers, already fixed

`UserHandleService.AllocateAsync` now has callers on every path the finding named, and three of
them already had a test. What was missing was the fourth (an Entra sign-in) and, more to the
point, anything that would catch the **fifth** — the account-creating path somebody adds next
year, who has no reason to know that a null handle is invisible rather than merely blank. That is
`AccountCreationHandleGuardTests`.

### W-S6 — not real on this build

The evaluation saw "three WebSocket circuits within 400 ms on some navigations" and named it the
likely root of W-A12. Measured: **one circuit, kept across every navigation.** A walk of six pages
signed out, then eight more signed in including five group-page tab switches, opened no second
SignalR handshake. Signing in opens one more, which is a full page load and correct.

Nothing to fix. There is now a test that fails if that changes.

### W-A12 — real, and not W-S6

The first click on a dialog's primary button after typing did nothing; the second worked.

The cause is that these buttons are `disabled` whenever their field looks empty — and the field
binds on `oninput`, so "empty" is **the server's opinion, one circuit round trip behind the
keyboard**. A click landing in that window is discarded by the browser without reaching anything,
because a disabled button dispatches nothing.

Reproduced through Playwright's trusted input, where a single click after character-by-character
typing failed to create the report — **intermittently**, which is what a race looks like. The
window measured about three milliseconds on localhost. Over a real connection it is the round
trip, and somebody who types the last character and clicks is inside it.

Four commit buttons now stay live and refuse in the handler, where the refusal can also say what
is missing — a disabled control explains nothing, least of all to a screen reader. `_saving` still
disables them, because that flag is set after the click and is not a state anybody can race.

## Key files

| what | where |
|---|---|
| Decline a token that is not a JWT | `Ben.Data.WebApi/Authorization/BearerTokenShape.cs` (new), wired in `Program.cs` |
| One console sink | `Ben.Data.WebApi/appsettings.json` — Console at index 0, MSSqlServer at index 1 |
| Every account gets an @name | `Ben.Web.Tests/Services/AccountCreationHandleGuardTests.cs` (new) |
| One circuit, and one live commit button | `Ben.Web.Playwright/Tests/FirstClickAfterTypingTests.cs` (new) |
| The four commit buttons | `ReportBuilder.razor`, `ClientRequestWizard.razor`, `AreaOfOperationDialog.razor` |

**No migration.**

## Left undone, on purpose

**Fifty-three other buttons gate on a field the same way.** A search box disabled until you type
has the same race and does no harm: you click again and search. The harm is in a dialog, where the
click IS the commit and the person walks away believing they saved. So this fixes the four commit
buttons the evaluation named and guards them by id; turning each of the other fifty-three into a
spoken refusal is a judgement per site, not a sweep.

**Make Public**, the fifth thing the evaluation listed, is a checkbox and not this mechanism. It is
not covered.

**The website host has no console sink at all** — its own `appsettings.json` declares only
MSSqlServer, and nothing in code adds one. Noticed while reading the merged configuration; out of
scope here, and it means `web.log` carries no application logging.

## Verified

| check | result |
|---|---|
| `dotnet build Ben.slnx` | 0 errors, 0 warnings |
| Ben.Web.Tests | 4,559 passed, 0 failed (25 added) |
| Ben.Service.RepositoryService.Tests | 319 passed, 0 failed |
| Playwright `FirstClick` | 3 passed, 0 failed, 0 skipped |
| The whole Playwright suite on a fresh database | 470 passed, 8 failed, 41 skipped in 25 minutes. All eight are the pre-existing set recorded in `ProjectNotes/AudioEditor-Audit-2026-09-06.md` — its seven, plus the eighth that audit recorded as failing in a full run and passing in isolation |
| W-A16, on a running host | 12 warning lines for 3 requests without the guard, **0 with it** — and a JWT-shaped token still reaches the handler and is validated |
| W-S7, on a running host | the sign-up error printed twice before, **once after**, with the warning and its link still printed and 52 Serilog lines still reaching the console |
| W-S6, on a running host | one SignalR handshake for the page, and none added by fourteen navigations |
| W-A12, through trusted browser input | one click after typing failed to create the report before the fix and succeeds after it |

### Each new test seen to fail without its fix

| reverted | failed |
|---|---|
| The allocator call removed from the case-invite path | `Every_account_the_api_creates_is_given_a_handle` |
| The two named exemptions removed | the guard flagged `MyProfileController`, proving the scan reaches real constructions |
| The Entra allocation removed | `Register_NewUser_IsGivenAHandle` |
| The field gate put back on `#report-create` | `The_primary_button_is_never_disabled_waiting_on_a_round_trip` |
| The field gate put back on `#area-look-up` | `No_commit_button_is_gated_on_a_field_the_browser_owns` |
| The token-shape guard disabled, on a running host | 12 Entra warning lines appeared for 3 requests |
| The console sink forced on beside configuration's, on a running host | the sign-up error printed twice again |

**One test was written, seen to pass against the un-fixed code, and rewritten.** The first version
of `The_primary_button_is_never_disabled_waiting_on_a_round_trip` watched the `disabled` attribute
with a MutationObserver — but the only transition after typing is *out* of disabled, so it could
never observe the state it was looking for. It passed against the broken code, which is worse than
no test. It now reads the button's state at the instant a character is typed, and fails.

## Data created while verifying

All of it in the throwaway `IsHauntedDb_p5` through `_p5o`, dropped at the end: two probe accounts
(`w-s7-probe@` and `ws7-after@example.com`) and a handful of reports created by the click tests.
Nothing was written to any other database.
