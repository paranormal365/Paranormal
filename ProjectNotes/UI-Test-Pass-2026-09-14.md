# Visual UI test pass — 2026-09-14

Ben: "use the mouse drag and run through testing the site … as much as you can think of different ways while keeping
a list of things that need to be fixed. Work through it like layers. From outer to inner workings. When you finish a
layer, stop to fix any bugs or issues found before starting the next inner layer."

**Environment:** local hosts built from `feature/beta-feedback-1` (api :5252, web :5078, wasm :5180) on the isolated
`IsHauntedDb_e2e` database. Not UAT: ishaunted.com does not carry this branch, and testing there would exercise old code
against shared data.

**Method:** the in-app browser, driven by sight — a screenshot before and after each action, clicks and mouse drags at
what is on screen. Desktop 1280 wide, phone 375, tablet 768; dark and light.

Each finding: what was done, what was seen, why it is wrong, and — once fixed — the commit.

## Layer 1 — The public shell, signed out

Walked: home (hero, near you, feed teaser, case map and cards), a public case and voting signed out, find a group,
pricing and its buy button, feed, what's on, equipment catalog, publications, the investigation request wizard to its
account step, help, what's new, contact, sign-in and sign-up — at 1280 and 375, dark and light.

- [x] **1.1 The home page's feed teaser and promoted-group card sat in a narrower, centred column** (860px) beside
  sections 1000px wide from the left edge, so their edges did not line up. → same 1000px column.
- [x] **1.2 Two city boxes one after the other on the home page** (the hero's Find Groups and What's Near You).
  → *Not changed: a design question for Ben* — they do different things (find groups / show events and places near).
- [x] **1.3 The sign-in page's browser tab said "Login"** while the page says "Sign In". → "Sign in".
- [x] **1.4 In dark mode, outline-primary buttons read as disabled** — "View" on the home page's case cards, "Find a
  group near you" on the feed: blue text at ~3:1 on the dark card beside a white "Group" button. app.css documents this
  as a deliberate palette choice below WCAG AA. → Ben: "whatever is best". Each outline colour's text is lifted to
  4.5:1 on both the card and the page (primary, info, danger, success each by its own amount).
- [x] **1.5 Seeded public events showed "08:00 PM UTC"** for a Tennessee walk: the development seed gave them no time
  zone. → seeded in America/Chicago at the intended local hours (fresh databases).
- [x] **1.6 The request form's example values read as entries** ("TN", "37201", "123 Main St", "Apt 4B") — I skipped
  State myself — and "Fill in the street, city, state and ZIP first." did not say which was missing. → placeholders
  start "e.g.", and the sentence names only the missing fields ("Fill in the state and ZIP code first.").
- [x] **1.7 "Gender" had no "(optional)"** beside "Birth Year (optional)". → added.
- [x] **1.8 Step 4 greeted the person with a red "Select at least one organization."** before they had done anything.
  → grey guidance: "Choose at least one organization to send your request to."
- [x] **1.9 On a phone the home map centres on the middle of the US**; the only pins sit at its right edge.
  → Ben: "best judgement". The first view centres on the middle of the pins at the same zoom (unless they span more than
  a continent); the person's own location still wins.
- [x] **1.14 In light mode the sign-in card's logo was a pale ghost on a white card.** → the navigation's colour behind it.
- [x] **1.10 In light mode on a phone the header's logo nearly vanished** (pale mark on the white header). → the mark
  gets the navigation's colour behind it below 992px, as it has beside the sidebar on wide screens.
- [x] **1.11 In light mode the sidebar's "Filter" placeholder was dark grey on the dark sidebar.** → light placeholder.
- [x] **1.12 Sign-up's example values were a seeded member's name** ("Sarah Mitchell", "@sarahmitchell") and read as
  entries. → "e.g. Casey Miller".
- [x] **1.13 Editing a verified address kept "✓ Address verified" and Next enabled** — the request would have gone to the
  groups near the old address. A lookup that found nothing also kept the old place. → any edit to street, city, state or
  ZIP clears the verification; "not found" clears the place.

Tool notes (not site defects): the browser tool's Backspace does not reach inputs; feed posts on the e2e database are
test posts with 8×8 grey pictures.

## Layer 2 — The signed-in shell

Walked as the SuperAdmin (Ben's seat), light then dark: sign-in, the desk, the bell and its drawer, notifications (mark
all read, opening a message), the profile menu and profile tabs, My investigations / cases / requests / evidence,
equipment, checkouts, upload files, field sessions, videos, media library, organizations (grid actions, More actions).

- [x] **2.1 The "work waiting" banners' links were blue on purple** in light mode (~2.4:1). → Bootstrap's alert-link.
- [x] **2.2 The desk said "All 0 upcoming"** with nothing scheduled. → "My investigations" when there are none.
- [x] **2.3 The bell's "99+" showed as "9…"** — the template caps icon badges at 28px with an ellipsis. → no cap on the bell.
- [x] **2.4 The desk said "5 unread messages" beside the bell's "Unread messages 99+"** — the desk counts group messages,
  the bell's row the platform's. → "unread group messages".
- [x] **2.5 Opening a notification looked like nothing happened**: the message opens below a list of 100+. → the page
  scrolls to it.
- [x] **2.6 The desk's date was the server's clock**, not the viewer's. → the viewer's.
- [x] **2.7 "My Cases" told a member with two open group cases "You have no active cases"** — it is the client's view.
  → a line under the heading says whose cases these are and where a member's are.
- [x] **2.8 Organizations grid: the More actions button had no icon** (an invisible button beside View), **and its menu
  opened clipped by the grid** — a sliver, its nine choices unreachable. Same button on the CMS pages grid. → an icon
  button like the other row actions, and BenDropdown's new Floating mode places the open menu against the window.
- [x] **2.9 Signed in, "Public Investigations" lists cases you can open that are not public** (by design) **with nothing
  saying so** — a member's private case looked published. → such cards carry a "Not public" badge (API: additive
  `IsPublic` on the discovery item).

- [x] **2.10 On a phone the footer did not wrap**: "Terms" sat past the right edge. → wraps, copyright on its own line.

Seen and fine: tooltips on grid icons; sign-out asks first; mark-all-read updates the bell and sidebar; profile map (pin below the fold).

## Layer 3 — A group's hub

Walked Paranormal365 as the SuperAdmin: every hub tab (Details, Members, Cases, Investigations, Calendar, Messages, Files,
Requests, Clients, CMS, Publications, Equipment, Roles, Addresses, Settings), Pending Requests, Billing — 1280 and 768.

- [x] **3.1 Three of the hub's fifteen tabs could not be reached with a mouse wheel.** The row scrolls sideways with its
  bar hidden: nothing said it went on past "Equipment", and a vertical wheel does not scroll sideways, so Roles,
  Addresses and Settings were keyboard-only on an ordinary Windows mouse; a ?tab=settings link opened on a tab off the
  edge. → BenTabs fades the edge that has more, turns the wheel into a sideways scroll over the row, and brings the
  chosen tab into view.
- [x] **3.2 Every case on the Cases tab said "Manager: Unassigned"** — the list query did not load the manager the name is
  mapped from (W-A9 fixed the same thing for Update). → included; `GetAll_NamesEachCasesManager`, seen failing first.
- [x] **3.3 Members grids printed "True"** in Active (hub members, admin user detail ×2). → Yes/No badges, as Roles does.
- [x] **3.4 The Clients tab's switch had no label** — only its grey consequence beside it. → "Taking new cases", with the
  consequence beneath.
- [x] **3.5 Billing said "a group with no plan is charged nothing"** under "Current plan: Small group · Active".
  → that sentence only for a group with no plan; otherwise "No charges or payments recorded yet."
- [x] **3.6 Pending Requests' "View more…" was inside the 3em box that clips the excerpt** — cut off with the text.
  → a button below the excerpt, and "Show less" when expanded.

Seen and fine: investigations map (pins arrive a moment after the tiles), calendar, messages, files, publications,
equipment, settings links, the tablet layout.

## Layer 4 — A case

Walked the Belmont case as the SuperAdmin: overview, timeline (edit an entry), investigations, files, transfers, messages
(a formatted message with a link), reports, notes (a formatted note), Edit Case, New Case.

- [x] **4.1 A column of stray marks ran between timeline cards**, like text cursors: the template draws a 2px rule down
  `.timeline::before`, and the case timeline used that class. → its own class.
- [x] **4.2 Timeline times were five hours out in the dialog.** It filled the picker with the stored UTC and saved what
  was typed as UTC while the cards show local time: an entry typed as 9:00 PM showed on its card as 4:00 PM. → filled and
  saved in the viewer's time.
- [x] **4.3 Add Entry opened an empty date box** (an empty Telerik date field rewrites itself — 2026-09-09). → starts at
  the current minute; still optional.
- [x] **4.4 The client's "Suggest a different date" sent local time as UTC** (arriving five hours early on the group's
  panel), from an empty date box. → sent in UTC, and it opens on tomorrow at 7 PM.
- [ ] **4.5 Clicking B and typing at once is not bold**: toolbar tools are a server round trip in the Telerik editor, and
  keys typed inside it go in unformatted. With a pause it works; Ctrl+B is instant. → *Not changed* (Telerik's).
- [x] **4.6 Bold in what people write was all but invisible**: the template sets b/strong to weight 500 beside 400 text —
  notes, messages, timeline entries, descriptions, research pages, and the editor while typing. → 700 inside running
  text (p, li, blockquote, the editor).
- [x] **4.7 After Send, the message you had written was below the fold** — the thread opens at its oldest and never
  scrolls, and a link card grows the new message after it lands. → the thread opens at its newest, goes there on Send,
  and stays there while content arrives unless the reader has scrolled up.
- [x] **4.8 "Date Proposals to Client" looked broken**: a loose calendar icon above a grey caption, with no sign it opens.
  → drawn as a small button.
- [x] **4.9 New Case suggested "e.g. Smith, Nashville TN" and Edit Case said "(surname, city state)"** — teaching the
  client-surname title the publish check warns about. → "e.g. Belmont Boulevard house, Nashville TN", "(the place, not
  the client's name)". New Case's State example starts "e.g." too.

Seen and fine: notes save formatted; the composer's link card appears once the address is finished and stays on the
sent message; transfers; the case header.

## Layer 5 — Research pages and the block editor

Walked a research page as the SuperAdmin and read it as James: the title and date, text blocks (typing, bold, lists,
paste), the Add bar (text, picture, file, link, map), the block handle and its menu, mouse drags, Files & links (Insert,
Remove), captions, link cards, map places and routes, Save now, Publish, leaving, two tabs — at 1280 and 375. Twenty-one new
browser tests (`CaseResearchEditorTests`) do each of these the way a person would, and found most of what follows first.

- [x] **5.1 Pasting formatted text onto the page made an empty text block.** The new block's editor did not exist yet when
  the words were handed to it, so they went nowhere. → the paste waits for the new block's editor to appear and is then
  pasted into it as the browser would, so the editor's own clean-up runs.
- [x] **5.2 Words typed straight after clicking back into an earlier text block were lost.** Clicking a drawn block opened
  its editor but left the keyboard on the page, so typing went nowhere. → the editor that opens takes the keyboard.
- [x] **5.3 Clicking into a drawn text block put the cursor at the start**, not where the click was. A first fix took the
  click's position on the screen and found the same spot in the editor, and by hand it still landed at the start: the
  editor draws the same words 14px further in, under a toolbar, so the spot was above the first line. → the click is
  counted in characters into the words, and the editor's cursor goes after the same character — mid-sentence, or at the
  end when the click was past the words.
- [x] **5.4 The bulleted-list tool** — the button was fine ("Insert unordered list"); the test typed before the list
  appeared, the same round trip as 4.5. → test waits for the bullet, as a person does. *Test fix, not a site defect.*
- [x] **5.7 Every block had two 44px buttons down its left side** — a drag handle and, below it, a "⋯" menu button that only
  appeared on hover (so a touch screen never showed it without a tap first) — which made short blocks tall and gave the
  page a double gutter. → one handle: drag it to move, click or tap it for the menu, Alt+Arrow to move from the keyboard.
- [x] **5.12 Members could not see pictures or files on a research page.** File access rules covered case files, timeline
  attachments and published videos, not research: every picture on a published research page was a broken image to the
  group except the person who uploaded it, and so were the older "File" research entries. → research attachments follow the
  Research tab's rule (the group's once published; the author's alone before), and older File entries are the group's.
  `FileAudienceAccessTests`, seen failing first.
- [x] **5.13 Pasted formatted text brought the code inside it along as words.** A `<script>` in the pasted HTML came out as
  a paragraph reading its code, and so would the stylesheet Word puts on the clipboard. The editor was told to strip
  script and style on paste, and Telerik's stripping removes only the tags and keeps what is between them — its
  documentation says the opposite. → those tags are left to the editor's own parser, which drops them whole.
- [x] **5.14 Letters typed straight after clicking into a paragraph were lost.** The editor opens after a trip to the
  server; keys pressed before it arrived went nowhere — every one of " in 1921" in the test, and more on a slow connection.
  → keys typed in that moment are held and given to the editor when it is ready. Plain characters and Backspace only; a
  shortcut, Enter or an arrow ends the hold. `WordsTypedTheMomentABlockIsClicked_AreKept`, seen failing with the hold off.
- [x] **Test fix: "saved" was read before the save.** The Save helper waited for the status to read Clean, which a page
  already saved once reads before the new save starts; the two-tabs test then closed its second tab mid-save and blamed the
  first. → the save status counts finished saves (`data-flushes`) and the helper waits for the count to move.
- [ ] **4.5 again: click B then type at once is not bold.** Telerik's toolbar is a server round trip. → *Not changed*;
  answered by the next research phase (item 241: the editor runs in the browser).

Measured and fine: a block move, menu open and text-block open each answered in 3–5 ms locally; words typed quickly are all
kept (an earlier loss was the browser tool's own typing).

## Layer 6 — Inner workings: seats, persistence, refusals, dragging

Ben, again: "use the mouse drag … Dont worry about the research editor. I am working on it in another project." Research
pages are left out from here. Walked: the group calendar (drag, resize, the ✕, the editor's Delete), signing in from deep
pages, the surfaces a mouse can drag (calendar, event floor plan, seat picker, audio mix, waveform regions).

- [x] **6.1 Signing in from a deep page landed on the home page.** A group's calendar opened signed out went to a bare
  sign-in page, and so did the header's Sign In from anywhere — seven pages sent people to `/login` with no way back.
  → one `SignInLink` builds the sign-in address with the page as a relative returnUrl; the header's link follows the
  person round the site. `SignInReturnsTests`, `SignInLinkTests`.
- [x] **6.2 The calendar's evidence box put Decline on its own line**, away from its reason box, with Accept beside it.
  → *cosmetic, noted.*
- [x] **6.3 Dragging an event on the calendar did nothing.** Updates were allowed, so an event could be picked up and
  dropped, but nothing handled the drop: it sprang back without a word. → a dropped or resized event is saved with every
  other detail as it was, and a line says where it went with Undo; a repeating event is refused in words (dragging one
  date would move the series); a server refusal is shown and the event stays put. The line and any refusal stick to the
  top of the scroll, so a drop far down the calendar is answered where it can be seen. `CalendarDragTests`.
- [x] **6.4 The ✕ on a calendar event deleted it on one click** — a public event with its sign-ups included — and the
  editor's Delete did the same; a server refusal ("archive the event there") was thrown away, the editor closed and the
  event stayed with nothing said. → both ask first, naming the event and its date (and that every repeat goes with a
  repeating one); a refusal is shown in its own words.
- [x] **6.5 The calendar, signed out, said so above a spinner that never stopped**, with no way to sign in. → no spinner
  once the load has failed; the sentence carries a Sign in link.
- [x] **6.6 Every restart of the local hosts signed the browser out.** The website keeps sign-ins in the browser through
  ProtectedLocalStorage (Data Protection), and on this Mac its default key folder `~/.aspnet` is owned by root, so its
  keys lived in memory and died with each restart. → *Not a site defect*: the production application pools load a user
  profile, where the keys persist (deploy runbook, "Why three application pools"). A shared key-ring setup for the
  website was tried and backed out — compiling the API's class into both hosts collides in the test project. Machine
  fix, for Ben: `sudo chown -R "$USER" ~/.aspnet`.
- [x] **6.7 Case voting, case comments, a hosted event's reviews, a tour page and polls sent people to sign in with the
  page's full address**, which the sign-in page's open-redirect guard refuses — so they too landed on the home page.
  → the same `SignInLink`; a guard fails the build if a sign-in link is built from the full address again.
- [x] **6.9 A group page's Back followed any returnUrl**, another website's included. → only a path on this site.

- [x] **6.10 Signed out, the events page said "Adding and changing events is for people who can manage the group's
  settings"** above "You've been signed out" — a permission sentence to somebody who is only signed out. → only to a
  signed-in person.
- [x] **6.11 The event pages' example values read as entries** ("Thomas House Weekend", "Meet at reception", "Mrs Cole,
  the manager" — fifteen of them), as 1.6 did. → each starts "e.g.". The rest of the site's placeholders were read one by one:
  twenty-five more were example values ("Room 217", "Jane Doe", "LAUNCH25", "123 Main St" on New Case…) and start "e.g."
  now; the others are instructions ("No limit", "Optional", "Search venues by name") or show what is used when a box is
  left blank (a tour letter's default subject), and stay as they are.
- [x] **6.12 A quick mouse sweep across a floor plan or seating plan skipped squares.** Mouse and pen paint a selection,
  and the square under the pointer was asked for only where each move landed; a fast sweep across four rooms chose
  three. The same component is the public seat picker. → every point along the line since the last move is asked for.
  `A_quick_mouse_sweep_chooses_every_seat_it_crosses`: 2 of 6 before, 6 of 6 after.
- [x] **6.13 "View as" showed the SuperAdmin's name and picture in the member's profile menu**, above the member's email
  address — the menu fetched them once and "view as" happens in the same visit (signing out and in as somebody else
  without reloading did the same). → the menu refetches when the person changes, and shows nothing of the last person
  meanwhile. `Impersonation_shows_their_world…` now opens the menu.
- [x] **6.14 A member's "work waiting: 1 investigation request" banner led to the group's Details.** The count is for
  anyone who can read the queue; the hub's Requests tab is an admin's, so `?tab=requests` fell back to Details, and his
  desk said "0 requests waiting on you" under it. → the banner opens the requests page, which every reader can use.
- [x] **6.15 The requests queue offered every member Accept and Decline**, which the server refuses without the
  client-requests grant — and Decline then took the request off the screen anyway, so it looked answered and came back on
  the next visit. → the buttons show only for whoever may decide, and a refused Decline says so and leaves the request.
  `A_members_request_banner_opens_a_queue…`.

- [x] **6.16 Signed out, the case audio mixer said "This case doesn't exist, or you don't have access to it."**
  → says the person is signed out, with a Sign in link that comes back to the mixer.
- [x] **6.17 The mixer's heading put its icon above the words.** → on one line.
- [x] **6.18 The mixer said "Add clips from the case's Files tab to get started"** beside a list of clips each with its
  own Add button. → "Press Add beside a clip to put it on a track" when the case has audio.

Measured and fine: a clip dragged along its lane lands at the time dropped on (80px → 10s); a moved event keeps its time
and length; the Delete question cancels cleanly; members may add, move
and delete calendar events (item 156 decided the calendar is member-open).


---

# Second pass — as other kinds of people

Ben, after the full suite passed: "run through testing the site visually again as a different type of person - except
for the research editor." Reached through the SuperAdmin's "view as", so no password is typed: Daniel Park (a client),
then James Thornton (an ordinary member), then Victor Reyes (a viewer). Same method — outer to inner, fix each layer
before the next.

Before starting, the full suite on the finished branch: 707 passed, 2 failed — both a research-test helper that, under
the whole suite's load, clicked the previous block's editor before the new block arrived (fixed; research set 45/45).

## Client pass — Daniel Park

Walked: Home, the sidebar, My Cases, his case (occurrence calendar, occurrences, shared access, people at the property,
messages with a bold word, the report PDF, the visit), My Requests (a draft through all four steps to Save as Draft, and
an accepted request), My Investigations.

- [x] **P.0 Signed out, a SuperAdmin page sent the person home**, not to sign in: a SuperAdmin whose session ended had to
  find the page again. Thirty-six SuperAdmin pages. → `SignInLink.ForRefusal`: signed out goes to sign in and back;
  anybody else still goes home.
- [x] **P.1 The grid's "Impersonate this user" tooltip stayed on the next page**, floating over the logo, after the row's
  button had navigated away. → the layout's delegated tooltip is keyed on the address, so a new page has a new tooltip.
- [x] **P.2 A client's Home said nothing about their case.** Clients get the visitor's hero (the member desk was group
  work — W-S5), which invited Daniel to "connect with groups near you" while his group had booked a visit. → a "Your
  case" strip above the hero: the case, its state, the next visit, a link. Nothing when there is no case.
- [x] **P.3 A "Not public" case card offered the public vote** (Daniel's own private case on the home page). The server
  already refused the vote. → no vote controls on a card the public cannot see; a line says who can see it.
- [x] **P.4 My Cases put the calendar icon on a line of its own** above "Next visit". → one line.
- [x] **P.5 My Cases told a client "The cases you work on as a member are on each group's Cases tab"** (my own layer 2
  line) as though he were a member. → "If you are also a member of a group…".
- [x] **P.6 The case's calendar said "9 occurrences" and marked no day**: finding the other days meant pressing every
  date. → a dot under each day with something logged.
- [x] **P.8 The main action on a draft request, Edit & Submit, and the case's Add Person were grey** and read as disabled.
  → primary and outline-primary.
- [x] **P.9 The report's PDF button did nothing visible** while the PDF was made, and nothing at all when it failed.
  → "Preparing…" with a spinner, and a sentence on failure. (The download itself works: 3.9 KB.)
- [x] **P.10 The downloaded report was named `report-Initial-Assessment-_-Belmont-Blvd-residence.pdf`** — the title's dash
  became "_" in the plain copy of the name a download reads. → names are plain letters, digits and hyphens.
- [x] **P.11 Asking again about an address already in hand gave no hint**: Daniel's draft for 4512 Belmont Blvd offered
  him the very group that had accepted his case there. → after Verify Address, a note names the earlier request and
  what became of it, with a link. Not a refusal.
- [x] **P.12 An accepted request's "View My Case" opened the client's first case**, not the one the request became — a
  client with two would land on the wrong one. → the case list carries its request id (additive API field), and the
  link uses it. `GetMyCases_ReturnsOnlyClientsCases` checks the id.
- [x] **P.13 My Investigations told a client "You have not been assigned to any investigations"** with a visit to his
  home booked. → a line says visits to your own property are on My Cases.

Seen and fine: sending a formatted message (bold kept, box cleared, thread at the newest); the four request steps keep a
draft's answers; Save as Draft returns to the list; the accepted request names its group under "Submitted To".

## Member pass — James Thornton

Walked Paranormal365 as James (a member: reads cases and investigations; no Files, Calendar or Clients grants; not an
admin): the hub (Details, Members, Cases, Investigations, Calendar, Messages, Files, Equipment), the Belmont case
(overview, points of contact, the publishing tour, timeline, messages, files and their recordings).

- [x] **M.1 "Choose contacts" was offered to every member** — ticking names and pressing Save met the server's refusal
  (case manager or group admin only). → the button for those two; a line saying who chooses for everyone else.
- [x] **M.2 The "How cases go public" tour was offered to members who cannot edit**, its first step pointing at an Edit
  Case button they do not have. → offered with Edit Case.
- [x] **M.3 Edit and Delete sat on every timeline entry** — the client's own report included — though only an entry's
  author or a group admin may change it; a refused Delete closed its question and left the entry without a word.
  → the buttons for the author or an admin; a refused Delete says so.
- [x] **M.4 A colleague's message to the client appeared unnamed on the group's side**, reading as the member's own.
  → a name on every message the viewer did not write.
- [x] **M.5 Recordings loaded as pale grey slabs on a dark page**: the loading overlay used a Kendo colour variable the
  theme does not define, and fell back to white. → Bootstrap's body colour.
- [x] **M.6 The hub's Files tab offered Upload, Share from User and Show Delete Log to a member with no Files permission**,
  and a "← Organizations" button inside the group's own hub. Row actions (publish, edit, delete) likewise.
  → each for whoever the server lets use it; a line saying who adds files; the back button only on the page of its own.
- [x] **M.7 Deleting a group file showed a red error that stayed until dismissed**: "Deleted. Audit record saved." → a
  success toast.
- [x] **M.8 A lone "?" at the right edge of Investigations** — the help beside Schedule an investigation, left behind when
  the button is not offered. → shown with the button.

Seen and fine: the Pending Requests button is the warning colour when something waits (the theme's yellow is olive); the
member's Members, Cases and Equipment tabs; an empty inbox says so; recordings draw their waveforms.

## Viewer pass — Victor Reyes

Walked Paranormal365 as Victor (the Viewer role, no grants). The hub gives him Details (the member count and the group's
own facts — no case figures), Members, Calendar, Messages, Files (the new "who adds files" line, no tools) and Equipment;
no Cases or Investigations tabs.

- [x] **V.1 A Viewer can add, move and delete the group's calendar events — public ones included — and post group
  messages.** → Ben: "make viewers read-only". Done at both layers the rank could slip through: the permission service
  answers no to any write for a Viewer whatever roles or grants they hold (a Viewer is put on the same starting role as a
  Member), and the twenty-one member-open writes — calendar events, investigations and their attendance, check-in, lead,
  duties and findings, group messages, timeline entries, request statuses and votes, place contacts — refuse a Viewer
  with a sentence. The calendar and messages say so instead of offering New event, the drag, the ✕, Compose and Reply.
  My-permissions carries `isViewer` (additive). `ViewerReadOnlyTests`, the service test (seen failing first),
  `ViewerSeatTests`. Earlier: Both controllers let any active member do it (item 156 kept the calendar "member-open"), and a Viewer is an
  active member. The page and the server agree, so nothing is offered that is then refused; whether the Viewer role
  should be read-only here is a policy question for Ben, and nothing was changed.

Seen and fine: no case figures on Details for a viewer; the tabs match the grants.
