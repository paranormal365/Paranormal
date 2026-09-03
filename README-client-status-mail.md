# Item 206 — client status mail

Branch: `feature/client-status-mail`, cut from `develop` at `77ee8ea`.

When a case changes state or a visit is scheduled, the client gets the same sentence the site
shows. The mail rail works and is observable (item 187); this is the templates and the triggers.

## One source of words

`CaseStatusWording` (Ben.Data.Common) holds the badge **label** and a one-line **client sentence**
per status — *"The group has taken your case and will be in touch to arrange a visit."* — and the
website's `CaseStatus.Label()` extension now delegates to it. The client's case page shows the
sentence under the badge (`data-testid="status-sentence"`), and the mail carries the same string,
so the two cannot drift. A moved-to-the-common-project wording is the whole reason the mail can
promise "the same sentence the site shows".

## The triggers

`ClientStatusMailer` (WebApi), one scoped service, called after the save that made the change true:

| Seam | Mail |
|---|---|
| `CaseController.Update` (status changed) | "Your case #2026-003 is now Accepted" + the sentence |
| `CaseTransferController` propose / accept / cancel | Transferred, then Accepted by the new group |
| `SubscriptionLapseJob` pauses open cases | Paused, with the sentence that nothing is lost |
| `InvestigationController.Create`, `ScheduleProposalController.Convert` | "A visit is scheduled for your case" with date, time (UTC) and place |
| `InvestigationController.Update` (time moved) | "A visit … has been rescheduled", naming the old and new time |
| `InvestigationController.Cancel` | "A visit … was cancelled" |

Every mail ends with an **Open your case** button to `/my-cases/{id}`, where times show in the
client's own clock.

## Who is mailed, and what is observable

Recipients are the case's clients (`CaseClientAccess`) **with a confirmed address**; an
unconfirmed address is skipped and logged, never mailed. When mail is not configured the mailer
logs at Debug and does nothing. A send logs at Information when it goes and at **Error when it does
not** — the line the mail diagnostics page and the error log read. A failed send never fails the
change that caused it: the status was already saved.

**Not built, worth a follow-up:** a per-client "email me about my case" switch. No opt-out exists
in the model today; the confirmed-address rule is the only gate.

## Proof

- `ClientStatusMailerTests` (7): every status has a label and a sentence of its own; a status change
  mails the site's words and the case link; an unchanged status sends nothing; only confirmed
  addresses are mailed; unconfigured mail is a quiet no-op; a failing send is logged, not thrown;
  a scheduled visit names when and where.
- Every existing controller and job test passes with the mailer injected (a quiet test mailer).
  Full suite 3,950.
- Browser: `ClientStatusSentenceTests` — a client opening their case sees the sentence under the
  badge, and it is the sentence for that badge.
