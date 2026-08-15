# Place Deduplication — design pass (Area 9 / P8)

**Status: design only. Nothing built.** The Area 9 plan called for a design pass before writing
code here, because the obvious implementations are all quietly destructive.

## What the problem actually looks like

Measured against the dev database (12 places), rather than assumed:

| Signal | Finding |
|---|---|
| Exact address string | `4512 Belmont Blvd, Nashville, TN 37215` appears **3 times** |
| Coordinates rounded to 4dp (~11m) | 2 clusters of 2 |
| Coordinates rounded to 3dp (~110m) | the same 2 clusters — widening found nothing new |
| Name variants | `Bell Witch Cave` and `The Bell Witch Cave`, identical coordinates |

**The two signals disagree, and that is the whole design problem.** Of the three Belmont Blvd rows,
two carry latitude 36.0913 and the third 36.1043 — about eight miles apart. Same address text,
different coordinates.

So:

- **Address-only matching** would merge a row whose coordinates insist it is somewhere else.
- **Coordinate-only matching** would miss two of the three Belmont Blvd rows entirely.
- **Either one alone is wrong**, and the disagreement is not rare — it is a third of the duplicate
  set in the only data we have.

The likely cause is a bad geocode on one row, which is exactly the situation where an automatic
merge picks the wrong survivor and destroys the good coordinates.

## Why the duplicates exist at all

Three sources, and they want different answers:

1. **The backfill.** `BackfillPlacesFromCases` deliberately gave every case its own place rather
   than merging on address, because merging on a migration would have been a silent guess. That is
   the Belmont Blvd trio. **These are the ones genuinely worth merging.**
2. **Inline creation.** `NewPlaceRequest` lets somebody type a place while scheduling, with no
   lookup against what already exists. That is `The Bell Witch Cave` next to `Bell Witch Cave`.
   **These are worth preventing, not merging.**
3. **Genuine distinctness.** Two flats at one postcode are two places. Any rule that merges on
   address alone gets this wrong, and gets it wrong invisibly.

## Recommendation

**Prevent first, merge later, never merge automatically.**

### Step 1 — a candidate finder (safe, no writes)

`GET api/places/candidates?...` returning likely-same places for a proposed name/address/coords,
scored:

- coordinates within ~11m (4dp) → strong
- normalised name equal (lowercase, strip leading `the`, collapse punctuation/whitespace) → strong
- normalised address line + postcode equal → strong
- city+state equal only → weak, never sufficient alone

Two strong signals agreeing is a confident match. **One strong signal with another contradicting it
is the interesting case and must be shown, never auto-resolved** — that is the Belmont Blvd trio.

### Step 2 — "did you mean" at the point of creation

Wire the finder into the inline place creation in `InvestigationPlacement`. Somebody typing
"The Bell Witch Cave" at coordinates that already have a place sees it offered before a second row
exists. This alone stops source (2) and is the cheapest real win.

### Step 3 — an admin merge, explicit and reversible-ish

A screen listing candidate clusters, where a human picks the survivor. On merge:

- repoint `Case.PlaceId` and `Investigation.PlaceId` at the survivor
- **keep the losing rows**, soft-deleted with a `MergedIntoPlaceId`, rather than deleting them —
  so a wrong merge can be traced and undone, and old links still resolve
- never merge across differing coordinates without the human seeing both

### What not to do

- **No automatic merge on write.** The signals disagree often enough that it would be wrong
  regularly and silently.
- **No unique index on address.** Two flats at one postcode are two places; the database is the
  wrong place to assert otherwise.
- **No merging during a migration.** Same reason the backfill deliberately did not.

## Open question for Ben

Whether **merge** is worth building at all yet. Step 2 (prevention) stops new duplicates and is
small. The existing duplicates are three backfilled rows in a dev database with no production data
behind them — they could equally be fixed by hand, or left, since a place with one case attached is
harmless until somebody wants the "who else has been here" view for that address.

My recommendation: **build step 1 and 2, skip step 3 for now.** Prevention is where the value is;
a merge UI is a lot of machinery for three rows nobody is looking at yet.
