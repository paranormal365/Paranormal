# The person purge, tested — and the bug that found (item 220)

**Branch:** `feature/person-purge-behaviour-tests`

Ben asked for the person purge to get the behaviour tests the case and group purges got in item
219. Writing them turned up a defect in `AppUserPurge` that no existing test could have seen.

## The defect

Deleting a person has two possible endings, and the screen **promises one of them in advance**:
the row is removed completely, or the row stays emptied because records still point at it. The
promise comes from a census of every foreign key into `AppUsers`.

The census skipped a list of tables the purge "empties" — and four of them it does not empty. It
empties them **partly**:

- a field session recorded **for an investigation** is the group's evidence and stays,
- an upload file **something else still holds** is left standing.

So for an account whose only remaining tie was a group field session, the census reported nothing
pointing at it, the preview promised a complete removal, the row delete was attempted — and the
database refused it, **after the anonymise had already been committed**. The SuperAdmin got an
exception and a half-done job, having been told the opposite would happen.

Worse, the refusal escaped: `ExecuteDelete` goes straight to the provider, so a foreign key
violation arrives as `SqlException`, not `DbUpdateException`. Both `catch (DbUpdateException)`
blocks in the purge — the one around the final row delete and the one that skips a file still in
use — were therefore catching an exception type that cannot occur there. The same narrow catch was
in `UploadFileRows.TryDeleteAsync`, which the case and group purges both call, so one
still-referenced file could have failed a whole purge.

## The fix

- `sweptEntities` keeps only tables the purge empties **entirely**. The four partly-emptied ones
  are handled by `GoingRowsAsync`, which names the exact rows about to be deleted so the census
  counts what will still be there afterwards.
- Both catches widened to the provider's exception, so a census gap degrades to the logged
  warning the code already intended rather than a 500. Same in `UploadFileRows`.
- The preview and the outcome now agree, which is the whole point of the screen.

## Tests

`AppUserPurgeBehaviourTests`, on the `SqliteTestDb` harness from item 219 — a real relational
database with foreign keys enforced:

- an account holding nothing of a group's disappears, row and all;
- an account that wrote a case note keeps an emptied row, and the note survives word for word,
  with the person stripped out of the row that had to stay;
- a session recorded on their own is destroyed with its bytes;
- a session recorded **for an investigation** survives, the preview says the row must stay, and
  what happens matches what was promised;
- every route back in — external logins, roles, tokens — is removed;
- a mistyped name deletes nothing;
- the preview's row promise matches the outcome in both directions.

`AppUserPurgeCoverageTests` gains the structural half: the two mechanisms are alternatives never
both, and the partly-emptied tables are named so neither can be skipped wholesale again.

Discrimination: reintroducing the original `sweptEntities` list fails three coverage tests and the
group-session behaviour test. Confirmed by running it.

## Docs

`site-administration.md` — the "kept, emptied" bullet now names the two cases it was missing: a
session recorded for an investigation, and a file something else still uses.
