# Sticky works inside pages again (10/13/2026)

Branch `fix/content-wrapper-sticky`, from master (54dedb85).

## The fault

The layout scrolls inside `main.app-body`. Between it and every page sits `div.content-wrapper`,
which the SmartApp template (`smartapp.min.css`, `.app-content .content-wrapper`) gives
`overflow-x: hidden`, presumably to keep wide content from scrolling the page sideways. `hidden` on
one axis turns the other into `auto`, so the wrapper became a scroll container that never scrolls.
Every `position: sticky` element in a page stuck to the wrapper instead of `.app-body`, so none of
them stuck.

Known casualties, three of which were built around it:

- the event page's index (`OrgEventPage.razor`, `sticky-lg-top`), which scrolled away;
- the seat picker's summary bar (`HostedEventPicker.razor.css`), now a bounded grid + fixed phone bar;
- the store's phone checkout bar (`ben-store.css`), now fixed to the window;
- the case editor's Save (`CaseEditPage.razor`), now in the header and after the form;
- dormant until now: the store buy box and order summary, the feed's new-posts pill, the calendar's
  alerts, the CMS page editor's side card, the plan designer's selection bar.

## The fix

`signal.css`: `.app-content .content-wrapper { overflow-x: clip; }`. `clip` cuts off the same
overflow at the same edge without making a scroll container, so sticky now pins to `.app-body`.
`.app-content` already had `overflow-x: clip` (signal.css), and that is what actually stops a wide
page scrolling `.app-body` sideways.

The workarounds above stay; their comments now say the cause was fixed.

## Checked

- Event page at 1440: scrolled 2200px, the index holds at the top of `.app-body` (with the old value it
  sat at −754px). At 375 the index is not sticky by design (`sticky-lg-top`, and it is taller than
  the screen).
- 48 pages at 375 and 12 at 1440, signed in as the SuperAdmin: no page scrolls sideways (document or
  `.app-body`), and every page measured the same with `hidden` and with `clip`.

## Added

- `Ben.Web.Playwright/Tests/StickyInPageContentTests.cs`: a sticky probe in `.content-wrapper` must
  pin while `.app-body` scrolls; a 2000px-wide probe at 375 must not scroll the document or
  `.app-body`. Both tests are needed: dropping the clip entirely would pass the first and fail the second.
- `SiteSweep` "scrolls sideways" now also checks `main.app-body`. It only checked the document,
  which stays exactly the window's width while `.app-body` scrolls.
