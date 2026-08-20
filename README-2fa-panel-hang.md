# Fixing the 2FA enrolment panel hang

Backlog item **#112**. Pressing **Turn on two-step sign-in** on the profile's Security tab leaves
the button reading "Starting…" for ever. No QR appears, no error is shown, and nothing on screen
says anything is wrong — which is the worst way for a page to fail, because it looks like it is
working.

## What is broken, precisely

**Not two-factor authentication.** The API is verified end to end against a live server with real
TOTP codes computed from the secret it issues: setup, enable, sign-in with an app code, sign-in with
a recovery code, single-use enforcement on recovery codes, and disable. `curl` gets a 200 from
`POST /api/me/2fa/setup` in milliseconds. The panel also rendered correctly through a browser once,
early in the session that built it, before it was finished.

It is **`TwoFactorPanel.razor`**, and specifically what happens after `BeginSetupAsync` is invoked.

## What the evidence already rules out

Recorded so the next attempt does not repeat these:

| Tried | Result |
|---|---|
| Waiting 30 seconds, sampling the panel every 5 | Stuck on "Starting…" the whole time |
| A 20-second `CancellationTokenSource` around the call | **No timeout message, no error, no re-render** |
| `finally { _busy = false; StateHasChanged(); }` | Never takes effect on screen |
| Swapping `PostAsync` for `SendExpectingReasonAsync` | No change |
| `GET /api/me/2fa` on the same page | Works — the panel renders "Off" from it |
| `curl POST /api/me/2fa/setup` with a real token | 200, fast |

The cancellation result is the important one. If the `await` were merely slow, the token would have
fired and the `catch` would have painted an error. It did not, and neither did the `finally`. So
**the circuit stops re-rendering**, and the fault is not the HTTP request.

## Where to look, in order

1. **Something doing sync-over-async and deadlocking the circuit's synchronisation context.** A
   blocked circuit fits every symptom above, including cancellation appearing to do nothing.
2. **`TelerikQRCode`'s first render.** It is the one component on this page used nowhere else in the
   product, and it is what the success branch renders. Worth testing by rendering the panel with the
   QR replaced by a plain `<code>` block: if the hang goes away, that is the answer.
3. **The interaction between the panel's `StateHasChanged` and Telerik's masked textboxes.**

## First diagnostic to run

Settle whether the request reaches the API at all. The API does not log requests today, so absence
of a log line proves nothing — add a line at the top of `MyTwoFactorController.BeginSetup` and
watch. That splits the problem cleanly in two and the rest follows from which half it lands in.

## The test

`AccountTests.EnrollingWithARealCodeTurnsItOn` is already written — it computes a real TOTP from the
shared key the page displays, enrols, and checks ten recovery codes come back. It is currently
`Assert.Ignore`d pointing at item #112. **Remove the Ignore; do not rewrite the test.** It passing is
the definition of done here.

The fixture's `[TearDown]` disables 2FA through the account's own endpoint with a fresh code, so a
failure part-way through cannot leave the shared test account demanding a code nobody has.

## Verifying

```bash
dotnet test Ben.Web.Playwright -p:IsTestProject=true --filter "TestCategory=Account" -e BEN_BASE_URL=http://localhost:5079
```

Both hosts run from their own project directories — launching from the repository root serves every
static file as 200 with zero bytes. And a Blazor Server page is interactive long after it renders,
so give the first interaction a generous timeout rather than reading a cold start as a failure.
