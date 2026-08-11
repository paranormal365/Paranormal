# Phase C — Correctness, Performance, Consistency

Branch: `feature/webapi-phase-c-correctness`

## Why

Phase C closes out the third tier of this session's WebApi/WebApp audit — not authorization holes
like Phases A and B, but real correctness and performance bugs: a structural bug that silently
overrides caller intent, a transaction gap that can leave data half-written, N+1 query patterns
(one of them on an unauthenticated public endpoint), check-then-insert races that surfaced as raw
500s, unchecked re-fetches that could NRE under a concurrent delete, and a couple of minor
async/logging gaps.

## What shipped

**C1 — `AdminEntityControllerBase.Create`'s `IsActive` bug.** `GetPropertyIfNotSet<T>`'s "was it
set" heuristic was `!val.Equals(default(T))` — for `bool`, `default` is `false`, so a caller-supplied
`IsActive: false` was indistinguishable from "unset" and silently forced to `true`. It was
structurally impossible to create an inactive entity through **any of the 28** generic admin
controllers. Fixed by making caller intent explicit instead of inferring it from the value: a new
scoped `EnableRequestBufferingFilter` (`Ben.Data.WebApi/Filters/`) lets `Create` re-read the raw
JSON body and check whether `isActive` was actually present (case-insensitively), falling back to
`true` only when it truly wasn't sent. Live-verified: created a real `UserAddressType` with
`isActive: false` via a SuperAdmin bearer token, confirmed it persisted as inactive on a fresh GET,
then cleaned up.

**C2 — Missing transactions.** `CaseController.AcceptClientRequest` and `CaseReportController.Publish`
each had two `SaveChangesAsync` calls with nothing tying them together — a failure between them
left a Case with no CMS pages (unretriable, since the guard rejects an already-Accepted
application) or a Published report with no client notification message. Both now wrap their two
saves in `db.Database.BeginTransactionAsync()`, guarded by `db.Database.IsRelational()` since the
in-memory provider used by tests doesn't support transactions and would fail every test otherwise.

**C3 — N+1 / latency.**
- `OrgCmsPageController.GetAll` re-checked the same loop-invariant `HasAccessAsync` permission
  twice per page and ran a separate `CountAsync` per page (~150 queries for a 50-page org) — hoisted
  the permission checks out of the loop and replaced the per-page counts with one grouped query.
- `PublicCaseDiscoveryController.GetAll` — an **unauthenticated public** endpoint — serially awaited
  an external geocoding HTTP call per unique city on every request, with no cap on the underlying
  case set. Removed the per-request geocoding entirely; the endpoint now reads whatever coordinates
  are already stored on the `Case` (already has `Latitude`/`Longitude` columns, populated at intake)
  instead of resolving them live, eliminating both the external call and its latency/rate-limit risk.
- `AdminAuditLogController.SendMessage` checked `AppUsers.AnyAsync` once per recipient in a loop —
  replaced with one batched existence query.
- `AdminRoleController.GetAll` used blocking, synchronous `_roleManager.Roles.ToList()` inside an
  async action — switched to `ToListAsync()`.

**C4 — Races surfacing as raw 500s.** Two check-then-insert races: `OrganizationMembershipRequestController.Apply`
could create duplicate pending requests for the same (org, user), and `AdminAuditLogController.SendMessage`'s
get-or-create for the "System Notification" `UserMessageType` could create duplicates. Added a
filtered unique index (`WHERE [Status] = 0`) on the former and a unique index on `UserMessageType.Name`
for the latter (new migration `AddRaceConditionUniqueIndexes`, applied to the dedicated SQL Server —
confirmed no existing duplicate data before applying). Both actions, plus `UploadFileVoteController.UpsertMyVote`
and `UploadFileShareController.ShareWithOrg` (whose unique indexes already existed but had no
`DbUpdateException` handling), now catch the race and either return the pre-existing Conflict
response or reconcile onto the row that won, since both of those are upserts by design.

**C5 — Null-after-refetch → 500 instead of 404.** Six `Update` actions fetched a `before` snapshot
(correctly null-checked), then re-fetched the same row *without* checking it and dereferenced it
with `!` — a delete landing between the two fetches threw an unhandled `NullReferenceException`.
Added the missing null check to all six: `AdminAppUserController.UpdateProfile`,
`OrganizationController.Update`, `OrganizationSettingsController.Update`, `UploadFileController.Update`,
`AdminUploadFileTypeController.Update`, `AdminUploadFileTypeExtensionController.Update`.

**C6 — Minor.** The two fire-and-forget metadata-extraction background tasks
(`MyCaseController.cs`, `UploadFileController.cs`) swallowed every exception with an empty `catch { }`,
so a systemic extractor breakage would have been invisible — both now log via a newly-injected
`ILogger<T>`. (`AdminRoleController`'s blocking `Roles.ToList()` was folded into the C3 fix above,
since it's the exact same line as that N+1 change.)

## Incidental finding (not fixed here)

While fixing C5's NRE bug in `UploadFileController.Update`, found that the action has **no ownership
check at all** — any authenticated user can edit any other user's file metadata via
`PUT /api/upload-files/{id}`. Out of scope for a correctness-only phase; flagged as a follow-up task
rather than fixed inline.

## Verification

129 new/updated tests (concurrency regression tests use real `Task.WhenAll` races against the
in-memory provider's genuine unique-index enforcement rather than mocking the failure, so they
exercise the actual catch-and-recover code path). Full suite green (1262 total, up from 1239 after
Phase B). Migration applied to the dedicated SQL Server after confirming no existing duplicate data
would violate the new unique indexes. Live-verified C1 via a real SuperAdmin bearer token: created an
admin entity with `isActive: false`, confirmed it persisted as inactive on a fresh GET (not just
echoed from the request), then cleaned up.
