# Signal phases 3 and 4 — the rest of Telerik, and Administration

Branch `feature/signal-phase-3-4-telerik-admin`, stacked on `feature/signal-phase-1-skin`
(PR paranormal365/Paranormal#1). Plan: `ProjectNotes/Signal-Redesign-Plan.md`.

## Phase 3 — Telerik (done)
Every Telerik widget the site uses is on `/styleguide` now, so each can be checked in both themes:
the chart (Field Kit player), calendar (date field, tour dates, a client's case), scheduler (a
group's calendar), upload (case files), window and tooltip join the grid, buttons, slider,
checkbox, numeric box, progress bar, tab strip and editor that were there.

`theme/telerik-signal.css` (byte-identical in both WASM hosts — `SignalCopiesStayInStepTests`):
- chart series from Signal (`--kendo-color-series-a…f`): accent, accent-2, success, warning,
  danger, rose — charts were the last thing on the site in the theme's stock palette;
- list items in popups (hover = accent-soft, selected = accent);
- the Bootstrap focus ring on Kendo inputs, pickers and buttons;
- calendar (today ringed, selected on the gradient), scheduler (accent events with an accent-2
  edge, today tinted, non-work hours sunken), upload, tooltip (the page's inverse).

Known limit: a chart keeps the colours it was drawn with until it re-renders, so toggling the
theme live leaves an open chart in the old palette until the page is reloaded.

## Phase 4 — Administration (done)
The dashboard already read as Signal. Ten more admin screens photographed in both themes
(`admin-*.png` in `docs/design/preview/signal-live/`) found these, all fixed:

- **The bar overflowed for anyone in several groups.** Each group was its own bar entry; the
  SuperAdmin's five pushed everything after Notifications off a 1280px window, behind a hidden
  scrollbar. They fold into one **Your groups** section whose card lists them; a group's own menu
  says "← Your groups". The phone drawer still lists them by name.
- **"Work waiting"** was a full alert per group above every page — a third of the window on admin
  screens. One compact card now, a row per group, each still dismissable.
- **Links in grids looked like text.** Kendo sets `.k-grid a { color: inherit }`, so a group's name
  on Your groups, a case's title, read as plain text in all 55 grids. Plain links get the link
  colour back.
- **Dark-mode links were 3.9:1.** `--bs-link-color` was the accent, short of 4.5:1 on a dark card.
  A `--ben-link` token (lighter violet in dark, 5.7–7:1) now carries words: links, kickers, outline
  buttons, card labels. `SignalContrastTests` measures links and kickers, and fails on the old value.
- **Icon grid buttons lost their names** while the tooltip showed (it moves `title` aside), so the
  new-group journey's second Accept was a nameless button — the one e2e test that had failed since
  before the redesign. 45 command buttons carry a visually-hidden name;
  `GridCommandButtonsKeepTheirNamesTests` holds the rule.
- All Cases: titles wrapped to five lines; fixed columns tightened. A group's menu scrolls inside
  itself when taller than the window; the group page's Back steps aside where the rail shows.
