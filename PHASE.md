# Phase B — High-Severity: File Controllers, Org Messages, Cross-Org Chain

Branch: `feature/security-phase-b-file-and-cross-org`

## Why

Phase B closes out the second tier of this session's WebApi/WebApp security audit — high-severity
findings that require an authenticated user with SOME legitimate relationship to the app (unlike
Phase A's unauthenticated/any-user holes), but that still let that user reach data or actions well
outside what they should ever see.

## What shipped

**B1 — Per-controller ownership checks**, each independently exploitable by any authenticated user:
- `UploadFilePermissionRequestController.Review` let anyone self-approve their own (or anyone's)
  access request — `ReviewedByAppUserId` was read from the request body, not the caller. `Submit`
  had the same body-spoofing issue for `RequestedByAppUserId`. `GetForFile` and the previously
  unaudited `GetPendingForReviewer` (a route parameter never checked against the caller) had no
  ownership check at all.
- `UploadFileRegionNoteController` — none of its five actions tied the caller to the file;
  `Delete` didn't even resolve the caller's identity.
- `UploadFileAudioClipController` — `ClipPreview`/`Clip` let any user extract audio out of
  someone else's private file, with `Clip` persisting it as a new file the caller owns (permanent
  exfiltration bypassing the source's visibility).
- `UploadFileAudioConfigController.Upsert`/`Delete` — no ownership check.
- `OrgMessageController.GetById` — checked only the route `orgId`, never that the caller was the
  message's author, a recipient, or (for the public-feed channel) anyone; the read has a side
  effect (marks read, increments `ViewCount`).
- `EvidenceVoteController.GetAll` — its own doc comment promised org-membership gating for full
  voter identities; the code enforced only `[Authorize]`.

**B2 — The systemic cross-org "broken ID chain."** A recurring shape across ~9 controllers: an
action checks `IsOrgMemberAsync(routeOrgId)` — is the caller a member of the org named in the
route — then queries the target resource by `caseId` (or another nested id) alone, without ever
confirming that id actually belongs to `routeOrgId`. A legitimate member of their own org could
supply their own `orgId` to pass the membership check, then pair it with any other org's real
`caseId` they know or can guess, and reach that org's data. Fixed via a new shared
`CaseOrgAccess.CaseBelongsToOrgAsync` helper (and the five duplicated private `IsOrgMemberAsync`
copies were consolidated into `FileAudienceAccess.IsOrgMemberAsync`) across:

`CaseReportController` (11 of 12 actions), `InvestigationController` (9), `CaseController`
(`UpdateTimelineEntry`/`DeleteTimelineEntry`), `CaseResearchController` (3),
`ScheduleProposalController` (3), `CaseTransferController.GetAll`,
`OrganizationMembershipRequestController.GetVotes`, `OrgCalendarController.GetAttendees`, and
`OrganizationAddressCrudController` (`GetMemberAccess` — plus `RemoveMemberAccess`, found while
verifying the file against source, not in the original audit list).

**B3 — `MyCaseController` under-privilege fix.** `LogOccurrence`/`UpdateOccurrence`/
`DeleteOccurrence` used a primary-client-only check while their siblings already supported
co-clients via the controller's own `IsCaseClient` helper — a real co-client got rejected trying
to log or edit their own occurrence entries. (`GetMyCase` and `AttachFile`, also named in the
original audit, turned out already fixed by this session's earlier item #4 work — reconfirmed
against current source rather than assumed.)

## Approach

Every fix follows the pattern already proven correct elsewhere in the codebase for the same
resource type — file-visibility checks reuse `FileAudienceAccess.CanViewFileAsync`, org-scoped
checks reuse the new `CaseOrgAccess`/`FileAudienceAccess.IsOrgMemberAsync` helpers, and actor IDs
are taken from `GetCurrentUserIdOrThrow()` rather than trusted from the request body throughout.

## Verification

95 new/updated tests, each proving the exact previously-working attacker shape is now rejected
(same-org membership + a different org's real resource id → 404/403) while the legitimate path
still succeeds. Full suite green (1239 total, up from 1188 after Phase A).
