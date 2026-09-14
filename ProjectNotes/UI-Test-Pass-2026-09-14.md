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

## Layer 5 — Research pages and the block editor

Found before the visual pass, by the new CaseResearchEditorTests (to be confirmed by eye when this layer is reached):

- [ ] **5.1 Pasting formatted text onto the page makes an empty text block.** HTML paste outside a text block inserts a
  new block but the words never arrive (editor shows an empty paragraph).
- [ ] **5.2 Words typed straight after clicking back into an earlier text block are lost.** Two blocks, click the first,
  type ", and more", Save now → the saved first block has no ", and more".
- [ ] **5.3 Clicking into a drawn text block puts the cursor at the start**, not where the click was: typing " and the
  second" after pressing End produced " and the secondFrom the first tab".
- [ ] **5.4 The bulleted-list tool** did not produce a list (test clicked by title; confirm the button and its name).
- [ ] **5.5 A typed link from the Add bar, then Refresh card, then Save now, stays "Unsaved changes".**
- [ ] **5.6 Captions test timed out** part way (picture alt/caption, file caption, reader) — find which step.

## Layer 6 — Inner workings: seats, persistence, refusals, saving
