# Site evaluation 2026-09-06 — Phase 2: membership doors and the case page

Branch: `feature/site-eval-phase-2-membership-doors`. Plan of record:
`ProjectNotes/Site-Evaluation-2026-09-06.md`, *Phase 2 — Membership doors and the case page*.

## The problem

A person added to a group through the security door got a membership rank and nothing else. Rank
alone opens nothing below Administrator: `HasAccessAsync` lets Owner and Administrator through and
then asks for a functional role or a direct grant, so a new Member or Viewer had `Case.Read` false.

What that looked like in the walk (W-M1, W-VW1): their desk listed the group's cases as links, the
visit roster named them Lead Investigator, and every one of those links opened a page that could
show nothing. Phase 1's predecessor turned the blank page into a sentence. This phase removes the
reason for the sentence.

## What this phase builds

1. **One rule for what a new member starts with.** A group setting — *new members start as* —
   defaults to the Investigator Role and is applied at every door that creates a membership:
   the admin/SuperAdmin `PUT …/membership`, an accepted membership application, and the seeders.
   Owner and Administrator are skipped because rank already opens everything for them; giving them
   a role row would be noise on the Members grid. The setting can be set to *nothing*, which is
   what every existing group has today, so no group's behaviour changes until somebody chooses.
2. **A door never offers what it cannot open.** The desk's case links and the roster's case
   reference check the same permission the case API enforces, so a link that would answer 403 is
   not a link.
3. **Screens re-read after a write** — the case header's manager (W-A9), the Team panel (W-A7),
   and the work-waiting banner (W-A3), all of which showed stale text until a full page load.
4. **Review and vote stays reachable** while a request is Under Review (W-A4), and the review page
   can put a request into review rather than only describing the state (W-A5).
5. **The accept dialog never proposes the client's surname** as the case title (W-A6).
6. **Enum labels and checkbox labels** — one display-name helper instead of raw enum names, and
   an accessible label on every checkbox group (W-A11).

## Not in this phase

The membership rank itself is unchanged: this adds what a rank has always lacked, it does not
re-rank anybody. Existing groups keep exactly what they have until an owner picks a default.

## Key files

- `Ben.Data.Source/Services/MemberDefaultRole.cs` — the one rule, called by every door
- `Ben.Data.Source/Services/OrgRoleDefaults.cs` — `PointAtStartingRole`, and `AddDefaultRoles` now
  returns what it staged so a caller can point at one
- Migration `AddOrganizationDefaultMemberRole` — one nullable column with its index and key.
  **Reaches the live database only at deploy — Ben runs it.**
- `Ben.Service.RepositoryService/Services/OrganizationSecurityService.cs` — the admin/SuperAdmin door
- `Ben.Data.WebApi/Controllers/Entities/OrganizationMembershipRequestController.cs` — the accepted-application door
- `Ben.Data.WebApi/SeedData/` — every seeder, so a fresh install does not ship in the broken state
- `Ben.Data.WebApi/Controllers/MyDeskController.cs`, `MyInvestigationsController.cs` — the two doors
  that offered what they could not open
- `Ben.Data.WebApi/Controllers/Entities/CaseController.cs` — the manager's name in the save answer,
  and the accept title
- `Ben.Data.Common/Enums/CaseTimelineEntryTypes.cs` — one exhaustive display-name helper
- `Ben.Web.Website.Library/Organization/OrgSettingsManager.razor` — the *new members start as* picker

## Verified

| check | result |
|---|---|
| `dotnet build Ben.slnx` | 0 errors, 0 warnings |
| Ben.Web.Tests | 4,504 passed, 0 failed |
| Ben.Service.RepositoryService.Tests | 319 passed, 0 failed (9 added) |
| Each new test against the un-fixed code | the starting role removed from the security door: 4 failed; from the accepted-application door: 1 failed; the role's own guards removed: 3 failed; the desk and roster filters removed: 2 failed; the manager navigation reload removed: 1 failed; the surname title restored: 1 failed |
| Playwright `MembershipDoors`, `MemberDesk`, `OrgSecurity`, `RequestStatusProgression`, `OrdinaryMember`, `CaseManagement` | 29 passed, 0 failed, 0 skipped |
| On a fresh database | all four seeded groups start people on the Investigator Role |
| By hand, the whole W-M1 path | a brand-new account added as a plain **Member** through the security door: its desk lists two open cases, `GET .../cases/{id}` answers **200**, and the group-side case page renders in full — where before the phase it showed the refusal sentence, and before that nothing at all |
| By hand, the setting | the picker offers "Nothing" plus all eight roles, shows Investigator Role as the group's choice, and the endpoint refuses a role belonging to another group with *"That role does not belong to this group."* |
| By hand, W-A9 | assigning a case manager now answers with **"AverageBen"** where it used to answer null, which is what redrew the header as "Unassigned" |
| The whole Playwright suite on a fresh database | 455 passed, 8 failed, 41 skipped in 25 minutes. All eight are the pre-existing set recorded in `ProjectNotes/AudioEditor-Audit-2026-09-06.md` — the seven that reproduce on master, plus `AddingAPieceOfGear`, the flaky eighth that audit already named |

## A cycle this change created, twice

An organization now points at one of its own roles, and every role points back at its organization.
That is a circular dependency between two rows, and EF's relational providers refuse a
`SaveChanges` containing one outright. It bit twice, in both directions:

- **Creating a group** staged the organization, its roles and the pointer in one save. Registration
  failed with a topological-sort exception. `NewOrganizationDefaults.AddAllAsync` now saves the
  rows first and the pointer second.
- **Deleting a group** marked the organization and its roles Deleted in one save, with the pointer
  still set. Setting it to null in the same save is not enough — both rows are still in the cycle.
  The pointer is now cleared in its own save before anything is staged for removal. The SuperAdmin
  purge was already safe, because it clears the column with a direct `ExecuteUpdateAsync`.

**Neither was catchable by the unit suite as it stood**: the in-memory provider does no
topological sort, so all 4,503 tests passed against both broken versions. `NewOrganizationDefaultsTests`
is therefore written on `SqliteTestDb`, and it fails against the single-save version.

Two repository guards caught real gaps in this change before any of it shipped: the purge coverage
test found that deleting a group would now be refused by the database (the new column is
`NoAction`, so the pointer has to be cleared before the roles are), and the LoadResult guard found
the settings screen swallowing a refused role list.

## Data created while verifying

All of it in the throwaway `IsHauntedDb_p2` and `IsHauntedDb_p2b`, dropped at the end: one account
`phase2.newmember@example.com`, its membership in Paranormal365, and the starting role set on that
group. Nothing was written to any other database.
