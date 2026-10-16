# fix/event-place-search: a real place search on the calendar's public event form

Seen on production 10/16/2026: Group → Calendar → New event → **Public** → "Where (a place)". Typing
"Bell Witch" or "Bell Witch Cave" and pressing Search said "No places found by that name", although
the public place Bell Witch Cave (b27248e7-8504-4329-ad2d-51dba75e8384, 430 Keysburg Rd, Adams TN)
exists. Branched from `develop` at 50c4395d.

## Cause

1. `OrgScheduler.razor` `SearchPlacesAsync` called `/api/places/candidates`, the duplicate-place
   matcher. It is not a search: with only a name it wants the exact name (after dropping case,
   punctuation and a leading "The") or a near-identical misspelling. A part of a name never matches.
2. `SearchPlacesAsync` read `.Items` and ignored `.Failed`, so a refused or failed request read as
   "No places found".
3. The full name "Bell Witch Cave" does match the matcher's rule. A test with the production row's
   exact values, sent the way the form sent it (no street, town or position), returns it. So the
   full-name miss was a failed request shown as an empty answer (point 2), not the matching rule.
   The production logs would say which failure. The name-only call is also the heaviest one the
   matcher gets: with no town it loads the whole Places table into memory before filtering.

## Fix

- New `GET /api/places/search?q=` (`PlaceController.SearchPublicLocations`). Signed in only (the
  class's `[Authorize]`, plus a refusal for a token that names nobody). Every typed word must appear
  in the name or the town (LIKE, case-insensitive by collation, wildcards escaped, no function
  around the columns). **Public locations only**, matching `OrgCalendarEventController`'s rule that
  a public event can't be at a private residence. At most 20 results. Names that start with the
  words come first.
- Client: `IBenPlacesClient.SearchPublicPlacesAsync` → `LoadResult<PlaceCandidate>`.
- `OrgScheduler.razor`: uses the new search, shows "Couldn't search places just now" (or the
  signed-out sentence) on failure, and "No public places match that…" when nothing matched. The
  disabled "Private home" rows are gone, because the search never returns one.
- `/api/places/candidates` and `PlaceMatcher` are unchanged.

## Not changed, worth a look

The hosted-event venue searches (`Manage/Events/OrgEvents.razor`, `OrgEventPage.razor`) also send a
bare name to the matcher, so a partial venue name finds nothing there either. They already show a
failure. Hosted events have their own venue rules (`HostedEventController`), so they were left for
a separate change.

## Tests

`Ben.Web.Tests/Controllers/PlaceSearchTests.cs`: part of a name finds Bell Witch Cave; the town
counts; every word must appear; a private residence is never returned; a caller who isn't signed in
is refused; the cap; ordering; wildcards are literal; and the matcher still needs the whole name.
With the search changed back to an exact-name match and the kind filter removed, 10 of the 21 fail.
