# Item 250 — Public locations as permanent evidence pages

Ben, 2026-09-21: *"I would like to be able to create public locations like Cragfont in Castillian
Springs, TN. Where people don't have to have a dedicated investigation to add files to the public
location."*

## The three decisions Ben made (2026-09-21)

1. **Anyone signed in may add, and it is screened.** The open door is the point — a public place
   page exists for the enthusiast with a photograph, and members-only would shut out exactly the
   person it is for. The feed's screener and held pile (item 217) go in front of it rather than a
   second moderation surface.
2. **Figures per place, and no league table.** Each place reports its own counts, split and
   averages. The site does not order one property against another. Cragfont is a real building
   with real owners, and "the third most haunted house in Tennessee" is a claim about them.
3. **One file, counted once, at the place** — whatever route it arrived by.

## What already exists (checked, not assumed)

- `Place`, `PlaceKind.PublicLocation`, `/places/{id}` (`PlaceView.razor`, 974 lines).
- `EvidenceVote` keyed on `UploadFileId`, with `EvidenceVoteType` Confirms / Disputes /
  Inconclusive and Ben's own signed score (+1 / 0 / −1). `CaseId` on it is already nullable.
- `FeedMediaReviewState` Pending / Approved / Held, `IFeedMediaScreener`, and
  `PendingMediaScreeningJob` — the whole screening pipeline.
- `ArchiveMediaPublication` already serves files of a **published field session** attached to a
  public place, and does so by asking the rule per request rather than caching a flag, so a
  retraction takes the bytes down.

## What does not exist

Adding a file **to a place** with no investigation behind it; the per-place total across routes;
the vote aggregates; the page's charts and evidence list; and the about-the-place / evidence
distinction.

## Why a new table rather than a `PlaceId` on `UploadFile`

Decision 3 says one total whatever the route, and there are two routes with different lifetimes.
A file from a published session is **derived**: `ArchiveMediaPublication` deliberately recomputes
it every request so that retracting a session takes its bytes down with it, and a snapshot would
outlive the publication. So `PlaceEvidence` holds only the **directly added** files, and the total
is that table plus the live archive query, de-duplicated by `UploadFileId`. Counting stays correct
and the binding-not-copying discipline survives.

## Slices

- **P1 — a file reaches a place, and is screened.** `PlaceEvidence` + migration, the authenticated
  add endpoint (refusing anything that is not a `PublicLocation`), the public list, and the
  evidence section on the place page.
- **P2 — voting, and the figures.** Votes on place evidence through the existing `EvidenceVote`,
  the counts, the split and the averages.
- **P3 — the place itself.** About-the-place photographs kept apart from evidence, and the charts.
