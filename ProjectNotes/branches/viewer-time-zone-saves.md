# fix/viewer-time-zone-saves: typed times are on the reader's clock

Branched from `develop` at 50c4395d, 10/16/2026.

## The bug

On production, a group's Investigations → **Schedule an investigation**, typed from Central:
Starts 10/16/2026 06:00 AM, Ends 10/30/2026 11:00 PM. The list then read
"10/16/2026 07:00 AM – 10/31/2026 12:00 AM CDT", one hour late.

Ben's rule: "if I enter 7pm to 7am, it knows I am referring to in my location which is
central, before adding it as UTC ... but for standard sake we keep it as UTC and convert the time
based on the user's location."

## The cause

`NewInvestigationWindow` saved `_when.Value.ToUniversalTime()`. The site is Blazor Server, so that
runs on the server and converts through the **server's** zone (production's is a fixed UTC-6 with no
daylight saving), not the reader's. 06:00 typed in CDT was stored as 12:00 UTC and read back as
07:00 CDT. `DateTime.Now` and `DateTime.Today` have the same problem: they are the server's clock.

The display was never wrong. The list (`BenTime`, through `Zones.ToZone`) and the calendar
(`ToViewerLocalTime`) both convert stored UTC correctly. Only the saved values were off. Visits
already saved this way stay an hour late until somebody edits them.

## What changed

| Where | Was | Now |
|---|---|---|
| NewInvestigationWindow: save | `.ToUniversalTime()` (server zone) | `InvestigationWindow.ToUtc(start, end, UserState)`, which converts each end through the viewer's zone. The form now says "Times are in your time zone (CDT)." |
| NewInvestigationWindow: default start | `DateTime.Now + 1 day` (server) | `NowInViewerTimeZone() + 1 day`, after `AuthReady` |
| SchedulingProposalPanel: save | **sent unconverted**, so a 7 PM option was stored as 7 PM UTC and read back 5 hours early | `InvestigationWindow.ToUtc(...)`; the dialog names the zone |
| SchedulingProposalPanel: default slot | `DateTime.Today + 7 days, 7 PM` (server) | the viewer's today; slots are seeded when the dialog opens |
| InvestigationRoster: check-in | default `DateTime.Now`, saved `.ToUniversalTime()` | default `NowInViewerTimeZone()` when the box opens, saved `ToUtcFromViewerLocal` |
| FieldKitPlayer: phone moments | `.ToUniversalTime()` | `DateTimeOffset.UtcDateTime`; an offset-less moment is taken as UTC. Not a bug in practice: the phone writes `Z`, so it was a no-op. |
| BenDateField / DateEntry | empty calendar opened on, and a bare time landed on, the server's today | the viewer's today (`DateEntry.Parse` takes an optional `today`) |
| OrgScheduler, MyCaseDetail | week/calendar opened on the server's today | the viewer's today, after `AuthReady` |
| OrgEquipmentServiceLog, AdminAuditLog, AdminStoreSellerDetail, EquipmentCheckoutDetail | date defaults from the server's today | the viewer's today |
| InvestigationPanel, OrgScheduler, MyCaseDetail form models; OrgEvents, OrgEventPage, PublicTourPage; StyleGuide; footer year | `DateTime.Now`/`Today` placeholders | `DateTime.UtcNow` placeholders; each is overwritten before it is shown, or a day either way does not matter |
| PublicEventDetail, EventMenuEditor | `DateTime.Today.Add(timeOfDay)` only to format a time | `DateTime.MinValue.Add(...)` |

Left as they were, because they are right: `InvestigationPanel` (case-bound) and `OrgScheduler`'s
event form name their clock with a zone picker and convert through it (`Zones.ToUtc`). Ben checked
the event form on production on 10/16 and its stored UTC was correct. `FeedComposer` already
converted through `ViewerTimeZone`.

## Guard and tests

- `Ben.Web.Tests/Services/ServerClockSourceGuardTests.cs` fails on `.ToUniversalTime()`,
  `.ToLocalTime()`, `DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now` and `TimeZoneInfo.Local`
  in `.cs`/`.razor` under Ben.Web.Website, Ben.Web.Website.Library and Ben.Web.Services, after
  stripping comments (including multi-line `@* *@`). The allow-list is empty and fails on stale
  entries. Checked against the un-fixed `NewInvestigationWindow.razor`: it reports lines 336, 517
  and 518.
- `Ben.Web.Tests/Services/ViewerTimeZoneSaveTests.cs`: the reported window, the round trip, an
  overnight 7 PM 10/31 → 7 AM 11/1 Central visit across the fall-back change (13 hours in UTC), the
  spring-forward night (11 hours), a Pacific viewer, and `DateEntry`'s `today`.

## Checked but not changed

- Group calendar, week of 10/11–10/17: the 10/16 event and investigation are drawn in the all-day
  row from Sat 10/17. `OrgScheduler.ToAppointment` hands the scheduler viewer-local times (Fri 07:00
  CDT), so it is not a conversion in our code. Telerik draws an appointment longer than a day in
  the all-day row. Starting on the next day looks like how Telerik lays out a multi-day bar that
  starts after midnight, but this was not confirmed: the layout runs after JS measurement, which a
  static render cannot reproduce. To check, open the week view in a browser.
- The event form's "Where (a place)" search uses the duplicate-place matcher, which matches a name
  exactly (or as a near-identical misspelling), not as a search, so "Bell Witch" never finds
  "Bell Witch Cave". See the report for this branch.
