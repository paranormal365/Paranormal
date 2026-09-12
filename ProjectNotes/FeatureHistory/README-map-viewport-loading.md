# Panning a map now asks about where it is

Ben, 2026-09-09: *"When a person scrolls, does it load investigations or cases in the new areas?"*

It did not. Two maps loaded once and then panned over a set that never changed, and one of them
asked for **500** cases and quietly stopped being the whole picture past that.

## What already worked, and why it is the pattern here

The Field Kit's *where you've been* map has done this properly for a while: bounded query,
debounced gesture, hard cap, and — the part that matters — **a footer naming what it is not
showing**. A partial map nobody is told about is worse than a smaller one they can reason about.

Everything below follows it rather than inventing a second convention.

## The two that changed

**A group's Investigations map.** A new `GET /api/organizations/{orgId}/investigations/map` returns
pins and nothing actionable, so it can be asked on every pan without the permission verdicts the
grid endpoint carries per row. Gated on membership exactly as the grid is. The page draws first
from the rows it already has, so the map is never blank while a request is in flight, then reloads
on each settled gesture and says *"showing the most recent N of M in view"* when the cap bites.

**The home page's public cases map.** `GET /api/public/cases` now takes the four bounds. The map
and the list are built from **one** answer, so they can never disagree — a reader has no way to
tell which of two pictures is the true one. Once the map has been moved the page says so: *"showing
N cases in this view"*, and *"no public investigations in this view"* rather than the flat "none
yet" that would be a lie about a map somebody just panned into the sea.

## Rules both endpoints share

- **All four bounds or none.** Three is a mistake, and it is refused rather than guessed at.
- **Corners normalised.** Map libraries disagree about which corner they hand over first, and a
  reversed box reads as "nothing here" rather than as an error.
- **A row with no coordinates is kept when unbounded and dropped when bounded.** It cannot be
  inside a box nobody can place it in, and keeping it would make the count disagree with the pins.
- **Debounced at 350 ms and cancellable.** A pan raises the event repeatedly; an answer that lands
  after the person has moved on describes somewhere the map no longer is, and drawing it would
  shift pins under their cursor.
- **A failed load leaves the last good answer up.** "Nothing here" and "we could not ask" look
  identical to a reader, and only one of them is true.

## Three maps deliberately left alone

Recorded as item 222 in `Future-Improvements.md`, with reasons: a place's own page (every pin is at
that one place, so panning can reveal nothing), and the profile and My Investigations maps (each
sits beside a list of the same rows, so a viewport map would disagree with the list next to it).

## Tests

Eleven new unit tests across the two controllers — the whole-map case, the in-view case, reversed
corners, a partial box refused, an unplaceable row, and a non-member refused the map as well as the
grid. Four fail against a mutant that drops the normalisation and the all-or-nothing check.

Playwright `MapViewport`: three quick drags produce exactly **one** bounded request, and panning
the public map makes the page speak about a view rather than about everything.
