# Item 234: the tour on the phone — a reserved seat, notifications, and a Haunted Tours tab

Branched from `develop` (`a3d6e43e`, 2026-09-11), the commit that closed item 233.

## Ben's brief, in his words

> "I would like to be able to let the tours use the website with coordination to the iPhone and
> iPad app where if the person downloads the app and creates an account free or paid, they can get
> notified where the tour is, when to get there and other stuff that would be available in the
> e-mail like a link to add to the calendar and a link to get directions to the start point. They
> would not be confirmed until the tour guide or manager approves them meaning they have settled
> how money will be or has been exchanged. Then, the person who is touring can confirm it on the
> app - if they want. This is just confirmation between the tour company and the person taking the
> tour the seat or seats have been reserved for the tour. I would also like to add the tours in a
> Haunted Tours tab in the iPhone and iPad app based on current location or looking up a location
> on the tab."

Four things are being asked for, and they are separable:

1. **A seat is reserved, not merely requested.** Approval by the guide or manager is where the
   business says the money is settled. **We never take the money** — the state is the two of them
   agreeing. The guest may then acknowledge, which is optional.
2. **Seats, plural.** A sign-up holds a number of places, and a date's capacity counts places
   rather than people.
3. **Being told**, on the phone: where the tour is, when to be there, a calendar entry, directions
   to the meeting point. Everything the item 233 guest email already says, which is why that
   email's facts are the source for both.
4. **A Haunted Tours tab**, by current location or a place looked up.

## What item 233 already left in place

- `GET api/public/tours?lat=&lon=&radiusMiles=&query=` answers with name, business, city, distance
  in miles, duration, next date, rating, cover image **and the tour's IANA zone** — verified live
  against the running API on 2026-09-11. The Haunted Tours tab is a client of this and needs no new
  server work to list tours.
- `GET api/public/tours/map` answers the pins.
- A date carries its guides, its capacity, its meeting point and its zone, and the phone already
  reads the zone through `EventClock`.
- The guest email knows how to say all of it, with an `.ics` attached.

## What the survey found, before anything was written

- **There is no push notification code anywhere.** No APNs entitlement, no device-token table, no
  sender, no `AppDelegate` that could receive a token — and no *local* notifications either.
- **The notification feature that exists is a badge**, not a store: one endpoint,
  `GET api/me/notification-summary`, which counts eight buckets out of live tables. There is no
  notifications table. `PlatformMessageService` is the only writer, and what it writes lands in the
  System messages bucket.
- **A sign-up is accepted the moment a guest clicks the link in their email.** Nobody approves
  anything, a row is one person, and capacity counts accepted rows in four places.
- **The app consumes none of the tour endpoints.** It has no tour model, no map, and — worth
  knowing — **no event detail screen at all**: `AppRoute.eventDetail` is declared, deep-linked to,
  and falls through to a placeholder.
- **The iPhone tab bar holds five** before iOS collapses the rest into "More". Events already lost
  its slot and lives under Profile.

## Two decisions Ben took (2026-09-11)

1. **Reminders and the in-app badge first.** The phone schedules its own reminders when a seat is
   reserved, and "your seat is reserved" lands in the badge the app already has. No Apple key, no
   secrets, works with the phone offline, and verifiable here end to end. **Real APNs is a named
   follow-up** for the one case that genuinely needs it — an approval arriving while the app is
   closed.
2. **Haunted Tours becomes a tab for everyone, and Field Kit becomes conditional** on belonging to
   a group, which is the only place Field Kit is useful. A visitor who downloaded the app for a
   ghost walk sees Tours; a working member still sees Field Kit.

## Phases

### Phase 1 — the seat: requested, reserved, acknowledged

**The state machine reuses what exists rather than widening it.** `RsvpStatus` is read in 28
places, including investigations, so tour states do not belong in it. A tour date's sign-up writes
`RsvpStatus = Invited` with a new `SeatStatus = Requested`; approval writes `Accepted` +
`Reserved`; a turn-down writes `Declined` + `TurnedDown`. Every existing count of "accepted"
therefore keeps meaning *has a place*, without one of them being edited to understand tours.

- New enum `TourSeatStatus { Requested, Reserved, TurnedDown }`, and on
  `OrgCalendarEventAttendee`: `SeatStatus` (nullable — **null is every non-tour event**, unchanged),
  `Seats` (int, default 1), `SeatDecidedUtc`, `SeatDecidedByAppUserId`, `GuestAcknowledgedUtc`.
  Migration `TourSeats`.
- **Capacity counts places, not rows.** The four capacity checks sum `Seats` over accepted rows.
  With `Seats` defaulting to 1 the sum equals the old count for every row that already exists, so
  nothing about an ordinary event moves.
- A request is never refused for fullness — **the approval is**, in words, naming how many places
  are left. A walk that fills is a waiting list, not a closed door, and the business decides.
- Endpoints: the business approves or turns down a request; the guest acknowledges theirs.
- Non-tour events keep today's behaviour exactly: no `SeatStatus`, one seat, accepted on confirm.

### Phase 2 — the business side, on the web

The date's sign-ups list gains the seats asked for, **Approve** and **Turn down**, and a count of
requests waiting. The guest sees their own state wherever they already see the date.

### Phase 3 — the phone: a seat you can see and acknowledge

The event detail screen the app has never had, because "the person who is touring can confirm it on
the app" needs somewhere to confirm it. It shows the seat's state and how many places it holds, the
meeting point, the guides, **Add to calendar**, **Directions**, and the optional acknowledgement.

**Reminders are scheduled on the device when a seat is reserved** — the night before and an hour
before, carrying the meeting point — and cancelled if the seat is turned down or the date moves.
Everything they say comes from the same facts as the guest email, which is the point of
`TourMailTokens`.

### Phase 4 — the Haunted Tours tab

`AppSection.tours` joins the five, Field Kit becomes conditional on belonging to a group. A tour
model and store in BenKit, a list by current location or a place looked up, and a tour screen with
its dates, gallery and reviews. `GET api/public/tours?lat&lon&radiusMiles` already answers all of
it, sorted by distance — no new server work to list a tour.

### Phase 5 — being told, and the documents

A tour-seat bucket in the notification summary, in both directions: a business sees requests
waiting, a guest sees an approval. Help text, screenshots, both PDFs, and the backlog entry closed.

## Where the phone work stands

**Staged and proven, not shipped.** Ben, 2026-09-11: *"Changes to iPhone and iPad are just being
staged and proven for now. We are still waiting on version 1.0.3 (4) to be approved from Apple."*
So nothing here bumps a build number or touches App Store metadata, and none of it goes out until
that release is through.

## Status

Planned 2026-09-11. **Phases 1 to 4 built**; 5 to come.

### Verified, not assumed

Watched happening against the running site, not inferred from a passing test:

- A guest asked for three places on a walk. The walk stayed at one place taken — a request holds
  nothing — and the page said *Your seat is with the tour — they'll confirm it* rather than
  offering the button again.
- The business approved it and the walk went from 1 place to 4. **One sign-up, three places.**
- Asked to approve a party of twenty into a walk of twenty with four gone, the server answered
  *"Only 16 places left on this date, and this is a request for 20."*
- The guest acknowledged, and the business's list showed **Guest confirmed** beside the seat.
- Turned down, the guest's page read *The tour couldn't take this booking* — the row is kept, not
  deleted, so somebody who is not coming can see that they are not coming.

### Phase 3, verified

- The events list opens a night. It had nowhere to go before — there was no event screen at all —
  so a walk could be reserved and never looked at again.
- `ishaunted://events/{id}` opens the right walk directly, which is the deep link that had been
  falling through to a placeholder since the routes were written.
- The screen reads the clock of the PLACE: a Nashville walk says CDT on a phone set to anything.
- **Not yet exercised signed in on the device.** The three seat states render from the same record
  the website renders, and the store and the reminder wording are covered by tests, but nobody has
  stood in front of the phone with a reserved seat on it. Signing in there means typing a password,
  which is not something this session does.

### Phase 4, verified

Watched on the simulator against the dev API: the **Tours** tab lists walks with their length,
rating and next night on the walk's own clock; a tour opens with its meeting point, guide, dates
and contact line; and a night from there opens the event screen built in phase 3. The whole chain
is anonymous — no sign-in anywhere in it.

**What the five-tab ceiling cost.** Ben chose *Tours in, Field Kit conditional*, and that frees a
slot only for somebody outside a group. So the compact bar is no longer a fixed list of five: it is
the sections that apply, taken in priority order, until five are full, with Profile pinned last.
For a ghost-walk guest that is Feed, Tours, Events, Profile — Field Kit is not offered at all. For a
group member it is Feed, Tours, Field Kit, My Cases, Profile, and **Investigations moves under
Profile**, the way Events already had to.

**Field Kit is hidden from exactly one person**: somebody whose entire connection to the site is
having been on a public event. A solo investigator with nothing recorded yet still gets it — hiding
the instrument from the person about to use it for the first time is the stranding that rule exists
to avoid — and so does a signed-out visitor.

The location permission string in `Info.plist` now covers finding tours as well as stamping a field
session. **It must ship with the tours release, not before it**, or the string describes a use the
build does not have.

### Found by building it

`OrgTourDateSeats` loaded its data in `OnParametersSetAsync`, which runs before auth resolves on a
hard navigation — so the page asked the API as nobody and rendered *That date could not be found*.
It waits for `AuthReady` now, the way the tour page beside it already did. This is the same trap
recorded in the Blazor AuthReady rule, and it presents as a missing record rather than as a
permission error, which is what makes it worth writing down twice.

`Endpoint` carries its query as `[URLQueryItem]`. Gluing `?radiusMiles=25` onto the path escapes
the whole string as one segment, and the Tours tab's first build answered 404 against an endpoint
that answers 200 to curl.

`Theme.haunt` is an ACCENT, not a panel colour. The seat panel used it as a background and came
out a bright purple with grey text on it. `Theme.mist` is what every other card in the app uses.

`PublicEventListItem` had no public initializer — Swift synthesizes one at `internal` visibility,
so the app module could not build a row of its own, which the calendar sheet needs when the screen
is holding the detail record. Declared inside the type, because an initializer added in an
extension does not replace the synthesized one.

The Playwright fixture's teardown had to **purge** its test business rather than delete it: a
business with a tour, a date and sign-ups attached is refused by the ordinary delete, in words, and
a run would otherwise leave one behind every time.
