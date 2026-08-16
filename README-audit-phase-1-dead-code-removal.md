# Audit Phase 1 — Dead Code Removal

Branch: `feature/audit-phase-1-dead-code-removal`
Source doc: [`ProjectNotes/Code-Audit-2026-08-16.md`](ProjectNotes/Code-Audit-2026-08-16.md)

## Why this phase first

Phase 1 of the five-phase audit remediation is **pure deletion** — no behavior changes, no new
code. It runs first because everything in phases 2–5 gets cheaper once the dead weight is gone:
fewer files to grep, fewer misleading architectural signals, and a smaller surface for every later
refactor to worry about.

Findings covered: **A1, A2, A3, D1, E1**.

## Scope

### 1. Delete the dead generic repository layer (A1 + E1)

`Ben.Service.RepositoryService` contains a complete generic repository/unit-of-work architecture
that **no production code path reaches**. `IRepositoryManager` is registered in DI
(`Ben.Data.WebApi/Program.cs:115`) and then never injected anywhere; meanwhile 127 controller and
helper files use `IDbContextFactory<BenDataContext>` directly.

**Delete (108 files):**

| Path | Files |
|---|---|
| `Repositories/*.cs` | 50 |
| `EntityInterfaces/*.cs` | 50 |
| `GenericInterfaces/` — `IRepositoryManager`, `IRepositoryBase`, `RepositoryBase`, `IAppUserRepositoryManager`, `IOrganizationRepositoryManager` | 5 |
| root — `RepositoryManager.cs`, `AppUserRepositoryManager.cs`, `OrganizationRepositoryManager.cs` | 3 |

**Keep — these are live and load-bearing:**
- `Services/AuditLogService.cs`, `Services/OrganizationSecurityService.cs`,
  `Services/AddressGeocodingService.cs`, `Services/PlaceGeocoder.cs`
- `GenericInterfaces/IAuditLogService.cs` (used in 104 files),
  `GenericInterfaces/IOrganizationSecurityService.cs` (used in 30 files)
- `GlobalUsings.cs`

**Also remove:** the `IRepositoryManager` DI registration in `Program.cs:115`.

**Tests deleted with it (E1)** — all four exercise only the deleted code:
`RepositoryManagerTests` (2), `UserRepositoryManagerTests` (14),
`OrganizationRepositoryManagerTests` (12), `RepositoryReadPathTests` (20). ≈48 tests.

Verified before deleting: the `EntityInterfaces` are referenced only by `Repositories/` and the two
manager classes — the layer is fully self-contained.

### 2. Delete the `Ben.Service.Security` project (A2 + D1)

The whole project is unreachable: `OrganizationSecurityAuthorizeAttribute` is applied to **zero**
call sites, `SecurityExtensions` is used in zero files, and its `IOrganizationSecurityService` is
injected nowhere — the interface controllers actually use is the identically-named one from
`Ben.Service.RepositoryService.GenericInterfaces` (both are registered side-by-side at
`Program.cs:116-117`). The project's own `Enums/OrganizationSecurityTable.cs` already concedes the
attribute "never fired."

It is also the only thing dragging **ASP.NET Core 2.2-era packages** into the solution (D1):
`Microsoft.AspNetCore.Mvc.Core 2.2.5`, `Mvc.Abstractions 2.2.0`, `Http.Abstractions 2.2.0` — 2018
packages, out of support, superseded by the shared framework since .NET Core 3.0.

**Actions:**
- Delete the project (7 `.cs` files), its `Ben.slnx` entry, and the `ProjectReference` from
  `Ben.Data.WebApi.csproj` and `Ben.Service.RepositoryService.Tests.csproj`
- Remove the dead DI registration at `Program.cs:116`
- **Fix the false Swagger claim** at `Program.cs:65` — the API description currently tells
  consumers "other routes enforce org-level membership via `OrganizationSecurityAuthorize`", which
  is not true. Replace with what actually enforces access: per-route ownership checks via the
  shared access helpers (`FileAudienceAccess`, `CaseOrgAccess`, …).

**Tests deleted with it (23):** `OrganizationSecurityServiceTests` (20 — tests the dead service)
and `OrganizationSecurityTableParityTests` (3).

> **Note on the parity test.** It guards a real bug: the two `OrganizationSecurityTable` enums are
> converted by a plain numeric cast, and 26 of 30 values once resolved to the wrong table
> (`OrganizationFiles` → `MembershipRequests`). That bug is only survivable *because* the code path
> is dead. Deleting the duplicate enum removes the drift hazard at its root, which is why the test
> goes too rather than being preserved.

**Keep:** `OrganizationSecurityServiceRepositoryTests` (30 tests) — that covers the *live*
service and is unaffected.

### 3. Remove Entity Developer designer fossils (A3)

`Ben.Data.Source/BenDataModel.efml`, `.edps`, and the two `.view` files were last touched in the
Initial Commit (2026-07-16) and describe 26 entities; the model now has 158. Nothing generates
anything — `BenDataContext.Generated.cs` has been hand-edited continuously (most recently
2026-08-15), so the `.Generated.cs` suffix actively misleads.

**Actions:**
- Delete the four designer artifacts
- Rename `Context/BenDataContext.Generated.cs` → `Context/BenDataContext.cs`, folding in the empty
  partial from `Context/BenDataModel.BenDataContext.cs`

**Deliberately out of scope:** flattening the ~130 `Entities/BenDataModel.X[.Generated].cs` file
pairs. That is a large mechanical rename with no functional gain; it belongs in a quiet-moment
pass, not here. Noted in the audit doc under A3.

## Explicitly NOT in this phase

`A4` (vendored wavesurfer fork in `wwwroot`) and `A5` (9,596-line `HomeSvg.razor`) are also
"removal" findings but both change what the app *serves* at runtime, so they need live
verification. They belong with the phase-2/3 work, not in a deletion-only branch.

## Verification plan

Deletion-only changes are exactly the case where a green build proves little on its own — the
compiler confirms nothing referenced the deleted code, which is the claim being made, but it says
nothing about runtime wiring. So:

1. `dotnet build Ben.slnx` — must stay at **0 warnings, 0 errors**
2. `dotnet test Ben.slnx` — expect **~2,148 passing** (2,219 baseline − ~71 dead-code tests),
   **0 failures**, and confirm the drop matches the deleted files exactly rather than hiding a
   silently-dropped live test
3. Run the WebApp against the dev SQL Server and exercise the paths that touch the two DI
   registrations being removed — org membership/permission screens especially, since
   `IOrganizationSecurityService` has two same-named registrations and only one is being deleted.
   A DI resolution failure surfaces at request time, not build time.
4. Confirm `dotnet list package` no longer reports the 2.2-era packages

## Expected outcome

| Metric | Before | After |
|---|---|---|
| Projects in solution | 12 (+5 Media) | 11 (+5 Media) |
| Deleted `.cs` files | — | ~119 |
| Tests | 2,219 | ~2,148 (all removed tests covered deleted code) |
| Out-of-support packages | 3 | 0 |

---
*Part of the audit remediation tracked in `ProjectNotes/Code-Audit-2026-08-16.md`. Per audit finding
D5, this README moves to `ProjectNotes/` once the branch merges rather than accumulating at the
repo root.*
