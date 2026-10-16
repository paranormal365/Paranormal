# Home event countdown, and countdowns on every event list (10/16/2026)

Branch `feature/home-event-countdown`, from develop at 50c4395d.

## What Ben asked for

1. "When a person signs up for an event, maybe we show how long until the event starts with a tiny
   card on the home page after they log in." Then: "Make it cool."
2. "When looking for events by location, why don't we show any that are public with their countdown
   and a thin progress bar until start time."
3. "Or events available for the person logged in."

## The plan

- **One countdown, everywhere.** `Kit/EventCountdown.razor` (+ `.razor.css`) is used by Home's card
  (`Look="Hero"`) and by every event row and card in the lists (the compact default). There is no
  second countdown.
- **One ticker per page.** `wwwroot/js/eventCountdown.js` runs a single `setInterval` for the whole
  page. Each tick finds every `[data-cd]` element and brings it up to date, so fifty rows cost one
  timer. It stops itself when none are left. `Kit/EventCountdownTicker.cs` folds every countdown's
  "start the ticker" into one interop call per render, per circuit. The import path is root-relative
  under `/_content/Ben.Web.Website.Library/js/`, like the other RCL modules.
- **No Blazor timer ticking over SignalR.** The server draws every countdown right for the moment the
  page is made, so prerendered and script-less pages read correctly. The browser then keeps it
  moving. The only server timer is one one-shot on Home's card, at the moment the shown event ends,
  so the card moves on to the next one.
- **All the clock work in C#.** `Ben.Web.Services/CountdownMath.cs` works out the exact UTC moments
  at which the words change: the days counting down, the day itself, the start and the end. It does
  this on the event's own clock (stored UTC, read in the event's `TimeZoneId`; Ben's rule), and it
  is correct across the 11/01/2026 clock change. The moments go into `data-cd-steps`, and the
  browser only compares them with its own clock. It never has to know a time zone.
- **One small endpoint for Home.** `GET api/me/next-events` (`MyNextEventsController`) gathers the
  four sources in one read. Doing it in the browser would mean four calls and four copies of "which
  of these count" on every visit to Home.
- **No extra calls for the lists.** The list records gain trailing, optional fields:
  - `PublicEventListItem.ListedUtc`, `.ReaderTie`;
  - `NearbyEventResult.EndDateTime`, `.ListedUtc`, `.ReaderTie`, `.OrganizationId`;
  - `PublicTourListItem.NextDateEndUtc`, `.NextDateListedUtc`.

  Older clients ignore them.

## What counts as "going" (Home's card)

| Source | Counts | Label |
|---|---|---|
| Hosted-event booking | Confirmed, Held, Requested: the three /my-events lists as "coming" | "You're coming", "Held for you", "Asked for" |
| Calendar event | RSVP Accepted | "You're coming" |
| Tour date | Seat Reserved (RSVP Accepted), or seat Requested and not turned down | "You're coming", "Asked for" |
| Investigation | On the team, not declined, not cancelled or completed (the member desk's rule) | "You're coming", or "You're invited" before they answer |

- **Requested and held bookings are included** because /my-events lists them under what's coming,
  and the date is in the person's diary. Leaving them out would make Home disagree with the page the
  bell sends them to. They never say "You're coming", and the words are the same ones /my-events
  uses.
- **Left out:**
  - a bare invitation to a calendar event;
  - a declined RSVP;
  - a turned-down seat;
  - a released or turned-down booking;
  - an event that was canceled, withdrawn by its venue, or removed;
  - anything already over.
- **A hosted event's calendar row is not counted twice.** A confirmed booking also writes an Accepted
  attendee on the event's calendar row. Only the booking counts, so the card opens the pass.
- **A tap goes to** the pass for a confirmed booking, and the event's page otherwise. A group-only
  calendar event opens the group's calendar, for a member. An event neither can open is not shown.
- **Start and end:**
  - A booking runs from its first booked night's start to its last night's end, using the event's own
    rule (doors at 7:00 PM unless a night says otherwise; `HostedEventCalendarSync.Window`). Released
    nights are skipped. A day pass covers the whole event.
  - An investigation with no end time is treated as running four hours.

## The states

| Phase | When (on the event's clock) | Words |
|---|---|---|
| later | two or more calendar days away | "Starts in 2 days" |
| tomorrow | the day before | "Tomorrow" / "Tomorrow night" |
| today / tonight | the day itself; "Tonight" from 5:00 PM | "Today" / "Tonight" |
| live | from the start to the end | "Happening now" |
| over | after the end | "Over" (lists dim it; Home moves on to the next) |

- **The digits:** days, hours and minutes, and seconds only in the last hour ("2d 04h 12m",
  "3h 05m", "9m 05s"). In the last hour the dot pulses faster.
- **Nothing upcoming means no card.** It is never an empty card.
- **Several upcoming:** the card shows the one running now, or else the soonest. "and N more" links to
  /my-events.
- **A failed load** says "Couldn't check what you're going to next." with Try again. It is never a
  silent gap that reads as "nothing coming up".

## The progress bar's starting point (Ben may want to change this)

**Chosen: from when it was listed to when it starts.**

- For a calendar event or tour date, that is the row's `DateCreated`. For a hosted event, it is
  `FirstPublishedUtc` (when it went on the public site).
- On Home, the bar starts when the person signed up: the booking's or RSVP's date, or the
  investigation invitation's.
- With no date at all, the bar covers the last 30 days. It is never shorter than one hour, so an event
  listed ten minutes before it starts is not shown as already full.

**Why:** a bar that fills toward the start honestly reads as "how much of the wait since it was
announced is behind you", and every record already has the date.

**The cost:** an event listed months ahead moves slowly for a long time.

**The other option** is a fixed window, for example a bar that only fills over the last 7 days. It is
one line to change: `CountdownMath.Baseline` (with `UnlistedWindow` and `ShortestWindow` beside it).

## "Events available for the person logged in"

- The list and the nearby search take `includeMine=true`. A signed-in reader who asks also gets their
  own groups' events that are not public, marked **Your group**. Their RSVPs on public events are
  marked **Invited** or **You're going**.
- **Asked for, never assumed.** The phone app already sends its token to these endpoints and does
  not know what a "Your group" row is. Without the flag nothing changes, signed in or not. Signed out,
  the flag changes nothing.
- **Never wider than today:**
  - A group-only event appears only for that group's active members. This is the same rule as the
    group's own calendar (`OrgCalendarEventController.GetAll`).
  - Case-bound and private-residence events never appear on a list or a map, even for members. These
    lists carry a pin. `PublicEventController.VisibleToReader` keeps `VisibleEvents`' two exclusions
    for everybody.
  - Pins stay approximate, as before. No exact address is added anywhere.
- **An invitation alone does not open a group-only event.** A non-member invited to a group's private
  night has no page for it (the public page answers 404), so it is left out rather than listed as a
  row that goes nowhere.
- **Group-only rows link to the group's calendar** (`/organizations/{id}/calendar`), because they
  have no public page.
- The website asks for the signed-in list only after sign-in resolves (AuthReady). If that call
  fails, it keeps the public list rather than showing an error over a list that is still true.
- The nearby search now keeps an event that is under way (it ends later than now, rather than
  starting later than now), so its card can say "Happening now".

## Where it shows

- Home: `Shared/HomeEventCountdown.razor` (asks) and `Shared/NextUpCard.razor` (draws), at the top
  of `Components/Pages/Home.razor`.
- What's On, `/events` and `/o/{group}/events`: `Organization/Public/PublicEventList.razor`.
- What's Near You on Home: the Events tab (`Shared/NearbyDiscovery.razor`), and the Tours tab
  through `Kit/BenTourCard.razor`'s new `ShowCountdown`.

## The design

- **Signal, dark mode first.** Every color is a token from `signal-tokens.css`, so light mode is the
  same rules:
  - the violet-to-cyan `--ben-accent-grad` for the bar, the dot and the tile digits;
  - `--ben-accent-2` (cyan) for "Happening now".
- **Home's card is one strip:**
  - the ghost's face (`/static/images/is-haunted-mark.svg`) in a soft violet halo with a cyan glow;
  - "You're coming · Event", the name, the group and the start in the event's time zone (`BenTime`);
  - ticking tiles ("02 DAYS 14 HRS 09 MIN", then "MIN SEC" in the last hour), and a 4px bar.

  The card's 1px edge is the gradient. Its fill is the ordinary card color, so the words read in
  both themes.
- **Motion, all off under `prefers-reduced-motion`:**
  - the ghost drifts 3px;
  - the dot breathes from the day before, and quickens in the last hour;
  - a sheen runs along the bar on the day itself;
  - the card's edge glows and breathes on the day, and turns cyan-first while the event is on;
  - the bar eases between ticks.

  With reduced motion the ticker also drops the seconds and counts in minutes.
- **Accessible:** each countdown is `role="img"` with an `aria-label` giving the start in words
  ("Ghost Walk. Starts Saturday, 10/24/2026 at 7:00 PM CDT"). Nothing ticks in a live region.

## Tests

- `Ben.Web.Tests/Services/CountdownMathTests.cs`:
  - the words: before, two days, weeks out, tomorrow night, tonight, today, running, over;
  - another zone;
  - the 25-hour day of 11/01/2026, and the day starting at the event's midnight across it;
  - the digits;
  - the spoken start;
  - the bar: before the baseline, halfway, at the start, running, ended, no listing date, listed
    minutes before, across DST;
  - which event Home picks: none, several, beyond the handful, running first, moving on.
- `Ben.Web.Tests/Services/EventCountdownScriptTests.cs`: the browser ticker under Jint agrees with
  the C# digits, words (across DST), bar and tiles.
- `Ben.Web.Tests/Controllers/MyNextEventsControllerTests.cs`: what counts, the labels, the links,
  canceled events, no double counting, not widening, and the total.
- `Ben.Web.Tests/Controllers/ReaderEventVisibilityTests.cs`:
  - a non-member, or a former member, never gets a group-only event;
  - a private-residence or case event never appears, even for a member;
  - a signed-out request is unchanged, and so is a signed-in one that did not ask;
  - invited is marked;
  - every row carries its listing date.
- `Ben.Web.Tests/Website/NextUpCardTests.cs`: what the card shows (no card, "and 2 more", "Asked
  for", the pass link, the refusal, tonight and running, moving on), the aria label, and the bar
  drawn before any script runs.
- The guards gain the new client methods (`LoadResultRenderedGuardTests`) and the new switch
  (`ReachableComponentTests`).

Before being relied on, the visibility, DST, other-zone, double-counting and canceled-event tests were
each run against code with that rule taken out, and each failed.

## Second commit: "What I'm going to" lists everything (10/16/2026)

Ben asked that the "and 2 more" gap be fixed: /my-events listed hosted bookings only, and Home's
card counts four kinds of thing.

- **The hosted bookings stay as they were.** Same rows, passes, party panels ("Change names and
  dietary needs") and ids. Each still-counted booking also gets the compact countdown, on the
  window of the nights it is for.
- **Below them, everything else Home counts**, in `Organization/Public/MyOtherEvents.razor`:
  calendar RSVPs, tour seats and team investigations. Soonest first, each with the compact
  countdown and the same standing words as Home ("You're coming", "Asked for", "You're invited").
- **Past items.** The page already keeps past bookings (a confirmed booking stays, for its review
  link), so the rest match that within limits: whatever ended in the last 30 days follows under
  **Recently**, most recent first. That is the same 30 days `api/public/events/mine` keeps.
- **The endpoint takes `take` and `pastDays`.** `api/me/next-events?take=200&pastDays=30` returns
  up to 200 items and up to 30 days back. Home still gets the defaults: the next 10, nothing past. A
  completed investigation is never counted as coming up, but is kept among the recent past.
- **The intro is true now:** "Events you've booked or asked to come to, the events and tours you
  said you'd come to, and the investigations you're on the team of. Nothing here is paid through
  this site."
- **"Nothing yet" only when there is nothing.** The empty and "Nothing coming up" alerts appear only
  when the section below is empty too. A failure loading that section is its own sentence with Try
  again.
- **Tests:** `MyEventsPageTests` (render) and three more `MyNextEventsControllerTests`. They were run
  against the old page and an endpoint that ignored `take`/`pastDays`, and failed.
