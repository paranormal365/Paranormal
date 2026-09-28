# The seeded hosted events reach the public site

`HostedEventDemoSeeder` built its two demo events — the Thomas House séance weekend and An
Evening of Evidence — as **drafts**, with a comment saying so on purpose: the seed exists for the
plan screens, and publishing looked like it would spend one of the group's event credits every
time a database was built.

`HostedEventStates.OnThePublicSite` is `[Published, Live, Ended]`. A draft therefore has no public
page at all — `PublicHostedEventController` answers 404 by design, so that a draft does not leak
that something is being planned. Every visitor-facing surface built on top of those two events had
nothing to open.

## How it stayed hidden

The long-lived `IsHauntedDb_e2e` database passes, because earlier sessions pressed publish on
those events by hand. A full Playwright run on a brand-new database
(`BEN_E2E_DB=IsHauntedDb_e2e_full`, 2026-09-17) failed **eight fixtures**, none of them on a
product defect — all of them on the precondition, worded as
*"the seeded rooms weekend is not on the public site"*:

- `EventFilesTests.A_public_file_reaches_a_visitor_and_a_staff_file_does_not`
- `EventAfterTests.A_guests_review_page_says_reviews_open_once_the_event_is_over`
- `EventGalleryTests.A_picture_added_by_the_host_leads_the_event_page`
- `EventGalleryTests.On_a_phone_the_ask_button_stays_on_screen`
- `EventGalleryTests.A_guest_posts_a_photo_and_the_organizer_sees_it_on_the_wall`
- `EventGalleryTests.The_add_photos_page_fits_a_phone`
- `EventRoomTests.The_room_shows_on_the_event_page_and_a_post_can_be_taken_down`

A seeded state that only works on a database somebody has already edited by hand is precisely the
drift the isolated-database harness was built to catch.

## What was done

A new block in the seeder, `PutThemOnThePublicSiteAsync`, publishes the two events **by the route
the publish button takes**, rather than by setting a column:

1. **The readiness checklist first.** `VenueGrants.ForEventAsync` then
   `HostedEventReadiness.Describe` — the same list the endpoint refuses on and the event page
   draws. If an item is not done the event stays a draft and the console says which one. A seeder
   that published past the checklist would be seeding a state the site would not let anybody reach.
2. **The entitlement, asked and then taken.** `HostedEventEntitlement.DescribeAsync` says whether
   this group pays by plan or by credit; `TakeForAsync` is what actually spends, in the same save
   as the publish.
3. **`FirstPublishedUtc` is set.** Without it the event would be charged for the first time the day
   somebody unpublished and republished it — the column exists precisely so that the second publish
   of an event is free forever.
4. **The umbrella calendar row is written**, through `HostedEventCalendarSync.SyncAsync`. This was
   a second drift nobody had noticed: a real create writes that row, the seeder never did. It is
   what carries `/o/{org}/events/{slug}`, the share tags, the reminder, the calendar file and the
   sign-up, and `PublicHostedEventRecord` reads its id. Written *after* the lifecycle state,
   because the row takes `IsPublic` from `IsOnThePublicSite`.

### The credit is seeded, not skipped

If the deployment's price list puts the group on the credit path and it holds none, one credit is
granted first and then spent by the ordinary path — the **SuperAdmin grant shape**:
`PriceAtPurchase = 0`, a `GrantedReason`, no provider reference, no receipt and nothing in the
ledger, because no money changed hands and a $99 sale that never happened has no business in the
money trail.

Nothing is granted to a group whose plan already includes hosting, and nothing to a group that
already holds credits. On a fresh dev database nothing excludes `TierCapability.HostEvents` from
any band and no `ActiveHostedEvents` cap is seeded, so `paranormal365` is on the plan path and
publishes for nothing — the original "it would spend a credit every time" worry did not hold, but
the code no longer depends on that staying true.

Same reasoning as `BillingDemoSeeder` opening its subscriptions through `PeriodOpener`: a seeded
row that did not come from the real code is a row that will quietly stop matching it.

### Idempotent and additive

Gated on its own marker, like every other block in the file: only an event still in `Draft` that
has **never** been live is touched. A second run finds nothing. An event somebody unpublished by
hand while testing keeps its `FirstPublishedUtc` and is left exactly as they left it — the seeder
never undoes a state a person chose.

## Files

| File | Change |
| --- | --- |
| `Ben.Data.WebApi/SeedData/HostedEventDemoSeeder.cs` | `PutThemOnThePublicSiteAsync` and `ThereIsSomethingToPublishAgainstAsync`; the two "Draft on purpose" comments now point at them |

No migration, no schema change, and nothing outside the development-only seeder.

## Proof

`BEN_E2E_DB=IsHauntedDb_e2e_pub2 scripts/run-e2e.sh --filter "FullyQualifiedName~Event|FullyQualifiedName~HomeMapTests"`
on a database name that had never existed: **136 passed, 1 failed, 21 skipped**. All seven hosted-event
fixtures above pass. The API's own log says what it did:

```
[HostedEventDemoSeeder] Put "Thomas House Séance Weekend" on the public site on the group's plan.
[HostedEventDemoSeeder] Put "An Evening of Evidence" on the public site on the group's plan.
```

and `/api/public/hosted-events/{id}` answers 200 for both, carrying a real `umbrellaEventId`.

## Two things found on the way

### `List_AuthUser_SeesVoteButtons` is a different bug

Listed with the eight, and **not** this cause — it is `HomeMapTests`, about the vote widget on the
home feed, and it never asks about a hosted event. It is nonetheless a real fresh-database failure,
and it is the **test** that is wrong.

`CaseVoteWidget` has one button doing two jobs. Unvoted, pressing it opens the three choices. Once
you have voted, its own label reads *"Your vote: … Press to take it back"*, and
`VoteButtonPressedAsync` returns after withdrawing the vote without ever setting `_picking`. The
test took the first `.vote-btn` on the page — and on a fresh database the default **Most Votes**
order puts the seeded cases this account has already voted on at the top. So the click withdrew a
vote, no chooser opened, and the assertion failed on behaviour that was working correctly. It only
passed on the long-lived databases, where drift had pushed an unvoted case to the front — the same
shape of rot as the seeder above, in a different place.

Fixed by asking for the unvoted button by its accessible name (`"Vote"`, exact) instead of by
position. Separate commit; nothing to do with the seeder.

### `scripts/run-e2e.sh` killed the run it refused to disturb

`trap cleanup EXIT` was armed **before** the "are these ports free" check, and `cleanup` ends in
`pkill -f "Ben.Data.WebApi"` and friends. So a run that politely refused to start because somebody
else's hosts were up then killed those very hosts on its way out.

That happened twice on 2026-09-17, in both directions between two sessions on this machine. The
second time it killed an API 1m51s into the suite: **124 of 158** tests failed on
`connect ECONNREFUSED ::1:5252`, with nothing wrong in the code. The trap is now armed only once
the ports have been found free, so a refusing run leaves nothing but its own empty temp directory.
