# fix/oct16-gaps: the gaps found while writing 10/16/2026's e2e tests

Branched from `integrate/oct16-fixes` at b49ab9d5, 10/16/2026. Read with the READMEs for
viewer-time-zone-saves, investigation-team-and-rsvp, event-place-search, public-event-page,
home-event-countdown and visual-polish, which this builds on.

Ben: "write any missing tests and watch for gaps and visual issues", "fix any issues discovered
along the way", and "make sure you account for all screen types when fixing gaps and visual
changes".

## The gaps and what changed

| # | Gap | Fix |
|---|---|---|
| 1 | A visit with no case could not be edited, rescheduled, canceled or deleted, so visits saved an hour late before the time-zone fix stayed wrong. | `PUT`, `POST {id}/cancel` and `DELETE` on `OrgInvestigationsController` (below). The grid shows **Edit** on every row the server says you may change; a case-less row opens `NewInvestigationWindow` in edit mode (`EditInvestigationId`), with **Save changes**, **Cancel this visit** and **Delete**, each asked inline. |
| 2 | "Times are in your time zone (CDT)" used today's date. | `InvestigationWindow.ViewerLetters(start, viewer, utcNow)`: the letters at the typed start, updating as it changes. |
| 3 | Nobody could take an invitee off a private calendar event. | Save takes off the members whose box was unticked (`OrgScheduler.UntickedInvitees`); Who's coming has **Remove** on every row, asked inline. `RemoveCalendarAttendeeAsync` now returns `(Removed, Error)`. |
| 4 | Who's coming offered only "Mark as coming". | `OrgScheduler.AnswersFor`: the two other sensible answers per state (coming, not coming, not answered); only the pressed button says "Saving…". |
| 5 | The team ✕ removed somebody in one click and dropped their duties silently. | Inline question: "Take James off the team? Their duty (Lead) goes too." (`InvestigationRoster.RemovalQuestion`), plus a line when they lead the visit. |
| 6 | Check-in and Attended hid the server's reason. | Both adapter calls return `(Entry, Error)` through `SendExpectingReasonAsync`; a refused Attended box is redrawn from what is saved. |
| 7 | Case page attendee ✕ had no name; "Add member" label was not tied to its select. | `title` + `aria-label`; `for` / `Id` per visit. |
| 8 | Inputs without ids or labels. | `calendar-place-search`, `venue-search`, `attend-email`, `attend-name`, each with `<label for>`. |
| 9 | Enter did not search; placeholders differed. | `@onkeydown` with `PlaceSearch.IsSearchKey`; one `PlaceSearch.Placeholder`: "Part of the name, or the town". None of the boxes is inside a form, so Enter submits nothing else. |
| 10 | "Schedule an investigation" only had the exact-name matcher. | For a public location, the typed name also runs `SearchPublicPlacesAsync`, listed under **Places we already have**. The duplicate check stays. Never for a private residence. |
| 11 | Unreachable "That is a private residence" warnings. | Removed from `OrgEvents` and `OrgEventPage`. The path that can still bring a residence (a typed venue matched on save to a home on file) is refused by `HostedEventController.ValidateAsync`, shown in the page's alert; now covered by a test. |
| 12 | No settings index link for `#settings-venue`. | `JumpLink.To(Nav, "settings-venue")` "Your venue"; a test checks every `settings-*` section has a link. |
| 13 | At 375px the investigations table scrolled sideways and the Team panel was half off screen. | See "Screens" below. |
| 14 | Who's coming at 375px squeezed long names beside the button. | A subgrid list: name, answer, buttons on one line from 576px; below that the buttons get their own line. |
| 15 | The Schedule window defaulted to this minute tomorrow ("6:32 AM"). | `InvestigationWindow.DefaultStart`: 7:00 PM tomorrow on the viewer's clock (`UsualStart`). No group setting for a usual start exists; hosted events default to 7:00 PM per event (`HostedEventCalendarSync.DefaultStart`), so the same hour is used. |
| 16 | An event page's Starts tile lost "7:00 PM CDT" on a phone and "Sun 10/25" wrapped. | Cause: `PageHero.razor.css` hid every fact's sub-line below 576px (`dd span { display: none }`), and its tiles were `minmax(4.5rem, 1fr)`. Now the sub-line shows at .7rem and tiles are at least 6.25rem, wrapping only at spaces. `BenFactRail.razor.css` used `overflow-wrap: anywhere`, which lets "Sun 10/25" split mid-word; now `break-word`. (BenFactRail itself is not used on any page today; every hero, including the event, tour, What's On, venue and org pages, draws its facts through `PageHero`.) |

## Changing a visit with no case: the API

All three on `api/organizations/{orgId}/investigations/{id}`, matched on the visit's own
`OrganizationId`, gated like the team doors: Investigation Read in the group, not a Viewer, then
`InvestigationAccess.CanManageAsync`. A visit with a case gets a 409 with "This visit belongs to a
case, so it's changed from the case's Investigations tab." (that door also mails the client).

- **PUT** `UpdateOrgInvestigationRequest(Title, ScheduledDateTime, EndDateTime, Description, PlaceId,
  NewPlace, Visibility)`. Same checks as the case-bound PUT: title, end after start, placement
  (`InvestigationPlacement`), sharing scope against place and plan, public slug. Neither place field
  keeps the place it has. Audited with before and after. 204.
- **POST cancel**: only a Scheduled visit, with a sentence for each other state. Audited. 204.
- **DELETE**: refused with a sentence when the visit has recordings, written accounts or anybody
  marked as there ("This visit has 1 recording, 1 written account and 1 person marked as there, and
  deleting it would lose them. Cancel it instead. A canceled visit keeps them on record."). Audited.
  204.

`Services/Investigations/InvestigationLifecycle.cs` holds what both controllers share:

- `SyncCalendarAsync`: the visit's own calendar entry (private, no hosted event or tour, title
  "Investigation: …") follows its title, times, description and zone; a canceled visit's reads
  "Canceled: Investigation: …". Entries somebody made by hand are left alone.
- `WhyCannotDeleteAsync`: the refusal above.
- `PrepareDeleteAsync`: duty assignments first (NoAction key; SQL Server refused the delete for any
  visit where somebody held a duty), binder entries and equipment checkouts detached, shares to the
  visit removed, and the visit's own calendar entry removed when nothing else hangs on it.

### Found along the way, on the case page's door

- `InvestigationController.Update` set `OrgCalendarEventId = request.OrgCalendarEventId`, and the case
  page always sends null, so **every edit on the case page unlinked the visit from its calendar
  entry**, which then sat at the old time. Null now leaves the link alone, and the entry is synced.
- Cancel there now marks the calendar entry canceled; Delete uses `PrepareDeleteAsync`, so it no
  longer fails in SQL Server when somebody held a duty, and takes the visit's own calendar entry.
- Not changed: the case-bound delete still allows deleting a visit with recordings (they cascade)
  unless a report cites them. Making it refuse like the case-less door would change a long-standing
  behavior and e2e tests this branch cannot run; Ben's call.

## Screens (gaps 13, 14, 16)

Bootstrap's edges are 576/768/992/1200/1400. Every rule below was checked at its edge (575.98 vs 576,
767.98 vs 768, 991.98 vs 992).

**Investigations list** (`OrgInvestigations.razor.css`), the site's established reflow
(`PlaceRoomsManager.razor.css`: hidden header, `data-label` per cell):

| Width | Layout |
|---|---|
| 320–575 (SE, 375/390/430 phones, a folded Duo) | one card per visit, label left / value right, title first, buttons on their own line |
| 576–991 (phone landscape, iPad 768/820 portrait, open Duo ~951) | cards whose cells flow into columns of at least 13rem; title and buttons span the card |
| 992+ (iPad Pro 1024/1180 landscape, 1280/1440/1920) | the table as before |

Cards start below lg, not md: the When cell and the action buttons do not wrap, so at 768–991 the
table was wider than the screen too. Separately, at every width the Team panel is held to the list's
visible width (`container-type: inline-size` on the list, `max-width: calc(100cqi - .5rem)` and
`position: sticky; left: 0` on `.inv-team-panel`), so even a table that scrolls sideways on a laptop
cannot carry the panel off screen. At 1920 nothing stretches further than it did.

**Team panel** (`InvestigationRoster.razor.css`): the manage table is one card per person below 768
and a table from 768 (five short columns fit an iPad portrait). The picker never exceeds the panel.

**Who's coming** (`OrgScheduler.razor.css`): one line per guest from 576 (the dialog is at most the lg
modal's 800px, so wide screens don't stretch it); below 576 the buttons drop to their own line,
right-aligned.

**Hero facts** (`PageHero.razor.css`): see gap 16. 320: two tiles to a row; 375–430: three where they
fit, else two; 576+: the 2×2 grid as before.

**Touch targets:** the new and changed controls (✕, Take them off / Keep them, Add and its picker,
Mark as …, Remove, Edit, the window's footer buttons) are at least 44px under
`(max-width: 991.98px), (pointer: coarse)`, so phones, tablets in both orientations and the Duo get
them and a desktop mouse at 1280+ does not.

**Themes:** every color in the new CSS is a `--bs-*` variable, so dark and light both follow
`data-bs-theme`. Nothing new moves.

## Tests

- `Controllers/CaselessInvestigationChangeTests.cs` (20): change, calendar follows, audit with before
  and after, place changed and kept, end before start, scope against plan, non-manager, Viewer, another
  group's visit, case-bound refused; cancel (status, calendar title, audit), already canceled,
  non-manager; delete (duties and calendar entry go, audit), refused with recordings/findings/
  attendance and the exact sentence, attendance alone, non-manager, case-bound; the case page's edit
  keeps and moves the calendar entry; the case page's delete clears duties.
- `Services/Oct16GapsAdapterTests.cs` (6): check-in, Attended, change, refused change, refused
  delete and cancel, removing a calendar guest all keep the server's sentence.
- `Website/Oct16GapsTests.cs` (36 with theory rows): zone letters at the start, the 7:00 PM default
  (including the night before the clocks go back), the removal question, the ✕ asks, check-in and
  Attended sentences, answers per RSVP state, unticking, Remove, Enter and the three search boxes,
  the labeled inputs, the case page's ✕ and Add member, no residence warnings, the settings index,
  the investigations list and roster cells carry `data-label`, the hero keeps its sub-line.
- `Controllers/HostedEventControllerTests`: a typed venue matching a home on file is refused in words
  (gap 11's remaining path; this passed before the branch, as it covers an existing refusal).
- **Discrimination**, by putting each fix back one at a time and running its tests
  (scratchpad `mutate.py`): the case edit's calendar link, the delete refusal, duties cleared on
  delete, the calendar sync, the guest answers, unticking, the zone letters, the default start, the
  removal question, and the two case-bound refusals each failed. The source tests fail on the files as
  they were (they look for the strings that were there).
- `Ben.Web.Tests`: 8388 passed, 1 failed (the known `CanvasDeployScriptGuardTests`, an untracked
  appsettings file a worktree lacks), 6 skipped.

## Not done

- No hosts were started and Playwright was not run (the day's e2e tests are uncommitted in the main
  checkout). The e2e checks to add are listed in the report for this branch.
- `docs/ishaunted-documentation.html` was regenerated; `docs/IsHaunted-Product-Documentation.pdf`
  still needs printing (the headless-Chrome line in `docs/README.md`; the sandbox here refused it).
- Help screenshots of the investigations list, the Team panel and the calendar editor are unchanged.
