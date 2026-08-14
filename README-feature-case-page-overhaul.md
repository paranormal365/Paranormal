# feature/case-page-overhaul — Area 5 (C1–C4)

Branched from `main` at `8e48464` (the release that shipped Areas 1, 2 and 3).

Plan source: [ProjectNotes/Feature-Roadmap.md](ProjectNotes/Feature-Roadmap.md), Area 5.

## What this branch is for

The case page today shows an org member very little of *why* the case exists, and shows the client
almost nothing of what the org has done. Both halves of that are this branch.

## Phases

### C1 — Original request on the case page

`AcceptClientRequest` snapshots the request's description and address onto the new case, but the
case description is then freely editable, so the two diverge. The Overview tab renders the literal
words "Client Request" as the Source — no link, no content — and **no endpoint lets an org member
read the originating `ClientRequest` at all** (`GET api/client-requests/{id}` is owner-or-SuperAdmin
only). `ClientRequestFile` attachments are never carried across either.

New org-scoped `GET /api/organizations/{orgId}/cases/{caseId}/client-request`, gated on active org
membership + the case belonging to that org + `ClientRequestId` being set. Returns submitted date,
description, city/state, gender/birth-year, and the request's attached file ids/names — displayed
from the request rather than copied into `CaseFile`, so there's one source of truth. Overview gets
an "Original Request" card, clearly marked as submitted by the client on a date, distinct from the
editable case description.

### C2 — Tiered timeline visibility

`CaseTimelineEntry` has one binary `IsPublic` flag. There is no org-vs-client tier: any active org
member sees every entry, and clients see only their own occurrences.

Replace with `Visibility`: `OrgOnly = 0`, `Client = 1`, `Public = 2`, each level visible to the
audiences below it, org members always seeing everything. Migration backfills `true → Public`,
`false → OrgOnly`. The new capability is the client side — `MyCaseDetail`'s timeline starts showing
org-authored entries marked `Client` or `Public`.

### C3 — Investigator binder

Nothing exists today: no per-investigator notes entity, no `InvestigationId` on
`CaseTimelineEntry`, no file attachment table on `Investigation`. Only `Investigation.Notes`, a
single shared HTML blob for the whole team.

Reuse the timeline rather than building a parallel store: add a nullable `InvestigationId` FK to
`CaseTimelineEntry` plus an `InstrumentReading` entry type. A binder is then the set of timeline
entries scoped to an investigation, attributed by the existing `AuthorAppUserId`, with files via the
existing `CaseTimelineEntryFile` join. Entries flow into the main case timeline for free.

### C4 — Notification tie-ins

Pending investigation invites (`Rsvp == Invited`, future-dated) join the notification summary
shipped in Area 1; badge on the "My Investigations" drawer item.

## Notes carried in from the roadmap exploration

- **Investigation RSVP already exists end-to-end** — member buttons, org attendee table, client date
  negotiation. That part of the original wish list is done; the binder is the real gap.
- Public/private for cases is already a sound two-gate model (`IsPublic` && status Public/Haunted,
  pseudonym-or-null client name). C2 changes timeline entries, not that.

## Conventions this branch follows

- Every mutation endpoint gets `IAuditLogService` auditing and xunit controller tests, 403 cases
  included.
- Auth-gated pages await `WaitUntilAuthReadyAsync` in **both** `OnInitializedAsync` and any separate
  `OnAfterRenderAsync` fetch.
- EF migrations run against the dedicated SQL Server (192.168.1.71), never Docker.
- Clean build at 0 warnings + full suite green before each commit; live browser verification before
  claiming a phase done.
