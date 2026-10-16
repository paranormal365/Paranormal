# fix/event-place-search: a real place search for events

Seen on production 10/16/2026:

- Group → Calendar → New event → **Public** → "Where (a place)". Typing "Bell Witch" or "Bell Witch
  Cave" and pressing Search said "No places found by that name", although the public place Bell
  Witch Cave (b27248e7-8504-4329-ad2d-51dba75e8384, 430 Keysburg Rd, Adams TN) exists.
- Group → Events → "Add an event" → Venue. Typing "Bell Witch Cave", Tab, Search: nothing useful
  appeared. Pressing Search a second time showed "Bell Witch Cave, 430 Keysburg Rd, Adams, TN".

Branched from `develop` at 50c4395d. Two commits: the calendar form, then the two event venue boxes
and the first-press problem in all three.

## Cause

1. **Not a search.** All three boxes (`OrgScheduler.razor` `SearchPlacesAsync`, `OrgEvents.razor`
   `SearchPlacesAsync`, `OrgEventPage.razor` `SearchVenuesAsync`) called `/api/places/candidates`,
   the duplicate-place matcher. With only a name it wants the exact name (after dropping case,
   punctuation and a leading "The") or a near-identical misspelling. A part of a name never matches.
2. **"No places found" before any answer.** Each box set its "searched" flag *before* awaiting the
   request, and its "nothing found" line showed whenever that flag was set and the list was empty.
   Blazor Server draws the page as soon as an event handler first awaits, so "No places found"
   appeared the instant Search was pressed and stayed until the answer arrived. The typed text was
   not the problem: it is bound with `@oninput`, so the click always sees it. The request was slow:
   with no town, the matcher loads the whole Places table (every column) into memory before
   filtering. So the first press showed the empty line while it waited; by the second press (or after
   waiting) the answer had arrived, which is the venue box's "only works the second time". The
   calendar form's full-name "No places found" is the same thing. A test with the production row,
   sent the way the form sent it, shows the matcher does return the cave for its full name.
3. **Failures hidden.** The calendar box read `.Items` and ignored `.Failed`. The two venue boxes put
   the failure in the page-top alert but still showed "No venues found by that name" under the box.
4. Nothing stopped an older, slower answer from replacing a newer one.

## Fix

- New `GET /api/places/search?q=` (`PlaceController.SearchPublicLocations`). Signed in only (the
  class's `[Authorize]`, plus a refusal for a token that names nobody). Every typed word must appear
  in the name or the town (LIKE, case-insensitive by collation, wildcards escaped, no function
  around the columns). **Public locations only.** At most 20 results. Names that start with the
  words come first. One query with a projection, not a table load.
- **One rule serves both kinds of event.** A public calendar event may not be at a private residence
  (`OrgCalendarEventController.PublicEventRefusalAsync`), and a hosted event's venue may not be one
  either (`HostedEventController.ValidateAsync`). `PlaceKind` has only those two values, so "not a
  residence" is "public location", and the venue boxes reuse the same endpoint. A test runs over every
  `PlaceKind`, so a new kind has to be decided there.
- Client: `IBenPlacesClient.SearchPublicPlacesAsync` → `LoadResult<PlaceCandidate>`.
- `Ben.Web.Services/PlaceSearch.cs`: the state all three boxes now share. It says "Searching…"
  while waiting, says "no places match" only after a search has come back empty, shows a failure (or
  the signed-out sentence) next to the box, treats an exception as a failure, and ignores an answer
  to an older search or one that arrives after the box was cleared.
- All three forms use it, with the placeholder "Search by place/venue name or town". The disabled
  "Private home" rows in the calendar box are gone, because the search never returns one.
- `/api/places/candidates` and `PlaceMatcher` are unchanged. The two screens that really are
  duplicate checks (`NewInvestigationWindow`, `BenPlaceCandidates`) still use them.
- Help: "A venue we haven't listed" in `organization-administration.md` now says to search by part
  of the name or the town, and that only public places are listed; `docs/ishaunted-documentation.html`
  regenerated. **`docs/IsHaunted-Product-Documentation.pdf` still needs printing again** (the
  headless-Chrome line in `docs/README.md`); the sandbox here refused to run Chrome.

## Tests

- `Ben.Web.Tests/Controllers/PlaceSearchTests.cs` (22): part of a name finds Bell Witch Cave; the
  town counts; every word must appear; a private residence is never returned; every kind a venue may
  be and no other; a caller who isn't signed in is refused; the cap; ordering; wildcards are literal;
  the matcher still needs the whole name. With the search changed back to an exact-name match and the
  kind filter removed, 10 of the first 21 failed.
- `Ben.Web.Tests/Services/PlaceSearchStateTests.cs` (11): while the answer is on its way the box says
  searching, not "nothing matched"; empty, failed, thrown and signed-out answers; a slow older answer
  doesn't replace a newer one; a late answer after clearing is ignored; and each of the three forms
  uses the search and never the matcher. With "searched" set before the wait and the stale-answer
  check removed (the old behavior), 3 of the 8 state tests fail; the form check fails on the two venue
  boxes as they were in the first commit.
