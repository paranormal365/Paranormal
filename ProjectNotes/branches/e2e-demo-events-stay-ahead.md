# fix/e2e-demo-events-stay-ahead: the test database's demo events stay in the future

Ben, 10/22/2026: "Do this task here: Keep seeded demo events in the future for e2e." It came out of
`fix/text-box-cursor`. On the default test database, `IsHauntedDb_e2e` (built in September), a full
browser run had 20 failures, such as "This event is over", "This event isn't taking bookings" and no
Ask form. The same tests failed on plain develop and passed on a fresh database.

Started from `develop` at 4a98f066.

## The cause

`HostedEventDemoSeeder` creates its two events once: the Thomas House Séance Weekend 30 days after
the database is first seeded, and An Evening of Evidence 45 days after. It never touches them again.
On 10/22 the old test database had the weekend on 10/16–17, already **Ended** (and due to be
archived on 10/31). An Evening of Evidence was on 10/30, eight days out.

## The fix: `KeepThemAheadAsync` in the seeder

On every API start (development only, like the rest of the seeder), a demo event whose last night is
less than 14 days away, or that the scheduled job has already moved on, is moved forward:

- **By whole weeks**, so the weekend is still a Friday and Saturday, back to its usual distance
  (30 or 45 days).
- **What moves with it:** the dates, the nights, the program, and any booking or go/no-go deadline.
  Program times move in the event's own time zone, so 9 PM stays 9 PM across the November clock
  change.
- **If it had already gone live, ended or been archived,** it goes back to Published, as it was
  when first published. Its after-the-event stamps are cleared: thank-you and results letters
  unsent, the room open again.
- **The group calendar's copy** of the event is rewritten to match (`HostedEventCalendarSync`).
- **Left alone:** an event that was called off (by its organizer, its venue or the site), and
  everything people added to it.

The archive job only turns down bookings still waiting, and the seeded dietary party is confirmed,
so nothing needed restoring there.

On the old database, the first start logged:

```
[HostedEventDemoSeeder] Moved "Thomas House Séance Weekend" 6 week(s) on, to 11/27/2026.
[HostedEventDemoSeeder] Moved "An Evening of Evidence" 6 week(s) on, to 12/11/2026.
```

## Also: a search box on Administration → Group subscriptions

One more test failed only on the old database: `NewGroupJourneyTests` could not find its new group.
The subscriptions grid lists every group, 25 to a page, with no search, and the old database has
more than a page of earlier runs' groups. A SuperAdmin on a busy site would have had to page through
too. The grid now has Telerik's own search box (`GridToolBarSearchBoxTool`), and the test searches
for its group.

## Found while running the suite, and fixed here (Ben: "keep on fixing issues you find")

The first full run on the old database left two failures. Neither was about dates.

**The home page's prices blinked.** `HomeJoin` fetched the prices again when the page came alive
and drew the list without the ghost walk and event lines until they arrived, so on every visit the
lines vanished for a moment and came back. A test caught it by grabbing the line just before it
went ("Element is not attached to the DOM"). The server's prices now ride into the live page
(`[PersistentState]`, as the Pricing page already does). `PagesDoNotBlinkTests` looks only for
spinners and could not see this, so
`EventCreditsAndToursTests.With_the_ghost_walk_subscription_on_its_home_page_line_never_vanishes`
watches the line itself. Without the fix it failed: "drawn, then disappeared when the page came
alive".

**A typed web address was joined onto the suggested one.** On the CMS's New Page dialog, the
address box was filled in from the title as it was typed. On a slow connection the fill landed
after somebody had clicked into the box, so their typing was added to it: the suite saved
`playwright-page-bd597160playwright-bd597160` and could not find the page. Two changes:

- The suggestion is now the box's **placeholder** (gray), never its text, and an empty box saves
  under it. The code never writes into that box, so nothing can be joined. Help text, the nine CMS
  help pictures (`Capture_CmsEditor`, on a fresh database) and the product PDF are updated.
- **`BenTypedBox` puts right a change from code that crossed a keystroke.** This applies to any
  box that code changes while somebody types. The browser confirms each screen update, and its
  messages arrive in order, so a keystroke that reaches the server before the confirmation was
  typed before the change landed. For those keystrokes only, the box is drawn once with the change
  and then with the typing. The page is told what was typed first, at once. A first version waited
  before telling it, and a Save already queued behind the keystroke then saved the old address.
  `CmsAuthoringTests.An_address_typed_after_the_title_is_kept_on_a_slow_connection` (Chrome
  throttled to 250 ms) showed `slow-page-…` instead of `slow-…` before the fix.

## Tests

- `Ben.Web.Tests/Services/DemoEventsStayAheadTests.cs`: whole-week moves land on a Friday at least
  the lead ahead and less than a week past it; a 9 PM session keeps 9 PM across the clock change.
- `HostedEventLifecycleTests.No_hosted_event_decides_what_it_is_from_a_timestamp` caught a first
  version that filtered on `CancelledAtUtc`. It reads `LifecycleState` now.

Browser runs on the **old** `IsHauntedDb_e2e`, without recreating it:

| | Before | After |
|---|---|---|
| The 28 failures from 10/22 | 2 checked, both failed ("This event is over"; no Ask form) | 27 passed; the 28th was the group journey, then fixed |
| Group journey + the other subscription-page tests | failed (group not on page 1) | 10 passed, 1 skipped |

Full suite on the **old** `IsHauntedDb_e2e`, 10/22:

| Run | Passed | Failed |
|---|---|---|
| Before this branch (from `fix/text-box-cursor`) | 1,028 | 28 |
| With the seeder fix | 1,053 | 2 (the home blink, the joined address) |
| With everything here | 1,057 | 1: `StoreProductTests.Product_description_is_styled` |

That last one read a paragraph at the moment the live page replaced it (NaN). It had passed in the
six earlier full runs. It now looks the paragraph up afresh on each read and reads again if it
catches the swap; the store product tests pass (5 of 5).

