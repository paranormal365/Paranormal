# Item 235, re-planned from day one: hosted events, end to end

## Context

Item 235 is the product for the third kind of paying customer, the **event creator**: a venue like
The Thomas House Hotel running a séance weekend, or a group renting a hall for a one-night show.
Eight sub-phases of it exist on `feature/hosted-events-235` (credits, the event, nights, bookings,
menus, passes, letters, a unified room-or-seat layout model) with ~200 green tests and **not one
screen**. The retrospective Ben asked for found that every design mistake so far was caught by a
question or a test, never by using the thing, and that two model shapes were modelled from the
first concrete case instead of the general one. Ben's response: *"lets completely start
hosted-events-235 from scratch and you plan out this design in detail with any missing or
overlooked options or gaps and break it into logical phases."*

This plan is written as if nothing existed, then mapped onto the branch: code that fits is kept,
code that does not is rewritten, and the verdict per file is in the table near the end. The plan
of record on disk, `ProjectNotes/FeatureHistory/README-hosted-events-235.md`, is superseded by this
document for phases and decisions and kept for its Status history; phase 1 rewrites its head to
say so.

## What Ben decided on 2026-09-12 (the constraints every phase is designed within)

1. **Re-plan from day one, keep what fits.**
2. **One plan per event, of one kind: Rooms or Seats.** *"They only get one per event."* A dining
   plan is NOT a third kind: tables bound nothing and are assigned by the organizer after
   bookings close, so dining is a seating assignment over confirmed bookings in its own phase.
3. **Guests pick seats by clicking them on and off or dragging across a run**, and the selection
   goes to *waiting for confirmation* as a **soft hold with a configurable expiry** that other
   guests see as pending and cannot pick. Confirmed = locked in.
4. **The venue chooses per event** whether guests **pick** on the plan (soft hold) or **ask** in
   words and are placed (no hold; a waiting list).
5. **One party per unit-night.** A bunk room sold bed by bed becomes six units. A party may span
   several units (a family taking two rooms).
6. **Prices are shown and never charged.** *"We can tell them the price, but we do not collect
   money."* Null is "ask the venue", zero is genuinely free.
7. **One event, one venue.** *"I don't want to allow multi venues."*
8. **A cancelled event gives the credit back, as a credit and never as money, when cancelled at
   least 48 hours before the first night.** Plan-slot events simply free the slot. Venue-withdrawn
   restores the credit regardless of timing (recommended, the organizer did nothing wrong).
9. **Confirming an event is happening** is the organizer's declaration when the venue is theirs, a
   recorded arrangement (who agreed, when, reference) when the venue is off the platform, and a
   **grant from the venue** when the venue is another group's published profile. Plus an optional
   **minimum number with a go/no-go date**. *"That sounds correct."*
10. **Hosting on business bands only; the Events permission area on every band.** (Closes the two
    pricing gaps open since phase 1B; production changes only when Ben ticks it.)
11. **An event ended 14 days with an undecided request or hold**: expire them with the note "the
    event has passed", tell the organizer, archive on the next pass.
12. **Dietary notes** are readable by anyone with booking-read access; the door sees flags for its
    own night only, never free text.
13. **Must work on computer, iPhone and iPad** — a compact view as well as a desktop view.
14. **Apple Wallet** for the pass: designed, marked as a future iPhone/iPad version, not built.
15. From the competitor screenshots: revenue splits and in-cart ticket sales are out; the
    calendar mock-up is *"just the concept"* — borrow the spanning bar and hover popovers only.
    *"Telerik has a qr code generator"* — `TelerikQRCode` for display on the web.
16. **Bands** (2026-09-13): a party wears a colour that says what they are registered for — *"blue
    could be the full event with food, purple the full event, red is day one"* — so a steward knows
    by glance, and the colour shows on the scanner beside the name. **Built in phase 7, not as a
    phase of its own**, because the door is the screen that reads it and configuration built ahead
    of its screen is how this codebase collected eight write-only features. The shape is written
    out under that phase; the one thing it cannot derive today is "with food", which waits for
    dining in phase 13 and is a by-hand band until then.

## What the exploration established (only what a builder needs)

- **Page template**: `Manage/Events/OrgEventPage.razor` — auth in `OnAfterRenderAsync(firstRender)`
  via `UserState.WaitUntilAuthReadyAsync(RendererInfo.IsInteractive)`, a private form class filled
  from the record, save tuple `(saved, error)` shown in `#event-note`/`#event-error`, `col-12
  col-lg-3` sticky index + `col-12 col-lg-9` body, sections as `card mb-3` with ids. Its index has
  four `— soon` placeholders (lines 145–151) that this plan replaces. The board template is
  `Manage/Tours/OrgTourDateSeats.razor` ("Waiting on you" first, refusals verbatim, money note).
- **Client**: `IBenAdminClient` is an aggregate of 14 slice interfaces over a `sealed partial`
  adapter; all DTOs already exist in `Ben.Service.Models/Entities/`; **none of the fifteen hosted
  route groups has a website method**. `SendExpectingReasonAsync` carries a refusal only when it is
  prose (< 400 chars, not JSON/HTML), which is exactly how every hosted refusal is written.
  `GetAsync` flattens 403 to null (the ItemResult gap); there is no `BenItemState`.
- **Responsive**: the library has no width-aware C#/JS; idioms are `BenTabs`' non-wrapping
  scrolling strip, `table-responsive`, CSS-grid collapse at 992 px, `col-12 col-lg-*`, a 575.98 px
  reflow, `prefers-reduced-motion` blocks. **The designer and board will be the site's first
  deliberate compact modes**, built from these idioms, never a width service.
- **JS**: collocated `*.razor.js`, imported with the **leading-slash** `/_content/Ben.Web.Website.Library/…`
  form (the relative form breaks under deep routes); `DotNetObjectReference` + `_disposed` after
  every await; **JS must never create, move or remove DOM nodes**.
- **Guards a page must satisfy**: AuthReady on routable pages; every `for=` has an `id=`; no
  `margin:auto;`; commit buttons gated on `_busy` only; no lower-case attrs on `<Telerik*>`; no
  external assets in the shell (vendor decoders under `wwwroot/plugins/<name>/` with `LICENSE` +
  `VENDORED.md`); every `Save*/Confirm*/…` handler referenced from markup; `BenListState` or
  `.Failed` wherever a listed `LoadResult` method is called; every bell bucket has a row; every
  `<HelpLink>` resolves to a real heading; purge covers every NoAction FK.
- **Navigation**: `@page` is the registration; org-manage pages hang off `OrgEventPage`'s index and
  a card in `OrgSettingsManager.razor` (events has none today); Playwright crawls enumerate
  `@page` from source, `{Token}` routes are skipped.
- **Harness**: five env-var seats (sarah = org admin, james = plain member, daniel = guest with no
  group); `OrgIdBySlugAsync`; `ClickUntilAsync`; `WaitUntilLoadedAsync`; **no viewport helpers**
  (override `ContextOptions()`; `NearbyDiscoveryTests.cs:28` is the template); the one real
  responsive assertion is `WasmEditorTests.cs:474`. `scripts/run-e2e.sh --filter <class>` on
  `IsHauntedDb_e2e`; `-p:IsTestProject=true` is mandatory. Tests build their own world through the
  API (`TourSeatTests.cs`) and purge it. `HelpMediaCapture` shoots dark 1440×900 @2× and needs a
  `width` parameter for phone shots.
- **Seeding**: `Ben.Data.WebApi/SeedData/*` runs at API startup; idempotent by stable identifier
  (`BillingDemoSeeder`'s per-block pattern); **nothing hosted is seeded**; `IsHauntedDb_player` is
  the test copy, `IsHauntedDb` is production; `dotnet ef` needs `--connection`.
- **QR**: display = `TelerikQRCode` (`TwoFactorPanel.razor:98`, inside the only allowed `bg-white`
  wrapper, `FixedLightUtilityGuardTests`); email = the server PNG (`EventPasses.Png`); scanning =
  nothing exists; `BarcodeDetector` where present, vendored `jsQR` otherwise; `getUserMedia` works
  on `localhost` but not on a LAN address over HTTP (the `https` profile is `:7050`).
- **Phone**: `Features/Events/` exists with `EventsView`/`EventDetailView` against
  `api/public/events`, `/mine`, `/rsvp?seats=`, `/my-seat/acknowledge`; additive-only while build
  4 is in review.

## Design of record

### Model, as a diff against the branch

| Entity | Verdict | Change |
|---|---|---|
| `HostedEvent` | CHANGE | + `LifecycleState` (Draft 0, Published 1, Live 2, Ended 3, Archived 4, Cancelled 5, VenueWithdrawn 6) replacing `IsPublished` (dropped; `IsLive`/`IsActive` become derived); + `LiveAtUtc`, `EndedAtUtc`; + `VenueArrangement` (Self 0 / External 1 / PlatformGrant 2), `VenueContactName`, `VenueAgreedOnUtc`, `VenueReference`, later `VenueGrantId` (SetNull, phase 9); + `MinimumGuests?`, `GoNoGoDeadlineUtc?`, `GoNoGoDecision` (Undecided/Go/NoGo), `GoNoGoDecidedUtc/By`, two once-only reminder markers; + `BookingMode` (**Ask 0**, Pick 1 — zero is what every existing row did); + **`HoldMinutes` int** default 2880 (not `TimeSpan`: EF maps it to SQL `time`, capped at 24 h), check 15…20160; + `LastDigestSentUtc` |
| `HostedEventLayoutUnit` | KEEP | as built (Rooms unit backed by `PlaceRoom`, Seats unit with `Label`; `Section`, `Capacity`, `Price`, `LayoutRow/Column`) |
| `HostedEventUnitBlock` | NEW | Unit (Cascade), Night? (NoAction; null = every night), Kind (Blocked / HouseHold), Note. Filtered unique per (unit, night) and per (unit) where night is null. Blocks are a child table because "the Suite is held back on Saturday only" is the common case |
| `HostedEventBooking` | CHANGE | `Status` += **Held 4, Expired 5** (append-only); + `HoldExpiresUtc?` (kept after Expired as the fact of when it lapsed). Unique filtered `(HostedEventId, LeadAppUserId) WHERE Status IN (Requested, Confirmed, Held)` |
| `HostedEventBookingNight` | CHANGE | + `ReleasedUtc?` stamped when the booking leaves Held/Confirmed, never cleared; + `People?` (null = whole party) so a party can split across two rooms; unique `(BookingId, NightId, UnitId)` replacing `(BookingId, NightId)` — **the index that collapsed a party's three seats to one**; **unique filtered `(NightId, UnitId) WHERE UnitId IS NOT NULL AND ReleasedUtc IS NULL` — the concurrency arbiter** |
| `HostedEventBookingGuest`, `HostedEventPass`, `HostedEventMenu/Item`, `EventCredit`, `EventAttendanceInvite`, `PlaceRoom` | KEEP | as built |
| `OrgCalendarEventAttendee` on the umbrella | KEEP, one writer | see the invariant below |
| `HostedEventStaff*`, `HostedEventCheckIn` (per night) | NEW, phase 7 | as the README's phase 5, check-in per night not per pass |
| `EventBookingAlertPreference` | NEW, phase 8 | per person per org: immediate / digest / cadence |
| `OrganizationVenueProfile`, `VenueHostingRequest`, `OrganizationVenueGrant` | NEW, phase 9 | as the README's phase 8 |
| `HostedEventSession*` | NEW, phase 10 | as the README's phase 4 |
| `HostedEventDiningTable`, `HostedEventDiningSeat` | NEW, phase 13 | over Confirmed bookings only |
| `EventBookingKind`, `EventBookingStatus` (phase 0) | DELETE | dead duplicates |

### The booking status machine, and what counts

- **Ask mode**: Requested → Confirmed | TurnedDown | withdrawn (deleted). **Pick mode**:
  Held(expires) → Confirmed | Expired | TurnedDown | released (deleted). Confirmed → Cancelled.
  Expired, TurnedDown, Cancelled are terminal; a new ask is a new row.
- **Held and Confirmed count against capacity and appear as pending/taken on every plan.**
  Requested, Expired, TurnedDown, Cancelled never do. Old DECISION 7 ("only Confirmed holds") is
  superseded; it survives as the Ask mode's behaviour.
- Ask-shaped Requested bookings are legal inside a Pick event (the email door and the shipped
  phone can only ask); Held is illegal inside an Ask event.
- Seat state is **derived**, never stored: free / held / taken / blocked / house / mine.

### Invariants (each pinned by a named test, each proved by breaking the rule once)

1. One live booking per lead per event (filtered unique index; `SqliteTestDb` honours it).
2. One unreleased party per unit-night (`BookingConcurrencyTests`: two contexts, the second
   `SaveChanges` throws and is translated to the sentence).
3. Every night row of a Held/Confirmed booking is a night of its own event.
4. A Seats unit has capacity 1. 5. A Rooms unit's `PlaceRoom` is at the event's place and org.
6. A live pass exists only for Confirmed. 7. **The umbrella row equals the booking's truth**:
   Confirmed → Accepted/Reserved/Seats=party; Requested or Held → Invited/Requested/Seats=party;
   everything else → row removed (`UmbrellaAttendeeTruthTests`, a theory over every status with
   enum coverage asserted). 8. An expired hold never counts. 9. Publish requires nights, a complete
   arrangement, and a slot or a credit. 10. One venue per event. 11. Held is only written on a
   Pick event. 12. **Only `BookingTransitions` writes a booking's status, `ReleasedUtc`, or an
   umbrella attendee** (`BookingWriterGuardTests`, regex over the API).

### Concurrency

The filtered unique index is the arbiter. A hold or a confirm runs in one transaction: insert
booking → insert night rows → save → write umbrella → commit. A unique violation (SQL 2601/2627,
SQLite 19) rolls back, re-reads the requested cells, and returns **409 "Seat H9 was taken a moment
ago. Pick another."** with fresh cell states so the picker repaints without a second call.
All-or-nothing for a party's several seats. Rejected alternatives: `rowversion` (no unit-night
row to version), serializable transactions (range locks over 1,200 cells escalate and deadlock),
an in-process semaphore (invisible to a second worker). Residual and accepted: two organizers
confirming the last two day passes at once can exceed the cap by one party (pinned as known).

### Jobs (the `IScheduledJob` pattern of `EventCreditExpiryJob`: scoped, five-minute pass, marker after send)

| Job | Does | Notifies |
|---|---|---|
| `HoldExpiryJob` | `BookingTransitions.Expire` per booking: Status Expired, `ReleasedUtc` on its rows, umbrella row removed; one save per booking in a try | guest letter ("your hold on C4–C6 lapsed at 6:00 PM; you're still on the list as a request") + bell; organizer sees it in the digest only |
| `HostedEventLifecycleJob` | Published→Live at the first night's start in the venue zone; Live→Ended after the last night; Ended→Archived at +14 d — **first expiring any Requested/Held with "the event has passed" and messaging the organizer, archiving next pass** (decision 11); go/no-go reminders at −7 d and −1 d; a bell row while a passed deadline is undecided | platform messages to deciders |
| `EventBookingDigestJob` (phase 8) | per event, daily inside the booking window, weekly outside, never when empty | deciders minus opt-outs |

### Permissions (`Services/Access/HostedEventAccess`, shipped in phase 4 with role grants; staff rows added in phase 7)

SuperAdmin, Owner, Administrator pass everything. Otherwise: `HostedEvent.Read` → see the plan;
`HostedEvent.Update` → edit event/nights/layout/blocks/arrangement/menus/go-no-go (publish also
needs `OrganizationSettings.Update`, because it spends money); `EventBooking.Read` → the board with
names and **dietary**; `EventBooking.Update` → decide, holds, edit, on-behalf, invite, passes;
`EventCheckIn.Create` → the door, with dietary *flags* for tonight only. Roles fail closed until a
band includes the Events area, which decision 10 puts on every band. A guard asserts no hosted
controller names `OrganizationSecurityTable.OrganizationSettings` except publish and credits.

### Phone compatibility (additive only while build 4 is in review)

The shipped app keeps decoding every field and route unchanged. `flags.canRsvp` is **false with a
sentence** for a Pick-mode event ("Seats for {name} are picked on a seating plan. Open
{site}/o/{org}/events/{slug} to choose yours."), so the shipped build never draws a button that
will 409. Ask mode with day passes → a Requested day pass through `HostedEventGuestDoor`; Ask mode
with none → 409 naming the page. `DELETE rsvp` on a Confirmed booking → 409 with the page; on
Requested/Held → withdraw through `BookingTransitions`. New fields on `PublicEventRecord` are
nullable. The future app version uses the `api/public/hosted-events/*` family.

### Security and performance

Holds require sign-in; at most **5 Held bookings per account** across events; a hold covers at most
the party size (≤ 40); a new 30/min `HostedBookingPolicy` on the public booking writes;
`Referrer-Policy: no-referrer` on the pass page and `referrerpolicy` on the `<img>`; the request
logger redacts `event-passes/{token}.png`; the door scan is per event and rate-limited per user.
Performance: today's board does ~480,000 LINQ passes per render (per-cell `PeopleIn` over every
booking). Replace with a three-query `PlanOccupancy` aggregate (units ~36 KB once; occupancy as
`{u,n,s}` ~12 KB for a half-sold weekend; names only in the org view), **one night pane at a time on
compact**, per-section components with `ShouldRender`, **no per-cell `@onclick`** — one delegated
pointer listener per pane in JS that toggles a class and calls back once on pointer-up.

### The compact view, as a rule (decision 13)

Every hosted screen has a declared behaviour at three widths and a Playwright fixture parameterised
over `(375,812)`, `(768,1024)`, `(1280,800)`: the primary control `ToBeInViewportAsync`, no
horizontal page scroll, wide content scrolling inside its own container. The plan on a phone is
read-mostly with per-unit sheets and 44 px cells; the board is cards with pills, not a grid; the
door is phone-first with 56 px buttons; the guest's picker has a sticky summary bar inside the
card; every animated state carries a `prefers-reduced-motion` block. A source guard,
`SeatMapHasCompactFallbackTests`, refuses a page that renders the grid without the compact list.

## Phases

Each phase leaves the site coherent, ends with a "verified by" walk on the running site against
`IsHauntedDb_player`, and updates tests, help, changelogs, the worklog and the product PDF in the
same commit. Phase 2 is deliberately the first screen and is shown to Ben before phase 3 starts.

### Phase 1 — Plumbing, the venue's rooms, and the defects that block everything

**Goal**: every hosted endpoint has a website client method, a venue can make a room bookable, and
the defects found by the three planners that need no model change are fixed.

- Client: new `Ben.Web.Services/IBenEventBookingClient.cs` + `WebApi/BenAdminClientAdapter.EventBooking.cs`
  added to the `IBenAdminClient` aggregate (layout GET/PUT, board, confirm/turn-down/cancel/edit,
  on-behalf, invite, dietary, menus GET/PUT, pass issue/revoke/reissue/email, scan, public
  request/my-booking/acknowledge/withdraw/pass/menus/mine), all via `SendExpectingReasonAsync`;
  `GetPublicHostedEventAsync` → `GetItemAsync`; new `Kit/BenItemState.razor`.
- `Organization/PlaceRoomsManager.razor`: *Sleeps*, *Bookable*, *Beds* columns and add-form
  fields; inline row edit; `ClearCapacity`; **compact reflow to stacked cards** (`td::before
  { content: attr(data-label) }`) — the org side's first deliberate reflow.
- Server fixes: `HostedEventLayoutUnitChoice.Id` so renaming a booked seat is not delete+create;
  layout refusals as `LayoutRefusalRecord(Sentence, UnitIds)` (409) so the designer can ring the
  seats; `.ics` nights built in the event's zone, not 18:00 UTC; `GetMyPass` returns a revoked
  pass instead of "not issued yet"; `Withdraw` on TurnedDown → 409; `POST bookings/{id}/pass/email`;
  place merge repoints `HostedEvents.PlaceId`; `PlaceRoomController.Delete` refuses by name when a
  unit references the room; delete `EventBookingKind`/`EventBookingStatus`.
- `OrgEventPage.razor` index: the four `— soon` spans become links (*Plan*, *Bookings*, *Menus*,
  *Staff and the door*, *Files* — the last two still `— soon` until their phases); Details card
  links to *Describe the venue's rooms*. `OrgSettingsManager.razor` gains an events card.
- Tests: `BenItemStateTests`; `PlaceRoomsBookableTests` (Playwright, three widths);
  `LabelAssociationTests`/`OrphanedHandlerTests` fire on the new inputs.
- Help: *Rooms* gains "Sleeps, bookable and beds". Shot `org-place-rooms-bookable`.
- **Verified by**: describe six Thomas House rooms on the running site; follow *Plan* from the event
  page and get an empty plan, not a 404.

### Interlude after phase 1 — item 239, the mail outbox (Ben, 2026-09-12: *"Yes, do that after merging Phase 1"*)

Not an item-235 phase, but it lands **between phase 1 and phase 2** by Ben's decision, because
three phases from here on add letters and it is cheaper to be born inside the outbox than
retrofitted into it. `OutboxEmailService` decorates `IEmailService` (one seam, twenty callers
untouched), `MailSenderJob` sends with backoff, transient and permanent failures are told apart,
bodies are scrubbed after thirty days while the metadata stays for ever, an unconfigured
deployment queues anyway, and the mail diagnostics screen grows the list of every letter with a
Retry. Full design and the five judgement calls: item 239 in `ProjectNotes/Future-Improvements.md`.
**Consequence for this plan:** phase 5's invite result field `Sent` becomes `Queued`, and every
"we could not send it" sentence in phases 3–8 becomes "it is queued" — write them that way from
the start rather than changing them later.

### Phase 2 — The layout designer (the first screen)

**Goal**: a venue lays out a Rooms plan in two minutes and a 400-seat Seats plan in five, on a
desktop or iPad, and reads and lightly edits it on a phone.

- Files: `Kit/Plans/BenPlan.razor(.css)` (one component; modes Edit / Read / Pick),
  `Kit/Plans/BenPlan.razor.js` (one delegated pointer listener per pane: click toggle, mouse/pen
  paint-drag via `setPointerCapture`, touch tap and press-250-ms-then-paint via
  `elementFromPoint`; toggles a class only; calls back once on pointer-up), `Kit/Plans/PlanModel.cs`
  (working copy, undo/redo cap 50, every operation, pure), `Kit/Plans/PlanLabels.cs` (A–Z, AA–,
  skip I/O, numbering directions, patterns `{row}{seat}`, `{row}-{seat}`), `Kit/Plans/BenPlanDesigner.razor`
  (toolbar, tray, selection bar, *Add a block*, unit sheet, legend), `Kit/BenPopover.razor` (the
  hover popover item 237 also needs — Blazor-owned state, CSS-anchored, tap opens the sheet on
  touch), `Manage/Events/OrgEventLayout.razor` at `/organizations/{OrgId:guid}/events/{EventId:guid}/layout`.
- Grid: CSS grid of `<button role="checkbox">`s positioned by `grid-row/grid-column`; cell size a
  `clamp()` token so 20 columns fit 1280 px; sections coloured by `TourPlate.HueStyle`; legend
  "Balcony · 120 seats · $18"; a row whose units share a section gets a gutter caption (floors
  for free). Keyboard: arrows, Space toggle, Enter opens the sheet, Delete, Ctrl/Cmd+Z, Shift+Arrow
  extends; a live region announces selection.
- Rooms: tray of `IsBookable && IsActive` rooms by floor; click-tray-then-cell or HTML5 drag;
  *Auto-arrange* one floor per row; unit sheet = sleeps-for-this-event, price/night, note,
  section, block (Blocked / HouseHold, this night or every night), remove.
- Seats: **Add a block** (rows A–M × 20, start number, direction incl. odd/even from centre,
  label pattern, section, price; live "13 rows × 20 = 260 seats, C4 … M20"); **Insert aisle**
  shifts columns; shift-click rectangle; section chip and row letter select; selection bar (set
  section / price / note / relabel / move / block / delete); capacity fixed at 1.
- Save = `SetEventLayoutAsync` replace-the-set; refusal shown verbatim with the offending units
  ringed via `UnitIds`; unsaved-changes guard via `RegisterLocationChangingHandler`; kind change is
  a deliberate control refused by the server when bookings exist.
- **Compact (≤ 767 px)**: read-mostly — toolbar collapses to Add block / Select many / Undo / a
  Tools dropdown; tray becomes a sheet placing into the first free cell; grid in `overflow:auto;
  touch-action: pan-x pan-y pinch-zoom` with 44 px cells and a *Fit* toggle; every per-unit edit
  through the sheet; one line says a 400-seat layout is a desktop or iPad job.
- Read mode overlays occupancy for one night (Free / Pending hatched / Confirmed / NotOffered /
  Blocked / House) with a `BenPopover` naming the party — organizer only.
- `HostedEventDemoSeeder` (first blocks): Thomas House org/place/six rooms, the Rooms event, the
  Seats event 12 × 20 in two sections with two blocked seats, granted credits — so the designer
  opens on something real.
- Tests: `PlanModelTests`, `PlanLabelsTests` (260 labels, C4 in row 3, odd/even, insert-gap shifts
  only the right side, undo exact, pair-or-neither positions); Playwright `EventLayoutDesignerTests`
  at three widths (block, shift-click, section price, aisle, save, reload; the Rooms refusal by
  name).
- Help: "The plan: rooms or seats", "Laying out a theatre quickly", "The plan on a phone"; shots
  incl. a 375-wide one (add `width` to `HelpMediaCapture.ShootAsync`).
- **Verified by, with Ben**: the Thomas House rooms plan on a desktop, the 260-seat Stalls with a
  centre aisle, then the same plan on an iPhone-width window with a price edited through the
  sheet. **Nothing in phase 3 starts until this walk has happened.**

#### Phase 2 as built (2026-09-12) — three departures from the plan above

1. **`BenPopover` is deferred to phase 5.** It was listed here for the Read-mode occupancy overlay,
   and phase 2 ships no screen that shows occupancy — the board is phase 5 and the public page is
   phase 6. Building it now would be a component with no consumer, which is the shape of the eight
   write-only features this codebase has had to go back and fix. It also has a real constraint
   nothing can settle without a consumer: the grid lives inside `overflow-x: auto`, which clips a
   bubble drawn above a seat, and where the popover is rendered depends on what the board needs
   from it. Phase 5 builds it against its actual use. Item 237 is unaffected — it did not have one
   either.
2. **Selection accumulates and never replaces.** The plan called for a shift-click rectangle and a
   *Select many* toggle for touch. The grid reports a gesture as "these squares, added or removed",
   so every tap already adds and a mode toggle would have been a lie; shift-extend is still there.
   The cost is that Clear is a button, on the selection bar beside the count.
3. **Squares are fixed lengths, not a shrink-to-fit clamp.** The clamp was written and did not
   work: a percentage in a grid ROW track resolves against a height that is auto, so a thirteen-row
   house collapsed and `overflow-y: hidden` cut six rows off the bottom while every test passed.
   Found by looking at a screenshot. A source guard and a Playwright measurement now hold it. Rooms
   get their own, larger square — a room is a name, a seat is a number.

Everything else shipped as written, plus `HostedEventDemoSeeder`, the layout page, 19 Playwright
tests at three widths, and the three help sections with their shots.

### Phase 3 — The event's truth: lifecycle, arrangement, go/no-go, modes, credits back

**Goal**: an event has one state, says who agreed it is happening, can depend on numbers, and
tells the organizer exactly what stands between the draft and going live.

- Migrations 1–3: `HostedEventLifecycle` (hand-written: add `LifecycleState`, backfill from
  Cancelled/Archived/IsPublished, drop `IsPublished`, re-index; `Down` recreates it),
  `HostedEventVenueAndGoNoGo`, `HostedEventBookingModes` (backfill Ask, `HoldMinutes` 2880 with
  the check constraint). `MigrationShapeTests` for 1.
- `HostedEventLifecycleJob`; `HostedEventCalendarSync` reads the state (public while
  Published/Live/Ended; title prefix for Cancelled and VenueWithdrawn); entitlement and credit
  queries read the state; `SubscriptionLimit.ActiveHostedEvents` counts Published/Live/Ended.
- Publish gates in `HostedEventController`: nights; arrangement complete for its kind (External
  needs contact + agreed-on; PlatformGrant refused in words until phase 9); plan with a unit or
  day passes; contact line; then slot or credit. `GET {id}/readiness` → `HostedEventReadinessRecord`
  (items with Done/Required/Href) and the same list enforced on publish.
- **Cancel restores the credit** ≥ 48 h before the first night (site setting
  `events.cancellation-credit-window-hours` beside the credit keys in `SiteSettingsService.cs`);
  no-go routes through cancel; **un-cancel re-spends the same credit if still spendable, else is
  refused naming where it went**; VenueWithdrawn restores regardless (phase 9).
- `POST {id}/go`, `POST {id}/no-go`; `UpsertHostedEventRequest` gains `BookingsCloseAtUtc`,
  `DayPassPrice`, `BookingMode`, `HoldMinutes`, `MinimumGuests`, `GoNoGoDeadlineUtc`, the
  arrangement fields.
- `OrgEventPage.razor`: *Ready to publish* checklist card above the publish button; *Venue
  arrangement* card (Self / External with fields / Ask the venue — greyed "coming with venue
  profiles"); *Guests* card grows *How guests choose* (Pick with hold hours 0.25–336 / Ask; default
  Pick for Seats, Ask for Rooms), *Bookings close* in the event zone, *Day pass price*, *Minimum
  numbers* with a Numbers card and Go / No-go; the state badge reads the lifecycle; cancel
  confirmation says whether the credit comes back and why. `OrgEvents.razor` rows show the state.
- Tests: `HostedEventReadinessTests`, `HostedEventLifecycleJobTests` (clocks-back weekend, running
  twice acts once), `EventCreditsTests` for restore/un-cancel, `HostedEventControllerTests`
  publish gates; Playwright `EventReadinessTests` (empty event names four things; fix; publish;
  checklist first in viewport at 375).
- Help: "Before you publish", "Who confirms the event is happening", "Minimum numbers", "Pick or
  ask", "Calling it off and your credit".
- **Verified by**: publish an empty event and be told the four things; fix; publish; cancel
  three days out and watch the credit return to the group's card; un-cancel; publish again for
  nothing.

### Phase 4 — Holds, transitions, capacity, access (server)

**Goal**: two guests cannot hold one seat, a hold lapses by itself, one piece of code writes every
booking status, and reading a guest's allergy needs the right to.

- Migrations 4–5: `HostedEventSoftHolds` (hand-written: `HoldExpiresUtc`, `ReleasedUtc` backfilled
  for TurnedDown/Cancelled, `People?`, night unique widened to include the unit, **pre-check that
  THROWs if two live parties already share a unit-night**, the two filtered unique indexes),
  `HostedEventUnitBlocks`.
- `Services/Events/BookingTransitions.cs` — the single writer (Request, Hold, Confirm, TurnDown,
  Release, Expire, Withdraw, ApplyUmbrella); `EventCapacity` rewritten: `Holds(status)` = Held or
  Confirmed; `PartyIn(unit, night)` replaces `PeopleIn` (one party per unit; the party must fit
  the unit's capacity; a split party checks `People`); block-aware `IsAvailable`;
  `WhyThisRoomCannotTakeThem` keeps its verbs. `Services/Events/PlanOccupancy.cs` — the three-query
  aggregate for board and picker. `HoldExpiryJob`. `EventDietary.includeRequests` →
  `includeUnconfirmed` (Requested + Held).
- Umbrella fences: `PublicEventController.Rsvp/CancelRsvp/AcknowledgeSeat` get the hosted branch
  (decision 5, finally); `OrgCalendarController` attendee approve/turn-down/delete/by-email/guest-
  invites refuse on an umbrella ("Places at {name} are booked through the event, not the
  calendar."); `PublicEventAttendanceController`'s umbrella write goes through `BookingTransitions`.
- `HostedEventAccess` + the Events area's role grants; every hosted controller switches from the
  settings key; the board withholds `LeadEmail` and dietary from anyone without `EventBooking.Read`.
- Rate limits and per-account caps; `HostedBookingPolicy`.
- Seeder: bookings in every status incl. the Held one re-armed to expire in 20 minutes; the
  duplicate-phrasing dietary notes; menus; the unclicked invite; the dev-only band settings.
- Tests: `BookingConcurrencyTests`, `UmbrellaAttendeeTruthTests`, `HoldExpiryJobTests`,
  `BookingTransitionsTests`, `HostedEventAccessTests` matrix, `PublicEventControllerHostedTests`
  (four RSVP rows), `EventCapacityTests` reworded; guards `BookingWriterGuardTests`,
  `UmbrellaFenceGuardTests`, `HostedControllersUseHostedEventAccessTests`, `RefusalIsASentenceTests`,
  `BookingStatusCoverageTests`; `OrganizationPurgeCoverageTests` will fire on `UnitBlocks` and be
  satisfied; discrimination proved by dropping the filtered index and watching one test fail.
- **Verified by**: two API clients hold H9 in the same second; one gets 409 naming H9; the Held one
  lapses in the job's next pass and the seat is free; james (plain member) opens the board URL and
  is refused in words.

### Phase 5 — The booking board, the menus page, the kitchen's sheet

**Goal**: every decision about a party happens on one page that reads as a queue on a phone and a
plan on a desktop.

- `Manage/Events/OrgEventBookings.razor` (`…/bookings`): nights as pills; **Waiting on you**
  first (requests, holds with "lapses in 41 min" ticking client-side, lapsed-still-asking,
  cancellation requests) with Confirm / Turn down / Extend hold / Release / Keep; the plan for the
  chosen night in Read mode; a units × nights grid behind a toggle (≥ 992 px default on);
  Everyone with pass state; day passes **per night** plus whole-event; *Release lapsed holds now*;
  Invite and Book-for-somebody (member picker or email; the invite result says whether the letter
  went); Dietary sheet and Print links.
- `Manage/Events/BookingDecisionSheet.razor` (`BenModal`, fullscreen on phone): confirm with per-
  night unit picker defaulting to what they hold/asked and *Move party* through the plan in Pick
  mode; edit with the pass-reissue warning; passes history with revoke (reason required — "the
  door reads this out"), reissue, email again; turn down / release with reasons; refusals shown
  inside the sheet.
- `Manage/Events/OrgEventMenus.razor` (sittings in the host's order with arrows; presets
  Breakfast/Lunch/Dinner/Snacks; one Save) and `OrgEventDietary.razor` (three numbers, tally,
  lines, include-unconfirmed switch, by-night filter, `@media print` one page per night).
- New endpoints: `POST bookings/holds/release-lapsed`, `POST bookings/{id}/hold/extend`, board
  gains `HoldExpiresUtc`, `UnitNights[].Pending`, `DayPassNights[]`; `GET dietary?night=`.
- **Compact**: cards not a grid; pills Queue (n) · Plan · Everyone · Passes; two taps from the
  event page; sheet fullscreen with a sticky action bar.
- Tests: Playwright `EventBookingBoardTests` (daniel asks for the Blue Room Fri–Sat as 4; refusal
  "sleeps 2 more" in the sheet; move to the Suite; pass issued; at 375 the Queue card's Confirm in
  viewport and the Plan tab scrolls), `EventMenusTests` (breakfast under Friday survives reload;
  "vegan × 2" and two lines for no nuts / nut allergy); `LabelAssociationTests` on repeated rows.
- Help: "The booking board", "Holds and what lapses", "Day passes by the day", "On the night,
  from your phone", "Menus", "The dietary sheet" (existing sections revised).
- **Verified by**: two browsers and a phone — the guest holds seats, the board shows them hatched
  within a refresh, the organizer confirms from the phone, the guest's bell rings.

#### Phase 5 as built (2026-09-12) — five departures from the plan above

1. **A sitting's dishes are a block of typed lines, not a row each.** The plan implied an editor
   with an Add per dish. Typing a menu *is* typing lines, and a form standing between a host and
   something they can write out in ten seconds is a form they will not use; the course comes before
   a colon (`Starter: Tomato soup`) because that is already how people write a menu down. Nothing
   is required and nothing is refused.
2. **The kitchen's per-night sheets are built by asking the server once per night**, not by
   splitting the lines client-side. The numbers a cook acts on are counted against party sizes the
   line does not carry, so a client-side split would print Saturday's allergies beside the whole
   weekend's head count — the one mistake the page exists to prevent.
3. **The night rule lives in `EventDietary.OnNight`**, pure and beside the counting, rather than in
   the controller: a party with no live night of its own is a pass for the whole run and counts on
   every night of it, and that claim needed a test that could see it break. Three tests hold it and
   each was watched failing with the rule broken.
4. **The seeder gained the menus and the party with allergies** — planned for phase 4, written
   here, because a screen that opens on nothing cannot be judged. Four sittings across the weekend
   including Saturday's breakfast under the Friday, and a confirmed party of four with three notes
   (two of them the same words) and one person nobody named. It confirms through
   `BookingTransitions`, like everything else that moves a booking.
5. **`BenPopover` is deferred again, to phase 6.** The board's plan is read-only and names nobody,
   so there is still no consumer; the guest's picker is the first screen that wants one.

Everything else in the menus and dietary slice shipped as written: the presets and the arrows, one
Save, the three numbers, the tally that never guesses, the night pills, `GET dietary?night=`, the
`break-after: page` print rule, and both help sections rewritten with four new shots.

### Phase 6 — The guest, end to end

**Goal**: a signed-in guest asks or picks, sees the answer, holds a pass; a stranger can ask for a
day pass and is told the truth on the confirmation page.

- `PublicEventDetail.razor`: hosted branch inside `event-act` before the tour-seat branches;
  `EventBookingCard` (Requested / Held with countdown / Confirmed / TurnedDown / Cancellation
  requested / Released / Venue withdrew / Called off / Go-no-go pending / Hold lapsed — colour and
  glyph and text); `AskForPlaceForm` (kind; nights as checkboxes — two of three is two ticks,
  Saturday-only day pass is one tick with no unit; party size; preferred unit with section, holds,
  price; guests with dietary; note; price line "you settle this with {org}, not with IsHaunted");
  **the picker** = `BenPlan Mode="Pick"` (party size derived from seats picked; on Rooms the rail
  sums `Holds`; "Hold these seats" → `POST holds`, availability-checked, 409 keeps the rest;
  availability polled every 30 s; hold expiry flips the card in place with *Pick again*; confirmed
  = locked, *Ask the venue to move you*); compact = sticky summary bar inside the card, grid in
  its own scroller, ≥ 40 px cells. Signed-out: plan read-only with availability, seats
  `aria-disabled`, email form for a day pass only.
- `Organization/Public/MyEvents.razor` (`/my-events`); the bell row retargeted from `/events`;
  `Organization/Public/MyEventPass.razor` (`/my-events/{EventId:guid}/pass`): `TelerikQRCode
  Size="240px"` inside a shared `Kit/BenQrPlate.razor` (the single fixed-light wrapper, added to
  `FixedLightUtilityGuardTests`; `TwoFactorPanel` refactored to use it), event/venue/lead/"Admits
  3"/nights with units, **pass code** = last 6 hex, *Email it to me*, print CSS; a revoked pass drawn
  at 40 % with a band and the reason, never a blank.
- `EventAttendanceConfirm.razor` (`/attending/{Token}`): hosted branch — "You've asked for a
  place" with kind/party/nights, *Open the event*, *Set a password to manage your booking*
  (reusing the forgot-password issuance).
- Letters: ask acknowledgement; hold placed (with the deadline in the venue zone); hold lapsed;
  go / no-go / venue withdrew; existing confirmed/turned down/released kept.
- New endpoints: `POST hosted-events/{id}/holds`, `GET {id}/plan` (public occupancy: states and
  counts, never names; the reader's own cells flagged), `GET my-booking/pass` incl. revoked,
  `POST my-booking/pass/email`, `mine` incl. Cancelled for unended events; additive fields on
  `PublicHostedEventRecord` and `MyHostedEventBookingRecord`; `EventAttendanceConfirmation` gains
  the hosted fields.
- Tests: Playwright `HostedEventGuestTests`, `HostedEventSeatPickerTests` (drag C4→C6 at 1280 →
  three `aria-checked`; tap three at 375 with the bar in viewport; keyboard Shift+Arrow with the
  live region; a second context sees them hatched and disabled; a 1-minute hold lapses in place),
  `AttendingTokenTests` (token read from mail diagnostics), `HostedEventPassTests` (revoke → band
  and reason); unit tests for every letter; guards `SeatMapHasCompactFallbackTests`,
  `FixedLightUtilityGuardTests`.
- Help: new `going-to-an-event.md` (asking, picking your seats with the legend and keyboard table,
  what the venue's answer means, your pass and the pass code, coming without an account, if the
  event changes) registered in `HelpCatalog.cs`; shots at 1440 and 375.
- **Verified by**: guest A drags a run; B sees it pending within 30 s; the venue confirms A; B sees
  taken; A's pass scans from the venue's laptop; incognito email → link → "asked for a place" →
  set a password → the booking on `/my-events`.

#### Phase 6 as built (2026-09-13) — six departures from the plan above

1. **`PlanCellState` moved to `Ben.Data.Common`** as `HostedEventPlanCellState`. The plan had the
   states living in the website's component library, which was right until the server had to
   answer the same question for the guest's picker. Two lists of the same six words in two
   assemblies is two chances to disagree about what "held" means in front of somebody buying a
   seat. How a square is DRAWN stayed with the component.
2. **The public plan sends only the squares that are not free.** Four hundred seats across three
   nights is twelve hundred rows of "nothing here" otherwise, and free is the default the picker
   already assumes.
3. **The summary bar is FIXED on a phone, not sticky.** Sticky was written and never stuck: the
   site's own `.content-wrapper` sets `overflow-x: hidden`, which makes it a scroll box on both
   axes, so a sticky child pins to the bottom of the whole page rather than the window — off
   screen from the first tap. The grid also takes a ceiling and scrolls inside it. Worth
   remembering: **page-level `position: sticky` does not work anywhere inside this layout.**
4. **`BenQrPlate` is the one component allowed a fixed-light background**, and the authenticator
   panel was refactored onto it. The guard now names the component rather than a growing list of
   pages.
5. **`AttendingTokenTests` is not written.** The plan wanted the token read from mail diagnostics;
   the outbox record deliberately carries no body, so the token is not reachable from the harness.
   The controller path has unit tests and the page's branch has none — the honest gap, recorded
   rather than papered over.
6. **`BenPopover` is deferred again**, now to phase 11. The picker turned out not to want one: a
   square's state is drawn on the square, and a bubble over a grid inside a scroller was solving a
   problem nobody had. The CMS's read-only plan (phase 11) is the first screen that wants names on
   hover.

Four defects the browser found that no unit test could:

- The pass door picked an **arbitrary booking** among a guest's history at one event, so a guest
  released once and confirmed later was shown "this booking was released" while their live pass
  sat one row below. `First` with no `OrderBy`.
- A **missing sprite symbol** (`qrcode`, which is a Bootstrap name) renders as an empty `<svg>`
  with no intrinsic size, which the spec says is 300×150 — a list came out twenty-two thousand
  pixels tall. `IconNameGuardTests` now reads every literal icon name against the sprite and found
  two more that were already wrong on live pages.
- **Letting a request go deletes it**, so the card raising "that's let go" was removed from the
  page along with its own sentence.
- **Calling an event off told nobody.** The organizer's screen has answered "everybody with a
  place has been told" since phase 3 and no letter existed; the note now counts what was actually
  sent.

Two things the plan asked for that are deliberately not there:

- **`SeatMapHasCompactFallbackTests`** is not written. The rule it was to enforce — "a grid must
  come with a compact list" — is not what the picker does: the grid takes a ceiling and scrolls
  inside it with the summary fixed to the window, which was photographed at 375 and works. A guard
  enforcing a rule the code does not follow is a guard somebody deletes. **The three-width
  Playwright fixture is the guarantee**, and every hosted screen now has one.
- **"Ask the venue to move you"** on a confirmed booking. *I can't make it* asks them to release
  it, which is the same conversation with a blunter name; a second button asking to be moved
  somewhere unspecified is a message, and messages are phase 11.

Everything else shipped as written: the anonymous plan, the picker with its refusal ringing the
square that went, the ask form, the booking card's six states, `/my-events`, the pass with its
short code, the bell retargeted, the ask/hold/called-off/going-ahead letters, the token page's
hosted branch, the thirty-second poll (which re-reads a hold only once its clock has run out),
18 new Playwright tests at three widths, five render tests, and the new `going-to-an-event` help
page with five shots.

### Phase 7 — Staff and the door

**Goal**: the person on the door is not a person with billing rights, and admits a party from any
device in under ten seconds with or without a camera.

- Migration `HostedEventStaffAndCheckIn`: `HostedEventStaff` (member or invited email, role
  label, flags See bookings / Decide / Run the door / Menus and dietary / Files), `HostedEventStaffInvite`
  (the invite-token pattern), `HostedEventCheckIn` **per night** (booking, night, arrived, left,
  recorded by, method), `HostedEventChecklist(+Item)`. `HostedEventAccess` learns staff flags;
  `SubscriptionLimit.EventStaff`.
- `Manage/Events/OrgEventStaff.razor` (cards on compact); public staff-invite confirm page.
- `Manage/Events/OrgEventDoor.razor` (`…/door?night=`), phone-first: night defaulting to today in
  the venue zone; `Kit/Scan/BenQrScanner.razor(.js)` — `getUserMedia` into a `<video>` the
  component renders, `BarcodeDetector` when present, vendored `jsQR` under `wwwroot/plugins/jsqr/`
  with `LICENSE` + `VENDORED.md` otherwise, JS returns strings only; a manual pass-code box; name
  search over a preloaded expected list; Expected / Arrived / Left with 56 px buttons; the scan
  card in the server's words with "party of 4 — 2 named" and adjustable heads, dietary **flags**
  for tonight; *Staff here now*; **print tonight's list** (Blazor Server cannot be offline; the
  help says real offline scanning is the phone app's). The page tells a LAN user why the camera
  is unavailable over HTTP and offers the manual path.
- Endpoints: staff CRUD/invite/confirm; `GET door?night=`, `POST door/arrive|leave|undo`,
  `door/scan` writes a per-night check-in, `POST door/lookup {code}`, `POST door/staff-here`.
- Tests: `HostedEventAccessTests` extended with staff rows; `HostedEventCheckInTests` (second scan
  says when they first arrived; Saturday scan of a Friday-only booking says so); Playwright
  `EventStaffTests`, `EventDoorTests` at 375 with the manual box; `VendoredPluginsAreDocumentedTests`.
- **Bands** (Ben, 2026-09-13): *"the organizer gets coloured wrist bands which mean different
  things — blue could be the full event with food, purple the full event, red is day one … lets
  their employees know by glance what a person is registered for … or lanyard colour or shirt
  colour or colour displayed on the QR code reader next to the name."* Built here because the door
  is where it earns its keep, and building the configuration before the screen that reads it would
  be a ninth write-only feature.
  - `HostedEventBand` per event: name, colour, and a **rule** — every night / one night only / day
    pass / by hand. Derived where it can be, because a venue that has to tag two hundred parties by
    hand will tag none of them. "With food" is NOT derivable today: nothing on a booking says a
    party is eating, so that is a by-hand band until dining lands in phase 13.
  - It appears in three places and is computed in one: the door's scan card (a coloured chip beside
    the name, with the band's **name** as well as its colour — a chip that is only a colour is
    useless to the one steward in twelve who cannot separate red from green), the booking board's
    Everybody list, and the guest's own pass so they know what to collect at the desk.
  - The colour is the venue's, and the site never invents one: a venue with orange wristbands in a
    drawer needs the screen to say orange.
- **Verified by**: a non-member helper on a phone admits a party by camera; the owner's board shows
  them arrived; the camera is covered and the same party is found by name; a party registered for
  the whole run reads as the band the venue named for that, on the door and on their own pass.

#### Phase 7 as built (2026-09-13) — what changed, and what Ben added mid-phase

**Ben added two things while it was being built, and both went in:**

1. **Walk-ups and the room-left count** (*"availability count for walk ups to event and on-sight
   sign ups"*). The door says how many more people could come in tonight — everybody expected plus
   everybody already through, against whichever ceiling the event has, and honestly null where it
   has none. `HostedEventWalkUp` records somebody who simply turns up: how many, and a name if they
   gave one. **No account is invented for them** — the site's settled rule for signing up a stranger
   is to send a link rather than to make an account from an address nobody verified, and a link is
   no use in a doorway. A walk-up past the last place is refused in words.
2. **Bands** (decision 16). Built as specified above, with the by-hand picker on the booking
   board's sheet — the bands page promises it, and a promise with no path is a write-only feature.

**Departures from the plan:**

1. **One staff table, not two.** `HostedEventStaffInvite` was dropped: an unaccepted helper is the
   same row with no account attached, which is exactly what makes it grant nothing. Two tables
   carrying the same five flags would be two places for them to disagree.
2. **An address is always an invitation**, even when it belongs to an existing account. The first
   version resolved the address and added them at once — a venue granting a stranger other people's
   names and allergies without asking. Access also reads the ACCEPTANCE, not the attachment, and a
   test pins that for a row the endpoints do not produce today.
3. **`HostedEventChecklist` is not built.** It had no screen in this phase, and a table with no
   screen is how this codebase collected eight write-only features. It waits for the screen that
   reads it.
4. **"Staff here now" is not built** for the same reason — nothing yet asks who is on shift.
5. **`VendoredPluginsAreDocumentedTests` checks the substance, not a format.** The shell guard
   only ASKED for a licence and a record in its prose; nothing checked. The new guard wants a
   `LICENSE*` file and a `VENDORED.md` naming a version and an address — its first draft demanded
   exact labels and wrongly accused ApexCharts, which has always had both. Bootstrap and Waves came
   inside the purchased theme and are listed as exceptions with that reason. jsQR ships with its
   licence and a record carrying the SHA-256, fetched once, only where `BarcodeDetector` is missing.
6. **The booking → band link is NoAction, not SetNull.** SQL Server refuses SetNull ("multiple
   cascade paths") because bands and bookings both cascade from the event; the endpoint clears the
   column before it deletes a band. The failed migration rolled back cleanly inside its transaction.

**Two findings worth keeping:**

- **The scan used the DECIDING permission**, so a steward with a phone had to be trusted with the
  whole board. It asks the door's own permission now, which is the coupling the staff table exists
  to break.
- **Per-night arrival (defect 19)** is `HostedEventCheckIn`, unique on (booking, night). The old
  single stamp on the pass stays: rewriting it into per-night rows would invent nights nobody came
  to.

Shipped: three migrations (staff, check-ins, walk-ups) plus the bands one, every one additive; the
staff page and its public acceptance page; the door with the camera (`BenQrScanner`, platform
decoder first, vendored jsQR on demand), code box, name search, walk-ups, room-left count, dietary
flags for tonight and a print layout; bands with their page, chip and by-hand picker; 36 new unit
tests and 22 new Playwright tests; help for staff, the door and bands with four shots.

**Verified by, as walked:** a steward added by email grants nothing until accepted and the list says
so; the door admits a confirmed party by name at 375 with a 56-pixel button, takes it back, records
a walk-up and refuses one past the last place; a band set on the page is the chip beside the name
at the door. **Not walked:** a real phone camera reading a real pass — the harness has no camera, and
it is the one thing in this phase that needs a person holding a phone.

### Phase 8 — Alerts and digests (item 238)

**Goal**: a request is answered because somebody was told.

- `EventBookingAlertPreference`; immediate alert on request/hold arrival to everyone
  `HostedEventAccess` says may decide ("A party of 4 has asked for the Blue Room, Fri–Sat — hold
  lapses Thursday 3 PM", linking to the board, never carrying contact details; one immediate
  letter, then a 15-minute rolling window collapsing the rest into "and 12 more");
  `EventBookingDigestJob` (oldest undecided first, holds lapsing next, what is left per section,
  arrivals during Live); a thread per event in the org's own messages once `OrgMessage.HostedEventId`
  exists (phase 11) — bell and email before that; per-person opt-out on the profile's
  notifications page; bell buckets `EventHoldsLapsing` and `MyEventHoldLapsing`.
- Tests: `EventAlertBatchingTests` (forty requests in an hour → one letter plus one summary),
  digest skips a quiet event, opt-out honoured; `NotificationBellCoversEveryBucketTests`.
- **Verified by**: mail diagnostics show one alert after two quick requests.

#### Phase 8 as built (2026-09-13) — what changed from the plan above

1. **Nothing is written when a booking arrives.** The alert is a five-minute job that finds new
   bookings by when they were made and keeps a cursor per person per event
   (`EventBookingAlertState`: `LastAlertUtc`, `AlertsCoverUpToUtc`, `LastDigestUtc`). So there is no
   hook in the request door or the hold door to forget, a letter that fails to send leaves the
   cursor where it was and is tried on the next pass, and an outage costs a late letter rather than
   a lost one. The digest marker lives on the same row, not on `HostedEvent.LastDigestSentUtc` as
   the model table said: the digest is per recipient, because each person's own bookings are
   filtered out and each person may be due at a different moment.
2. **The rule is pure and tested without a clock** (`EventBookingAlerts.Decide`): the first new
   booking after 15 quiet minutes is sent on the next pass; anything inside a rush waits until 15
   minutes pass with nothing new **or an hour has passed since the oldest of it**, whichever comes
   first. The ceiling was not in the plan and is the difference between a summary and a
   request nobody hears about during a steady trickle. Forty in an hour → the first letter, then one
   summary covering the other thirty-nine. Nothing older than an hour before a person's first pass
   is sent to them, so turning the feature on does not post a back-catalogue.
3. **Three modes, not a cadence setting**: *As they arrive* (default, with the digest too), *A daily
   letter*, *Nothing*. The digest's cadence is not a choice — daily while `IsOpenForRequests`,
   weekly otherwise — because nobody needed to pick it and a setting nobody needs is a setting
   somebody gets wrong. It is per person **per group**, on `/notifications`, and lists only groups
   the person decides for, including a group they are only helping at (with "You're helping at
   Phantom Nights" so they know why).
4. **Recipients are asked the way the board asks**: active members plus accepted staff with
   *Decides*, each confirmed by `HostedEventAccess.CanDecideBookingsAsync(..., eventId, db)`. A
   steward handed only the door is never written to. Their own booking is never news to them.
5. **The bell** found three defects rather than two buckets: deciders were owners and administrators
   only (now role grants and staff *Decides* too); Held was never counted as waiting on the venue;
   and a guest's own hold was announced to them as "A venue answered you". A hold lapsing within a
   day moves from the queue's count to `EventHoldsLapsing`, so a booking is one number on the bell.
   `MyEventHoldLapsing` is the guest's row.
6. **The staff-room thread** waits for phase 11 as planned; the letters and the bell carry it until
   then.
7. **Verified by, honestly**: the harness has no outgoing mail, so "one alert after two quick
   requests" is proved against a real SQLite database and the real job with a recording mailer
   (`EventBookingAlertJobTests.Two_quick_requests_are_one_letter_to_the_person_who_decides`), not by
   reading mail diagnostics on the running site. Discrimination was proved for the rush window, the
   hour ceiling, the retry-after-failure cursor, the daily cadence, staff *Decides* in the bell,
   Held excluded from the guest's answered row, and the settings endpoint's refusal. The settings
   card is walked by Playwright at 1280/768/375 (`EventBookingLettersTests`).
8. **Both purges** name the new tables: the group purge takes the preferences, the person purge
   sweeps both (settings about a person are of use to nobody once the person is gone). Migration
   `EventBookingAlerts`, two `CreateTable`s, applied to `IsHauntedDb_player`.

### Phase 9 — Venue profiles and grants (the third arrangement)

**Goal**: publishing at another group's venue is gated on that venue saying yes, and the venue can
withdraw with a reason.

- Migration `VenueProfilesAndGrants` (as the README's phase 8) plus `HostedEvents.VenueGrantId`
  SetNull. The publish gate detects a place that is the subject of another org's published venue
  profile and requires a grant covering every night. Revoke → `VenueWithdrawn`, organizer and
  confirmed guests told, passes revoked, **credit restored regardless of timing** (decision 8).
- Screens: `Organization/Venue/OrgVenueProfile.razor`, `OrgVenueRequests.razor` (approve with
  switches offer rooms / photos / history / our staff may help, decline, revoke), a read-only
  board view for the venue when granted, public `PublicVenuePage.razor`; the organizer's *Ask the
  venue* option becomes live and shows grant state in the readiness list. Bell
  `VenueRequestsToDecide`.
- **Claiming a venue that is already on the site** (Ben, 2026-09-13: *"If someone has created an
  event at a venue and the venue doesn't have an account, how and can they claim the account and
  prove they are the right owners later?"*). Most venues arrive as a place typed in by the
  organizer running an event there, months before the venue itself hears of us. This is the flow
  that turns that row into theirs.
  - **A venue claims the PLACE, not the event.** The event belongs to the group that ran it, and a
    claim never touches it: it changes who must say yes to FUTURE events at that address, and it
    gives the venue a profile page. Nothing already booked moves, nothing is cancelled, and no past
    guest list changes hands. A claim that could seize an organizer's weekend would be a way to
    steal one.
  - **Proof, cheapest sufficient first.** (1) A code to the contact already ON the place record —
    the phone or address a stranger can already see, which is exactly the channel the world
    associates with that venue. (2) An email at the venue's own domain, when the place has a
    website: controlling `@thomashousehotel.com` is the ordinary proof of being the Thomas House.
    (3) Failing both, an **adjudicated claim**: the claimant submits what they have — a licence,
    a listing, a utility bill — and a SuperAdmin decides, through the same queue shape the place
    duplicate merge and the request review already use.
  - **The group that created the place is told and may object**, and a contested claim goes to a
    human. Never an automatic transfer: the commonest false claim is a competitor, and the second
    commonest is a former manager.
  - **Where it starts**: "Is this your venue?" on the public place page and on the event's venue
    card, so the two places somebody notices it are the two places they are looking at it.
  - **What a claim is not**: it does not make the venue an organizer, does not give them the
    booking board of somebody else's event (that is a grant, and the venue asks for it), and does
    not let them see who came to events they had no part in.
- Tests: `VenueGrantTests`; `VenuePlaceClaimTests` (a claim never moves an event, an objection
  suspends it, an adjudicated claim needs a human); Playwright `VenueHostingTests` in two orgs.
- **Verified by**: Nashville Paranormal cannot publish at Thomas House until Thomas House approves
  Oct 30–Nov 1; a revoke turns the badge to *Venue withdrew* in both browsers and the credit comes
  back; the Thomas House claims its own place, is refused until it proves the address, and the
  weekend already booked there is untouched throughout.

#### Phase 9 as built (2026-09-13) — what changed, and what Ben added mid-phase

**Ben added contact details while it was being built** (*"Maybe we track venues and contact
information public / private?"*, *"Links to website. Phone public and private maybe for
scheduling..."*, *"This will be completed by validation or verification of who is in charge of venue"*,
*"Or a rep of the venue"*, *"Code validation is okay as long as we verify we have the right people"*).
`PlaceContact` (Website / Phone / Email, public or private to the recording group) went in, and it is
what made the plan's first proof possible: the exploration found that `Place` has **no phone, email or
website at all**, so "a code to the contact already on the place record" had nothing to send to.

**Departures from the plan:**

1. **Only a verified venue gates anything.** The plan said the publish gate detects "another org's
   published venue profile". That would let a competitor write a profile for the Thomas House and stop
   every organizer there with one form. So a profile gates nothing and cannot be published until the
   group is confirmed (`VerifiedUtc`), and one place has at most one confirmed venue (filtered unique
   index). The claim flow is therefore not a separate afterthought but the only way a venue gains
   power.
2. **At a confirmed venue, its yes is the only answer**, whatever the organizer picked in the
   arrangement box. The venue card replaces the box there.
3. **A request is always about one event**, from that event's page, with its nights copied at the
   moment of asking. The public venue page has no "ask to host here" form: a yes to something
   unspecified is the yes that gets disputed. A night added after the yes is not covered, and the
   readiness list says so.
4. **Photos are not lent in this phase.** The grant lends rooms (the plan editor offers the venue's
   rooms), the building's story (on the public event page) and the venue's own people (read the board
   and run the door through `HostedEventAccess`, never decide). Photos belong with the flashy page in
   phase 11, where the event page's media lives.
5. **Proof, as built.** A code (six digits, hashed, 24 h, five tries) goes only to a **public email
   address recorded by somebody outside the claiming group at least seven days before the claim** —
   so the claimant cannot supply it, nor have a friend's group type it in that afternoon. A proved
   claim then stands for **seven days** while every group that knows the place (events, rooms,
   contacts, investigations, cases there) is messaged and may object; `VenueClaimJob` confirms it if
   nobody does. No proving address → the claimant sends evidence and a SuperAdmin reviews at
   `/admin/venue-claims`. An objection always goes to a person. Phone codes are not offered: the site
   cannot send texts, and phone numbers are for scheduling and for the reviewer to call.
6. **The claimant's role is recorded** (Owner / Manager / Representative), per Ben's "or a rep of the
   venue".
7. **Once confirmed, the venue keeps the public contact details.** Others' public details show as
   "Added by …" until the venue presses *That's right* or removes them; other groups add private notes
   only, and the card says why.
8. **Undoing a confirmation** (SuperAdmin) takes the page down and stops the gate but leaves grants
   already given standing — they were given in good faith by whoever answered for the building then.
9. **Verified by, honestly.** The ask, the yes and the withdrawal — with the venue's reason arriving on
   the organizer's card — were walked in Playwright across two groups (`VenueHostingTests`, a fresh
   group purged afterwards on SQL Server); that the event cannot publish until the yes, and that the
   credit comes back on a withdrawal, are proved against real rows in `VenueGrantTests` rather than by
   pressing Publish, which would spend a credit in the shared e2e database; and the adjudicated claim through to "Run as a venue by" (`VenueClaimTests`). The code
   path and the objection week cannot be walked without mail and a clock, so they are proved on SQLite
   with the real controllers and job (`VenuePlaceClaimTests`), each rule broken once to watch its test
   fail. "The weekend already booked there is untouched throughout" is both a behaviour test and a
   source guard over every file that handles a claim.

### Phase 10 — Sessions and classes ("the ghost hunt but not the dinner")

As the README's phase 4, unchanged in substance: `HostedEventSession*`, first-come sign-up with a
waiting list for a confirmed booking or staff, programme per night in the venue zone, `.ics` per
session, promotion mail; the *Programme* index link goes live. Compact: the programme is a
per-night timeline, the most-looked-at thing during an event.

#### Phase 10 as built (2026-09-13) — departures from the README's phase 4

1. **No leaders table.** `LedBy` is text ("Dr Carol Hughes and Ben"): the programme prints a name,
   and nobody asked for a leader to have an account, a login or a roster of their own. A helper who
   leads a session is on the event's staff list already.
2. **Places taken is a stored counter and the concurrency token**, not a count of rows inside a
   serializable transaction. Two saves racing for the last place cannot both succeed; the loser is
   retried on a fresh read and queued. Pinned by a two-context test that the token is what refuses
   the second save (proved by removing it).
3. **A queue that does not skip.** Promotion is in order, and a party at the head waits for enough
   places for all of them rather than letting one person behind it take a single free place. The plan
   did not say either way; skipping would be read by the party as the site losing their place.
4. **Sign-up needs a Confirmed booking** (up to party size) or accepted staff. A Requested or Held
   booking is told "signing up opens once the venue confirms your place". The plan's `EventSessions`
   subscription limit is not enforced — the credit is the product for an event, and a second meter
   on the same event would be two prices for one weekend.
5. **One bell bucket, not two.** `EventScheduleChanges` (a confirmed guest's programme changed since
   `ProgrammeSeenUtc`, cleared by opening the event page). Promotion from the queue is a letter, not
   a bucket: it is good news that needs no action, and the page shows "You're in" the next time.
6. **Calendar files** go per session through a website relay
   (`/calendar/hosted-events/{event}/sessions/{session}.ics`) so the link works wherever the page does.
   **Found:** `IcsBuilder` calls `ToUniversalTime()`, which shifts a database-read time (kind
   Unspecified) by the SERVER's own offset; the session rows mark their times UTC explicitly. Other
   callers pass times they computed as UTC and are unaffected, but the builder would be safer taking
   `DateTimeKind.Unspecified` as UTC.
7. **The session's cancellation stamp is `CalledOffUtc`**, not `CancelledAtUtc`: the lifecycle guard
   forbids reading an event's `CancelledAtUtc` as a state, and a session's own stamp sharing the name
   would have tripped it for a legitimate reason.
8. **MyEvents does not list sessions yet**; the event page is where a guest's places show. Recorded
   for phase 12's after-event pass.
9. **Verified by.** `HostedEventSessionTests` on SQLite (capacity 2 → third queued at position 1;
   leaving promotes and writes; the party is not jumped; unconfirmed refused; outside the nights
   refused; the counter refuses a racing save; moving writes and rings the bell until seen;
   cancelling writes to the queue too; a draft programme is invisible; the calendar file is 9 PM at
   the venue), three rules broken once each. Playwright `HostedEventProgrammeTests`: the seeded guest
   signs up and the count reads "1 of 15", the host adds a session that lands at 7:30 PM on its night
   and removes it, and the programme fits at 1280/768/375.

### Phase 11 — Files, the flashy page and ads, the room

As the README's phases 6, 7 and 9 with two changes: the CMS `EventBooking` section embeds
`BenPlan Mode="Read"` with the public occupancy projection and a night picker ("Balcony nearly
full" without names) and a sticky *Ask for a place* on phone; the room's moderation lives on the
board's *Room* tab. `OrgMessage.HostedEventId` lands here and phase 8's staff-room delivery
switches on.

#### Phase 11 as built (2026-09-13) — what changed, and what Ben added mid-phase

Three slices: 11a files, 11b the room and the photo wall, 11c the gallery, the dressed page, event
sections on a group's pages and event ads.

1. **Files have an audience, not a permission** (11a). Each file is for the team, for confirmed guests
   or for the public, in folders; the guest's *Downloads* page lists only what reaches them. Files go
   through the media ingest like every other upload; SVG is refused.
2. **Uploads during an event belong to the uploader** (Ben: *"Uploads during an event belong to the
   uploader but can be sent to and shared with event organizer and venue"*). A room photo lives in the
   poster's library; *Also send to the organizers and venue* shares that file with the hosting group
   rather than copying it. Deleting their own message is always the poster's.
3. **The room, not a board tab** (11b). The plan put moderation on the board's *Room* tab; it lives in
   the room itself (`/events/{id}/room`), where a moderator sees what they are hiding. Hide, unhide,
   close and reopen are the team's; reports also reach the site's own queue.
4. **Who may post photos** (Ben): the organizer picks *team only* or *team and guests* per event; the
   default, and every event that existed before, is team and guests.
5. **The photo wall is behind an account** (Ben: *"behind the venue or organizer's login account so
   outsiders cannot see the photos being taken"*). A full-screen slideshow at `/events/{id}/wall` for the
   event's team and the venue's members holding a standing grant — never for guests or visitors, even
   though guests may post to it.
6. **Consent, once** (Ben): the first time a guest adds a photo they agree, in words, that it may be
   shown in the room and on the wall; the agreement is recorded per person per event and asked again at
   the next event.
7. **Guests' photos never reach the public gallery.** The gallery (11c) holds only pictures the hosts add
   themselves, up to fifty, resized and stripped of location; the people in a guest's photo agreed to
   nothing public. The venue's photo library is phase 12's, not lent here.
8. **Storage per event** (Ben: *"a max storage size for an event like 2,000 mb"*): the site setting
   `events.storage-megabytes`, default 2,000, shared by files, gallery and room media, refused in words
   before the upload is kept.
9. **The dressed page** is the event's own page, not a CMS section: gallery slideshow, a countdown to
   the first night, and on a phone a sticky *Ask for a place* that jumps to `#event-act`.
10. **Event sections on a group's pages, one shape.** Four CMS sections — booking, programme, pictures,
    venue — resolved at read time from one server projection; a section pointing at an event that is
    not the group's own or not on the public site says so. The booking section links to the event's
    picker rather than embedding `BenPlan` read-only: one live plan, not two to keep in step.
11. **Event ads.** An ad may lead to one of the group's events; the card carries the event's name and
    date, the click goes to the event page, and it drops out of rotation once the event ends.
12. **Not done here:** phase 8's alerts are still bell and email; posting them into a staff thread on
    `OrgMessage.HostedEventId` waits until somebody asks for it, since the room is for the event, not
    for the booking queue. The iPhone share-to-event is phase 14's.

Tests: `HostedEventFileTests`, `EventRoomTests` (unit and Playwright), `EventAdTests`,
`CmsEventSectionTests`, Playwright `EventFilesTests`, `EventGalleryTests`; captures
`event-gallery.png`, `event-page-phone.png` and the room and wall shots.

#### Slice 11d as built (2026-09-13) — guests without an account

Ben's decision is in *Still Ben's* below. Built as proposed, with these shapes:

1. **A pick is not a booking.** `HostedEventEmailPick` (+ places) lives beside the bookings for fifteen
   minutes. Every booking still has a lead account; no account is made until the emailed link's button
   is pressed, so a mistyped or invented address leaves nothing behind and never puts a hold on the
   account of whoever really owns the address. The places read as pending on every plan
   (`PlanOccupancy` gained a fourth query) and a second pick of the same square is refused by its own
   filtered unique index.
2. **The click becomes an ordinary hold** through `BookingTransitions`, with the event's own deadline
   starting at the click and the same letters. A square the venue or a signed-in guest took in the
   meantime is named ("A1 was taken a moment ago"), and the rest go back.
3. **Two arbiters, one gap, accepted.** The bookings' index cannot see picks, so the signed-in hold door
   checks for live picks first (after retiring lapsed ones). A race between the two doors in the same
   instant is settled at the click, in words.
4. **Anti-swamp, in the order it bites:** six picks per address per ten minutes (a new rate-limit
   policy); one live pick per email per event (index; picking again replaces the first); three live
   picks per email across the site; unproven picks may hold at most a quarter of a night's places,
   never fewer than eight.
5. **Name, email and phone on every web booking.** Signed-in guests are asked only for what the account
   lacks (a new `GET my-contact` fills the form, folded to one line when complete). The phone is stored
   on the booking (`ContactPhone`) and shown on the organizer's decision sheet; it is never written to the
   profile. Empty first/last names on the profile are filled. The disclosure sentence is shown on every
   form. The hosted email door (day passes) asks for the three too; the shipped phone app's RSVP and a
   host's booking-for-somebody do not.
6. **The link manages the booking without a password** for a month: see whether the venue answered, let
   the places go. After confirmation, letting go needs signing in. A button, never the page load, holds
   the places, so mail scanners hold nothing.
7. **Privacy:** a pick nobody confirmed is deleted a day later with the name and phone in it; a confirmed
   pick's row goes after a month. Only the token's SHA-256 is stored. With no mail server the link is
   logged, as the public sign-up link already was; the browser tests read it via `BEN_E2E_API_LOG`.
8. Account creation for proven addresses moved into `EmailLinkAccounts`, shared by both email doors.

Tests: `HostedEventEmailPickTests` (25, the index, the pending check, the share ceiling and the lapse each
seen failing with its rule removed), Playwright `HostedEventEmailPickTests` (3), the stranger test in
`HostedEventGuestTests` rewritten; captures `choosing-without-an-account.png`,
`hold-your-places-link.png`.

### Phase 12 — After the event

**Added 2026-09-13 (Ben), for after the event:**

- **Retention.** *"a data retention policy. I am not sure how long after an event we should retain
  links to collected files like images or media."* Proposed, Ben to confirm the length: a site setting
  defaulting to **90 days after the last night**, letters to the organizer 30 and 7 days before; then
  the event's own files and gallery go, and the room's links to guests' photos go — the guests' files
  themselves stay in their libraries, because they are theirs.
- **Pick and zip.** *"allow the organizer the ability to pick from a list to download and just zip up
  whatever they pick into a .zip file."* A list of the event's files, gallery pictures and photos guests
  sent to the organizers; tick, download one zip.
- **Save for next time.** *"ask if they want to save the ads, menu whatever they have created for the
  event — so they can use it again."* Folded into *Copy this event*: the ad, menus, programme, plan and
  files offered as the starting point of the next one, and kept on the group even when the event is
  archived.
- **What the venue keeps.** *"remember the venue from now on and use the room or seating as a starting
  point next time someone reserves the venue. Images of the venue should also be saved, either ones taken
  by the venue owner / employee, or ones shared with the venue by an organizer."* The last plan used at a
  place becomes the default plan for the next event there (the venue's own when it has one, otherwise the
  last organizer's); and the venue profile gains a photo library holding its staff's pictures and any an
  organizer chooses to share with the venue.

**Also added 2026-09-13 (Ben):** "If we generate a gallery, we can send that link in a thank you link
including any upcoming events hosted by the organizer." The once-only thank-you letter carries the
event's public gallery link (phase 11c) and the organizer's upcoming published events, so the letter
that closes one weekend opens the next.

`HostedEventLifecycleJob`'s archive rule with decision 11; **Copy this event** (`POST
{id}/copy {Name, StartsOn}` → a Draft with nights shifted, units without bookings, menus remapped by
night index, staff copied, arrangement reset); `GET bookings/export.csv`; reviews via `TourReview`
on the umbrella; a once-only thank-you letter with the gallery link.

#### Phase 12a as built (2026-09-13) — copy this event, and the spreadsheet

1. **Copy this event** is a page (`/organizations/{org}/events/{id}/copy`), not a button: every part the
   organizer made is offered with its size and ticked — plan with blocks, menus by night position,
   programme (unpublished, nobody signed up, called-off sessions left out), bands, helpers who accepted
   (unanswered invitations stay behind), the advert as a Draft with its counters at zero, and files
   (unticked by default: copied through the ingest into the new event's own storage, since a file row
   belongs to one event and retention will later tidy the old one's away).
2. **Dates move by whole days on the venue's clock**, so a 7 PM séance stays at 7 PM across a clock
   change; the bookings deadline and the go/no-go date move the same way. Go/no-go is reset.
3. **The venue's yes does not come across:** External keeps the contact and loses the date and reference;
   PlatformGrant loses the grant. The page and the readiness list say so.
4. Names are unique per group, so the page suggests the old name with the new year; the server refuses a
   clash in the same sentence the create form uses. Copying needs the event-edit permission, not billing:
   it spends nothing.
5. **Download as a spreadsheet** on the board: every booking with the guest-given phone, nights and places,
   guests and dietary notes, and the nights they arrived. Formula-looking cells are written as text.
6. Still to come in phase 12: the thank-you letter with the gallery link and upcoming events, reviews,
   retention with its warnings, pick-and-zip, what the venue remembers, and sessions on *My events*.

Tests: `HostedEventCopyTests` (11; the clock-change shift and the formula escape seen failing with the
rule removed), Playwright `EventCopyTests` (copy at 1280 and 375, the spreadsheet download); capture
`event-copy.png`.

#### Phase 12b as built (2026-09-13) — the thank-you, reviews, sessions on My events

1. **Reviews are their own table** (`HostedEventReview`), not `TourReview` with an optional tour: every tour
   query would otherwise guard against a review with no tour. Same record shapes as tour reviews, so pages
   read both alike. Eligible: the lead or a guest named with an account on a **confirmed** booking, once the
   event is Ended or Archived, for 60 days after the last date; the organizer can turn reviews off. Hide,
   never edit; editing clears a hiding; hidden reviews leave the average and stay visible to their author.
2. **Where:** the event page while it is up, and `/my-events/{id}/review` (the thank-you's link, which keeps
   working after the event is archived). Before an event, its page shows what guests made of the group's
   *earlier* events when there are any.
3. **The thank-you** (`HostedEventThankYouJob`): 12 hours after the last night ends on the venue's clock,
   once per confirmed party (`ThankedUtc`), the event stamped when all are done. Carries the organizer's
   note, the gallery link only if there are pictures, the review link when reviews are on, and up to three
   upcoming published events. On by default; never sent for an event that ended more than a week before, so
   shipping it does not write to guests of old events. Nothing is stamped while mail is off.
4. **After the event** page for the organizer: the thank-you switch and note (shows who it went to once
   sent), the reviews switch, every review with hide/show, and links to copy the event and download the
   spreadsheet.
5. The two switches default to true in the database for existing events, written into the migration by
   hand: an EF `HasDefaultValue(true)` on a bool would silently store true when an organizer saves false.
6. **My events** lists the sessions a guest signed up for under each event, with waiting-list and
   called-off marks, and a *Say how it was* button when a review is open.

Tests: `HostedEventAfterTests` (8; the confirmed-only rule, the hidden-review average, once-per-party and
the one-week limit each seen failing with the rule removed), Playwright `EventAfterTests` (3); capture
`event-after.png`. Still in phase 12: retention with warnings, pick-and-zip, what the venue remembers.

#### Phase 12c as built (2026-09-13) — retention, pick and zip

1. **90 days** (Ben: *"90 days is fine"*), as the site setting `events.retention-days`. Applies to Ended,
   Archived, Cancelled and VenueWithdrawn events, never drafts or live ones.
2. **What goes:** the event's files and gallery pictures (rows, then the upload rows where nothing else points
   at them, then the bytes) and the room's links to photos. **What stays:** the event, bookings, programme,
   reviews; guests' own photos in their libraries; shares a guest made to the organizers.
3. **Warnings** a month and a week before, as a platform message and email to the billing recipients, with the
   date and a link to *Keep the files*. Nothing is removed within a week of the last warning, so an event the
   job first sees late is warned, not cleared. An event with nothing to lose is stamped without a letter.
4. **Pick and zip** (`/organizations/{org}/events/{id}/keep`): the event's files, gallery, photos posted in the
   room by the organizer's own members, and photos guests sent to the organizers — not a guest's photo that
   was never sent. Up to 100 per zip, in folders. The zip is written to a temporary file on the API and
   streamed through a ticketed website relay, so a large download never passes through the Blazor circuit.
5. Ben's review of the *After the event* page (a screenshot marked "wording makes no sense"): the thank-you
   switch now reads *Email a thank-you to everyone who attended*, with a plain line saying when it is sent and
   what it contains; "parties" became "confirmed bookings" and "guests who came" became "attendees".

Tests: `HostedEventRetentionTests` (5; never clearing on a late first warning and leaving unshared guest photos
off the list each seen failing with the rule removed), Playwright `EventKeepTests` (a picked file comes back
inside the downloaded zip); capture `event-keep.png`, `event-after.png` re-shot. Still in phase 12: what the
venue remembers (last plan, photo library).

#### Phase 12d as built (2026-09-13) — what the venue remembers (phase 12 complete)

1. **The last plan is remembered by reading it, not by storing a copy.** An empty plan page offers up to five
   earlier plans at the same place: the verified venue's own first, then this group's, then others' published or
   archived events; among those, plans from events that have happened before ones only drafted for later.
   *Start from this plan* loads the units into the designer unsaved and undoable. Another group's prices and
   notes never come across, rooms this event may not offer are left out and counted, and a plan that is not on
   the offered list cannot be fetched by id.
2. **The venue's photo library** (`VenuePhoto`): the venue's own uploads are kept at once; an organizer offers a
   gallery picture with *Offer to venue* (the same file, referenced), which waits for *Keep it* / *No thanks*.
   When the organizer is itself the venue the offer is kept at once. Kept pictures show on the published,
   verified venue page through a new anonymous `venue-photo` endpoint. Up to 60 per venue.
3. **Files survive for as long as something uses them.** A kept picture outlives the event's 90-day tidy-up,
   because the upload row cannot be deleted while the library points at it; declining never removes the
   organizer's gallery picture. Picture fitting (1920×1080, re-encoded, no metadata) moved into
   `PictureFitting`, shared by the gallery and the library.
4. Not built: adding a venue picture back into a new event's gallery — a gallery picture is one file per event
   (unique index), so that would need a copy; left until somebody asks.

Tests: `HostedEventEarlierPlanTests` (3; the offered-list rule and the price rule seen failing with the rule
removed), `VenuePhotoTests` (5; the accepted-only public rule seen failing), Playwright `VenueMemoryTests` and
`VenuePhotoLibraryTests`; capture `venue-photos.png`.

### Phase 13 — Dining as a seating assignment

`HostedEventDiningTable` + `HostedEventDiningSeat` per sitting, over Confirmed bookings only;
`Manage/Events/OrgEventDining.razor` (`…/dining?sitting=`): a tray of confirmed parties with size
and dietary badges, the same `BenPlan` canvas drawing tables (round ≤ 8 seats) with "6/8" and
aggregated dietary badges, click-party-then-table or drag on a pointer, *Same as last sitting*,
over-seating refused in words; "Table 4" on the guest's pass page; a per-table column on the
kitchen print. No "who sits with whom" input in v1 — the booking note is the interim.

#### Phase 13 as built (2026-09-13) — dining tables

1. **Tables belong to the event, seating to each sitting** (a sitting is one menu of one night). `HostedEventDiningTable`
   (name, seats ≤ 40) and `HostedEventDiningSeat` (sitting, table, booking, how many of the party). A party can sit at
   several tables; together its rows never exceed the party, and a table never exceeds its chairs — refused in words
   naming the table, the chairs left and the party ("Seat 2 here and the rest at another table").
2. **Tap a party, then a table**, not the plan canvas the plan named: a dining room is a list of a dozen tables, the
   same gesture works with a thumb and a mouse, and what the kitchen needs is a list that prints. *Seat the same way*
   copies another sitting for everybody at both, leaving out and naming anyone who no longer fits. *Print table list*
   prints each table with its parties and their dietary notes (the per-table column the plan gave the kitchen sheet).
3. **Confirmed parties present that night only.** Every read filters on the booking's status, so a cancellation leaves
   nobody's name on a table; a request withdrawn after having been seated removes its seat rows with it.
4. **Who:** the event's editors or a helper handed *menus and dietary* — the kitchen's job, and the reason that helper
   sees dietary notes.
5. **Menus now keep their ids.** Saving the menus used to replace every sitting, which would have wiped the seating on
   every typo; a sitting sent back with its id stays the same row. Removing a meal or a table removes its seating.
6. **Guests** see their table for each sitting on their pass ("Sat 10/31 · Dinner · Table 4", with how many when a party
   is split).
7. One cascade path only (seat → sitting); the seat's links to table and booking are NoAction, handled by the tables
   save, the request withdrawal and the organization purge.

Tests: `HostedEventDiningTests` (6; the chairs rule, the confirmed-only read and the kept menu ids each seen failing with
the rule removed), Playwright `EventDiningTests` at 1280 and 375; capture `event-dining.png`.

### Phase 14 — The phone

**Added 2026-09-13 (Ben): sharing photos to an event from the app.** "If someone is using our iPhone
or iPad app, they should be able to share photos to that link during their event — either while taken
in app or shared from library." Built on phase 11's room: a Share Extension (and an in-app camera
button on the event hub) posting to `POST api/public/hosted-events/{id}/room` with the same rules —
the event's team, or confirmed guests when the organizers allow guests' photos — through the outbox
so a photo taken in a basement with no signal sends when it can. The photo stays the uploader's; the
"also send to the organizers and venue" choice is offered as on the web.


As the README's phase 10 with the guest planner's order: server first (already done in phase 4);
`PublicEventRecords.swift` gains `hostedEventId`/`hostedEventName`/`hostedBookingMode`/
`hostedBookingUrl`; `EventDetailView` swaps the seat panel for a `HostedBookingPanel` (404 →
*Ask for a place* in `SFSafariViewController`); native `EventPassView` renders the QR from the
cached token with `CIFilter.qrCodeGenerator()`, works offline, shows revoked honestly, raises
brightness; `EventsView` rows badge "3 nights"; `MyBookingReminders`; the door with
`DataScannerViewController` (the real offline scanner); then the hub, programme, menu, files,
stream, outbox, Live Activity and widget as written. Picking stays on the web until the web picker
has been lived with. No build bump on the branch.

#### Phase 14a as built (2026-09-13) — the booking on the phone, and the pass that works offline

1. **Server first, one line of it.** `GET api/public/hosted-events/mine` returned a row per booking ever made, so a guest
   who had asked, withdrawn and asked again saw the event three times (860 rows for the fixture guest). It now returns
   one row per event: the live booking (Requested, Held, Confirmed) if there is one, otherwise the newest. The website's
   My events reads the same list and got the same fix. Test `One_weekend_is_one_row_however_many_times_it_was_asked_for`.
2. **Fixtures from the real API** (`Ben.Web.Playwright/Capture/IosFixtureCapture.cs`): the event, `mine` with one row per
   status, a pass, the umbrella event, programme, menus, files and room, with the pass token scrubbed to
   `fixture-pass-token`.
3. **BenKit.** `HostedEventRecords.swift` (statuses append-only, as on the server), `HostedEventsStore` (a 404 on
   my-booking is "none", not a failure), `PublicEventRecord.hostedEventId/hostedEventName` (nullable, additive).
   `PassCache` keeps the last pass per event in Application Support (protected until first unlock). A 403/404 removes the
   copy, because the venue withdrew it or the booking is gone. An unreachable server shows the copy with when it was
   saved. A deliberate sign-out removes every kept pass; a session that merely expires does not.
4. **The app.** `EventDetailView` shows `HostedBookingPanel` for a hosted night. It shows the status in a word, lets a
   request or a hold go, and has a pass link. With no booking, it opens the event's website page in an in-app Safari
   sheet and reads the booking again when that closes.
   - *What I'm going to* sits under Profile, with deep links `my-events` and `my-events/{id}/pass`.
   - `EventPassView` draws the QR with `CIFilter`, the short code, what the pass admits, nights and seating, a withdrawn
     band, and the saved-copy note, and sets the screen to full brightness while open.
   - Picking stays on the web, as planned.
5. **Two faults the walk found, fixed.**
   - The panel read the booking once, before auto sign-in had finished, and a failed read fell through to "Ask for a
     place". It is now keyed on the session, and a failed read says so rather than inviting a second booking.
   - **A cold start with no signal signed the person out**, the exact case the offline pass exists for.
     `SessionStore.restore` treated an unreachable `api/me` as a dead session and wiped the Keychain, and
     `TokenSession.refresh` did the same on a transport error or a 5xx. Now only a refusal ends a session: an unreachable
     server or a 5xx keeps the tokens, restore runs again when the app comes back to the foreground, and a 401 ends the
     session only when the refused request actually carried a token. My events re-reads when the account arrives.
6. **Simulator note:** `scripts/build.sh` builds unsigned, and an unsigned app's Keychain does not survive a relaunch on
   the simulator. Any walk that relaunches must build signed for the device id.

Tests: BenKit `HostedEventsTests` (12: fixtures decode, offline pass, cleared pass, withdrawn pass, 404 as none, forget,
deep links, website URL) and four new `SessionStoreTests`. The restore rule, the refresh rule and the tokenless-401 rule
were each broken in turn and their tests seen failing. BenKit 403 green; .NET unit suite 5,619 green; Playwright HostedEvents 114/0.
Simulator walk as the guest on iPhone 17 Pro Max: booking panel, My events, pass, pass with the API unreachable, then an
online cold start still signed in. Help: *the mobile apps* gains "An event you've booked" with two captures from
`HelpMediaCaptureTests.testCaptureMyEventsAndPass`; *going to an event* points to it.

#### Phase 14b as built (2026-09-13) — the event on the phone: programme, menus, downloads, the room

1. **One screen per event** (`EventHubView`, `my-events/{id}`), reached from *The event* on What I'm going to and
   *Programme and room* on a confirmed booking.
   - It shows the pass, programme, menus, downloads and room, **each only when there is something behind it**.
   - The store turns "not for you" (a 404 programme, the 403 on menus before a place is agreed, a 404 room) into
     `.ok(nil)`, so the row is left out. An unreachable server is still a failure, and the hub says so.
2. **Programme** (`ProgrammeView`).
   - Sessions are grouped by day on the venue's clock, with the zone named.
   - *Sign up* asks how many of the party when more than one may come.
   - A full session shows *Join the waiting list*. A party waiting while places are still free is told why ("There are
     2 places left for 3 of you, so you're on the waiting list — number 1"), because "0 of 2 taken" beside
     "waiting" read as a fault.
   - *Give up* or *Leave* asks first. Calendar button: the whole programme as `.ics`.
   - Opening a changed programme marks it seen, which clears the bell's row.
3. **Menus** (`MenusView`): each meal by night, courses in the order the host typed them, dietary labels. The served
   time is the venue's own and is never converted.
4. **Downloads** (`DownloadsView`): by folder. A tap fetches the file into Caches under its own name, made safe as a
   path component, and opens it in Quick Look, which previews it and offers share.
5. **The room** (`EventRoomView`, `RoomComposerView`) follows the web room's rules, with the server deciding.
   - **Posting.** Write, or add photos and video from the library or the camera. Several go in as one post each,
     with the words on the first. A refusal part-way stops and keeps what hasn't gone, so nothing is lost and
     nothing is sent twice.
   - **Agreement and sharing.** The once-per-event photo notice is shown in the organizers' words, and *Post* waits
     for *I agree*. *Also send to {hosts}* shares the photo with the hosts.
   - **Post actions** (press and hold): on your own post, send it on later or take it down; on anyone else's,
     report it with an optional reason.
   - **Moderation** stays on the website; a moderator is told so.
   - **Media** loads with the signed-in token, through `AuthenticatedImageLoader`'s new endpoint-keyed overload.
   - **Paging** fetches older posts `before` the oldest shown.
6. **Links.** `my-events/{id}` → the hub; `events/{id}/room` → the room; `events/{id}/photos` (the photo wall's
   code) → the room with the composer open. `events/{id}` alone is still a calendar date.
   - **Not claimed as universal links yet.** The App Store build parses `/events/{id}/…` as an event detail with no
     screen. Claiming `/events/*/photos`, `/events/*/room`, `/my-events` and `/my-events/*` in
     `AppleAppSiteAssociation` waits until a build carrying these screens is live, and the help makes no promise
     about scanning the wall until then.
7. **Fixtures:** `IosFixtureCapture` now signs the guest up for a session, adds a file for guests and posts a photo
   before capturing, then undoes all three. An empty files list and an empty room had proved nothing. The captured
   photo came back as `image/jpeg` from a PNG, which the test now pins: the app must not assume the type it sent.

Tests: BenKit `HostedEventHubTests` (15 tests, all against the real captures):
- **Decoding:** programme, menus, files and room.
- **Absent is not failed:** three cases, plus unreachable-is-failure.
- **Writes:** the sign-up body and its refusal; the room post's multipart fields; the agreement refusal in the
  server's words; `before` paging.
- **Downloads and time:** the safe download name; a venue time that is never shifted.
- **Links.**

The 403-as-absent rule and the safe-name rule were each broken and seen failing. BenKit 418 green; .NET help and
changelog tests 78 green. No server code changed.

Simulator walk as the guest, iPhone 17 Pro Max, on the player copy:
- **Setup:** the BenCo rooms event was given a published programme (three sessions, one for six and one for two),
  a supper menu and a guest file, as data on the testing copy.
- **Programme:** signed up for two, then a party of three was waitlisted on the two-place session.
- **Downloads:** the guest pack opened in Quick Look.
- **Room:** two library photos posted after agreeing, one sent to BenCo, one taken down.
- **Link:** the `/events/{id}/photos` address opened the composer on a cold start.
- **Not walked:** the camera (no simulator camera) and reporting somebody else's post (the guest is the only
  poster); both are covered by the store tests.

Help: *the mobile apps* gains "During the event" with four captures from
`HelpMediaCaptureTests.testCaptureDuringTheEvent`, and *going to an event* points to it.

Next: 14c, the door scanner and `GET api/me/hosted-event-duties`.

#### Phase 14c as built (2026-09-13) — the door on the phone, with and without a signal

**How the scanner works came from Ben during the build.** It is for the people working the door, and:
- *"it looks up and clicking the reservation allows the doorman to check them in as having arrived to event"*;
- *"The scanner screen is just the camera capturing the QR code to use."*

The first version checked a party in the moment its code was read, and showed the answer over the camera. It was
rebuilt as three steps:
- **Scan.** `DoorScannerView` is only the camera. It reads one code and closes.
- **Look up.** The door screen looks the pass up without recording anything (`checkIn: false`) and shows its
  reservation under **Scanned pass**. A pass that isn't for this event, was withdrawn, or isn't expected tonight
  is said in words instead.
- **Check in.** Tapping the reservation opens `ReservationCheckInView`: the name, party size, nights with room or
  seat, band, guest names, dietary flags and pass code. **Check in as arrived** records it, for all or some of the
  party. Tapping a party on tonight's list opens the same screen; the list's **Check in** button is the quick path.

1. **Server.**
   - **`GET api/me/hosted-event-duties`** lists the doors this person may run. Candidates are narrowed to:
     - events of the person's groups;
     - events where they accepted a helper's place;
     - events at a venue whose people they are, where the venue lent its staff.

     Each candidate is then decided by `HostedEventAccess.CanRunTheDoorAsync`, the same rule the door's
     endpoints use, so a listed door never refuses. Only published, live or just-ended events within two days of
     the last night are listed.
   - **Arrival time.** Door arrive and scan accept an optional `ArrivedUtc` for an arrival kept without a signal.
     `DoorClock` takes a future time, or one more than a day before the night, as now. When a kept arrival is
     earlier than one recorded since, the earlier one wins, on both the night's arrival and the pass stamp.
2. **BenKit `DoorStore`.**
   - **Kept on the phone** (Application Support, protected until first unlock): the duties list; each night's list,
     plus the event's last night; and a queue of arrivals made without signal.
   - **Looking up with no signal** matches a scanned token's last six characters against the kept list's pass codes,
     the code the door would type.
   - **Checking in with no signal** keeps the move with its time, its people count and, for a scan, the token, so
     the server re-checks the pass when it is sent.
   - **Sending the queue:** oldest first; stops at the first move that can't get through; drops a refused one and
     reports it by name ("Daniel Park: This pass was withdrawn by the venue."). One send at a time per store on disk.
   - **Needs a signal, and says so:** walk-ups, because they are counted against the places left; taking back an
     arrival already recorded. Undoing an arrival still waiting on the phone just drops it.
   - **Clean-up:** a 403/404 on a door removes that event's kept lists, and a deliberate sign-out removes everything
     the door kept.
3. **The app.**
   - **Profile → Doors I'm running** shows only when there is a door; the duties are kept, so it is still there
     offline.
   - **`DoorView`** has:
     - the count and places left;
     - Scan a pass;
     - an always-visible name-or-code search;
     - Still to come and In, with Check in, a press-and-hold for part of a party, and Undo;
     - walk-ups with swipe to take back;
     - a night picker;
     - banners for the kept list, arrivals waiting to send, and arrivals refused later.

     The screen stays awake while it is open.
   - **Simulator hook:** `-doorScanCode` (DEBUG only) stands in for the camera on a device without
     `DataScannerViewController`.
4. **Found by the walk: an offline cold start was anonymous.** 14a kept the tokens but the app still came up
   signed out without `api/me`, so the kept door, and anything behind being somebody, could not be reached.
   `SessionStore` now keeps the last confirmed identity (`FileIdentityStorage`). A cold start that can't reach the
   server, or is rate-limited, is that person (`identityIsKept`), and the next `restore()` confirms it. A refusal,
   a session ending or signing out forgets it.

Tests:
- **.NET `HostedEventDoorDutiesTests` (9).** Duties for a door member, a plain member, an accepted steward, an
  unanswered invitation, the kitchen helper and the venue's porter (including a revoked grant). A late arrival
  keeping its time; a wrong clock clamped; earlier-wins; a late scan keeping its time on the arrival and the pass.
  The access rule, the clamp and earlier-wins were each broken and seen failing.
- **BenKit `DoorStoreTests` (14)**, on fixtures captured through `IosFixtureCapture` (`hosted-duties`,
  `hosted-door`, `hosted-scan-admitted`, `hosted-scan-refused`; pass codes replaced, sample trimmed to three
  parties). Broken and seen failing: keeping offline arrivals, reporting a refused kept scan, code-suffix matching,
  and look-up recording nothing.
- **Session:** four tests for the kept identity; using it and forgetting it were each broken and seen failing.
- **Totals:** BenKit 436, .NET unit 5,628, Playwright HostedEvents 114/0.

Simulator walk as Sarah (BenCo organizer), iPhone 17 Pro Max, player copy:
- **Who has doors:** Profile shows Doors I'm running with four BenCo doors. James and Daniel get none (checked over
  the API).
- **Camera and code:** Scan a pass on a device with no scanner says so. Typing `013ba7` finds Daniel.
- **Scan online:** the reservation appears; check in 2 of 3; the server records 2; Undo returns it to 0.
- **Walk-up:** 2 written down, then swiped back.
- **Offline cold start:** still Sarah. The kept door opens with its banner. A scan finds the reservation on the
  phone; checking in keeps it with "1 arrival waiting to send". Back online it went through the scan endpoint and
  the server has the party in.
- **Not walked:**
  - a real phone camera reading a real pass (the simulator has no camera);
  - the kept arrival time surviving on a real night. The walk's event is a month away, so the server rightly
    recorded the send time; the unit tests cover a night that is today.

Help:
- *the mobile apps* gains "Running the door" and "With no signal", with three captures from
  `HelpMediaCaptureTests.testCaptureTheDoor`.
- *organization administration*'s door section describes the app, scan-then-reservation, and offline arrivals.

Next: 14d, the Share Extension and the outbox for photos to an event.

#### Phase 14d as built (2026-09-13) — the outbox, and sharing photos to an event from Photos

1. **`RoomOutbox`** (BenKit) keeps room posts that couldn't be sent: words, the photo or video, *send to the
   hosts*, the agreement to the photo notice, and the time.
   - **Where:** the App Group container `group.com.ishaunted.ios`, so the Share Extension can add to it. Every index
     read and write goes through `NSFileCoordinator`; a test adds twenty at once and loses none.
   - **Files:** moved in when they are the app's own scratch copy, copied when another process owns them, deleted
     once sent.
   - **Format:** a photo in any format other than JPEG is re-encoded to JPEG with ImageIO, because the server
     decodes with SkiaSharp and an iPhone's HEIC may not read there.
   - **Size:** a video over 95 MB is refused in words when added, because the site's proxy refuses bodies over
     100 MB.
2. **`RoomOutboxSender`** sends oldest first and stops at the first post that can't get through.
   - A 4xx is kept under *Couldn't be sent* with the server's sentence, and the next post is still tried.
   - One send at a time per outbox.
   - `RoomOutboxDrain` (app) sends when the app comes forward, when `NWPathMonitor` sees the network return, and
     when a room opens — never while signed out.
3. **The room with no signal.** `RoomCache` keeps each room's last read, so the room opens offline with a banner.
   - When a post can't reach the server, the composer puts it and every unsent photo in the outbox and says so.
   - The room lists *Waiting to send* with *Send now*, and *Couldn't be sent* with swipe to remove.
   - The composer now converts library photos to JPEG and refuses an oversized video before uploading. The 14b
     composer labelled every photo `image/jpeg` whatever its bytes.
4. **`IsHauntedShare`, a Share Extension target.**
   - **What it takes:** up to 20 photos and 5 videos.
   - **What it offers:** events from `ShareableEvents`, a list the app writes to the App Group. It holds the
     events the person is confirmed at plus the doors they run, and what each room allowed when last opened
     (photos taken, notice, host names).
   - **What it asks:** a caption, the notice with *I agree* (asked when needed or unknown), and *Also send to
     {hosts}*.
   - **What it does:** saves to the outbox. **It has no sign-in and uploads nothing.** Sharing the Keychain with
     it would need a new access group, which moves every existing person's tokens and signs them out unless
     migrated. The app sends on its next open with signal, and the extension says so.
   - **Provisioning:** the App Group is added to the app's entitlements and to the extension's. Xcode's automatic
     signing has to register the group on both App IDs (`com.ishaunted.ios`, `com.ishaunted.ios.share`) at the
     next device or TestFlight build. The simulator needed nothing.
5. **Sign-out** removes the outbox, the kept rooms and the shareable list along with passes and doors.

Tests: BenKit `RoomOutboxTests` (10). Broken and seen failing: the JPEG conversion, a refusal being kept apart,
and the rules learned in a room surviving the next list. BenKit total 446. No server code changed.

Simulator walk as Sarah (BenCo organizer), player copy:
- **Room online:** the room was opened with signal and its rules were recorded.
- **Offline, in the app:** a cold start pointed at a dead address opened the kept room with its banner. A library
  photo with a caption was posted and kept.
- **Share Extension:** from the Photos app, Share → IsHaunted showed the photo, Sarah's events and *Also send to
  BenCo*. The shared photo was saved to the outbox as JPEG.
- **Back online:** both kept posts appear in the room. Separately, a post kept while the app was closed was sent by
  the launch send within three seconds.
- **Not root-caused:** twice, the first automatic send after the app came forward stalled until its request timed
  out. The posts stayed kept, and *Send now* sent them in seconds, while the server took the same files by curl in
  under half a second. Suspects: the API's ONNX photo screener warming up after a restart, and a stale keep-alive
  connection on the simulator's loopback. Worth watching on a device.
- **Not walked:** the camera (the simulator has none), and a real device with the App Group provisioned.

Help: *the mobile apps* gains "Photos with no signal" and "Sharing photos from the Photos app", with two captures
from the walk.

Next: 14e, reminders, the widget and the Live Activity. Ben has asked for a full audit of item 235 before merging,
plus SuperAdmin event dashboards and removal, and the advertising material; those come first.

### Phase 15 — Seed walk, screenshots, documentation, runbook

The README's 2.8 and 12: extend `HostedEventDemoSeeder` to every table above; walk the running
site as sarah (decider), james (the member who must not see the board) and daniel (the guest);
`HelpMediaCapture` shots for every `org-event-*`, `public-event-*`, `my-events-*`, `org-venue-*`
screen at 1440 and 375; rebuild the product PDF, the six persona PDFs, both iOS PDFs, the investor
overview and the tour-and-event business offer; `the-mobile-apps.md`; the production runbook
(verify no 235 table exists on production before the first migration; the two band settings Ben
ticks by hand; the three jobs; the `https` profile for LAN camera testing).

### Phase 16 — Who is on, and when: a simple rota for the team

**Added 2026-09-13 (Ben), at the end of the plan:** *"let an organizer construct 'work schedules' for
employees and volunteers in order to be sure every position needed is covered … generalized positions
like Greeters, Food, Tour Guide, Presenter, Session, Speaker … without this becoming some kinda tracking
system for employees … an employee logs in and could see what they are supposed to be doing at different
times and if they are at the door, they would have the ability to log people's arrival."*

**Recommendation: worth building, kept deliberately small.** The question a venue actually has on the
Friday is "is breakfast covered, and who is on the door at seven" — a coverage question, not a
timesheet. So:

- **Positions are a short list the organizer edits**, seeded with Door and greeting, Food, Room service,
  Guide, Presenter, Set-up and clear-down. A word and a colour, nothing more — no job titles, no pay
  grades.
- **A shift is a position, a time on a night (venue clock), how many are needed, and who.** "Saturday
  7:00–10:00 AM · Food · needs 2 · Sam, Priya". Anyone on the event's staff list may be put on one;
  somebody not yet on it is invited the way staff already are.
- **The coverage view is the point:** each night as a strip of time with every position's shifts, a
  shift short of people in amber with "needs 1 more", and a line at the top counting the gaps. On a
  phone, a list by time.
- **Sessions feed it, not duplicate it.** A programme session with a leader shows on the rota as a
  Presenter shift automatically, so a talk is never listed twice or forgotten.
- **Each person sees "My shifts"** on the event (and in a letter with a calendar file when they are put
  on one or it changes). The phone app shows the same list in phase 14's staff view.
- **Permissions stay separate and explicit.** Being on the door shift does not by itself grant the door;
  putting somebody on a Door shift offers "also let them check people in", which ticks the existing
  Run-the-door flag. A shift never widens what a person can see.
- **What it will not do:** no clocking in or out, no hours, no attendance record of staff, no
  performance anything. The door log stays about guests. That line is what keeps it from becoming an
  employee-tracking system.
- Copying an event (phase 12) brings the positions and the shift pattern, without the names.

### Phase 17 — Before merging: audit, SuperAdmin oversight, demo content, walks and advertising

**Asked for by Ben on 2026-09-13, in the middle of phase 14d:** *"Before we merge this enormous branch and new
functionality, complete a code audit and review on the new hosted event process and find any poorly written
code, gaps in code or missing functionality or missing tests. Double-check that we have everything needed, and
offered, by someone providing the hosting software experience."*

He also asked for:
- free images (Unsplash) for tickets, ads, menus, events and venues, *"aligned with the topic, fitting the area"*;
  he will review them once the documents exist;
- help, changelogs, a new Hosted Events advertising PDF including the iPhone app, and updates to the existing PDFs
  including the investor overview;
- *"visually run end to end each type of person for the event and just simulate the sending of emails"* on
  `IsHauntedDb_player`, with screenshots and video for the help and PDFs;
- a printable one-page, front-and-back advertisement for the whole product, and one for each part we can charge
  for (individuals, groups, ghost walk tours, venues, and event hosts);
- in the SuperAdmin home dashboard, a tab for an events dashboard with charts about venues, organizers and
  events;
- a SuperAdmin list of every event (organizer, event, dates, view, delete). Delete removes the event, credits the
  event credit back to the organizer, and emails a generic rejection with the ability to appeal to create the
  event again.

Sub-phases, in this order, because each feeds the next:

- **17a — Audit and fixes.** Server, website and iPhone app, read against the plan of record and against what a
  hosting product is expected to offer. Findings go in a table with severity, the fix or the reason it waits, and
  the test that pins it. Fixes are made in the phase with a discriminating test.
- **17b — SuperAdmin oversight.**
  - An *Events* tab on the SuperAdmin home with charts: events by state over time, credits bought and spent,
    bookings and people, venues and organizers.
  - An events list (organizer, event, dates, state; view; remove).
  - Removal as a recorded state, not a purge: the credit goes back to the organizer, the organizer and any
    confirmed guests are told, and the email carries an appeal link. The appeal is a short form; SuperAdmin
    answers it; upholding it restores the event as a draft.
- **17c — Demo content and imagery** on `IsHauntedDb_player`: free-licensed photographs for the event pages,
  galleries, venue libraries, ads, menus and passes, recorded with source and licence.
- **17d — Walks, as every person.** Organizer, venue, door helper, guest with an account, guest without one, and
  SuperAdmin. Emails are written to a local folder and photographed rather than sent. Screenshots and short
  recordings are kept for the help and the documents.
- **17e — Documents.**
  - A Hosted Events advertising PDF, including the iPhone app.
  - Updates to the product PDF, the persona PDFs, the iOS PDFs and the investor overview.
  - One-page front-and-back advertisements: the whole product, individuals, groups, ghost walk tours, venues and
    event hosts.
- **17f — Help and changelog sweep**, then the full suites, then merge readiness for Ben.

#### 17a as built (2026-09-13)

The audit and its findings table are in `README-hosted-events-235-audit.md`. Fixed or built in 17a, each with a
test that failed when the rule was broken:

- **A1:** unit tests for the nine endpoints no test reached.
- **A2:** the hosted booking rate limit on eight public writes, with a guard.
- **A3:** audit rows for lifecycle changes, booking decisions, passes and venue withdrawal.
- **A4:** *Write to your guests*:
  - migration `HostedEventAnnouncementsAndAccess` adds the `HostedEventAnnouncements` table;
  - three endpoints, and a card on the booking board;
  - email plus site message, ten a day per event.
- **A5:** *At a glance*, from `GET …/summary`.
- **A6:** social cards with pictures on event and venue pages.
- **A7:** access notes on the event, public page, confirmation letter and iPhone hub. They are seeded on
  both demo events.
- **A8, A9, A14:** tidy-ups.

The migration is applied to `IsHauntedDb_player` and the e2e database. A11 (the first automatic outbox send
after the app comes forward can stall) and A12 (no hosted iPhone UI test in the normal suite) stay open,
as recorded in the audit.

Verified by:
- the full .NET unit suite, 5,658 passed;
- BenKit, 446 tests;
- the app build;
- Playwright `EventBookingBoardTests`, 14 of 14 on the e2e database, including the letter, the at-a-glance
  card and the public access notes;
- the iPhone fixtures re-captured from the real API.

#### 17b as built (2026-09-13)

- **Removed.** `HostedEventLifecycleState.Removed = 7` counts as called off and is kept off the public site.
  `HostedEventRemoval` records the previous state, a private note, whether the credit came back, how many guests
  were told, and the appeal: its state, message, answer and who acted. Migration `HostedEventRemovals`.
- **Removing.** `HostedEventRemovals.RemoveAsync`:
  - returns the credit whatever the timing, and makes the event unpaid again;
  - withdraws every pass and syncs the calendar row.
  After the save, guests get the ordinary not-going-ahead letter with no reason. The organizer's side (the event's
  creator, the group's creator and its billing contacts) gets a generic removal letter, a site message and the
  appeal link.
- **Appeals.** One per removal, from the organizer's event page, by anyone who may edit the group's events.
  SuperAdmins get a message. An upheld appeal brings the event back as a **draft**; a declined one needs a reason.
- **Screens:**
  - `/admin/events` — appeals first, then a grid of organizer, event and venue, dates, state and people coming,
    with view and remove;
  - `/admin/events/{id}/remove`, which says what removal will do before the button;
  - an *Events* tab on `/admin/dashboard` (`?tab=events`) with six cards and nine charts;
  - `EventRemovedCard` on the organizer's event page;
  - a SuperAdmin nav entry.
- **Tightened:** Restore only acts on an archived event.
- **Help:** *Hosted events* in the site administration guide; *If IsHaunted removes your event* for organizers.

Verified by:
- `HostedEventRemovalTests` (8), with discrimination checks on the credit, the restore guard and the audit
  snapshot;
- the full .NET unit suite, 5,666 passed;
- Playwright `AdminEventOversightTests`, 2 of 2 on the e2e database, with no unhandled API errors in the run.

### FUTURE (recorded, not scheduled)

- **Apple Wallet** (decision 14): `eventTicket` pass, serial = pass id, barcode message = the
  token, altText = the pass code, `voided` on revoke, a reissue is a **new serial** (barcode content
  cannot change on a serial), colours from `TourPlate`, back fields = nights with units, venue
  address (confirmed guests may see it even when hidden), contact line (how to pay is the venue's
  line, never ours). Server: Pass Type ID certificate + WWDR G4 in configuration, `manifest.json`,
  detached PKCS#7 via `SignedCms`, `GET my-booking/pass.pkpass` as `application/vnd.apple.pkpass`
  `private, no-store`, attached to the confirmation letter. Step two: the PassKit web service +
  APNs so a withdrawn pass voids itself. Phone: `PKAddPassesViewController`, the pass-type
  entitlement, "In Wallet" via `PKPassLibrary`. Google Wallet is the same shape with a JWT.
- Per-ticket payments (README phase 11) — design only; decision 6 says the site takes no guest
  money.
- Waiting-list auto-offer when a hold lapses (24 h to accept, then the next); accessible and
  companion seat `Tags`; group blocks under a name; per-night day-pass caps; print the plan for
  the box-office wall; room-mate matching and pre-booking questions.

## Keep / change / rewrite / delete — the branch against this plan

| Piece | Verdict | Phase |
|---|---|---|
| `Enums/HostedEventBookingKind`, `HostedEventBookingStatus` (+Held, Expired), `HostedEventLayoutKind` | KEEP / append | 4 |
| `Enums/EventBookingKind`, `EventBookingStatus` | **DELETE** | 1 |
| `BenDataModel.HostedEvent.cs` | CHANGE (state, arrangement, go/no-go, mode, hold minutes) | 3 |
| `BenDataModel.HostedEventBooking.cs` (unit, booking, night, guest) | CHANGE (`HoldExpiresUtc`, `ReleasedUtc`, `People`, `UnitBlock`) | 4 |
| `HostedEventPass`, `HostedEventMenu`, `EventCredit`, `EventAttendanceInvite`, `PlaceRoom` | KEEP | — |
| `BenDataContext.cs` item-235 block | CHANGE (indexes, constraints) | 3–4 |
| Migrations `20260911*`, `20260912*` on the branch | KEEP (the chain applies fresh on production if the tables are absent — verify first) | 3 |
| `Services/Events/EventCapacity.cs` | **REWRITE** (holds count; one party per unit; blocks) | 4 |
| `Services/Events/BookingTransitions.cs`, `PlanOccupancy.cs` | NEW | 4 |
| `EventDietary.cs` | CHANGE (one line) | 4 |
| `EventPasses.cs` | KEEP | — |
| `EventGuestMailer.cs` | CHANGE (zone fix; asked / held / lapsed / go / no-go / withdrew letters) | 1, 6 |
| `HostedEventGuestDoor.cs` | CHANGE (shared Ask path for the phone; writes through `BookingTransitions`) | 4 |
| `HostedEventCalendarSync.cs`, `HostedEventEntitlement.cs`, `EventCredits.cs` | CHANGE (state queries; credit restore) | 3 |
| `Scheduling/EventCreditExpiryJob.cs` | KEEP (pattern source) | — |
| `Controllers/Entities/HostedEventController.cs` | CHANGE (gates, readiness, go/no-go, blocks, `Id` matching, structured refusal, access) | 1, 3, 4 |
| `HostedEventBookingController.cs` | **REWRITE the decision half**, keep the shape and the pass/door/invite routes | 4–5 |
| `HostedEventMenuController.cs` | KEEP (permission line) | 4 |
| `PlaceRoomController.cs` | CHANGE (delete refusal) | 1 |
| `Controllers/Public/PublicHostedEventBookingController.cs` | CHANGE (holds, plan, pass incl. revoked, mine incl. cancelled) | 6 |
| `PublicHostedEventController.cs` | CHANGE (additive fields) | 6 |
| `PublicEventController.cs` Rsvp/CancelRsvp/Acknowledge | **CHANGE — the hosted branch never written** | 4 |
| `PublicEventAttendanceController.cs` | CHANGE (small) | 4 |
| `PublicEventPassController.cs` | KEEP (+ referrer policy) | 6 |
| `OrgCalendarController.cs` attendee endpoints | CHANGE (umbrella fence) | 4 |
| `AdminPlaceMergeController.cs` | CHANGE (repoint events) | 1 |
| `NotificationSummaryController.cs`, `NotificationRows.cs` | CHANGE (Held counted; new buckets and rows) | 4, 8 |
| `OrganizationPurge.cs` | CHANGE (blocks, preferences, staff, grants) | 4, 7, 8, 9 |
| `Manage/Events/OrgEvents.razor`, `OrgEventPage.razor` | KEEP, extend | 1, 3 |
| `Organization/PlaceRoomsManager.razor` | CHANGE | 1 |
| `Organization/Public/PublicEventDetail.razor` | KEEP, extend (hosted branch in `event-act`) | 6 |
| `Organization/Public/EventAttendanceConfirm.razor` | CHANGE (hosted branch) | 6 |
| `SuperAdmin/AdminEventCredits.razor`, billing credits card | KEEP | — |
| `IBenOrganizationClient` hosted methods | CHANGE (`GetItemAsync`) | 1 |
| Tests on the branch (`EventCapacityTests`, `HostedEventBookingTests`, `EventLayoutTests`, `EventPassTests`, `EventDietaryTests`, `HostedEventGuestDoorTests`, `HostedEventMenuTests`, `EventGuestMailerTests`, `IcsBuilderMultiEventTests`, credit tests, controller tests) | KEEP; reword state and one-party assertions | 3–4 |
| Help `organization-administration.md` hosted sections | CHANGE ("Rooms and bookings" → "The plan and the bookings"; the two modes and the hold; "Confirming the event is on") | 3, 5 |
| Changelog `website.md` / `api.md` hosted entries | KEEP; add per phase | each |
| `README-hosted-events-235.md` | CHANGE (head says this plan supersedes phases and decisions; Status kept; defects recorded) | 1 |
| `Ben.iOS` Events feature, `InvestigationsStore.swift` | KEEP; extend | 14 |

## Gaps and defects found, and where each is fixed

| # | Found | Fix | Phase |
|---|---|---|---|
| 1 | Unique `(BookingId, NightId)` + `GroupBy(night).Last()` collapse a party's three seats to one | index `(Booking, Night, Unit)`; group by (night, unit) | 4 |
| 2 | No concurrency protection: `Confirm` checks capacity in C#, no transaction, no live index | filtered unique index + transaction + 409 | 4 |
| 3 | DECISION 5 never built: the shipped phone's umbrella RSVP writes an Accepted attendee with no booking; `CancelRsvp` flips a confirmed booking's row | hosted branch in `PublicEventController` | 4 |
| 4 | Calendar attendee approve/turn-down/delete are a third umbrella writer | fence | 4 |
| 5 | Two doors write two umbrella shapes for one state | `BookingTransitions` single writer | 4 |
| 6 | Any org member sees guest emails and (until 2.2c) dietary on the board | `HostedEventAccess` | 4 |
| 7 | `PlaceRoomsManager` never sets `IsBookable`: no Rooms plan can be offered | fields | 1 |
| 8 | `BookingsCloseAtUtc`, `DayPassPrice` write-only | on the request | 3 |
| 9 | Auto-archive promised in help and card; no job exists | `HostedEventLifecycleJob` | 3 |
| 10 | `.ics` nights written as 18:00 UTC (1 PM Nashville) | venue zone | 1 |
| 11 | `GetMyPass` says "not issued yet" for a revoked pass | return latest incl. revoked | 1 |
| 12 | `Withdraw` on TurnedDown records a cancellation request of nothing | 409 | 1 |
| 13 | Renaming a booked seat is delete+create; layout refusals are prose only | `Id` on the choice; `LayoutRefusalRecord` | 1 |
| 14 | Place merge strands `HostedEvents.PlaceId`; room delete 500s on a unit reference | repoint; refuse by name | 1 |
| 15 | `/attending/{token}` says "You're coming" after a hosted ask | hosted branch | 6 |
| 16 | Bell row "A venue answered you" links to `/events` | `/my-events` | 6 |
| 17 | A passwordless email-link account has no way to sign in and pick | *Set a password* | 6 |
| 18 | A guest re-request releases their held units immediately; confirming a lapsed hold could win seats another guest holds | transitions keep a fresh hold; the index arbitrates | 4 |
| 19 | Check-in is one stamp per pass; a three-night event cannot record Saturday | per-night check-in | 7 |
| 20 | No "email the pass again" despite `EmailedUtc` | endpoint + button | 1, 5 |
| 21 | Board render cost ~480,000 LINQ passes | `PlanOccupancy` | 4 |
| 22 | Cancel never touched the credit; un-cancel undefined | decision 8 rules | 3 |
| 23 | Dead phase-0 enums | delete | 1 |
| 24 | `GetPublicHostedEventAsync` reads 403 as not found | `GetItemAsync` | 1 |

## Verification, end to end

1. After every phase: `dotnet build Ben.slnx` warning-free for touched projects; `dotnet test
   Ben.slnx` green; every new rule test seen failing first with its rule broken;
   `scripts/run-e2e.sh --filter <phase classes>` green on `IsHauntedDb_e2e` at all three widths;
   the phase's "verified by" walked on the running site against `IsHauntedDb_player` as sarah,
   james and daniel.
2. Phase 2 ends with Ben at the screen before phase 3 begins.
3. **CHECKED 2026-09-12, read-only, and the answer is good: no item-235 migration has ever reached
   production.** `dotnet ef migrations list --connection "<IsHauntedDb>"` reports all seven —
   `HostedEvents`, `EventCredits`, `EventCreditGrants`, `HostedEventBookings`,
   `HostedEventBookingCancellationRequests`, `EventPasses`, `EventLayoutUnits` — as **Pending**;
   the last one applied there is `20260911171519_TourSeats`. So the whole chain applies fresh on
   production and **every destructive concern in the migration plan is confined to
   `IsHauntedDb_player`**: the hand-written `EventLayoutUnits` rename, and migration 4's pre-check
   that throws when two live parties already share a unit-night, only ever run against test data.
   Re-check with the same command before the first production deployment, because a hand-run
   `database update` between now and then would change the answer.
4. Phase 15 reprints every document and walks the whole thing once more as a guest on an iPhone in
   Safari, an organizer on an iPad, and a venue on a laptop.

## Still Ben's, but nothing waits on them

- ~~**One credit, how many performances?**~~ **Decided 2026-09-13 (Ben): no limit.** Asked about a run of the same show
  on several days (*"dinner and a play at a venue. So Friday, Saturday, Sunday..."*), and whether one $99 credit should
  cover it indefinitely, Ben concluded: *"I don't think people would try to take advantage of using the site event
  hosting forever. So my thought about limit number of times may be overthought."* One credit covers one event however
  many dates it has, within the existing cap of 366 dates per event (`HostedEventController.MaximumDates`), which stays
  as a guard against a typed year rather than a pricing rule. A span-based rule (one credit per 31 days of a run) was
  proposed and not adopted; revisit only if usage shows events kept alive for years.

- The exact **hold default**: 48 h is used; per-event editable 15 min – 14 days.
- ~~Whether **anonymous** email-link guests should ever hold seats (plan says no).~~ **Decided
  2026-09-13 (Ben):** they may, without an account or a password, but not without the organizer being
  able to reach them and not in a way that lets somebody swamp an event with unconfirmed holds.
  *"First Last Name, E-mail Address, Phone Number required so the event organizer can contact them to
  make arrangements for collecting fees. We can let them know during sign up that we only collect their
  information for the event organization."* **Built as slice 11d** (as-built record under phase 11). The design:
  name, email and phone required for every booking (a signed-in guest is asked only for what their
  profile lacks); a plain disclosure at sign-up that the three go to the organizer for this event only;
  a signed-out pick shows the seats **pending for 15 minutes and becomes a real hold only when the
  emailed link is clicked** — so a fake or mistyped address never blocks a seat — then the normal hold
  expiry applies; one live hold per email per event, the existing per-hold seat cap, and a per-address
  rate limit on unconfirmed picks. The link lets them manage the booking with no password; setting one
  stays optional. Phone is required but not verified (the site cannot send texts).
- Whether the public plan shows **counts** or only states (plan says states only).
- **Person hard-purge** with hosted references: the census blocks and names the events (the
  existing tool's philosophy), account closure withdraws the person's undecided bookings and asks
  the venue to release confirmed ones.
