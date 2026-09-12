# A person can delete their own field session (item 218)

**Branch:** `feature/delete-a-field-session`

## The gap

Found while closing item 180 Phase B. A session's recordings and its document are ordinary files
the person owns, so they appear in Upload Files and the new delete dialog covers them — but the
session holds them with a Restrict key, so the file delete refused with "part of a field session"
and there was nowhere to go next. The only doors that ever removed a session were the SuperAdmin
orphan purge and *retract*, which unpublishes without deleting.

## What this builds

`DELETE api/field-sessions/{sessionId}` — the submitter's own session, and nobody else's
(NotFound rather than Forbid, the same as retract: whether a session exists is not a fact a
stranger gets from a status code). It removes the share links, the file rows, the session, and
then the upload rows and their bytes one at a time through `UploadFileRows.TryDeleteAsync`, so a
recording something else still holds is left standing rather than failing the whole delete. The
place is left alone, exactly as retract leaves it.

## Three refusals, each an existing rule rather than a new one

- **Recorded for an investigation.** It is the group's evidence and outlives whoever carried the
  phone. That is the rule the account purge already keeps — it destroys personal sessions and
  spares the group's — and the one the case purge keeps from the other side, detaching sessions
  rather than destroying them. One person must not erase a night's work from a case by tidying
  their phone's history.
- **Cited by a case report.** Deleting it would leave a finished report pointing at nothing.
- **Published to a place's archive.** Deleting is retraction by another door, so it follows the
  retraction rule: paid plans only. Publish-then-remove is the exploit that rule exists for.
  Choosing never to publish is not gaming anything, so an unpublished session deletes freely on
  any plan.

## UI

`MyFieldSessions.razor` gains a delete beside **Play back**, and a confirmation naming the date,
the place, the reading count and the recordings that go with it. A session recorded for an
investigation shows *the group's* where the button would be, rather than a missing control. The
server's own sentence is rendered when it refuses, because the three refusals have three different
answers.

## Tests

`FieldSessionDeleteTests`, on the `SqliteTestDb` harness — a real relational database with foreign
keys enforced, which is what makes the delete order testable at all:

- a session of your own goes with its recordings, links and bytes;
- somebody else's is a plain not-found and nothing is touched;
- each of the three refusals, with nothing touched;
- a published session on a **paid** plan does delete (the other half of the pair, so a refusal
  that fired on every published session could not pass);
- an unpublished session deletes on a free account;
- the place survives.

Discrimination, both confirmed by running against broken code: dropping the share-link sweep makes
the database refuse the file-row delete (that key is NoAction), and dropping the investigation
guard lets a group's session be destroyed.

Playwright `MyFieldSessionsTests.A_session_of_your_own_offers_a_delete_that_asks_first` drives the
row control and the dialog, and presses nothing.

## Docs

`the-mobile-apps.md` gains **Deleting a session**. `your-files.md` said deleting a whole session
was not yet possible from the site — no longer true, and now corrected to point at the page.

## No migration

Nothing about the schema changed.
