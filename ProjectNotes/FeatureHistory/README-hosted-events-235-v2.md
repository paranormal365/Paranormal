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
- **Verified by**: a non-member helper on a phone admits a party by camera; the owner's board shows
  them arrived; the camera is covered and the same party is found by name.

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
- Tests: `VenueGrantTests`; Playwright `VenueHostingTests` in two orgs.
- **Verified by**: Nashville Paranormal cannot publish at Thomas House until Thomas House approves
  Oct 30–Nov 1; a revoke turns the badge to *Venue withdrew* in both browsers and the credit comes
  back.

### Phase 10 — Sessions and classes ("the ghost hunt but not the dinner")

As the README's phase 4, unchanged in substance: `HostedEventSession*`, first-come sign-up with a
waiting list for a confirmed booking or staff, programme per night in the venue zone, `.ics` per
session, promotion mail; the *Programme* index link goes live. Compact: the programme is a
per-night timeline, the most-looked-at thing during an event.

### Phase 11 — Files, the flashy page and ads, the room

As the README's phases 6, 7 and 9 with two changes: the CMS `EventBooking` section embeds
`BenPlan Mode="Read"` with the public occupancy projection and a night picker ("Balcony nearly
full" without names) and a sticky *Ask for a place* on phone; the room's moderation lives on the
board's *Room* tab. `OrgMessage.HostedEventId` lands here and phase 8's staff-room delivery
switches on.

### Phase 12 — After the event

`HostedEventLifecycleJob`'s archive rule with decision 11; **Copy this event** (`POST
{id}/copy {Name, StartsOn}` → a Draft with nights shifted, units without bookings, menus remapped by
night index, staff copied, arrangement reset); `GET bookings/export.csv`; reviews via `TourReview`
on the umbrella; a once-only thank-you letter with the gallery link.

### Phase 13 — Dining as a seating assignment

`HostedEventDiningTable` + `HostedEventDiningSeat` per sitting, over Confirmed bookings only;
`Manage/Events/OrgEventDining.razor` (`…/dining?sitting=`): a tray of confirmed parties with size
and dietary badges, the same `BenPlan` canvas drawing tables (round ≤ 8 seats) with "6/8" and
aggregated dietary badges, click-party-then-table or drag on a pointer, *Same as last sitting*,
over-seating refused in words; "Table 4" on the guest's pass page; a per-table column on the
kitchen print. No "who sits with whom" input in v1 — the booking note is the interim.

### Phase 14 — The phone

As the README's phase 10 with the guest planner's order: server first (already done in phase 4);
`PublicEventRecords.swift` gains `hostedEventId`/`hostedEventName`/`hostedBookingMode`/
`hostedBookingUrl`; `EventDetailView` swaps the seat panel for a `HostedBookingPanel` (404 →
*Ask for a place* in `SFSafariViewController`); native `EventPassView` renders the QR from the
cached token with `CIFilter.qrCodeGenerator()`, works offline, shows revoked honestly, raises
brightness; `EventsView` rows badge "3 nights"; `MyBookingReminders`; the door with
`DataScannerViewController` (the real offline scanner); then the hub, programme, menu, files,
stream, outbox, Live Activity and widget as written. Picking stays on the web until the web picker
has been lived with. No build bump on the branch.

### Phase 15 — Seed walk, screenshots, documentation, runbook

The README's 2.8 and 12: extend `HostedEventDemoSeeder` to every table above; walk the running
site as sarah (decider), james (the member who must not see the board) and daniel (the guest);
`HelpMediaCapture` shots for every `org-event-*`, `public-event-*`, `my-events-*`, `org-venue-*`
screen at 1440 and 375; rebuild the product PDF, the six persona PDFs, both iOS PDFs, the investor
overview and the tour-and-event business offer; `the-mobile-apps.md`; the production runbook
(verify no 235 table exists on production before the first migration; the two band settings Ben
ticks by hand; the three jobs; the `https` profile for LAN camera testing).

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
3. Before any migration reaches production: `SELECT MigrationId FROM __EFMigrationsHistory WHERE
   MigrationId LIKE '2026091%'` and `SELECT name FROM sys.tables WHERE name LIKE 'HostedEvent%'`;
   if the tables are absent the chain applies fresh; if present, count
   `HostedEventBookingNights` rows before migration 4, whose pre-check throws rather than deletes.
4. Phase 15 reprints every document and walks the whole thing once more as a guest on an iPhone in
   Safari, an organizer on an iPad, and a venue on a laptop.

## Still Ben's, but nothing waits on them

- The exact **hold default**: 48 h is used; per-event editable 15 min – 14 days.
- Whether **anonymous** email-link guests should ever hold seats (plan says no).
- Whether the public plan shows **counts** or only states (plan says states only).
- **Person hard-purge** with hosted references: the census blocks and names the events (the
  existing tool's philosophy), account closure withdraws the person's undecided bookings and asks
  the venue to release confirmed ones.
