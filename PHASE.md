# Dedupe & Perf Pass

Branch: `refactor/dedupe-and-perf`

## Why

A user-requested review pass: look for redundant code that should be common models/services/
extensions/helpers, and obvious performance leanness. Three parallel Explore agents surveyed the
WebApi controller layer, the Blazor UI layer, and the data/service/mapping layer for concrete,
already-duplicated patterns (not speculative "could be nicer" observations). Scoped with the user
to a tier covering the two real defects found plus UI consolidation (items #1-#6 of the findings
list); server-side paging and a shared test-data builder were deliberately left for a later pass.

## Approach

- **`CaseStatus` label/badge logic** was reimplemented independently in 5 components and had already
  drifted — a Haunted case rendered a different badge color depending on which page you viewed it
  from, and one copy silently fell back to the raw enum name for statuses it forgot to handle.
  Consolidated into one `CaseStatusExtensions.Label()`/`.BadgeClass()` pair in
  `Ben.Web.Library/Services/`. Also found and fixed a genuine duplicate-badge bug surfaced by this
  consolidation: `OrgPublicCaseDetail.razor` showed "Haunted" twice (status badge + separate
  IsHaunted badge) for the same case, the same bug class fixed twice already this session for the
  Public badge.
- **N+1 in `OrganizationController.GetAllWithPermissions`**: `HasAccessAsync` opens its own
  `DbContext` internally and is called twice per org in a loop — up to 8N queries for N orgs.
- **`IsOrgAdminAsync`** (Owner/Administrator check) hand-copied into 6 controllers despite its
  sibling `IsOrgMemberAsync` already living in a shared helper — consolidated alongside it.
- **Auth-guard boilerplate** (`IsInteractive`/`AuthReady`/redirect-to-login) copy-pasted across 15+
  page components — the same pattern that has already caused two real bugs this session when a copy
  was missing or wrong. Factored into one reusable helper.
- **Delete-confirmation dialog boilerplate** duplicated across ~15 components — factored into one
  shared `ConfirmDialog` component.
- **`AdminAllCases.razor`/`AdminAllInvestigations.razor`** were near-identical twin pages —
  consolidated their shared shell.

## Verification

- `dotnet build` clean and the full test suite green after each step.
- Live-verify the pages touched by the badge consolidation (status colors match across all 5
  former call sites) and the auth-guard/dialog consolidation (no regression in redirect/delete
  behavior).
