# feature/event-scenario-gaps: Jim's overnight conference, walked against the code

Branched from `develop` at 44dbe6ef ("Merge branch 'feature/event-credits-ghost-walk-plan' into
develop"), 10/21/2026.

> **DEPLOY NEEDS A MIGRATION: `20261021220236_EventScenarioGaps`.**
> It only adds columns and two indexes; nothing is rewritten:
> - `HostedEvents`: `EvidenceDays`, `ResultsInDays`, `SendMissedYou` (**true for existing rows**),
>   `MissedYouNote`, `AsksForVotes`, `ResultsUrl`, `ResultsSentUtc`, `DoorTicketToken` (unique when set).
> - `HostedEventBookings`: `AskedAtTheDoorUtc`, `ResultsSentUtc`.
> - `HostedEventSessionSignUps`: `AttendedUtc`, `AttendanceRecordedByAppUserId`.
> - `HostedEventStaff`: `StartsSessions`.
> - `EventAttendanceInvites`: `ConfirmsOnAccept`.
> - `OrgMessages`: `OrgCalendarEventId` (foreign key to `OrgCalendarEvents`, indexed) for a tour date's room.

Sources: Ben's *Event Document.md* (Jim at The Factory, Franklin TN), *Event Menu Management.md*, and the
"Meeting or Event or Tour" and "Start of Investigation" parts of *IsHaunted.com AI Errors -- 10-20-2026.md*.
Business rules of 10/21/2026 (ProjectNotes/branches/event-credits-ghost-walk-plan.md): one credit, one
event of contiguous days; anyone buys credits, paid subscribers pay 20%; IsHaunted never collects attendee
money.

Status is what the code and the tests did **before** this branch; **Now** is after it.

## Jim's story, sentence by sentence

| # | Sentence | Before | Where / what was missing | Now |
|---|---|---|---|---|
| 1 | Jim rents The Factory and pays its deposit | Outside the site | Venue arrangements are recorded (`HostedEvent.VenueArrangement`, contact, agreed-on date) | Unchanged |
| 2 | He purchased one event credit | **Built** | `EventCreditCheckout`, `POST api/organizations/{id}/subscription/checkout/event-credits`, `POST api/me/event-credits/checkout`, `/pricing`, `/my-event-credits`; `EventCreditTests`, `EventCreditsAndToursTests` | Unchanged |
| 3 | Jim schedules the event (overnight, contiguous days) | **Built** | `HostedEventController` create/update, `BuildNights`; `/organizations/{OrgId}/events`; `Oct16EventLengthTests` | Unchanged |
| 4 | He has a list of people; creates email on the site and sends it | **Partly built** | Writing to guests reached only people already booked (`HostedEventAnnouncementController`); **Invite by email** took one address and no message | **Fixed**: `POST …/bookings/on-behalf/invite-many` (pasted list, own message, typos handed back); board dialog **Invite people by email** |
| 5 | Charge them or not — we don't handle money | **Built** | Pass page, plan legend and booking card say nothing is paid here; `DayPassPrice` shown, never charged | Unchanged; door and ticket pages say it too |
| 6 | Makes it public; price in the announcement details | **Built** | Publish lifecycle; `Description`, `ContactLine`, `DayPassPrice` on `/o/{org}/events/{slug}`; `PublicEventDescriptionTests` | Unchanged |
| 7 | Listed on the site, searchable | **Partly built** | `/events` lists it; search is by location only (`api/public/search/nearby`); no keyword search on `/events` | **For Ben** (keyword search) |
| 8 | Appears in the map on the home page | **Partly built** | Home map (`PublicCaseDiscovery`) shows cases only; `NearbyDiscovery` events tab is deliberately list-only | **For Ben** |
| 9 | Newsletter to people looking for this type of event ("if we decide") | **Missing** | No interest/notify-me model | **For Ben** (his "if we decide") |
| 10 | People contact Jim on the app, by email or phone if specified | **Partly built** | Free-text `ContactLine`, group's public email/phone, the ask-for-a-place note; no message-the-organizer | **For Ben** |
| 11 | They request tickets on the site; Jim approves once paid | **Built** | `POST api/public/hosted-events/{id}/bookings`, board confirm; no "paid" flag by design (confirming is the "paid" act); iOS sends guests to the website to ask | Unchanged |
| 12 | Or Jim can generate tickets on the site | **Partly built (broken)** | "Book somebody in" sent no account and the server refused every one; on-behalf "confirm now" issued no pass and sent no letter | **Fixed**: **Send somebody a ticket** (invitation marked `ConfirmsOnAccept`; opening the link confirms the place and sends the pass); `CreateOnBehalf` with confirm now issues the pass and the letter |
| 13 | Assigned seating / seat chart designer | **Built** (assignment partly) | `/layout` designer, guest seat picker; the decision sheet kept one choice **per night**, so a 3-seat party confirmed into one seat | **Fixed** in `BookingDecisionSheet` (one picker per place held). Server per-unit capacity check with a multi-seat party: **For Ben** to confirm on the seeded seats event |
| 14 | Or a hotel room for a sleeping event | **Built** | Rooms layout, per-night `HostedEventBookingNight`, sheet room pickers | Same per-night fix as 13 |
| 15 | Tickets delivered via the site, the iPhone app, or email | **Built** | `/my-events/{id}/pass` (`MyEventPass`), iOS `EventPassView`, confirmation email with inline QR + hosted PNG; `HostedEventPassTests`, `EventPassTests` | Unchanged |
| 16 | QR code Jim scans as people arrive, to track who attended | **Built** | `/door` (`OrgEventDoor`, `BenQrScanner`), `HostedEventCheckIn` per night, iOS `DoorView`; `EventDoorTests` | Unchanged |
| 17 | Sell tickets at the door; site gives a QR to print or display | **Missing** | Walk-up head count only; no QR, no poster | **Fixed**: `HostedEventDoorTicketsController`; door card with QR, `/organizations/{OrgId}/events/{EventId}/door-tickets/poster`, public `/door-tickets/{Token}`; the door confirms door requests ("Paid — confirm") |
| 18 | Catered; creates the menu on the site; shown in the app | **Built** | Menu builder (`/menu-builder`), `EventMenuDocument`, iOS `MenusView`/`DesignedMenuView`; `EventMenuBuilderTests` | See the menu table |
| 19 | Another menu for a snack table | **Built** | "Throughout the event" menus | Unchanged |
| 20 | Gives his nephew permission to accept tickets by scanning | **Built** | Staff invite with **Run the door** (`HostedEventStaff.RunsTheDoor`), iOS "Doors I'm running"; accept page linked to "your groups", which a non-member doesn't have | **Fixed**: accept page opens **the door** for a door helper |
| 21 | Two speakers; a timed program | **Built** | `HostedEventSession` (`LedBy` is the speaker), `/sessions`, guest `HostedEventProgramme`, iOS `ProgrammeView`; `HostedEventProgrammeTests` | Unchanged |
| 22 | Classes with limits on who can attend | **Built** | `Capacity`, `RequiresSignUp`, waiting list with promotion | Unchanged |
| 23 | …and track who attends | **Missing** | No attendance per class | **Fixed**: `AttendedUtc`, `PUT …/sessions/{id}/sign-ups/{signUpId}/attended`, **There** on the roster, count of people there; the door may mark and read the roster |
| 24 | "Time To Investigate" button notifies everyone; the app submits evidence | **Partly built** | iOS-only launch (item 252), text "{title} is starting / {name} started the group's session"; staff needed door rights to launch | **Fixed**: **Start the sessions** on the event page and door on the website; plain notice "Sessions are now being accepted for this event"; `HostedEventStaff.StartsSessions` lets a guide start them without the door |
| 25 | Thank-you to those who came | **Partly built** | `HostedEventThankYouJob` thanked every confirmed booking, whether or not the door saw them | **Fixed**: thank-you goes to checked-in parties (everyone when the door recorded nobody) |
| 26 | "We missed you" to those unable to attend | **Missing** | No letter | **Fixed**: `EventGuestMailer.SendMissedYouAsync`, `MailKinds.EventMissedYou`, own note and switch |
| 27 | Letter says evidence in about a week, accepting for three days | **Missing** | No window anywhere | **Fixed**: `EvidenceDays`, `ResultsInDays`, `HostedEventEvidence.WindowSentence` in both letters and on the event page |
| 28 | (Accepting evidence for three days) | **Missing** | No deadline; also confirmed hosted guests were refused ("Evidence can be offered by people who attended this event") | **Fixed**: deadline enforced in `EventEvidenceController.Submit`; confirmed bookers and named guests may submit |
| 29 | Jim picks which evidence to include | **Built** | Evidence queue, accept/decline (`EventEvidenceQueue`, `PUT …/evidence-submissions/{id}/review`) | Unchanged |
| 30 | He creates a page using a CMS-type builder | **Partly built** | CMS had event programme/booking/gallery/venue sections, none for evidence | **Fixed**: `CmsSectionType.EventEvidence` ("An event's evidence"), resolved live and ranked |
| 31 | Final email with a link to the evidence page and a QR code | **Missing** | Only the pass letter carried a QR | **Fixed**: `HostedEventResultsController.SendResults` (`POST …/after/results`), inline QR + `GET api/public/hosted-events/{id}/results-qr.png`, `MailKinds.EventResults`, once per party |
| 32 | They could see the event results in the app | **Missing** | No iOS results view | **Fixed**: `GET api/public/hosted-events/{id}/results`; iOS `EventResultsView` from the event hub |
| 33 | On the site, the sessions run in the session player | **Partly built** | Field sessions sent to the event play for the team (`GroupFieldSessions` → `/field-sessions/{id}`); guests and the public can't reach them unless a session is published to a place | **For Ben** |
| 34 | Any EVPs will be displayed | **Partly built** | Session markers only in the player; the EVP detector runs in the audio editor | **For Ben** (with 33) |
| 35 | He could ask people to vote for the best evidence | **Partly built** | `EvidenceVote` + widget existed, not on the event's evidence list | **Fixed**: `HostedEvent.AsksForVotes`; vote widget on the event page's list |
| 36 | Ranked by positive votes, then by time of the evidence | **Missing** | Listed oldest-submitted first | **Fixed**: `EventEvidenceController.RankedAcceptedAsync` — "confirms" votes, then when caught (`UploadFileMetadata.CapturedAtUtc`, else when offered); used by the page, the CMS section and the app |

## Menu rules

| Rule | Before | Where / what was missing | Now |
|---|---|---|---|
| Saved but not shown until published | **Built** | `EventMenu.PublishedUtc`, `PublicEventMenuController.PublishedAsync`; `EventMenuRulesTests` | Unchanged |
| No meal until chosen; titles for the planner only | **Partly built** | The planner's `Title` went out in the public JSON | **Fixed**: guests are sent the heading in its place |
| Categories site-wide; planner adds unique ones, reused later | **Built** | `MenuCategory.NameKey` unique, `FindOrAddCategoryAsync` | Unchanged |
| Item fields: name, description, image, category, allergens, vegan | **Built** | `MenuLibraryItem` (dietary flags, nine allergens, note) | Unchanged |
| Categories as sections, items under them | **Built** | `EventMenuSection` / `EventMenuEntry` | Unchanged |
| Price mode per item: in the meal / covered by the event / individual (price required) | **Built** | `MenuPricing`, `WhyItCannotBeSaved` | Unchanged |
| Menu-level price tiers (children, seniors, first responders) | **Built** (free text) | `EventMenuPrice` labels typed by hand | **Improved**: one-tap **+ Adults / Children / Seniors / First responders / Military** |
| Covered-by-event noted in the ticket information | **Partly built** | Only beside the booking card on the event page | **Fixed**: also in the confirmation letter (`EventMenuPresentation.IncludedWithTheEvent`, shared) |
| Extras like alcohol, designated when added | **Built** (as "priced on its own" + note) | No separate "extra" or 21+ mark | **For Ben** |
| Reorder categories and items | **Built** | Up/down buttons in `EventMenuEditor` | Unchanged |
| Per-date assignment; reusable | **Built** | `EventMenuServing` many-to-many, throughout-the-event | Unchanged |
| Copy as "Name Copy 1, Copy 2…" | **Built** | `EventMenus.CopyTitle` | Unchanged |
| Several published menus per meal | **Built** | No limit per meal | Unchanged |
| Appears on the event's calendar | **Built** on the event page's dates list | Not on the group's own calendar or the .ics | **For Ben** (which calendar he means) |
| PDF | **Built** | `EventMenuPdf` (Telerik) | Credit mark added |
| Designed looks incl. ghost-hunting themes | **Built** | 25 designs (13 ghost hunting) | Unchanged |
| Tiny footer credit "IsHaunted.com © {year} ISHAUNTED LLC" (+ logo) | **Partly built** | Text only; the branch notes promised the site mark, and no design drew it | **Fixed**: small site mark beside the credit on the web, the PDF and iOS. Wording kept as "Menu made with IsHaunted.com · © {year} ISHAUNTED LLC" |
| Logo or image on the menu | **Built** | `LogoUploadFileId` | Unchanged |

## Ben's earlier notes (10/20/2026)

| Note | Before | Now |
|---|---|---|
| Messaging among people checked in to the same event or tour | Hosted events: the room, open to **confirmed** guests (not check-in), no badges. Tours: nothing | **Tours**: a walk's room (`TourRoom`, `api/public/tour-dates/{dateId}/room`, `TourDateRoom.razor`, iOS `TourRoomView`) for checked-in guests and the guides. **Hosted**: unchanged membership — see For Ben |
| Organizer, guide and lead messages look different | No role on a room post | **Fixed**: `AuthorRole` ("Organizer", the helper's role, "Venue"; "Guide" on a walk) — badge and colored edge on the web and iOS |
| Start-of-sessions message, plain; delegable | See row 24 | **Fixed** (row 24) |

## Tests

- **Unit/API** (`Ben.Web.Tests/Services/EventScenario/`, `Website/EventScenarioScreensTests.cs`, additions to
  `FieldLaunchServiceTests`, `EventMenuRulesTests`, `HelpPromisedEventControlsTests`): 51 new or changed. Each fix
  was undone in turn and its test failed (attendance at the evidence door, ranking, on-behalf pass, the
  missed-you split, the planner's title, the delegated launch).
- **Browser** (`Ben.Web.Playwright/Tests/EventScenarioTests.cs`, category `EventScenario`): written, not run.
- **iOS**: `BenKitTests/EventScenarioTests.swift`. The two fixtures (`hosted-results.json`, `tour-room.json`) are
  written from the API's records because the endpoints are new; capture them from a running API next time.

## For Ben

See `README-event-scenario-gaps.md` at the root.
