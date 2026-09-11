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

## Status

Planned 2026-09-11. Phase 1 in progress.
