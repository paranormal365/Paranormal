# Place Deduplication — design pass (Area 9 / P8)

**Status: designed, then steps 1 and 2 built.** The Area 9 plan called for a design pass before
writing code here, because the obvious implementations are all quietly destructive.

## What the problem actually looks like

Measured against the dev database (12 places), rather than assumed:

| Signal | Finding |
|---|---|
| Exact address string | `4512 Belmont Blvd, Nashville, TN 37215` appears **3 times** |
| Coordinates rounded to 4dp (~11m) | 2 clusters of 2 |
| Coordinates rounded to 3dp (~110m) | the same 2 clusters — widening found nothing new |
| Name variants | `Bell Witch Cave` and `The Bell Witch Cave`, identical coordinates |

### Correction: the coordinate disagreement was fake

The first draft of this document made a lot of the three Belmont Blvd rows carrying coordinates
about eight miles apart, and concluded that address and coordinates disagree often enough that
neither can be trusted alone.

**That was wrong, and Ben called it.** The dev seeder hardcodes `Latitude = 36.1043, Longitude =
-86.7930` for two of those cases — and that is the *Shelby Street Bridge* coordinate, copy-pasted.
The `36.0913` rows are the correct ones; that is the Belmont University area, where Belmont Blvd
actually is. Nothing geocoded them differently. It was seeded noise, and building a design around
it would have produced machinery to defend against a problem that does not exist.

Worth keeping as a caution: **duplicate-detection rules derived from seed data are only as real as
the seed data.** The genuine signal here is much simpler.

## Why the duplicates exist at all

Three sources, and they want different answers:

1. **The backfill.** `BackfillPlacesFromCases` deliberately gave every case its own place rather
   than merging on address, because merging on a migration would have been a silent guess. That is
   the Belmont Blvd trio. **These are the ones genuinely worth merging.**
2. **Inline creation.** `NewPlaceRequest` lets somebody type a place while scheduling, with no
   lookup against what already exists. That is `The Bell Witch Cave` next to `Bell Witch Cave`.
   **These are worth preventing, not merging.**
3. **Genuine distinctness.** Two units at one address may be two places — which is why the rule
   below only *offers* a match and never applies one.

## The rule (settled with Ben)

**Same address, and less than a tenth of a mile apart.** Both, not either.

The conjunction is what makes it safe. A hotel or an apartment block is one address with many
units, and two investigations there are plausibly the same building — so address alone is not
enough to distinguish them, and proximity alone would merge neighbours. Requiring both means the
rule only fires when the address text agrees *and* the map agrees.

A tenth of a mile (~160m) rather than the ~11m the first draft proposed: geocoders disagree by a
building or two routinely, and 11m would miss matches that are obviously the same place.

Named places with no street address — a landmark — match on **normalised name plus the same
proximity**, since that is all they have.

Normalisation: lowercase, trim, collapse internal whitespace, drop punctuation, and strip a leading
`the` from names. Enough to catch `The Bell Witch Cave` against `Bell Witch Cave`.

### Step 1 — a candidate finder (safe, no writes)

`GET api/places/candidates?...` returning places matching the rule above for a proposed
name/address/coordinates. Reads only; it never merges or creates anything.

### Step 2 — "did you mean" at the point of creation

Wire the finder into inline place creation. Somebody typing "The Bell Witch Cave" sees the existing
place offered before a second row exists. This alone stops source (2) and is the cheapest real win.

**A gap found while building it:** there was no UI for case-less creation at all. The endpoint from
P2 (`POST api/organizations/{orgId}/investigations`) had never been reachable from a screen — a
write-only feature. `NewInvestigationWindow.razor` is that screen, and the candidate lookup lives
inside it, debounced, as the place is typed.

Verified live against the dev database, signed in as sarah:

- Typing `The Bell Witch Cave` offered **both** existing rows — `Bell Witch Cave` (3 investigations)
  and `The Bell Witch Cave` (1) — which is exactly the duplicate pair this document opened with.
- **Use this place** attached the new visit to the existing `Bell Witch Cave`. Re-opening the form
  afterwards showed still **two** places on file, the first now reading **4 investigations**. No
  third row was created.
- Typing `bell witch cave` in lower case matched both, so the normalisation holds outside the tests.

### Step 3 — an admin merge, explicit and reversible-ish

A screen listing candidate clusters, where a human picks the survivor. On merge:

- repoint `Case.PlaceId` and `Investigation.PlaceId` at the survivor
- **keep the losing rows**, soft-deleted with a `MergedIntoPlaceId`, rather than deleting them —
  so a wrong merge can be traced and undone, and old links still resolve
- never merge across differing coordinates without the human seeing both

### What not to do

- **No automatic merge on write.** Offering a match is reversible by ignoring it; performing one
  is not, and the person creating the place is the only one who knows whether it is really the
  same building.
- **No unique index on address.** Two flats at one postcode are two places; the database is the
  wrong place to assert otherwise.
- **No merging during a migration.** Same reason the backfill deliberately did not.

## Decided

**Steps 1 and 2 built. Step 3 (merge) deliberately not.** Prevention stops new duplicates and is
small; a merge screen is a lot of machinery for three backfilled rows in a dev database that nobody
is looking at. A place with one case attached is harmless until somebody wants the
"who else has been here" view for that address.
