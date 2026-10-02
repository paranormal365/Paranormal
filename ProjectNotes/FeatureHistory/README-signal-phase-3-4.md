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

## Phase 4 — Administration (in progress)
The dashboard already reads as Signal (cards, the violet Ben likes, charts). The pass is over the
working screens: settings, logs, the billing grids, places, cases, the store back office, mail and
roles — photographed in both themes by `SignalShots` (`admin-*.png` in
`docs/design/preview/signal-live/`) and fixed where they still read as the old template.
