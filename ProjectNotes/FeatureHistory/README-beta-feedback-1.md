# Beta feedback round 1: billing, cases, research pages and editors

Branch: `feature/beta-feedback-1` (off develop, 2026-09-14). Plan of record: the approved plan in this session,
summarised here so the branch explains itself.

## Why

Ben walked the site as the beta group "Apple-Beta" on 2026-09-14 and marked up eight screens: a grid border that
stops short of the action buttons, no way to buy from the pricing page and no switch to take plans off sale, nothing
drawing the eye to pending requests, Research offered as a timeline entry type when it has its own tab, a cramped
Edit Case dialog with a misaligned checkbox, plain textareas for case notes and client messages, and — the one real
feature — a research **page** per case note, built as a stack of draggable blocks.

## Decisions

1. Pricing buttons send a visitor to start a group on that band, and an owner to their group's billing page.
2. SuperAdmin switch `billing.purchases-enabled` (on when unset): off keeps prices visible, removes the buttons,
   says plans aren't on sale, and the API refuses checkout in a neutral sentence.
3. Research drafts are private to the author until Publish.
4. Client messages get a small HTML editor; `BodyHtml` is new and `Body` stays plain for the iPhone app in review.
5. The research page is a stack of blocks (text, image, file, link card, map) — reusable `Kit/Blocks` components
   with no case knowledge; a read-only reader for everyone else; works from 375px up.
6. Link cards show a rich preview (title, description, thumbnail, domain) on research pages, messages and the feed,
   through a guarded server-side fetcher; thumbnails are copied small onto our storage. This reverses the
   2026-09-11 host-only rule on purpose.
7. The map block holds deliberately chosen places — never pre-filled from the case's address — as up to 10 numbered
   stops with a route between them. The author picks the route style per map (none, straight lines, walking, driving);
   a leg Apple cannot route is drawn as a dashed straight line; the reader sees distance and time per leg and a total.
   Routes are worked out when the page is viewed, never stored.
8. Text editing stays on Telerik (Ben's choice over Summernote/TinyMCE: flexibility and fit with C#). `BenEditor` gains
   paste clean-up, custom tools and insert-at-cursor so the research rail can drop a reference where the cursor is.
9. No table block yet (Ben asked to be reminded).

## Parts, in build order

A grid border · B cases list · C timeline picker · D Edit Case + New Case + description sanitizing · E notes ·
F client messages · G1 purchasing switch · G2 buy path · G3 band cards · H research pages (model, schema, API,
client, Kit/Blocks, page, timeline merge, docs) — then the changelog catch-up, help pages, pictures and PDFs, a
product walk as every persona, and the full e2e run.

## Status

- 2026-09-14: branch opened; `CaseEditorTools.Prose` (the shared toolbar) committed first.
- Mid-build asks folded in: grid row buttons became icons with a tooltip (A2); Edit Case became a page, not a larger
  dialog (D); link cards in composers are fetched only once an address is finished or the box is left with one.
- Parts A–G committed with their unit and Playwright tests green.
- H1–H7 committed: block model, schema (`ResearchPages`, applied to the player copy), API, client, link previews,
  Kit/Blocks with the map block and routes, the research page and rail, and dated research pages on the timeline.
- H8 in progress: help "Research pages", changelog lines, deploy-runbook entry, a seeded research page on the
  Belmont case, and the research Playwright fixtures (page, map block, viewports). The first run of those fixtures
  found a real autosave defect — a save that carried the editor's last keystrokes still said "Unsaved changes" and
  refused to publish — and 38.5px touch targets on wide screens; both fixed.
- H8 committed; the finishing steps done: changelog catch-up, help pages and pictures, product and persona PDFs, the
  investor overview, and the product walk extended to the new screens.
- A visual UI test pass (Ben: "use the mouse drag … work through it like layers, from outer to inner"), recorded in
  `ProjectNotes/UI-Test-Pass-2026-09-14.md`, six layers each fixed and committed before the next: the public shell,
  the signed-in shell, a group's hub, a case, research pages and the block editor, and the inner workings (seats,
  dragging, signing in, refusals). The largest finds: members could not see pictures on a published research page;
  signing in from any deep page landed on the home page; calendar events could not be dragged and deleted on one click;
  a member's Decline removed a request it had not declined; "view as" showed the SuperAdmin's name.
- Ben, during layer 5: the research document is to become Notion + Canva + OneNote + Obsidian Canvas, edited in the
  browser (Blazor WebAssembly) with the working copy in browser storage — Future-Improvements item 241. He is building
  that in another project; the research editor was left alone from layer 6 on.
- Full e2e run on the finished branch, then merge to master and develop.
