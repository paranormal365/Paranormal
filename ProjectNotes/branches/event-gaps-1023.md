# fix/event-gaps-1023

A walk of the hosted-event journey on 10/23/2026 (live site, read-only, plus a code read against Ben's Event
Document). Ben said "fix them" for the five with no business choice in them.

| # | Gap | Fix | Tests |
|---|---|---|---|
| 1 | Taking a booked date off a published event failed in the database (bare error); nobody holding a place was told about new dates, times or venue | `WhyDatesCannotGoAsync` refuses in words, naming the date and the parties; dead bookings' night rows, unit blocks and emailed-pick places on a removed date are cleared. New mail kind `event-changed` (`SendEventChangedAsync`) after a save that changes dates, usual times, zone or venue on an event taking bookings | `HostedEventControllerTests`: three new, all fail on the old controller |
| 2 | "Call it off" and "No, call it off" worked on an event that had happened | `Cancel` refuses once Ended/Archived or past the last night's end (an event under way can still be stopped); go/no-go refuses once started. Buttons hidden | four new tests, fail on the old controller |
| 3 | A guest turned down was refused "Ask again" ("You have already asked") | TurnedDown no longer blocks a new request; change/withdraw act on the standing booking first (`Newest`) | `ABookingRequestCommitsWithItsLetterTests`, fails on the old code |
| 5 | Auto-archive at 14 days closed the page, results link and evidence while reviews/evidence ran 60 days; the job never synced the calendar row | Waiting requests still closed at 14 days (`CloseWaitingAfter`); archive at 62 days (`ArchiveAfter`); the job syncs the calendar row on archive | `HostedEventLifecycleJobTests` |
| 7 | Guest messages had no link on the event's menu | Added to `ContextRail`; guard test that every `EventScreens.Organizer` screen is on the menu | `EventMenuReachesEveryScreenTests`, fails without the link |

Help (going-to-an-event, organization-administration), webapp changelog, product PDF.

## Still open, waiting on Ben

- What one credit covers with separate dates, and moving a paid event's dates (#8 of the walk).
- Archiving an upcoming event: block it, or tell the guests (#4).
- Polish: un-cancel's "free" sentence, no-shows reviewing, guests changing party size, walk-ups and
  after-event letters, reviews in the app, a long day pass listed night by night.

Not yet run in the browser suite: the test database server was unreachable on 10/23.
