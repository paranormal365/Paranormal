# Telerik popups that open off the bottom of the screen

Ben, 2026-09-09, on the case **Investigations** tab: opening *Propose Dates*, then a date/time
field, drops a calendar that runs off the bottom of the window. The popup's own footer — the
buttons that commit the chosen date — cannot be reached, and the page will not scroll to them.

> "This might occur on other forms and using other components as well."

It can. Eleven dialogs in `Ben.Web.Website.Library` put a Telerik picker, dropdown, combo box or
multi-select inside a `BenModal`, and the popup for every one of them is positioned by the same
code.

## What it turned out to be

Measured on the seeded case at 1280x720, before any change:

| | |
|---|---|
| popup height | 417px |
| popup top | 411px |
| popup bottom | 828px, i.e. **108px below the window** |
| room below the field | 309px |
| room above the field | 411px |

Telerik places a popup below its anchor and, when there is no room below, **flips** it above —
`collision: {horizontal:"fit", vertical:"flip"}`, its default, with no parameter that reaches it.
Flip is all it does. A 417px popup fits neither the 309px below nor the 411px above, so it is
left hanging off the bottom edge. Nothing rescues it afterwards: `.k-animation-container` is
`position:absolute` on `<body>`, so the popup adds no scroll height of its own, and a dialog is a
fixed layer regardless.

A dialog centres its content vertically, which is exactly what puts a field in the half of the
window where this is true. That is why it shows up in dialogs and not on ordinary pages.

## The fix

`Ben.Web.Website/wwwroot/js/popup-fit.js`, loaded once from `App.razor`. After a popup appears —
or changes size, which it does when its content streams in over the circuit, when the Date/Time
tab is switched, or when the month changes — it is nudged back inside the window:

- a popup that already fits is **not touched**, so dropdowns, tooltips, grid filter menus and the
  editor's own popups behave exactly as before;
- a popup that fits the window somewhere is moved until it does, even if that covers the field it
  belongs to — a native `<select>` does the same, and a covered field beats an unreachable button;
- only a popup taller than the whole window gets a height cap and an internal scrollbar.

Not a stylesheet, because the decision depends on where the anchor happens to be in the window and
CSS cannot see that. Not per-dialog settings, because the next dialog somebody writes would arrive
with the bug already in it.

## Tests

`Ben.Web.Playwright/Tests/PopupInModalTests.cs`, category `PopupInModal`. Each was run against the
un-fixed code:

| Test | Without the fix |
|---|---|
| `DateTimePicker_popup_stays_inside_the_window` | fails — `bottom=828` against a 720px window |
| `The_popups_own_Set_button_commits_a_date` | fails — "viewport ratio 0" |
| `A_popup_with_room_below_it_is_not_moved` | passes, as a guard should |

The third guards the fix rather than the defect, so it was checked the other way: a mutant that
clamps every popup to the top edge makes it fail with `popup y=8` against an anchor at 751.
