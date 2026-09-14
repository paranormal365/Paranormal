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
- [ ] **1.4 In dark mode, outline-primary buttons read as disabled** — "View" on the home page's case cards, "Find a
  group near you" on the feed: blue text at ~3:1 on the dark card beside a white "Group" button. app.css documents this
  as a deliberate palette choice below WCAG AA. → *Ben's call*: raise to 4.5:1, or keep.
- [x] **1.5 Seeded public events showed "08:00 PM UTC"** for a Tennessee walk: the development seed gave them no time
  zone. → seeded in America/Chicago at the intended local hours (fresh databases).
- [x] **1.6 The request form's example values read as entries** ("TN", "37201", "123 Main St", "Apt 4B") — I skipped
  State myself — and "Fill in the street, city, state and ZIP first." did not say which was missing. → placeholders
  start "e.g.", and the sentence names only the missing fields ("Fill in the state and ZIP code first.").
- [x] **1.7 "Gender" had no "(optional)"** beside "Birth Year (optional)". → added.
- [x] **1.8 Step 4 greeted the person with a red "Select at least one organization."** before they had done anything.
  → grey guidance: "Choose at least one organization to send your request to."
- [ ] **1.9 On a phone the home map centres on the middle of the US**; the only pins sit at its right edge.
  → *Not changed*: the map's fixed framing drives "cases in this view"; noted for Ben.
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

## Layer 3 — A group's hub

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
