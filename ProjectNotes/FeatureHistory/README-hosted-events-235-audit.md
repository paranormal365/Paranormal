# Item 235 — pre-merge audit (phase 17a)

Asked for by Ben on 2026-09-13, before merging `feature/hosted-events-235` (88 commits, about 129,000 changed lines
outside migrations): *"complete a code audit and review on the new hosted event process and find any poorly written
code, gaps in code or missing functionality or missing tests. Double-check that we have everything needed, and
offered, by someone providing the hosting software experience."*

## How it was done

1. **Mechanical checks over every hosted endpoint.** A script found 157 actions across the organizer, public, venue
   and SuperAdmin controllers. For each it recorded whether:
   - a unit test calls the action;
   - a Playwright test mentions its route;
   - the action checks access before doing anything;
   - a public write carries a named rate-limit policy.
2. **Pattern sweeps over the 310 changed source files** (C#, Razor, Swift): TODO/FIXME, `NotImplementedException`,
   empty `catch`, blocking waits, `DateTime.Now`/`Today`, discarded tasks, unbounded public lists.
3. **A reading pass** of the riskiest paths: booking writes and transitions, credits at publish and cancel, the door,
   the room, jobs, and the phone's offline stores.
4. **A gap check against what a hosting product is expected to offer.** Compared with the Eventbrite, Tito, Ticket
   Tailor and venue-booking pattern:
   - listing and discovery, booking and seat choice, capacity and waiting lists;
   - confirmations and reminders, passes and door check-in, staff roles;
   - telling attendees, the programme and sessions, food and dietary needs, files, the room and photos;
   - reviews, exports, copying an event;
   - the host's numbers, social sharing, accessibility information;
   - platform oversight for the operator.

## What was sound

- **Access is consistent.** Every organizer-side action checks `HostedEventAccess`, or hands off to a helper that
  checks first (`DecideAsync`, `SetAsync`, `MoveAsync`, `SaveAsync`, `ChangeAsync`). SuperAdmin controllers carry the
  class-level policy. The guard test `HostedControllersUseHostedEventAccessTests` already holds this.
- **Scoping is consistent.** Every event read by id also matches its organization id.
- **Public lists are bounded** (`Take(50..200)`, room pages of 50, wall 200, venue 20).
- **No** TODO, FIXME, `NotImplementedException`, blocking waits or discarded tasks in the hosted code.
- **Rate limits:** a site-wide limiter covers every endpoint.
- **Letters go out at every state change a guest must know about:** asked, held, hold lapsed, confirmed, turned down,
  released, called off, going ahead, venue withdrew, pass, programme change, promoted from a waiting list, staff
  invitation, thank-you. A confirmed guest also gets the day-before reminder through the umbrella date
  (`EventReminderJob`).
- **Coverage:** 148 of the 157 endpoints are reached by a unit test or a Playwright test.

## Findings

Severity: **High** — wrong or unsafe behaviour. **Medium** — a gap a host will notice, or accountability missing.
**Low** — tidiness.

| # | Severity | Finding | Decision |
|---|---|---|---|
| A1 | Medium | **Nine endpoints no test reaches:** `KeepZip` (the 100-file limit), `ExportBookings` (CSV formula escaping), `GetBoard`, `ReleaseLapsedHolds`, `ReissuePass`, staff `Save`, venue profile `Save`, `GetMyContact`, `ProgrammeCalendar` | Fixed in 17a: a unit test for each behaviour that matters |
| A2 | Medium | **Stricter booking limit only partly applied.** The plan's `HostedBookingPolicy` (30 a minute) is on *hold places* but not on *ask for a place*, *change my booking*, *post to the room* (the expensive one — every photo is screened), *review* or *report*. Nothing pins which public writes carry it | Fixed in 17a, with a guard test naming the public writes that must |
| A3 | Medium | **No audit trail.** The hosted area writes nothing to the site's audit log — not publish, cancel, uncancel, archive, go/no-go, booking decisions, pass withdrawal or venue withdrawal — although 76 other controllers do. Some rows record who acted (`DecidedBy`, `RevokedBy`), but a dispute about who called a weekend off has no history | Fixed in 17a for lifecycle, decisions, passes and the venue's withdrawal; SuperAdmin removal (17b) logs from the start |
| A4 | Medium | **Hosts can't write to their guests.** There is no "message everyone confirmed" for *parking has moved* or *doors open at eight*. The room reaches only people who open it; changes to the programme mail only its sign-ups. Every hosting product has this | Built in 17a: *Write to your guests* on the booking board (confirmed guests, optionally one night, optionally requests too), sent through the guest mailer and the bell, and recorded with its count |
| A5 | Medium | **No at-a-glance numbers for the host.** The event page opens on its readiness checklist; bookings by state, people confirmed, places left, arrivals and the review average need four screens | Built in 17a: an *At a glance* card on the event page from one summary endpoint |
| A6 | Medium | **Shared links look bare.** The event page's social card has no picture although the event has a gallery; the venue page has no social card at all | Fixed in 17a |
| A7 | Low | **No accessibility information.** There is nowhere to say *stairs only to the ballroom*, *low lighting*, *hearing loop in the bar* — standard on venue and event pages | Built in 17a: *Access and what to know* on the event, shown on the page and in the confirmation letter |
| A8 | Low | **Stale scaffolding in `OrgEventPage`.** The `MenusPageShipped` and `StaffPageShipped` flags and `IndexEntry`'s "— soon" branch are dead (all true); the index also has no link to the event's room | Fixed in 17a |
| A9 | Low | `PromoteGroupPage.RestoreDraft` swallows every exception | Narrowed to `JsonException`: a draft restore should ignore malformed JSON, not every failure |
| A10 | Low | `OrgEvents` and `OrgEventPage` default new dates from `DateTime.Today` on the server | Left: form defaults only; the host changes them and the saved date is what the host typed |
| A11 | Medium | **The phone's first automatic send can stall.** In the 14d walk, the first send after the app came forward twice waited out its timeout (the posts stayed kept and sent on the next try) | Open: walked again on a device in 17d; the behaviour is already safe |
| A12 | Medium | **No iPhone UI test runs a hosted flow in the normal suite.** The help captures walk them, but only on request | Recorded for 14e, which adds reminders; the captures stay the regression walk until then |
| A13 | Medium | **No oversight for the operator:** no SuperAdmin view of events, venues or organizers, and no way to remove an event | 17b (Ben's request) |
| A14 | Low | `HostedEventStaffController` takes a `UserManager<AppUser>` it never uses | Removed |

## Recorded as not built (unchanged, and the reason)

- **Waiting list that offers a lapsed place to the next person automatically.** Needs an accept window and letters;
  the waiting list is a manual queue today (FUTURE in the plan).
- **Custom questions at booking.** Needs a question builder and answer storage (FUTURE).
- **Checklists, and "staff here now".** No screen yet reads them (phase 7's record).
- **The rota** (phase 16), **Apple Wallet** (FUTURE), **14e reminders, widget and Live Activity.**
- **Payments.** Decision 6: the site takes no guest money.

## Fixes made in 17a

(Filled in as each lands, with the test that pins it.)

- **A1** — `HostedEventUncoveredEndpointTests` (9), one per endpoint:
  - the board refused to a plain member;
  - releasing only lapsed holds;
  - reissue withdrawing the old code and refusing an unconfirmed booking;
  - the spreadsheet: this event only, with the BOM, formulas written as text, refused to a plain member;
  - keep.zip: at least one file, at most 100, nothing it wasn't offered;
  - a staff address being an invitation that grants nothing;
  - a venue page saved privately and not published unconfirmed;
  - contact prefill preferring the listed phone;
  - the programme calendar leaving out called-off sessions and unpublished programmes.

  The formula escape and the 100-file cap were each broken and seen failing.
- **A2** — `HostedBookingPolicy` on asking for a place, changing a booking, room posts, sending a photo on, reports,
  reviews and session sign-ups. `HostedPublicWritesAreRateLimitedTests` names all eight and failed when room posts
  lost the attribute.
- **A3** — `BenControllerBase.SaveAndAuditAsync` takes the before-picture from the change tracker, saves, and writes
  the audit row (skipped when the request has no audit service, as in unit tests). Used for publish, unpublish,
  cancel, uncancel, archive, restore, go/no-go, confirm, turn down, release, pass withdrawal, pass reissue and the
  venue's withdrawal. `Publishing_and_calling_an_event_off_are_written_to_the_audit_log` failed when cancel went
  back to a plain save.
- **A4** — *Write to your guests*:
  - a new table records each letter (subject, body, optional night, whether unconfirmed parties were included,
    the recipient and emailed counts, who sent it);
  - `HostedEventAnnouncementController` has the history, the audience count and send, all behind
    `CanDecideBookingsAsync`;
  - one letter per party lead, by email through `EventGuestMailer.SendAnnouncementAsync` (event name in the
    subject, the group's address as reply-to, escaped body) and by site message;
  - refused in words for an empty or long subject or body, an unpublished or called-off event, a date of
    another event, nobody to write to, and a tenth letter in a day;
  - the card sits at the foot of the booking board, shows the count as choices change, and needs a second click.

  `HostedEventAnnouncementTests` (9) failed when the confirmed-only rule was dropped (4 failures) and when the
  night filter was dropped (4 failures). Playwright: the board test sends a letter through the card, and
  accepts the day-limit refusal in words when a shared database has already reached it.
- **A5** — `HostedEventSummary` and `GET …/summary` behind `CanReadBookingsAsync`, with the *At a glance* card
  on a published event's page. Places are counted night by night, holds count as taken, and a unit blocked
  every night is not also taken away for a named night. `HostedEventSummaryTests` (2) failed when the block
  was counted twice and when holds stopped counting. Playwright checks the card and its link to the board.
- **A7** — `HostedEvent.AccessNotes` (2,000 characters) is on the organizer's event page and the public event
  page under *Getting in and getting around*. It also goes in the confirmation letter before the pass
  (`A_confirmation_says_how_to_get_in_and_around_before_the_pass`) and in the iPhone app's event hub. The
  demo seeder fills it on both seeded events where it is empty, and Playwright reads it on the public page.
- **A13 (17b)** — SuperAdmin oversight:
  - *Events* tab on the dashboard;
  - `/admin/events` list and `/admin/events/{id}/remove`;
  - `HostedEventLifecycleState.Removed` and a `HostedEventRemovals` table carrying the appeal;
  - the organizer's *Removed by IsHaunted* card with its appeal, and the SuperAdmin's uphold or decline.

  `HostedEventRemovalTests` (8) failed when the credit return was dropped (4 failures) and when the restore
  guard below was dropped. A strict audit fake failed when the appeal decision's before-picture was not an entity
  (2 failures); that bug returned 500 after saving and was found by the e2e run, not the unit tests. Playwright
  `AdminEventOversightTests` (2) cover remove, appeal, uphold and the dashboard tab.
- **Found while building 17b:** *Restore* turned any event into a draft, including a called-off one. That skipped
  the credit re-spend un-cancelling applies, and would have let a removed event skip its appeal. Restore now acts
  only on an archived event, and nothing on the organizer's event page changes a removed one.
- **Also fixed while writing help:** the *Rooms and bookings* help still said the booking board "is being built
  now".
- **A6** — `SocialCard` makes a site path absolute. The event page's card uses the first gallery picture; the venue
  page has a card with its first kept photo and a one-line summary. The Playwright gallery test reads the
  prerendered HTML for `og:image` and `summary_large_image`.
- **A8** — The dead flags and the "— soon" branch are gone, and the index links to the room on the event's page.
- **A9** — `RestoreDraft` catches `JsonException` only.
- **A14** — The unused `UserManager` dependency is removed.
