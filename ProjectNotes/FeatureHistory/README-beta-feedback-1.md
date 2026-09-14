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
