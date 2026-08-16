# Contact & Support Tickets (backlog item #79)

Branch: `feature/support-tickets`

A page where anyone — signed in or not — can ask for help or reach staff, and a queue where staff
answer them.

## The shape of it

**The ticket is the record. Email is a notification on top, and is allowed to fail.**

That is the whole design decision. `SmtpEmailService` exists but SMTP is unconfigured, so an
email-only contact page would not work today and would fail silently when it did. A stored ticket
works either way, keeps state (answered vs forgotten), and lets a second admin see the first
already replied.

Which leaves one real question: **how does a staff reply reach someone with no account?**
The answer is a **tracking link** — an opaque token that gives the sender their own thread to read
and add to, no sign-in required. That is why this works with no mail at all.

| Piece | Where |
|---|---|
| Entities + enums | `BenDataModel.SupportTicket*.cs`, `Enums/SupportTicketEnums.cs` |
| Anti-spam | `Ben.Data.WebApi/Services/SupportFormGuard.cs` |
| Public form + tracking | `Controllers/Public/PublicSupportTicketController.cs` |
| Staff queue | `Controllers/Admin/AdminSupportTicketController.cs` |
| Contact details | `SiteSettingKeys` (`contact.*`) + `Controllers/Public/PublicSiteContactController.cs` |
| Pages | `Support/ContactPage.razor`, `Support/SupportTicketTrackingPage.razor`, `SuperAdmin/AdminSupportTickets.razor` |

## Anti-spam, in the order it runs

1. **Honeypot** — a field kept in the DOM, positioned off-screen and `aria-hidden`, that a person
   never sees. `display:none` is avoided deliberately; the better bots skip hidden fields. A tripped
   honeypot returns **200 with an invented reference and stores nothing** — telling a bot which
   check caught it is free tuning information for whoever wrote it.
2. **Form token** — data-protected and purpose-scoped, issued when the form renders. Proves the
   page was open at least 3 seconds and at most 6 hours. Signed, so the clock can't be moved by the
   client. Expired is reported honestly (a real person hits it by leaving a tab open); every other
   failure is deliberately indistinguishable.
3. **Rate limits** — 5/day per email address, 3/hour per IP. Email is stored lower-cased so capitals
   can't walk around the limit.

**No CAPTCHA.** It would be the public site's first third-party runtime dependency. If these three
stop being enough it belongs *on top of* them, not instead. Signed-in users would skip it anyway —
the account is the rate limit.

## Two bugs worth remembering

**Non-deterministic hashing.** IP hashing was first built on `IDataProtector.Protect`, which is
non-deterministic — the same address would have hashed differently on every call, so every rate
limit would have been silently unenforceable while looking like it worked. It is a keyed HMAC now,
with a test asserting the same address hashes alike. (Keyed, not bare SHA-256: the IPv4 space is
small enough to enumerate, so an unsalted hash of an address is trivially invertible and "we only
store a hash" would be an empty claim.)

**Awaiting `AuthReady` during prerender.** `/admin/support-tickets` hung outright — no error page,
no log line, just a URL returning nothing — because `AuthReady` is only ever signalled from the
interactive circuit. Found by verification, not by tests. It now has a source-scan test across every
routable page (`AuthReadyPrerenderGuardTests`), because this was the second occurrence in one
session.

That test taught its own lesson twice: it first flagged `ClientRequestWizard`, which is *correct* —
`OnAfterRenderAsync` never runs during prerender — and then flagged it again because a comment
inside that method mentioned `OnInitializedAsync`. A guard that cries wolf is one people learn to
ignore, so it checks only the lifecycle methods that block the response, and matches the signature
line rather than the whole span.

## Privacy choices

- **`SourceIpHash`, not the address.** Rate limiting only needs to know whether two submissions came
  from the same place; equality on a keyed hash answers that.
- **Two record types, not one with fields blanked.** `SupportTicketPublicRecord` has nowhere to put
  an assignee, an IP hash, or an internal note — asserted structurally in a test, so a field added
  for staff cannot leak by being forgotten.
- **Internal notes are filtered in the query**, not in the UI.
