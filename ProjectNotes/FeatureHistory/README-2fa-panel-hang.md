# The 2FA enrolment panel hang — item #112

**Fixed.** Pressing **Turn on two-step sign-in** left the button reading "Starting…" for ever: no
QR, no error, nothing on screen to say anything was wrong. The worst way for a page to fail,
because it looks like it is working.

## The cause

`TelerikMaskedTextBox` **does not splat unmatched attributes — it throws**:

```
System.InvalidOperationException: Object of type
'Telerik.Blazor.Components.TelerikMaskedTextBox' does not have a property
matching the name 'aria-label'.
```

That `aria-label` had been added the day before, as the fix for a real accessibility finding:
`LabelAssociationTests` caught a `<label>` pointing at nothing, because Telerik renders no `id` on
its inner input — only a `data-id` GUID. It was added on the reasonable assumption that Telerik
splats what it does not recognise.

The exception is thrown **during render**, not during the call. Under Blazor Server that kills the
circuit, so the page froze on the last frame it had drawn — the one with the button reading
"Starting…". Which is why every symptom pointed away from the truth:

| Observed | What it seemed to mean |
|---|---|
| API answered in milliseconds | the server was fine, so it must be the client's request |
| `finally { _busy = false; StateHasChanged(); }` never took effect | the `await` had not returned |
| a 20-second cancellation token never fired | the call was stuck somewhere uncancellable |
| clicking a different tab did nothing | *(this was the tell — the whole circuit was dead)* |

**It was found by reading the browser console** through Playwright's `Page.Console` and
`Page.PageError`. Nothing server-side reported it. What justified looking at the client at all was a
`Console.WriteLine` at the top of the API action, proving the request arrived, was handled and
answered — which split the problem cleanly in two.

## The fix

Both code boxes — the enrolment panel and the sign-in page — are now **plain inputs**. Not a retreat
from preferring Telerik components; this one genuinely cannot do the job:

- no `id`, so no `<label for>` can ever name it, and it has no accessible name at all;
- it throws on an unmatched attribute, during render, killing the circuit;
- no `inputmode="numeric"` and no `autocomplete="one-time-code"`, so a phone offers neither a
  numeric keypad nor the code it has just received.

A plain input gives all three, plus a real label. `TelerikAttributeSplattingTests` now scans every
`.razor` file and fails on any Telerik tag carrying a plain HTML attribute — it reports the original
bug by name when reintroduced.

## Fixed alongside, all found by chasing this

**`LockedOut` was reported as "invalid email or password".** Found when a run of probes locked the
SuperAdmin account and the page said the password was wrong — sending somebody to reset a password
that was right, when only waiting helps. Sign-in now distinguishes five refusals, and the help
documents what each one means.

**`BenTestBase.FillCredentialsAsync` could sign in as the wrong person.** It checked that the *DOM*
held the credentials, which is not the same as the server holding them: under Blazor Server an
`InputText` accepts characters before its circuit connects. The sign-in page pre-fills developer
credentials in Development, so a submit landing in that window did not fail — it **succeeded as the
developer account**, navigated away, and left every caller believing it had signed in as whoever it
asked for. A suite-wide source of tests failing later, somewhere unrelated, while looking at the
wrong person's data.

**Two tests that lied.** `SigningInWithTwoStep…` shared the fixture's account, so its result
depended on what the previous test had left behind. `TypeHandleAsync` waited longer and longer for a
page that was never slow.

## The thing I got wrong, recorded because it cost the most

I read a failing test as a slow cold start and raised timeouts to 60, then 90, then 120 seconds.
Measured, the page is **interactive about 450ms after navigation on a cold host**, and server render
is 9ms. The real fault was that a character typed before the circuit connects is not merely
ignored — the first interactive render **overwrites the field from the server's empty value**, so
the keystroke is erased. The cure is to type again, not to wait longer. Those tests now run in about
two seconds; they were taking ninety.

A generous timeout on a fast page buys nothing and hides the next real regression behind a minute
and a half of nothing.

## Verifying

```bash
dotnet test Ben.Web.Tests --filter "FullyQualifiedName~TelerikAttributeSplatting|FullyQualifiedName~LoginAttempt|FullyQualifiedName~UserHandleService"
dotnet test Ben.Web.Playwright -p:IsTestProject=true --filter "TestCategory=Account"
```

The site runs on **5078** from `Ben.Web.Website`, its configured port — `BEN_BASE_URL` defaults
there, so no override is needed. Run it from its own project directory: launching from the
repository root serves every static file as 200 with zero bytes.
