# feature/telerik-15: Telerik UI for Blazor 14.1.0 → 15.1.0

Ben, 10/14/2026: "Do the telerik upgrade and check the changes from telerik to see if we need to change
the styles used and there might be new features offered we could use in the site", and "verify where
we use it we can update its usage to meet the new version requirements."

Started from `fix/e2e-preexisting-failures`, which sits on `feature/session-player-engine`.

## Versions

| Package | Was | Now | Why |
|---|---|---|---|
| Telerik.UI.for.Blazor (6 projects) | 14.1.0 | **15.1.0** | Newest (10/13/2026). 14.2 never existed. |
| Telerik.Documents.Fixed (API) | 2026.2.519 | **2026.3.923** | Not .1014: Blazor 15.1.0 requires exactly 2026.3.923, and Ben.Web.Tests loads both. |
| System.Security.Cryptography.Pkcs | pinned 10.0.13 in the 6 Telerik projects | **pins removed** | Telerik 15.1 brings 9.0.21, which is patched. The Ben.Data.Source pin stays, because EF Core's SQL client still brings a flagged 10.0.0. |

The target is still net10.0; the package targets net8.0, so no .NET change.

## What had to change in our code

1. **Document Processing namespaces** (2026.3.805+). `Telerik.Windows.Documents.*` became
   `Telerik.Documents.*`, a compile break, in four files:
   - CaseReportPdfGenerator
   - CaseReportController
   - EventMenuPdf
   - CaseReportFieldSessionTests

   The package and assembly names are unchanged.
2. **Scheduler (15.0 rendering redesign):**
   - The default toolbar gained a **New Event** button that opens Telerik's own edit form, which
     the group calendar never shows. The calendar and the style guide now declare their toolbar
     (navigation, date picker, views), so there is one New event: ours.
   - The **✕ on an appointment is gone**; Telerik moved delete into its own edit form. Deleting is
     the Delete in our editor, behind the same confirm (`data-testid=calendar-editor-delete`).
     `CalendarDragTests.Deleting_an_event_AsksFirst_AndCancelKeepsIt` replaces the ✕ test.
     `ViewerSeatTests` now checks that a viewer has no toolbar New Event and no editor Delete.
3. **Theme.** 15.1 ships Kendo themes 14.6.1 (from 14.3.0), the same major. Every `.k-*` class and
   `--kendo-*` variable named in `telerik-signal.css` still exists. The 22 classes the theme dropped
   are old Scheduler internals, and we target none. The comment in the three identical copies of
   `telerik-signal.css` now says so.

## Behavior changes to know about (no code change)

- **Grid CSV export (15.1):** a cell starting with `= + - @` is exported with a leading apostrophe,
  so a spreadsheet cannot run it as a formula. This affects the audit log export. It is a security
  improvement.
- **Editor (15.1):** content sanitization and link-scheme validation. Retest pasting into the rich
  text editors and `mailto:`/`tel:` links.
- **Scheduler look:** flat navigation buttons; the views pick becomes a segmented control on wide
  screens and a dropdown on narrow ones; month view's "more" shows a count; recurrence editing is a
  separate dialog (unused here).

## New in 15.0–15.1 worth considering (not built; for Ben to choose)

- **Floating ToolBar (15.1):** a toolbar that follows a selection. Candidates are the canvas boards,
  and formatting a selection in the CMS and rich-text editors.
- **Scheduler, adaptive rendering (15.0):** a compact layout on phones, for the group calendar on a
  phone.
- **Scheduler, read-only appointments (15.0):** per-event lock, e.g. events a viewer or an
  integration owns.
- **Scheduler, "more events" popover:** a busy month day stops truncating.
- **MultiSelect with a checkbox list and sticky Select All (15.0):** member, tag and type pickers.
- **Diagram with rich-text shapes and connectors (15.0):** case relationship maps, family trees on
  the research boards.
- **`ITelerikIconService` (15.0):** remap Telerik's SVG icons in one place to the site's icon set.
- **LLMKit AI primitives (15.0):** Chain of Thought, Tool Call, Citation and Reasoning. Worth it only
  with an AI assistant feature.
- **DatePicker `FocusedDate` (15.0):** open the calendar on a chosen month, e.g. an event's month.

## Verification

- Solution builds with 0 errors and no Telerik or obsolete warnings.
- Unit tests run.
- Calendar, viewer, style guide, case report and event menu browser tests run, then the full suite.
- Visual check of the calendar and the style guide's Telerik panel in light and dark.
