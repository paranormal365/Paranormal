# A case can finally be deleted — by a SuperAdmin, and by nobody else (item 183)

**Branch:** `feature/delete-a-case`

## The gap

Item 183, found 2026-08-24: no `DELETE` for a case existed anywhere — not on `CaseController`,
not on `AdminCaseController`, not for SuperAdmin. Timeline entries, files and notes could each be
removed; the case holding them could not. A test case created against the shared database had to
be removed with raw SQL, and any mistaken or duplicate case was permanent.

The item left the shape to Ben and named three candidates. This branch builds two of them
together, because on their own each is half an answer:

1. **A SuperAdmin can delete a case**, behind a preview and a typed title.
2. **A group is told it cannot**, where it would look for the button, with the route to ask.

## The rule this states

A case is a record of real work, usually for a paying client, so a group **closes** a case and
keeps it. `CaseStatus.Closed` already existed and was already reachable in **Edit Case**; what did
not exist was anywhere saying that closing is the answer and deletion is not. The Edit Case dialog
now says so under the status field, and links to `/contact` for the one thing closing cannot fix —
a duplicate or a mistake.

## What deleting a case does

`CasePurge` (SuperAdmin only, `GET`/`DELETE api/admin/cases/{caseId}/purge`) splits the work in
two, and the preview shows both blocks before anything happens.

**Destroyed** — everything that exists only because the case does: timeline entries and their
files and experience tags, case files, notes, messages, research, reports and their sections,
contacts, votes, transfer logs, client access and invite rows, feed-post consents, scheduling
proposals, and the case's investigations with their attendees, findings and duty assignments.
Files are destroyed only where they are the case's **own** copy-on-attach copies; a person's
original, merely linked, is left alone, and each row goes through `UploadFileRows.TryDeleteAsync`
so a file something else still holds stays standing.

**Kept, unlinked** — anything belonging to somebody else that merely mentions the case: feed
posts, calendar events, video projects, evidence votes, public pages, equipment checkouts.

**Kept, whole** — **field sessions**. A recording belongs to the person who made it, so the purge
sets `InvestigationId` to null, which is precisely what a personal session is: it returns to its
owner with its files, readings and share links untouched. Deleting somebody's night's work
because a case was a duplicate would be the worst thing this feature could do.

**Notices, not refusals** — a client on the case (named), and a public case. Neither blocks the
delete, in the spirit of item 212's decision. There is no refusal at all: unlike the person and
group purges, deleting a case cannot lock the platform out of anything. The typed title is the
guard, checked on the server as well as in the UI.

## Tests

- `CasePurgeCoverageTests` — the load-bearing one, derived from the model rather than a list:
  every relationship whose principal the purge deletes from, and whose delete behaviour would
  leave the database to refuse, must be swept or cleared by a statement naming that column. This
  is the test the group purge did not have when production refused twice. It also guards the
  other direction: eleven sets the purge must never delete from, each with its reason.
- `AdminCasePurgeControllerTests` — 11 preview and confirmation tests: the counts, the
  kept-versus-destroyed split, only-our-own-copies, the client notice, the typed title, and that
  a mismatch touches nothing.
- Three discrimination runs, each confirmed to fail against broken code: dropping the `CaseVotes`
  sweep, deleting sessions instead of detaching them, and dropping the case-copy filter.
- Playwright `AdminDeleteCaseTests` — the grid's link in, the preview, the dead confirm button,
  and the group-facing "closed, not deleted" line. Nothing presses the button, for the reason the
  delete-user suite gives.

- `CasePurgeBehaviourTests` — the purge actually running, against a real relational database with
  foreign keys enforced (`SqliteTestDb`). The case and its children gone, the recording detached to
  its owner, the feed post surviving unlinked, only the case's own copy destroyed, the storage
  directory removed, a mistyped title touching nothing, and an empty case deleting cleanly.

The delete path was briefly untestable: it is built from `ExecuteDeleteAsync` and
`ExecuteUpdateAsync`, which the InMemory provider does not implement (probed, not assumed), and
the SQLite package could not be restored while the local NuGet source was missing. That source is
back, so the harness exists and the delete is covered — see `README-purge-behaviour-tests.md`.

## Docs

- `site-administration.md` — **Deleting a case**: the two blocks, what survives, the two notices.
- `working-a-case.md` — **Closing a case, and why you cannot delete one**, with the `/contact`
  route for a mistake.

## No migration

Nothing about the schema changed.
