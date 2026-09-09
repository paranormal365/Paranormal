# An investigation that runs longer than one night

Ben, 2026-09-09:

> "Could we schedule an investigation over more than one day? I need to create a week-long
> investigation for Apple to use for verifying the App. Also, there could be weekends for
> investigations instead of a single day or night or overnight."
>
> "Make single-day investigations the norm. If you need multi-day, check a box for it to be
> available."
>
> "You usually arrive like 3pm on one day and the investigation ends 8am the next day, this is
> considered a single-day investigation."

## Where it already works, and where it does not

The **data** has never been the problem. `Investigation` carries `ScheduledDateTime` and a
nullable `EndDateTime`, the create and update endpoints store whatever they are given, and
`MyDeskController` already counts a visit as upcoming while its end is still ahead. A week-long
investigation is storable today.

What is missing is everything a person actually sees:

| Where | Today |
|---|---|
| Schedule / Edit dialog | the end is a full date-and-time picker labelled **"End Time (optional)"** — nothing says a later day is allowed, and nothing stops a date typed by accident |
| Propose Investigation Dates | the same, per slot |
| Six display sites | print the end as a **time only**, so a week-long visit reads as though it ended that evening |
| Server | accepts an end **before** the start without comment |
| `NewInvestigationWindow` | has no end field at all |

## The rule this builds

A **single-day** investigation is one arrival and one departure. It may cross midnight — 3pm until
8am the next morning is one investigation, not two days — so the end is a **clock time**, and a
time at or before the start's belongs to the next morning.

A **multi-day** investigation is anything longer: a weekend, a week. It is behind a checkbox, so
the ordinary case stays two fields and a time.

## Built

- **`InvestigationWindow`** in `Ben.Web.Services` — `SingleDayEnd` and `NeedsMultiDay`, the one
  copy of the rule. Three screens ask it, so they cannot disagree.
- **`ToDisplaySpan`** in `DateTimeViewerExtensions` — three readings: `07:00 PM – 11:30 PM`,
  `03:00 PM – 08:00 AM next day`, and `09/14/2026 03:00 PM – 09/21/2026 08:00 AM`.
- **Seven display sites** now use it: the case Investigations list, the proposal cards, the
  client's case page (visits and proposal slots), My Investigations, and the group's
  investigations table. The public investigation page gains a second date only when the visit
  genuinely spans days.
- **Three forms** get the same shape — end as a clock time, with **Runs over more than one day**
  turning it into a date and time: the case schedule/edit dialog, the Propose Dates slots, and
  the case-less Schedule window, which had no end field at all.
- **The server refuses** an end at or before the start, on create, on update, and on every
  proposed slot. Nothing checked it before, and the calendar event built from it inherited the
  same reversed window.
- Help: a new **How long a visit runs** section in `working-a-case.md`.

Not in scope: the iOS app, which shows the start only. Correct as far as it goes, and build 3 is
with App Review — a change there costs a new build. Recorded rather than done.
