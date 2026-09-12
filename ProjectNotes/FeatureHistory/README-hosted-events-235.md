# Item 235: Hosted events — venues, multi-night events, bookings, sessions, staff, files, pages, collaboration, and the phone

## Context

Ben, 2026-09-11, on the three kinds of paying customer: personal, ghost tours, and **event creators**, "the one I don't think we have addressed". His brief across the day, kept in his words in the README that Phase 0 writes:

- Venues like **The Thomas House Hotel**: events over multiple nights with multiple people; schedules; check in and out; checklists / to-do lists; files for the event in a folder (PowerPoint slides); attendees using the iPhone/iPad app during the event to upload and share with other attendees and organizers.
- "Start to finish": a certain number of **rooms, each allowing a certain number of guests**; selling access to **people outside those staying**; a **menu** for the food served.
- **Permissions for the different employees** of the event. Ideas he has not thought of: **advertisements**, and **flashy event pages** they can create.
- Publish event info in the apps; optional **classes/sessions with capacity** ("Operating the Ovilus, 9–10am, up to 15"); some events have them, some do not.
- An ordinary **organization may buy an event package** to hold an event.
- **Collaboration**: Thomas House runs its own events, but somebody else may want to host there and ask permission to use its photos, history, rooms and capacity. "I want people to be able to work together."
- Events tie into a **new Event section of the iPhone/iPad app**.
- **QR-code tickets** after the organizer confirms a reservation: revocable, reservation editable, emailed or generated in the app; an Apple Wallet pass is a future enhancement.
- "Bells and whistles and flash make this site better for hosting tour pages and event organizations. Functionality and look and style are important."
- Break it into phases and sub-phases; put it on its own branch so nothing in flight is at risk.

The survey (three read-only passes over the code) found far more in place than the brief assumes, and the genuinely new ground is sharp-edged. Existing: `OrgCalendarEvent` with capacity, zone, slug, public flag, place; tour seats with request→reserve→turn-down and multi-seat parties; walk-up email invites that create passwordless accounts; guest mail with `.ics`; 24h reminders; attendee evidence submissions with a review queue; per-org rooms at a place (`PlaceRoom`, no capacity); custom roles + tier-gated permission areas; the CMS page/section/template system; free SuperAdmin-approved ads; per-tour billing with mid-period proration; the feed with media screening and scheduled posts; on the phone, an event detail screen, seat reminders (local notifications), tours discovery, and single-shot uploads. **Not existing anywhere**: tickets/prices, nights, room capacity, room↔event, sessions with capacity, event check-in, checklists, menus, per-event files/folders, per-event staff, org-to-org grants, venue ownership of a place, kind-targeted tiers, packages, push, event-scoped sharing, phone-side evidence submission.

## The branch

`feature/hosted-events-235`, cut from `develop` (`d9c8a94d`, 2026-09-11) once everything in flight was landed and pushed: the Price Bands and Site Settings tidy (`d9c8a94d`, merged to `master` as `04877ba7`), the item 232 fix (`0ff19631`), and the clock sweep (`cc9ce1cc`). Every phase below is committed here; nothing reaches `develop` until Ben says so.

## Status

**Phase 0 done, 2026-09-11.** Every append-only value is fixed before anything depends on its
number, and the four guards that keep those enums honest all fired first and then passed:

- `RolePermissionCoverageTests` named all six new security tables as unassignable until the role
  editor got a row — with a description — for each. That is the guard working exactly as designed.
- `MyOrgPermissionsAreaCoverageTests` named `Events` as an area the permissions endpoint would never
  mention, so no screen could have offered it.
- `PermissionAreaMapGuardTests` held once the six map rows existed.
- New `TierLabelCoverageTests` scans the price-band page for a label per cap, capability and area,
  because those three switches end in `_ => value.ToString()` — a value nobody labels does not break
  the page, it just asks a SuperAdmin to set `EventFilesMegabytes` by its enum name.

Suite: .NET 8,213 pass, 0 fail. No migration, no behaviour change, nothing yet reads the new values.

**Phase 1.1 done, 2026-09-11** — the model, the migration, the umbrella sync and the entitlement.

- `HostedEvent` and `HostedEventNight`, `OrgCalendarEvent.HostedEventId` with a filtered unique
  index so the database says "one umbrella" rather than a convention somebody has to remember.
  Migration `20260911223215_HostedEvents`, reviewed for drops (all five are in `Down`, undoing only
  what it made) and applied to `IsHauntedDb_player` with an explicit `--connection`.
- `DatesAreSeparate` is in from the start, per Ben: false is a stay (consecutive nights, one booking
  across several), true is a run where **the event is the production and each date is a performance
  of it**. Billed once either way — a monthly show is one production on twelve dates.
- `HostedEventCalendarSync` writes the umbrella row: the event's name, summary, place, zone and slug,
  public only while published, spanning the first date's start to the last date's end **in the
  event's own zone**. Eight tests pin the timekeeping with no database in them, including the
  weekend that straddles the clocks going back — 30 October to 1 November 2026 reads as 52 hours off
  a wall clock and is 53 hours lived, and the span carries the extra hour. My arithmetic was wrong
  first and the test caught it.
- `HostedEventEntitlement` is the single place that answers "may this go live, and what does it
  cost": a plan holder against `ActiveHostedEvents` with the guard's own wording, everybody else
  against credits, with a refusal that names the $99 and says nothing already built is lost.
- `OrganizationPurgeCoverageTests` fired on both halves — a table belonging to a group that nothing
  deletes, and a foreign key that would refuse the deletion. Hosted events now purge with their
  group, after the calendar rows that name them.

Suite: .NET 8,227 pass, 0 fail.

**Phase 1.2 done, 2026-09-11** — the endpoints, the event list, the event's own page, and the public
layout.

- `HostedEventController` (list, one, plan, create, update, one date, publish, unpublish, archive,
  restore, cancel, uncancel) and `PublicHostedEventController` (one by id, one by org and slug,
  what's on). Publishing is its own endpoint because it is the only act that spends money.
- **A venue we have never listed** (Ben, 2026-09-11): searched first, and otherwise the name and
  address are entered with the event. The place is created, geocoded best effort, and marked public
  — the opposite of every other inline place here, where the cautious answer is "somebody's home".
  Entering one that already exists quietly uses the existing row, because a venue is shared and two
  rows for one building splits its map pin and its history in half.
- The calendar refuses to edit an umbrella row, naming the event and pointing at it.
- `OrgEvents.razor` and `OrgEventPage.razor` (anchor index, not tabs), with the publish confirmation
  that says what it costs, what is left and that it does not come back. `PublicEventDetail.razor`
  grows a dates section when the row is an umbrella, and is untouched for every ordinary event.

**Found by opening it.** The public page showed "4 hr — on your feet" for a hotel weekend, which is
a walk's phrase; and it offered the evidence form although the host had not asked for it. Both are
the "not ghost hunting related" rule leaking, and both are fixed — the note now says "over 3 nights"
where that is true, and the evidence section is the host's switch.

**Three guards fired and were right.** `OrganizationPurgeCoverageTests` on a table nothing deleted;
the help-link guards refused the branch until the "Hosted events" chapter existed; and
`LabelAssociationTests` caught a label pointing at a rich editor, which is not a labelable control.

Suite: .NET 8,239 pass, 0 fail. Walked on the running site against `IsHauntedDb_player`.

**Venue made editable on the event page, 2026-09-11** (Ben). The same search and the same "enter
one we haven't listed" as when the event was made, behind a deliberate *Move it somewhere else*
rather than a dropdown somebody brushes past. The move happens on save.

**Phase 1B in progress, 2026-09-11** — credits: the model, the spend, the purchase and the tests.

- `EventCredit` with migration `20260912003414_EventCredits` (no drops in `Up`), applied to
  `IsHauntedDb_player` with an explicit `--connection`. Owned by a group **or** a person, never
  both, said by a check constraint rather than left for every query to remember.
- `EventCredits` holds every rule in one place, and the one that matters most is **oldest usable
  first**: spending the newest would let the oldest lapse while its owner was actively using the
  site, which is somebody's money quietly thrown away.
- **The year is checked at both ends** — the credit unexpired, and the event's first date inside its
  year. Ben's "a year to have hosted the event" read literally, and it stops a credit being parked
  on a placeholder dated years out. The refusal names both dates.
- The credit is taken **in the same save as the publish**, so two tabs cannot spend one credit twice
  and a credit can never go missing without an event going live for it.
- Purchase through Stripe Checkout, metadata `ih_event_credits`, fulfilment idempotent on the
  payment reference, taxed through the same resolver a subscription uses, ledger Charge and Payment
  pair with a receipt number. Price and on-sale switch are site settings, grouped under a new
  **Events** section; the price is frozen on each credit so moving it never reaches one somebody
  holds.
- Thirty-one tests. Beside the rules above: a refusal names the $99 and leaves the draft with all
  three of its nights intact; re-publishing never spends a second credit; and a credit that runs out
  before the event is refused without being taken.

**Two guards fired and were right.** `OrganizationPurgeCoverageTests` on credits blocking a group's
deletion, and `LoadResultRenderedGuardTests` on the venue search swallowing a refusal — a search
that was refused and a search that found nothing look identical to a person, and saying "no venues
found" when the truth is "we couldn't ask" sends them off to invent one that already exists.

**Phase 1B finished, 2026-09-12** — the warning job, the card, the SuperAdmin refund, and the walk.

- `EventCreditExpiryJob` warns the holder thirty days out, once per credit, marked on the credit
  itself. **Email where there is an address, the bell where there is not** — a credit somebody paid
  for must not lapse unannounced because a group has no reachable billing contact or a deployment
  has no mail server, and on a dev machine that is every deployment. The marker is stamped after
  the send, so a failure is retried and the residual risk is a duplicate rather than silence.
- The **credits card** on a group's billing page, at `#event-credits`: what is held and when each
  lapses, what each cost, and — for the ones already spent — which event each went on, because the
  question a year later is "did we pay for the October weekend?" and a list that drops the answer
  cannot be asked. The refusals on the events list and the publish confirmation both link into that
  anchor, so no refusal is a dead end. Its `ItemResult` means a refusal can never be drawn as "this
  group has no credits" — one of those is somebody's money.
- **`/admin/event-credits`**, and it exists because `RefundedUtc` was otherwise a column only SQL
  could write. A refund needs a reason, marks the credit unspendable whatever its date, and writes
  the credit adjustment on the ledger — untickable for one already put back through Stripe. A
  **spent** credit is refused and the refusal names the event: handing back the credit for a live
  event would make that event's own payment record untrue, and re-publishing would then never
  charge. An **expired** one may still be refunded, which is the goodwill case.

**Twenty-five .NET tests and three Playwright tests.** The help-link guards fired first, as designed,
and refused the branch until both chapters existed. The lapsed-credit test was proved to
discriminate by removing the lower bound from `DueAWarningAsync` and watching only that test fail.

**Phase 2.1 done, 2026-09-12** — the booking model, its migration, and the counting rule.

- Six tables and four columns, all additive: `HostedEventRoom` (which rooms this event offers, with
  a capacity override for the weekend a double sleeps four), `HostedEventBooking` (a party, not a
  person), `HostedEventBookingNight` (one row per booking per night per room, because capacity is
  asked per room per night and a range would make it a question about overlapping intervals),
  `HostedEventBookingGuest` (an account is optional — a guest brings a partner who will never sign
  up), `HostedEventMenu` + `HostedEventMenuItem`. `PlaceRoom` gains `Capacity?`, `IsBookable` and
  `BedNote`; `HostedEvent` gains `BookingsCloseAtUtc?`. Migration `20260912141103_HostedEventBookings`,
  `Up` reviewed operation by operation — four AddColumn, six CreateTable, eighteen CreateIndex, no
  drops and no alters — and applied to `IsHauntedDb_player` with an explicit `--connection`.
- **`HostedEventBookingStatus` is a new enum rather than `TourSeatStatus`.** That one has no way to
  say a guest withdrew, because a walk's sign-up is simply deleted; a booking cannot be, since the
  room-nights it held and the dietary notes it carried are a record the venue has already catered
  against. `Cancelled` is a state, kept and dated. The word differs too: a room is *confirmed*, not
  *reserved*.
- **`EventCapacity`**, mirroring `TourSeats` for the reason that class exists — four screens asking
  the same question four ways is four chances to put two parties in one bed. Only `Confirmed` holds
  anything (DECISION 7); a party of zero still counts as one person; a room with a null capacity
  cannot be over-filled while a room stating zero sleeps nobody; and editing excludes the booking's
  own beds, so moving a party from the Blue Room to the Suite is not refused by itself.
- **27 tests, proved to discriminate**: letting a request hold a bed failed three of them, and
  counting the event instead of the night failed one. Both were reverted and the suite is green.

**Phase 2.2a done, 2026-09-12** — the venue's side of bookings: the board, the decisions, the rooms.

- **`HostedEventBookingController`** at `api/organizations/{orgId}/events/{eventId}/bookings`. The
  board is one call — rooms offered, how full each is per room per night, and every booking — because
  a host deciding a Saturday needs all three on one screen. Confirm puts the party in rooms that
  need not be the ones they asked for, since moving a party of three out of a double and into the
  suite is the commonest thing a host does and making them turn it down and ask again would be
  absurd. Turn-down and cancel both release the umbrella row. Edit re-checks capacity but excludes
  the booking's own beds, so a party moving rooms is not refused by the beds it is leaving.
- **Confirmation writes the umbrella attendee** with `RsvpStatus.Accepted` alongside
  `TourSeatStatus.Reserved` and `Seats` = party size — the pair every existing count already
  understands, so the public list, the reminder job, the calendar file and the shipped phone all
  count the party without knowing what a booking is. A test asserts `TourSeats.PlacesTaken` reads
  it, which is the actual contract.
- **Rooms offered** on `HostedEventController` (`GET`/`PUT .../rooms`). Replaces the whole set,
  because the screen is tick boxes; refuses a room with confirmed bookings rather than quietly
  dropping it, and refuses a room belonging to another group's description of the venue.
- **A guard caught a real defect before it shipped.** `OrganizationPurgeCoverageTests` named
  `HostedEventBookingNights.PlaceRoomId` and `HostedEventRooms.PlaceRoomId` as NoAction references
  the group purge never deletes — so deleting a group would have been refused by the database,
  which is exactly what happened on production twice before that guard existed. Both are now purged
  before `PlaceRooms`.
- **8 behaviour tests on `SqliteTestDb`** with foreign keys on, proved to discriminate: dropping the
  unique index on booking-night failed the "a party cannot hold two rooms on one night" test. Suite:
  .NET 5,095 pass, 0 fail.
- **Still open in 2.2**: the guest's own door (request, my-booking, acknowledge, cancel), the
  anonymous email path, create-on-behalf by email through the guest-invite token, the dietary tally
  and menus endpoints. Create-on-behalf takes an existing account today and says so.

**Phase 2.2b done, 2026-09-12** — the guest's own door.

- **`PublicHostedEventBookingController`**: ask for a place, read what the venue said, change it,
  acknowledge it, withdraw it, and `GET api/public/hosted-events/mine`.
- **Asking is deliberately not capacity-checked.** A request holds nothing, so refusing one because
  a room is full would close the very waiting list the host wants. What IS checked: the event is
  published, not called off, and inside its deadline.
- **Withdrawing means two different things**, and this is the rule worth remembering. A REQUEST is
  simply deleted: nobody was holding anything, and leaving it in the queue would have the host
  decide on a party that is not coming. A CONFIRMED booking is not — the venue has catered, staffed
  and possibly turned somebody else away against it — so the guest ASKS and the host releases it. A
  room freed without the host knowing is a room that stays empty. New columns
  `CancellationRequestedUtc` and `CancellationReason`; migration
  `20260912143529_HostedEventBookingCancellationRequests`, two AddColumn and nothing else, applied
  to `IsHauntedDb_player`.
- **A guest editing what the venue agreed to sends it back as a request.** Changing the party size
  or the nights makes what was agreed to no longer the thing being asked for, and a party that
  quietly grew from two to six would be sleeping in a room nobody checked. Changing only a note or
  the guest names does not.
- **"One booking per person per event" means one LIVE booking**, or somebody who cancelled in March
  could never come in October.
- The guest's record is deliberately not the host's: no other party's details, no decision-maker's
  name, no dietary note but their own party's. A test proves the query cannot see another party's.
- **13 behaviour tests** on `SqliteTestDb`, proved to discriminate. Suite: .NET 5,112 pass, 0 fail.
- **Help, changelogs and worklog updated in this commit**: a *Rooms and bookings* chapter in
  `organization-administration.md`, entries in the website and service changelogs, and
  `ProjectNotes/DailyLogs/2026-09-12.md`. Product PDF rebuilt.

**Phase 2.2c done, 2026-09-12** — the email door, the menus and the kitchen's sheet. 2.2 is closed.

- **The anonymous email path is not a second door.** A stranger reaches a hosted event through the
  very machinery that already signs people up to a public event — `EventAttendanceInvite` pointed
  at the umbrella row, one single-use token, a fortnight. Building a second invitation, a second
  expiry and a second "is this really your address" would have given the site two answers to one
  question, and the older one would have gone on being the one that was maintained.
- **What is different is what the click lands as.** On an ordinary public event, confirming means
  you are coming. On a hosted event nobody comes until the venue says so (DECISION 7), so the click
  writes a DayPass booking in the Requested pile and an umbrella attendee row in the
  `Invited`/`Requested` pair — which is the shape a walk's requested seat already has. `isTour`
  became `asksRatherThanComes` and every branch that read it now reads that instead.
- **A second click never demotes a place the venue agreed to.** A confirmed booking holds the
  umbrella row at `Accepted`/`Reserved`, so the confirm path leaves a reserved row alone rather than
  writing it back to Requested. That one is easy to miss and would have quietly un-booked somebody.
- **The email door only ever sells a day pass**, and an event selling none refuses and names the
  page where a room can be asked for. Rooms are chosen night by night and a hyperlink is not a
  booking form; taking an address there and doing nothing with it would be worse than saying no.
- **Create-on-behalf by email is its own endpoint, returning its own shape.** No booking comes back
  from it, deliberately: an invitation holds no room and no day pass until somebody clicks it, and a
  booking-shaped answer would tell a host they had reserved something they had not. The
  `on-behalf` refusal for a missing account now names this door. **The known gap, recorded rather
  than hidden:** a host who took a phone call cannot hold a room for an address, only for an
  account. Holding one would need an unclaimed-booking model with no user id, and an unclaimed
  booking cannot write the umbrella attendee row every count on the site reads — so it would hold
  beds while the public number disagreed with it. The help text says make it against an account.
- **Mail failure is reported honestly to a host** and swallowed for a stranger. A host is not
  probing for accounts; they are the person who will stand at a door wondering why nobody came.
  The invitation is saved either way and the link is in the log.
- **Menus are one card for the whole weekend** (Ben, 2026-09-12: *"they may have a breakfast and
  lunch and dinner and snacks I guess. Maybe we just make it part of the Menu overall."*), replace
  the whole set on save, and each sitting must belong to a night of this event.
- **His message caught a real ordering defect before it shipped.** I had sittings sorted by serving
  time within a night, which prints an eight o'clock breakfast *before* the seven o'clock dinner it
  followed. A night here is the whole stay-period — the evening people arrive through the morning
  they come down — which is the only way breakfast has a night to belong to at all, and it means
  the host's own order is the only correct one. Proved by reverting the sort and watching the
  breakfast test fail.
- **Position in the list is the order**, for sittings and dishes alike; the `SortOrder` inputs are
  gone rather than left in to be ignored, because a parameter that lies is worse than none.
- **The dietary tally groups on the note as typed** and on nothing cleverer. "No nuts" and "nut
  allergy" are one requirement to a cook and two strings here; a tally clever enough to merge them
  would eventually merge two that are not, and a cook acting on a wrong merge poisons somebody.
- **The unnamed count is the number that matters most.** A party of four who listed two names
  leaves two people the kitchen knows nothing about, and a sheet showing only the notes would read
  as complete when it was half a weekend's guests. `EventDietary` is pure and separate from the
  controller for the same reason `EventCapacity` is.
- **A privacy fix taken in passing, and worth naming.** The booking board handed dietary notes to
  any org member, because reading the board takes membership. Those are named people's allergies,
  so they are now withheld from a member who cannot decide a booking, and the tally endpoint takes
  the deciding permission rather than membership. DECISION 10's door-staff view is phase 5's.
- **Menus reach a guest whose place is confirmed**, per the model's own rule, and a guest still
  waiting is told the venue publishes it once their place is agreed. *If Ben would rather a
  requester saw it too, that is a one-line change and the refusal is the only thing that moves.*
- **27 new tests**, two of them proved to discriminate by breaking the rule and watching only the
  right test fail: the breakfast ordering, and one-live-booking-per-person. Suite: .NET 8,374 pass,
  0 fail, 9 skipped.
- **Help, changelogs and worklog updated in this commit**: three new sections in
  `organization-administration.md` (inviting by email, menus, the dietary sheet), website and
  service changelog entries, `ProjectNotes/DailyLogs/2026-09-12.md`. Product PDF rebuilt.

**Ben's ask, 2026-09-12, taken next:** *"We should probably send a confirmation e-mail or offer it.
Generate a QR code for the confirmation the event organizer can scan to check them in when they
arrive so check in is smoother."* That is phase 2.3 (mail) and phase 3 (QR passes) as already
planned, brought forward to run back to back — 2.3's `EventGuestMailer` first, because a pass with
no letter to travel in reaches nobody.

**TWO CONFIGURATION FINDINGS, both from phase 0's new enum values and neither yet fixed.** On
`IsHauntedDb_player`, and almost certainly on production too:

1. **Every band includes `HostEvents`**, so no group is ever asked for a credit and the whole $99
   product is unreachable. That is the fail-open rule working as designed — a capability nobody has
   ruled on is included — but it means the credit path is live code nobody can reach until a
   SuperAdmin unticks hosting on Free, Small group and Large group. This is Ben's pricing decision,
   so it has not been made here.
2. **The `Events` permission area is on no band.** Phase 0 added it as area 10 and no band has been
   told about it, so custom roles cannot be granted anything under Events on any plan. Owners and
   administrators pass by role and would never notice.

The Playwright test that walks the refusal skips itself on this deployment for finding 1, and says
so rather than passing quietly.

**The SuperAdmin grant, added on Ben's word, 2026-09-12.** Nothing could put a credit in a group's
hands except a real card payment, so support had no remedy for "they paid and it never landed" and
the refund screen beside it could not be exercised on any deployment — there was never a credit to
refund. Phase 1B.6 had assumed a test-mode Checkout stub that does not exist.

- `GrantedReason` on `EventCredit`, migration `20260912122616_EventCreditGrants` — one nullable
  column, `Down` drops only what `Up` made, applied to `IsHauntedDb_player` with an explicit
  `--connection`.
- **A grant is not a sale and writes nothing to the ledger.** A $0 charge and payment pair would
  put a sale that never happened into the money trail, and a receipt would say somebody paid
  nothing. The reason and the granting admin are the record instead, which is why the reason is
  required rather than optional.
- **Ordinary in every other respect** — a year to use, spent at publish, oldest first, warned at
  thirty days, refundable while unspent. A granted credit that behaved differently would be a
  second product wearing the first one's name.
- It reads as **Granted** on the admin grid and **Given to you** on the group's card, never
  "$0.00": a zero in a money column reads as a bug and sends somebody hunting for a lost payment.
- Refunding a granted credit revokes it and writes no ledger row, because there is nothing to hand
  back. That guard was proved to discriminate by removing it and watching only that test fail.
- The quantity is clamped rather than refused, at the same ceiling the purchase uses — a typed 500
  is a slip, not an instruction.

Suite: .NET 8,277 pass, 0 fail. Playwright: the credit and admin categories, 18 pass and one
skipped for finding 1, against the running site on `IsHauntedDb_player`.

**FIVE PRE-EXISTING PLAYWRIGHT FAILURES ON THIS BRANCH, none of them from this phase.** A full run
turned up `OrgList_ShowsBenCo`, `TheMapTabDrawsTheMap`,
`A_visitor_reads_the_groups_finding_on_the_public_case_page`,
`CaseDetail_AuthUser_AllThreeVoteButtonsVisible` and `VoteCounts_PersistAfterPageReload`. Every one
of them reproduces identically with this phase's work stashed and the branch built from `HEAD`, so
they belong to the branch already and want their own look. The sidebar one is the clearest read: the
group's name resolves in the DOM and is `hidden`, which is a collapsed sidebar rather than a missing
group. `The_share_dialog_offers_a_picker_and_a_choice_fills_the_card` failed once and passed on the
re-run, so it is flaky rather than broken.

**A full clean Playwright run is still owed on this branch.** The one attempted here was compromised
— the solution was rebuilt underneath it, which is the same mistake as restarting a host mid-run —
and it was stopped rather than reported.

Each phase records its own "Verified, not assumed" section here as it lands.

## Hard rules the plan keeps

- **The site never takes guest money in the first release.** A confirmed booking means "the venue says money is settled", exactly `TourSeatStatus`. Per-ticket Stripe is the optional last phase and stays design-only until Ben decides.
- **Additive only while iOS 1.0.2 (4) is in review.** New response fields are nullable with defaults; no existing route changes meaning; the shipped app's `JSONDecoder` ignores unknown keys. Phone work is staged on the branch, no build-number bump.
- Migrations to `IsHauntedDb_player` with an explicit `--connection`; production only after a phase is verified on the running site.
- Pages over modals for manage-everything surfaces; every refusal has a UI path; help text + screenshots + product PDF + pitch in the same branch; tests proven to discriminate; Playwright fixtures captured from the real API; never `git add -A`.
- **Look and style are a deliverable**, not a finish. Every public-facing surface in this plan (event page, venue page, programme, booking card, the phone hub) gets a design pass against the tour page as the floor, not the ceiling.

## The model, in one paragraph

A **`HostedEvent`** is the product a business pays for (name, venue place, nights, description, cover, page, staff, files, menus, rooms offered, sessions), the way `Tour` is. Each hosted event owns exactly **one `OrgCalendarEvent` umbrella row** spanning first check-in to last check-out, kept in sync by a service — so the shipped phone, the public list, the 24h reminder job, `.ics`, evidence submission and the `/o/{org}/events/{slug}` URL all work on day one with no phone change. Nights are `HostedEventNight` children, not calendar rows. A **booking** is a party (lead guest, party size, overnight with room-nights or a day pass) that the venue confirms; confirmation writes the umbrella attendee row so every existing count keeps meaning "has a place". **Sessions** carry absolute times with first-come capacity and a waiting list. **Staff** are org members via a new `Events` permission area, or non-members via an emailed invite, with per-event flags. **Files** live in a folder under the org's storage with an audience. The **page** is an `OrganizationPage` with four new live-resolved section types. **Ads** target an event. A **venue profile** (history, photos, rooms, house rules) plus a hosting request produce the site's first **org-to-org grant**. Attendee sharing is an **event room** on the feed's message model, private to confirmed guests and staff, promotable to the public feed or the archive.

## Phases

Each phase ships independently and is verified on the running site before the next begins. The phone follows one phase behind the server it needs. "Verified by" is what to watch, in the order Ben checks things.

### Phase 0 — Branch, plan of record, append-only enums, guards (server)

0.1 Branch `feature/hosted-events-235`; `README-hosted-events-235.md` from this plan (brief verbatim, decisions, phases, Status updated per phase); backlog item 235 in `ProjectNotes/Future-Improvements.md`.
0.2 Enum appends, all at once, no migration (ints in the DB, so early deploy is safe and later phases cannot disagree on numbers): `OrganizationPermissionArea.Events=10`; `OrganizationSecurityTable` HostedEvent=40, EventBooking=41, EventSession=42, EventFile=43, EventCheckIn=44, EventChecklist=45; `SubscriptionLimit` ActiveHostedEvents=14, EventSessions=15, EventStaff=16, EventFilesMegabytes=17; `TierCapability` HostEvents=4, EventCredits=5, EventTicketing=6; `CmsSectionType` EventProgramme=10, EventBooking=11, EventGallery=12, EventVenue=13; `OrgMessageChannel.EventRoom=5`; new enums `EventBookingStatus {Requested, Confirmed, TurnedDown, Cancelled}`, `EventBookingKind {Overnight, DayPass}`, `EventFileAudience {Staff, Attendees, Public}`, `EventCheckInMethod {Door, Self, Qr}`, `VenueHostingStatus`. Six Events rows in `Ben.Data.Common/Constants/PermissionAreas.cs`.
0.3 Tests: `PermissionAreaMapGuardTests` / `RolePermissionCoverageTests` fail until the map rows exist (discriminates); new `TierLabelCoverageTests` asserting every limit/capability value has a non-default label on `AdminSubscriptionTiers.razor`.
0.4 Verified by: builds, suite green, tier page shows the new labels with nothing set. Billing effect: none.

### Phase 1 — The event package: model, umbrella row, billing, manage page, public page (server + web)

1.1 Migration `HostedEvents`: `HostedEvent` (OrganizationId, Name unique per org, UrlName, Tagline, Description sanitised, PlaceId required and not a private residence, HideExactLocation, TimeZoneId, StartsOn, EndsOn, DefaultCheckIn/OutLocal, IsPublished, DayPassCapacity?, ContactLine, CoverUploadFileId?, Mail templates, ArchivedAtUtc, CancelledAtUtc, later VenueGrantId?/OrganizationPageId?), `HostedEventNight` (Date, Title?, CheckIn/OutLocal?, Notes, SortOrder — generated from the date range), `OrgCalendarEvent.HostedEventId` (unique), `OrganizationSubscription.EventCountAtPeriodStart`. Service `HostedEventCalendarSync` writes the umbrella row; `OrgCalendarController.Update/Delete` refuse umbrella rows with "This date is managed by the event *{name}* — change it there", linked.
1.2 Endpoints `Controllers/Entities/HostedEventController.cs` (copy `TourController` shape: entitlement → create → charge remainder → plan note): GET list / GET one / GET plan / POST / PUT / publish / unpublish / archive / restore / cancel / PUT nights/{id} / POST mail-preview. Public `Controllers/Public/PublicHostedEventController.cs`: GET `api/public/hosted-events/{id}`, `?lat&lon&radiusMiles&query`, `/map` (mirror `PublicTourController`); `GET api/public/organizations/{org}/events/{slug}` gains `HostedEventId` additively.
1.3 Billing: `SubscriptionTierResolver.IsBusinessKind` += HauntedProperty (DECISION 1); `TourBilling` → `ProductBilling` (alias kept one phase); `BillableUnits.PriceAsync` counts tours + active events; `Describe` "2 tours and 1 event"; `SubscriptionQuoteResponse.UnitsAre` "tours"|"events"|"products"; Stripe metadata `ih_events`; `TourAddOnService.ChargeRemainderAsync(…, ProductKind)` with idempotency prefix `eventadd-` (existing `touradd-` keys untouched); `PeriodOpener` snapshots the event count. Banded tiers: `HostedEventEntitlement.WhyNotAnotherAsync` = capability `HostEvents` → limit `ActiveHostedEvents` → credits (Phase 11). Site setting `AllowHostedEvents` beside `AllowTourBusinessSignUps`.
1.4 `HostedEventAutoArchiveJob`: `ArchivedAtUtc` 14 days after the last night unless a booking is unresolved (DECISION 2), platform message to admins.
1.5 Pages `Manage/Events/OrgEvents.razor` (`/organizations/{OrgId}/events`, copy `OrgTours.razor`), `OrgEventPage.razor` (`/organizations/{OrgId}/events/{EventId}`, copy `OrgTourPage.razor`'s anchor-index cards: Details, Nights; later cards join); calendar editor shows the "managed by" banner; `PublicEventDetail.razor` hosted layout (nights, venue card, cover, contact line) and `PublicEventList.razor` "3 nights" badge; org nav "Events" beside "Tours"; billing quote copy for events/products; SuperAdmin tier card note. **Design pass**: cover image hero, nights strip, the tour page's rhythm.
1.6 Tests: `ProductBillingTests` (HauntedProperty is a business kind — fails before), `HostedEventControllerTests` (umbrella row created/public/slug shared; umbrella update refused; Free ladder refused naming `HostEvents`; capability-with-cap refused naming the cap), `HostedEventAddOnTests` (`eventadd-` and `touradd-` keys distinct on the same day), Playwright `HostedEventBillingTests.cs` (register kind=3, quote `UnitsAre=="events"`, two events → 2 units), `HostedEventPublicTests.cs`.
1.7 Docs: `organization-administration.md` "## Hosted events", `site-administration.md`, `HelpMediaCapture.cs` shots `org-events-list`, `org-event-details`, `public-hosted-event`, PDF, pitch panel.
Verified by: as the Thomas House test org, create a 3-night event; it appears once on `/o/thomas-house/events`; the calendar shows the managed-by link; `/api/public/events` includes it (what the shipped phone sees); the billing quote counts it.

### Phase 1B — Event credits, end to end (server + web)

**In this arc, and early, because without it nobody outside a business plan can publish anything.**
Ben set the price at **$99** and said to ship the purchase, 2026-09-11. It sits directly after phase
1 so the refusal phase 1 writes has somewhere real to send people.

**Ben's rule, in his words (2026-09-11):** *"If someone hosts an event and is hosting from their
group or organization, they can buy an event credit. This is a single event they can schedule or
host. They have a year to have hosted the event otherwise they lose the credit. For multiple events
they would need multiple credits, one for each event. Each event would dock the member or group the
credit for the event."*

So: **one credit, one event.** Not a subscription, not a period allowance — a thing you buy, spend
once, and lose if you do not use it within a year of buying it. A group that wants three events
buys three credits. Creating an event docks a credit; the credit is gone whether or not the event
is later cancelled (a refund is a decision for a person, not a rule).

Called a **credit**, not a package, everywhere in code and copy — that is the word Ben used and it
is the word that says what it is.

**1B.1 Model + migration `EventCredits`.** `EventCredit` (OwnerOrganizationId **or** OwnerAppUserId — "the member or group", so an individual
hosting from a personal org can hold one; PriceAtPurchase, Currency, PurchasedUtc, ExpiresUtc =
purchased + 12 months, ProviderCheckoutRef, ProviderPaymentRef, ReceiptNumber, SpentUtc?,
SpentOnHostedEventId?, RefundedUtc?). Site settings `EventCreditsEnabled` (on) and `EventCreditPriceUsd` (**99**) — a setting, not a
constant, so the price moves without a deploy and `PriceAtPurchase` on the row means a change never
reaches a credit somebody already holds.

**1B.2 Buying one.** `POST api/organizations/{org}/event-credits/checkout` (and the personal-org
equivalent) opens a Stripe Checkout session against a one-time price, metadata `ih_event_credit`,
quantity chosen by the buyer. The webhook in `StripeFulfillmentService` writes the credit rows plus
the ledger Charge/Payment pair and a receipt number, exactly as a seat purchase does, and is
idempotent on replay by `PaymentReference`. Tax through `TaxResolver.ForOrganizationAsync` + `TaxOn`,
the same path a subscription takes — a credit is a digital service sale and is taxed like one.

**1B.3 Spending one.** `HostedEventEntitlement` takes the **oldest unexpired unspent** credit at
publish when the tier's capability or cap would otherwise refuse, inside the same transaction as the
publish so two tabs cannot spend one credit twice. The refusal, when there is none to take, names
what is missing and links to the purchase.

**1B.4 Expiry and the warning.** A daily job mails the holder thirty days before a credit lapses and
writes a platform message for anybody who cannot be emailed; `ExpiresUtc` past with `SpentUtc` null
is simply gone — no reclaim, no silent extension. A refund is a **SuperAdmin action**, not a
self-service button: `RefundedUtc` exists on the row and a refunded credit cannot then be spent.

**1B.5 Pages.** Billing page card: credits held with their expiry dates, credits spent with the
event each went on, and a Buy control. The publish confirmation from the decisions above. The
refusal on `OrgEvents` links straight to the card.

**1B.6 Tests.** `EventCreditTests`: the oldest unexpired credit is the one spent (not the newest —
fails before if the order is wrong); an expired credit is never spent; publishing twice spends one;
a refunded credit is refused; the webhook is idempotent on replay; the thirty-day mail goes once per
credit. Playwright `EventCreditTests.cs` through the existing test-mode Checkout stub: buy two,
publish an event, see one left and a receipt on the receipts page.

**Volume pricing is deliberately not built.** Three-for-the-price-of-two is an easy thing to add
later and an easy thing to regret early; the shape is a quantity discount on the Checkout line, not
a second product.

**When a credit is spent — settled by Ben, 2026-09-11:** *"I think the credit should be spent after
a certain point. If we are getting responses and confirmations, obviously the event has been planned.
So the person must understand and confirm they will be charged the event credit before they can get
too far in."*

**The credit is spent at PUBLISH**, and that is the same moment the business meter starts. One
concept governs money for both kinds of customer: going live is the commitment. Nothing before it
costs anything, because nothing before it involves anybody else — a draft has no page, takes no
bookings and sends no confirmations. Publishing is precisely the act that lets responses start
arriving, which is Ben's line for "too far in", and it is the last moment at which stopping is still
free.

The rules that follow:

- **Drafting is free and reversible.** Build the whole thing — nights, rooms, programme, menus,
  staff, files, the page — without spending anything. A person who changes their mind loses nothing,
  which is the fault with spending at creation: a mistyped event would cost real money.
- **Consent is explicit, and it is the publish button.** Publishing an event funded by a credit
  opens a confirmation that says plainly what it costs, what is left, and that it does not come back:
  *"Publishing spends one event credit. You have 2 left, the next expiring 14 March 2027. The credit
  is not returned if you take the event down."* Nobody is charged by a click that did not say so.
- **No refund on un-publishing**, and **re-publishing never spends a second.** One event, one credit,
  for the life of that event. The credit is recorded against the event (`SpentOnHostedEventId`), so
  the question "have we paid for this one?" has one answer for ever.
- **The year is checked at publish, against both ends.** The credit must be unexpired *and* the
  event's first night must fall inside its year — which is Ben's "a year to have hosted the event"
  read literally, and stops a credit being parked by publishing a placeholder dated 2031. The
  refusal names both dates: *"This credit runs out on 14 March 2027 and this event's first night is
  2 April 2027. A credit bought today would cover it."*
- **No credit, no publish.** The event stays a draft and the refusal links to where credits are
  bought. The work is not lost and nothing is destroyed — it simply does not go live.

**Settled 2026-09-11:** an unspent credit gets a warning email at thirty days, and the billing page
carries a line showing what is held and when each one lapses. It costs nothing to send and saves the
support ticket that arrives when somebody finds out a credit expired last week.

### Phase 2 — Bookings: rooms with capacity, day passes, party guests, dietary, menus, mail, bell (server + web)

2.1 Migration `HostedEventBookings`: `PlaceRoom` += Capacity?, IsBookable, BedNote; `HostedEventRoom` (offered rooms, CapacityOverride?); `HostedEventBooking` (lead AppUserId, PartySize, Kind, Status, DecidedUtc/By, GuestAcknowledgedUtc, Note, UmbrellaAttendeeId?, ProgrammeSeenUtc later); `HostedEventBookingNight` (BookingId, NightId, PlaceRoomId); `HostedEventBookingGuest` (DisplayName, AppUserId?, DietaryNotes); `HostedEventMenu` (NightId, Title, ServedAtLocal, Notes) + `HostedEventMenuItem` (Course, Name, Description, DietaryTags, SortOrder); `HostedEvent.BookingsCloseAt?`. Pure `EventCapacity` (mirror `TourSeats`): confirmed parties only count (DECISION 5), per room per night, day-pass cap.
2.2 Endpoints: organiser rooms/bookings grid/create-on-behalf (by user or by email through the existing guest-invite token)/confirm `{nights:[{nightId, placeRoomId}]}`/turn-down/cancel/**edit** (party size, room-nights, guests — Ben asked for editable reservations)/guests/dietary-tally/menus/mail-preview. Public signed-in: POST booking, GET/PUT my-booking(+guests), acknowledge, DELETE (cancel while Requested; a cancellation request while Confirmed), `GET api/public/hosted-events/mine`. Anonymous email request→confirm on the umbrella creates a DayPass booking (one door). Umbrella `rsvp?seats=` from the shipped app → DayPass booking request when day passes exist, else a sentence (DECISION 3).
2.3 Mail + bell: `EventGuestMailer` beside `TourGuestMailer`; `TourMailRenderer` generalised to a placeholder dictionary (`{{event.*}} {{booking.*}} {{session.*}}`, tour placeholders unchanged); `.ics` one VEVENT per booked night; buckets `EventBookingsToDecide`, `MyEventBookings`.
2.4 Pages: `OrgEventBookings.razor` (`/organizations/{OrgId}/events/{EventId}/bookings`, copy `OrgTourDateSeats.razor` + a rooms × nights grid); `OrgEventPage` cards Rooms offered, Menus, Guest email, Dietary tally; `PlaceRoomsManager.razor` gains Capacity/Bookable/Beds; public page "Ask for a place" (kind, party, guests, dietary, preferred room) + "your booking" card + menus; My events page for a person.
2.5 Billing: none new; umbrella `AttendeeCapacity` = offered room capacity + day passes.
2.6 Tests: `EventCapacityTests` (per-night discrimination; day-pass cap 0; Requested never counts), `HostedEventBookingTests` (confirm writes umbrella attendee with Seats=PartySize and the reminder job picks it up; turn-down never; anonymous path lands as DayPass; bucket counts), `EventGuestMailerTests`, Playwright `HostedEventBookingTests.cs` (party of 3 into a room that sleeps 2 → refusal in the row → moved to the Suite → confirmed → mail in diagnostics; dietary tally).
2.7 Docs: rooms, bookings, day passes, menus, the guest email; `your-profile.md`; shots; PDF.
Verified by: two browsers — guest asks, organiser bell 1, confirm with a room, guest bell 1, acknowledge; the phone dev build shows the umbrella event under "mine" with a reserved seat.

### Phase 3 — Tickets: QR passes after confirmation, revoke, edit, email or in-app (server + web)

Ben's ask, placed right after bookings because a pass is a property of a confirmed booking.
3.1 Migration `EventPasses`: `HostedEventPass` (BookingId, Token (signed, opaque), IssuedUtc, RevokedUtc?, RevokedReason?, ReissuedFromPassId?, EmailedUtc?). Editing a booking that changes party size/room-nights reissues the pass (old one revoked, new emailed); explicit **Revoke** with a reason; **Reissue**.
3.2 Endpoints: organiser `POST .../bookings/{b}/pass/issue|revoke|reissue|email`; guest `GET api/public/hosted-events/{id}/my-booking/pass` (token + human summary; the phone renders the QR); `GET .../pass.png` (server-rendered QR for the email and the web); door `POST .../door/scan {token}` answers who, party, room, nights, and refuses a revoked/unknown/other-event token in words (Phase 4 wires it to check-in).
3.3 Pages: pass card on the guest's booking (web) with the QR, "Email it to me"; organiser booking row: Issue / Revoke / Reissue with the reason captured; mail template placeholder `{{booking.pass}}` (inline QR image, `.ics` still attached).
3.4 Tests: `EventPassTests` (revoked token refused with the sentence; reissue revokes the old; editing party size reissues; scan of another event's token refused — each a distinct line), Playwright `HostedEventPassTests.cs` (confirm → pass visible → revoke → pass page says so).
3.5 Future enhancement recorded, not built: **Apple Wallet pass** (`.pkpass`, needs a Pass Type ID certificate) for events and tour dates.
Verified by: confirm a booking, open the guest's pass, revoke it, the door scan refuses it by name.

### Phase 4 — Sessions and classes with first-come sign-up and a waiting list (server + web)

4.1 Migration `HostedEventSessions`: `HostedEventSession` (Title, Description, StartsAtUtc, EndsAtUtc, PlaceRoomId?/LocationText, Capacity?, RequiresSignUp, SortOrder, CancelledAtUtc, ChangedUtc), `HostedEventSessionLeader`, `HostedEventSessionSignUp` (unique per session+user, WaitlistedUtc?), `HostedEvent.ProgrammePublishedUtc`.
4.2 Endpoints: organiser CRUD, leaders, cancel, roster, add-a-guest, publish programme (rules: within the nights in the venue zone, `EventSessions` limit, capacity ≥ current sign-ups); attendee `GET .../sessions` (mine/taken/capacity/waitlisted), `POST/DELETE .../sessions/{s}/sign-up` (confirmed booking or staff; full → waitlist position; cancellation promotes and mails), `.ics` per session and per programme.
4.3 Bell + mail: `MySessionSignUps`, `EventScheduleChanges` (ChangedUtc after the booking's ProgrammeSeenUtc); promotion and cancellation mail.
4.4 Pages: `OrgEventPage` "Sessions" per-night timeline editor; roster page `/organizations/{OrgId}/events/{EventId}/sessions/{SessionId}`; public "Programme" per night with "Sign up (3 of 15)"; My events lists sessions with `.ics`. **Design pass**: the programme is the most-looked-at thing during an event — timeline, room colour, now/next.
4.5 Tests: `HostedEventSessionTests` (capacity 2, third is waitlisted; deleting the first promotes and mails — fails before; unconfirmed 403; outside the nights 400; two concurrent sign-ups never exceed capacity), Playwright `HostedEventSessionTests.cs` (Ovilus 9–10 cap 15; counter "1 of 15"; `.ics` DTSTART in venue time).
Verified by: programme in venue time; a second browser's sign-up increments the counter; `.ics` lands at 9:00 local in Calendar.

### Phase 5 — Staff, the Events permission area, non-member staff, check-in/out, checklists (server + web)

5.1 Migration `HostedEventStaffAndCheckIn`: `HostedEventStaff` (AppUserId?/InviteEmail?, DisplayName, RoleLabel, flags CanSeeBookings/CanDecideBookings/CanCheckIn/CanManageSessions/CanManageFiles/CanManageChecklists, SortOrder), `HostedEventStaffInvite` (token pattern of `EventAttendanceInvite`), `HostedEventCheckIn` (BookingId, GuestId?, NightId?, ArrivedUtc, LeftUtc?, RecordedBy, Method), `HostedEventChecklist` + `HostedEventChecklistItem` (Text, AssigneeStaffId?, DueUtc?, DoneUtc?, DoneBy), `HostedEventSessionLeader.StaffId?`.
5.2 Access: `Services/Access/HostedEventAccess` (owner/admin → all; custom-role grant on the table → that table; staff flag → that flag); every Phase 1–4 organiser endpoint switches to it; role editor "Events" section greyed unless the tier includes the area; `EventStaff` limit.
5.3 Endpoints: staff list/flags/invite/confirm (public token page)/remove; door `GET .../door?night=`, check-in/out/undo, `door/scan` wired to check-in (Method=Qr); self check-in from the phone recorded as `Self` (unverified) for the door to confirm; checklists CRUD + done/undone; `GET api/me/event-tasks`.
5.4 Pages: `OrgEventStaff.razor`, `OrgEventDoor.razor` (big-button per-night list usable in a phone browser; scan box), `OrgEventChecklists.razor`; nav entries only when access says so; a member without rights who types the URL sees the sentence.
5.5 Bell: `MyEventTasks`.
5.6 Tests: `HostedEventAccessTests` matrix (owner / role with EventCheckIn only / staff CanCheckIn only / plain member / non-member invitee — distinct 200/403 patterns), Events area greys on a tier without it, `HostedEventCheckInTests`, Playwright `HostedEventDoorTests.cs` (invite door@…, confirm, sign in, only Door visible, check in a party of 3, the grid shows 3 arrived; `RefusalHonestyAuditTests` extended).
Verified by: a non-member helper runs the door from a phone browser; the owner sees arrivals; a plain member cannot see bookings.

### Phase 6 — Files for the event (server + web)

6.1 Migration `HostedEventFiles`: `HostedEventFile` (UploadFileId, Folder, Audience, SortOrder); stored at `OrgFilePath(orgId, "events/{eventId}/…")`; counted under the tier's storage; `EventFilesMegabytes` optional.
6.2 Endpoints: list by folder / upload (multipart or the chunked uploader with target `event:{id}`) / PUT folder-audience-order / DELETE / copy-to-media-library; attendee `GET api/public/hosted-events/{id}/files` + download through audience access; folder traversal refused.
6.3 Page `OrgEventFiles.razor` (copy the org files page with a folder breadcrumb, audience select, chunked uploader); public "Downloads" card.
6.4 Tests: `HostedEventFileTests` (Staff → guest 403; Attendees → confirmed 200, unconfirmed 403; Public → anonymous 200; storage sentence; `..` refused), chunked-upload target extension, Playwright `HostedEventFilesTests.cs`.
Verified by: upload from the web, download on the phone dev build as a confirmed guest, staff-only file absent from the guest's list.

### Phase 7 — The flashy page, share to feed, event ads (server + web)

7.1 Migration `HostedEventPages`: `HostedEvent.OrganizationPageId?`, `HostedEventGalleryImage` (max 50 at 1920×1080 like tours), `OrganizationAd.HostedEventId?`.
7.2 Endpoints: `POST .../page {layout}` applies one of three seeded `CmsTemplateScope.Page` layouts ("Weekend ghost hunt", "One-night lock-in", "Conference") and links it; the four new section types are **resolved live** (programme, booking card, gallery, venue with rooms/history) like `EmbeddedInvestigations`; gallery CRUD; `POST .../share-to-feed {scheduledForUtc?, text}` via the scheduled-post path; ads `TargetKind="event"` + `HostedEventId`, shown by `PublicPromotedGroupsController` until the last night, on discovery, the public events list and nearby; still free and SuperAdmin-approved.
7.3 Pages: `OrgEventPage` "Page" (pick a layout → `OrgCmsEditor`), "Gallery", "Sharing" (feed + "Advertise this event" preselecting the event on `PromoteGroupPage`); `OrgPublicSection.razor` renders the four types; `PublicEventDetail.razor` shows the authored page when published; OG image from cover/gallery; `PromotedGroupsCard` shows event ads with dates. **Design pass is the point of this phase**: full-bleed hero, countdown to the first night, sticky "Ask for a place", programme timeline, gallery slideshow (reuse the tour slideshow), venue card with history, sponsor strip; the same treatment applied back to `PublicTourPage.razor` so tours get the flash too.
7.4 Tests: `CmsEventSectionTests` (stored JSON ≠ what the visitor receives; a cancelled session vanishes from the projection; booking card hides after `BookingsCloseAt`; `PublishedPages` cap names the page), `PublicPromotedGroupsTests` (event ad excluded the day after), Playwright `HostedEventPageTests.cs` (apply layout, publish, `og:image` + programme present), `OrgAdJourneyTests` extension for the event target.
Verified by: the page on a phone-width viewport; OG preview validates; the ad rotates on discovery after approval.

### Phase 8 — Collaboration: venue profiles, hosting requests, org-to-org grants (server + web)

8.1 Migration `VenueProfilesAndGrants`: `OrganizationVenueProfile` (OrganizationId, PlaceId, HistoryHtml, HouseRules, MaxOvernightGuests, IsPublished — unique per org+place, so `Place` still has no owner), `OrganizationVenuePhoto`, `VenueHostingRequest` (venue org, requesting org, FromDate, ToDate, Message, Status, DecidedUtc/By, GrantId), `OrganizationVenueGrant` (ValidFrom/To, AllowRooms/AllowPhotos/AllowHistory/AllowStaff, RevokedUtc) — **the site's first org-to-org grant**; photos exposed by writing `UploadFileOrganizationShare` rows on approval and deactivating them on revoke; `HostedEvent.VenueGrantId?`.
8.2 Endpoints: venue profile/photos/publish; requests list/approve (switches + dates)/decline/revoke; public venues search + venue page; guest org requests/withdraw; `POST .../events` accepts `venueGrantId` (offered rooms may include the venue's `PlaceRoom`s; history/photos feed the `EventVenue` section; venue members may be staff with `AllowStaff`); bucket `VenueRequestsToDecide`; platform messages both ways. Billing: the event is the guest org's product; the grant costs nothing (DECISION 4).
8.3 Pages: `Organization/Venue/OrgVenueProfile.razor`, `OrgVenueRequests.razor`, public `PublicVenuePage.razor` (`/o/{org}/venue/{placeSlug}` with history, public rooms with capacity, photos, availability from grants and own events, "Ask to host an event here"); `OrgEvents` "at a venue that granted us dates"; `PlaceView.razor` links to published venue profiles.
8.4 Tests: `VenueGrantTests` (N shares for N photos and the guest picker lists them; revoke drops photos/history from the projection but keeps rooms; outside ValidFrom/To refused; venue staff only with AllowStaff), Playwright `VenueHostingTests.cs` (Thomas House publishes; Nashville Paranormal requests Oct 30–Nov 1; approve rooms+photos; the group's event uses Room 217; the public page shows the venue's history and photo).
Verified by: two orgs in two browsers walk request→approve→event; revoke and watch the photos vanish on reload.

### Phase 9 — Attendee sharing during the event: the room (server + web)

9.1 Migration `EventRoomMessages`: `OrgMessage.HostedEventId?` with `ChannelType=EventRoom`; `HostedEvent.RoomClosesAtUtc?` (last night + 7 days), `RoomHiddenAtUtc?` (DECISION 6).
9.2 Endpoints: `GET .../room?after=` (confirmed guests + staff), `POST .../room` (text + one media file through the feed upload path and `PendingMediaScreeningJob`; accepts a client-minted `postId` for idempotent retry; `isBroadcast` for staff), report; organiser promote {toPublicFeed | toPlaceArchive} (public copy with attribution; archive via the evidence publish path), hide, close. `FeedController` public queries exclude `EventRoom` (guard). Retention follows the org tier's limits; promoted or kept media is exempt.
9.3 Pages: public event page "Room" tab for guests/staff (copy the feed composer/list), organiser moderation inline, `OrgEventPage` "Room" card.
9.4 Tests: `EventRoomTests` (a public-feed query never returns a room message — fails before; unconfirmed 403; post after close refused; promote copies and leaves the original; retention deletes an unkept photo, not a promoted one), Playwright `EventRoomTests.cs`.
Verified by: a photo posted from the phone dev build appears for another guest and never in the public feed until promoted.

### Phase 10 — The phone: the Event section (staged on the branch, no build bump)

The rule proposed for the tab bar: `MeSurfaces` gains `attendingEventNow` / `staffingEventNow` (a confirmed booking or staff row on an event whose window is first night − 24h … last night + 24h). When either is true, `.events` moves to the **front** of the priority list, so a guest's bar reads Events, Feed, Field Kit, Tours, Profile and Field Kit is un-hidden for the pure-guest case (at a paranormal event, that person is exactly who wants the instrument). The rule moves into a unit-tested `TabPolicy` in BenKit with today's outputs pinned first. iPad's sidebar shows everything that applies.

10.0 Foundations: additive records (`PublicEventRecord.event: PublicEventPackage?` with nights, sessions, staff, files, menu, myBooking, myCheckIn, myPass, canPost, isStaff, programmeUpdatedUtc; `PublicEventListItem.eventName?/isMultiNight?`; five optional bell buckets; two `MeSurfaces` booleans); `EventPackageStore`; `EventCache` (Application Support/Events/{id}/event.json, programme.json, posts.json, files/, outbox/) with a `.stale(since:)` state so a hotel basement with no signal still shows the programme; `AppRoute` cases for programme/session/menu/files/stream/booking/pass/check-in (+ staff door/checklists/now-board); deep links `/events/{id}/sessions/{sid}`, `/events/{id}/programme`, `/o/{org}/events/{slug}`; an `AttendingView` for `/attending/{token}`; AASA claims `/events/*` (stale since item 234), the new paths, and `/attending/*`; `UniversalLinkClaimsTests` flipped; fixtures captured from a seeded "Thomas House Weekend" spanning today with a confirmed booking for `james.thornton@benco.dev` and a staff row; `FixtureNullDriftTests` keys; `TabPolicyTests`, `DeepLinkParserTests`, `EventPackageStoreTests`.
10.1 Event hub + programme: `EventHubView` (hero, hosted-by, booking panel, Now/Next strip on the venue's clock, rows to Programme/Menu/Files/Stream/Pass/Check in, stale banner), `EventProgrammeView` (grouped by night, room, leader, "12 of 15"/"Drop in", cancelled struck through), `EventSessionDetailView` (Add to calendar via the existing button); `EventsView` rows say "3 nights". UI test `EventHubUITests` proves the Events tab is present on iPhone.
10.2 Class sign-up + reminders: sign-up panel mirroring the seat panel; `SessionReminders` (mirror `SeatReminders`: 15 minutes before per session, a day-before digest, identifiers `session-{eventId}-{sessionId}-{suffix}`, idempotent `sync` that fires an immediate "moved/cancelled" notification when the cached programme differs — how schedule changes reach a phone without push); tests mirror `SeatReminderTests`.
10.3 Menu + files: `EventMenuView` (with the guest's own dietary line at the top — a card to hand the kitchen), `EventFilesView` (download to the cache, QuickLook, "Keep for offline"); Info.plist photo-library string extended, shipping with this sub-phase.
10.4 The stream + the outbox: `EventStreamView` (broadcasts pinned), `EventStreamComposerView` (text + one attachment, ≤80 MB via the single-shot path; larger video refused in words until 10.9); `EventOutbox` persisted like `SessionFileStore` (media moved not copied; drains on foreground / network up / after a load; 5xx backs off, 4xx surfaces the sentence and stops); broadcasts become local notifications on poll; `BGAppRefreshTask` while attending; camera/microphone strings extended.
10.5 Booking + pass + check-in: booking panel (Requested / Confirmed + "Got it" / Turned down / not booked → `EventBookingView`); `EventPassView` renders the QR from the server token (CoreImage `CIQRCodeGenerator`), works offline once fetched, shows "revoked" honestly; "I'm here" posts a Self check-in; booking reminders as a third lead set; bell rows with destinations; calendar write string extended.
10.6 Organiser on the phone: `EventDoorView` (Expected / Arrived / Left, search, self-reported badge, tap to confirm, **scan a pass with `DataScannerViewController`**), check-ins queued in the outbox when offline; `EventChecklistsView` (offline ticks, last-write-wins); `EventNowBoardView` (iPad-first lobby board, idle timer off, Broadcast button).
10.7 Polish: guide/staff photos and galleries drawn at last (modelled since item 233, never rendered), event map, `CoreSpotlight` items per session, Handoff.
10.8 Live Activity "up next" + home-screen widget: new `IsHauntedWidgets` target, locally updated from the cache (moved to an App Group), honest about freshness. No push.
10.9 Chunked, resumable video: `ChunkedUploadClient` against the server's `api/chunked-uploads` (Cloudflare refuses bodies over 100 MB, so a séance video cannot ride the single-shot path); the room post accepts `{uploadFileId}` from a completed session; the outbox resumes rather than restarts.
Verified by (simulator, `-autoSignIn` DEBUG hook with `BEN_ATTENDEE_*` / `BEN_STAFF_*` secrets): Events is the first tab during the seeded event; the hub shows Now/Next; kill the API and the hub still renders with the stale banner; sign up, see the pending reminder; post a photo with the API down, it sends when the API returns; the pass QR scans at the door on a second simulator.

### Phase 11 — OPTIONAL PAYMENTS: Stripe Checkout per ticket (design only until decided)

`TierCapability.EventTicketing`; `HostedEventTicketType` (name, price, kind, quantity); paid Checkout replaces the "money is settled" click; refunds cancel; Stripe Connect needed for the venue to be paid. No work until Ben decides the site should take guest money at all; decision 1 says it does not.

### Phase 12 — Documentation consolidation, product PDF, pitch, production runbook

Re-run `HelpMediaCapture` for every `org-event-*`, `public-event-*`, `org-venue-*` shot; rebuild the documentation PDF and persona docs; turn `docs/tour-business-pitch.html` into a tour-and-event business pitch and regenerate the offer PDF; `the-mobile-apps.md` lists what the phone shows; `docs/deploy-production.md` entry (below).

## Credits are the product, not a fallback

**Ben, 2026-09-11:** *"If we sell events like credits to everyone, wouldn't that be more profitable
than selling a monthly event? I mean, the event is usually like a weekend with classes and
presentations and that is the event I was foreseeing. Do you see it differently?"*

**I don't see it differently, and the reason is larger than the one in the question.** Metering an
event monthly does not just make the bill awkward — it anchors the price to the wrong product. The
tour rate exists because a ghost walk is cheap, constant and earns a little every time it runs. A
weekend with classes and presentations is none of those things: it is occasional, it is a lot of
work, and it is worth real money to the person running it. Charging a fraction of a tour's monthly
rate for it prices a hotel weekend as though it were a walk round a block.

What the host is actually comparing us against is not a subscription. It is the per-ticket cut a
ticketing platform takes, which on a forty-guest weekend runs into the hundreds. A flat fee per
event is both **more** than metering would earn us and **obviously less** than the alternative
costs them, which is the rare pricing shape where the honest argument and the profitable one are
the same argument.

| For one weekend event | Roughly |
| --- | --- |
| Metered monthly at the tour rate, published three months ahead | three or four months of one unit |
| A flat credit | one fee, set against what the event is worth, not against a walk |
| What a ticketing platform would take on 40 guests | a per-ticket cut, an order of magnitude more |

**And it reaches a customer metering never will.** A group that runs one fundraiser a year will not
take out a subscription for a weekend, so today they are worth nothing to us. They will buy a credit
without blinking. That is not cannibalised revenue; it is revenue that does not otherwise exist.

**So the shape is:**

- **A credit is the headline price and the default for everybody** — groups, individuals hosting
  from a personal org, and businesses alike. One event, one price, paid at publish.
- **The subscription is the plan for people who run events for a living.** Thomas House is always
  hosting; it wants the page, the bookings, the door and the staff on all year, and it would resent
  buying twelve credits. It buys the flat business plan, and its events are governed by a cap on the
  tier (`SubscriptionLimit.ActiveHostedEvents`, already appended in phase 0 — unset means no cap).
- **Nothing is metered per event, anywhere.** This deletes work rather than adding it: no
  `ActiveEventsAsync` in `BillableUnits`, no `EventCountAtPeriodStart`, no `eventadd-` proration, no
  `ProductBilling` rename. `HauntedProperty` still joins `IsBusinessKind`, but only so it resolves to
  the flat business tier instead of the member ladder — `TourBilling.Units` already returns one unit
  for a business with no tours, so that path needs no change at all.
- **Tours are untouched.** Per tour, per month, exactly as item 233 shipped it. That model is right
  for tours precisely because a tour is the durable thing an event is not.
- **Show the break-even.** When somebody's credits in a year pass what the plan would have cost, the
  billing page says so and offers the switch. That converts the operators who have grown into it,
  and never gouges the ones who have not.

**What this changes about the phases.** Credits stop being phase 11's deferred nicety and become the
way most people host an event at all, so phase 1 must ship the entitlement with a real answer rather
than a hook: a business plan holder publishes against its cap, and everybody else is told they need
a credit. Whether the *purchase* (Stripe Checkout, ledger, receipt) ships in this arc or a later one
is Ben's call — the refusal is honest either way, and phase 1 is not blocked by it.

**Settled 2026-09-11: $99 a credit, and the purchase ships in this arc** as phase 1B. Against a
forty-guest weekend that is a fraction of what a ticketing platform's per-ticket cut would take, and
a low single-digit percentage of what the host collects — cheap enough to say out loud, and several
times what metering at the tour rate would have earned.

## The superseded rule: metered per live event

**Ben, 2026-09-11:** *"An Event Creator can host many events. What if one is at The Thomas House,
another at Waverly Manor and another at The Octagon House. How does this get billed?"*

**The venue is not the unit. The event is.** Three events is three units whether they are at three
houses or three times at one. Nothing about a building is billable: a venue that lends its rooms
under a grant (phase 8) is paid nothing by us and charged nothing for lending them, and the event
belongs to whoever created it. So in Ben's example the event creator pays for three, Thomas House
pays its own subscription for its own events and nothing for hosting somebody else's, and Waverly
and the Octagon — if they are not customers at all — never enter the arithmetic.

**But "active event" as I first recommended it is wrong, and this question is what shows it.** A
tour is a durable asset: one route, run for years, earning every time. Paying monthly for it is
honest. An event is an occurrence. Charging monthly from the day it is created until fourteen days
after it ends means the meter runs on **planning time** — an operator who lays out Halloween in June
pays five months for one weekend, and one who throws it together in September pays one. That
punishes the better operator for being the better operator, and it makes the bill impossible to
predict, since it moves with how far ahead somebody happens to work.

**The rule instead: an event is counted while it is PUBLISHED and not yet archived.** One sentence
to explain — *you pay for an event while it is live* — and every consequence falls out right:

- Planning privately costs nothing. A draft nobody can see is earning nothing and should cost
  nothing; there is no page, no bookings, no door, no work being done for them.
- The meter starts the day it goes public and opens for bookings, which is the day the site starts
  doing the job they are paying for.
- It still stops on its own, fourteen days after the last night (Ben's decision 4 — the auto-archive
  stays exactly as it was).
- Three events live together in October is three units in October. The same three run one a month is
  about one unit a month. The bill tracks what is actually on.
- Un-publishing to dodge a period is self-defeating: it takes the page down and closes the bookings.

`BillableUnits.ActiveEventsAsync` therefore counts `IsPublished && ArchivedAtUtc is null`, beside
`ActiveToursAsync`'s `RetiredAtUtc is null`. Proration on publish rather than on create: the
remainder is charged the day an event goes live, with idempotency key
`eventadd-{org}-{periodStart}-{from}-{to}`.

**One thing to watch, not to build yet.** An operator running many small events pays linearly and
may reach a number that feels wrong to them before it feels wrong to us. A ceiling — *"three live
events included, more at £X each"* — is the standard answer and fits the existing tier shape
(`SubscriptionLimit.ActiveHostedEvents` already exists from phase 0, and the tier row is the ceiling).
Worth having ready; not worth guessing the number before a real operator has run a real year.

**SUPERSEDED the same day** by the section above: nothing is metered per event at all. The reasoning
here still stands and is kept because it is what led to the better answer — publishing, not
creating, is the moment of commitment, and that is now where a credit is spent and where a business's
event starts counting against its cap.

## What this is actually for

**Ben, 2026-09-11:** *"The idea is to allow someone to schedule and track and organize an event that
is not ghost hunting related."*

That is the scope, and it changes the wording everywhere more than it changes the model. A hosted
event is **an event**: dates, a place, rooms or tables with capacity, a programme, staff, a door, a
menu, files, a page, bookings the host confirms. Nothing in the schema below says "paranormal", and
nothing should. A wedding weekend, a writers' retreat, a dinner-theatre run and a Halloween lock-in
are the same record with different words in the description.

Three consequences, all of them decisions made here rather than defaults inherited:

- **Copy is neutral.** Field labels, refusals, help text and the guest email say "guests", "nights",
  "sessions" and "the venue" — never "investigators", "hunts" or "evidence". The paranormal words
  stay where they belong: on tours, cases, investigations and the Field Kit.
- **The kind of business is not a gate.** `HauntedProperty` and `PublicEventProvider` are how a
  venue is *priced*, not what it is *allowed to hold*. An investigation group with the capability
  may host a book launch; the site should not have an opinion about that.
- **The event room and the evidence queue are optional.** A venue running a play does not want an
  evidence queue on its page, so the paranormal-flavoured surfaces (evidence submission, the archive
  publish path) are switched per event, defaulting off for a non-paranormal one.

It does not widen this arc's work. It narrows the vocabulary, which is cheaper to get right now than
to rename later.

## A second customer this model has to fit: the resident company

**Ben, 2026-09-11:** *"The Thomas House hosts a play company. They put on plays there about once a
month and sell dinner with the tickets. We could offer ads and booking but not account settlement as
an add-on. You tell me what you think."*

**It fits, and it is worth taking.** Strip the ghosts out of a dinner-theatre night and what is left
is the plan above: a venue, a date, a menu sold with the seat, a party size, a table, a pass at the
door, a public page and an ad — with the money settled between the company and its guests, never by
the site. Nothing new is needed to describe it, and it is the same sale the venue is already being
made.

**One thing about it does not fit, and it is a pricing bug waiting to happen.** A monthly show is
*one product on twelve dates*, not twelve events. Billing per active event would charge a company
twelve times for one show, which is the wrong number by an order of magnitude and the sort of thing
that loses a customer at renewal. That is exactly the shape a `Tour` already has — one product, many
dates, sign-ups per date — so the fix is not a third entity but a flag on the event:

- `HostedEvent.DatesAreSeparate` (default false). False is a stay: consecutive nights of one event,
  one booking spanning several of them, the shape a haunted-hotel weekend has. True is a run: each
  "night" is an independent date, a booking belongs to one date, capacity and the menu are per date,
  and the programme is per date.
- Billed **once**, either way. The unit is the product, not the date — the rule tours already prove.
- A run gets a "add the next month's dates" affordance rather than twelve creations, and the
  repeat-event template idea in the list below stops being a nicety.

**What the play company needs that the paranormal weekend does not:** tables rather than bedrooms
(the same `PlaceRoom` with a capacity, named "Table 4"), a per-date menu chosen at booking rather
than announced, and a seat/table preference. All three are small against what is already planned.

**What I would not do:** sell it as a separate add-on product with its own price and its own screen.
It is the same event product with a flag, and a second SKU for it would double the billing surface
to sell the same thing twice.

**DECISION NEEDED:** whether `DatesAreSeparate` lands in Phase 1 (cheap now, one bool and a branch
in the calendar sync) or waits until a real resident company asks. Recommendation: the bool and the
billing rule land in Phase 1 so the price is right from the first day; the per-date booking and menu
work waits for Phase 2 and is small there.

## Migrations, in order

HostedEvents (1) → EventCredits (1B) → HostedEventBookings (2) → EventPasses (3) → HostedEventSessions (4) → HostedEventStaffAndCheckIn (5) → HostedEventFiles (6) → HostedEventPages (7) → VenueProfilesAndGrants (8) → EventRoomMessages (9) → EventPackages (11). Each: `dotnet ef migrations add <Name> --project Ben.Data.Source --startup-project Ben.Data.WebApi`, review `Up` for unintended drops (the snapshot was touched by `TourSeats` today), then `database update … --connection "<IsHauntedDb_player>"`. Never `IsHauntedDb` from a dev box.

## Production notes (for the runbook)

- Every phase is additive to what iOS 1.0.2 (4) reads; the umbrella row is an ordinary public event. The one behavioural change the shipped app can see is the umbrella `rsvp` (DECISION 3).
- Before Phase 1.3 reaches production: count HauntedProperty orgs with a live subscription; any will be re-priced onto the business tier at their next period (DECISION 1).
- After Phase 0/1: the SuperAdmin ticks `HostEvents` on the tiers that should have it, leaves `ActiveHostedEvents` unset or sets it, sets `AllowHostedEvents`. No-row-no-cap fails open.
- Jobs registered in `Program.cs`: `HostedEventAutoArchiveJob`; retention extensions (Phase 9). Stripe metadata `ih_events` and prefix `eventadd-` are new keys. Storage `events/{eventId}/` under each org's path; verify `DeleteDirectoryAsync` on the blob implementation.

## Decisions

**Settled by Ben, 2026-09-11:**

1. **The site takes no guest money in this arc.** The venue confirms; QR passes issue on confirmation; the guest is told how to pay in the contact line. Phase 12 stays design only.
2. **HauntedProperty becomes a business kind** billed per active product (tours + events), prorated mid-period like a tour. A venue already on a headcount plan is re-priced at its next period, with the tier-change notice.
3. **Event credits are the product, at $99, and the purchase ships in this arc** (phase 1B). One credit buys one event, for anybody; unused credits expire a year after purchase with a warning at thirty days; the credit is spent at publish, behind a confirmation that says what it costs and that it does not come back. A business plan is the alternative for people who run events for a living, capped by `ActiveHostedEvents` rather than metered. **Nothing is billed per event per month.**
4. **An event stops counting against a business's cap 14 days after its last night**, when a job archives it (unless a booking is still undecided). Restorable. It does not start counting until it is *published* — drafting is free for everyone, and publishing is the single moment that spends a credit or occupies a slot.

**Routine judgement calls, taken as recommended (say so if any should differ):**

5. Umbrella RSVP from the shipped app creates a day-pass booking request when day passes exist, else a sentence naming the web page.
6. A venue may not sponsor a guest org's event on its own plan; the guest org pays.
7. Only Confirmed bookings hold a room-night; Requested ones are shown as "asked for" so over-asking is visible.
8. Event room: posts close 7 days after the last night, readable for 30, media under the tier's retention limits.
9. Nights cap 31.
10. Door staff without CanSeeBookings see names and dietary flags for their night only.

## Ideas Ben has not mentioned (each fits an existing pattern; none are in scope until he picks them)

Waivers with a signature at booking and a tick on the door; printable A6 passes with the venue map; name badges and per-night/per-session rosters; booking waiting lists with automatic offers; room-mate matching ("happy to share"); a printable per-service dietary sheet for the kitchen; staff shift roster with `.ics`; incident log exportable with the event's files; a lobby "what's happening now" board page; attendee "find my group" opt-in; printable schedule PDF and a "know before you go" from the mail template; sponsor strip section and a sponsor line on the ad; merchandise list ("ask at the desk"); after-event gallery + event reviews (reuse `TourReview`); "copy last year's event" templates; weather/contingency broadcast; lost and found; accessibility notes (stairs, lighting, hearing loop); pre-booking questions to the venue; venue availability calendar. Phone-specific: lights-out red mode for a séance (reuse `BlackoutOverlay`); Siri/Spotlight "when is the Ovilus class"; NFC tap-in; watchOS glance; post-event digest; per-booking group chat (needs a messaging model the app does not have).

## Verification, end to end

1. After every phase: `dotnet build Ben.slnx` warning-free for the touched projects, `dotnet test Ben.slnx` green, new tests seen failing first where they pin a rule, `scripts/run-e2e.sh --filter <phase>` green on the isolated database, then the phase's "Verified by" watched on the running site as the two test orgs (Thomas House as a HauntedProperty, Nashville Paranormal Society as a group).
2. Phone phases: `swift test` in BenKit, the app built for iPhone 17 Pro and an iPad simulator, the "Verified by" walked signed in as the seeded attendee and the seeded staff member.
3. Phase 13: every changed help page opened in the browser pane, both PDFs rebuilt and read, `HelpMediaCapture` re-run.
