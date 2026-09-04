# The purges can be run in a test at last (item 219)

**Branch:** `feature/purge-behaviour-tests`

## What was wrong

Closing item 183 turned up something bigger than item 183: **no purge in this repo had a
behaviour test of its delete path.** Not the case purge, not the person purge, and not the group
purge — the one production refused twice.

The cause is mechanical. Every purge is built from `ExecuteDeleteAsync` and `ExecuteUpdateAsync`,
and the EF **InMemory provider implements neither**; it throws "not supported by the current
database provider" on the first statement. That was probed, not assumed. So the tests could cover
previews and refusals and nothing past the transaction, and the delete order — the part that
actually goes wrong — was guarded only by source-scanning coverage tests.

The fix needed a real provider. It was attempted while closing 183 and blocked: any package
restore failed because the configured local NuGet source `/Users/ben/telerik-blazor` did not
exist. Ben restored it, so this branch is the follow-through.

## What this adds

- **`SqliteTestDb`** — a real relational database, in memory, schema created, **foreign keys
  enforced**. The model carries SQL Server column types (`nvarchar(max)`, `varbinary(max)`) SQLite
  cannot parse, so a model customizer drops every explicit column type and any server-specific
  default or computed SQL. Relationships, keys and delete behaviours are untouched, which is the
  half these tests are about. The connection is held by the handle so every context the factory
  returns shares one database.
- **`CasePurgeBehaviourTests`** — eight tests running the real purge: the case and its children
  gone; the field session detached to its owner with its document intact; the feed post surviving
  with a null case reference; only the case's own copy-on-attach file destroyed and the person's
  original untouched; the storage directory removed; a mistyped title touching nothing; the result
  counts; and an empty case deleting cleanly.
- **`OrganizationPurgeBehaviourTests`** — two tests over the exact shape that broke production: a
  group with a case, an investigation, an attendee and a duty assignment hanging off that
  attendee. Deleting removes all of it and leaves the people, who are not the group's property.
- `Microsoft.EntityFrameworkCore.Sqlite`, test-only. Nothing ships against SQLite.

## What it caught

Immediately, unprompted: an invalid foreign key in a fixture the InMemory tests had accepted
without complaint (`CaseNote.AuthorAppUserId` left at `Guid.Empty`). Foreign keys being enforced is
the point of the harness, not a side effect.

## Discrimination

Each guard was run against deliberately broken code and confirmed to fail:

- Remove the `CaseNotes` sweep from `CasePurge` → the database refuses the delete, the transaction
  rolls back, the case survives, the test fails.
- Remove the field-session detach → the session is destroyed with the case (its FK cascades), and
  the test that says a recording goes back to its owner fails.
- Remove the `InvestigationDutyAssignments` sweep from `OrganizationPurge` → **the exact refusal
  production gave on 2026-09-03**, now a red test.

One lesson worth keeping: most of a case's children are `Cascade`, so breaking their order proves
nothing. A delete-order test has to break a **NoAction** table to mean anything. The first attempt
used `InvestigationAttendee`, which is Cascade, and passed against broken code.

## Left open

`AppUserPurge` has no behaviour test yet. The harness makes one cheap and it is worth doing next.
The coverage tests stay alongside all of this: they name the missing table in one line, which a
refused-delete failure does not.
