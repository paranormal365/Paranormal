# Site evaluation 2026-09-06 — Phase 3: the results document on the group's public site

Branch: `feature/site-eval-phase-3-public-report`. Plan of record:
`ProjectNotes/Site-Evaluation-2026-09-06.md`, *Phase 3 — The results document on the group's
public site*.

## The problem

W-P3, and it is a gap rather than a defect: **a published report exists only on the client's own
case page.** The group writes it, publishes it, the client reads it, and that is where it stops.
Nothing on the group's public site or in its CMS can carry any of it, so the thing Ben asked for
during the evaluation — "the document being part of the group's organization page on our site" —
could not be done at all.

The report is also the one document a group would most want to show. A case's public page today
offers the client's own description of what happened and a timeline. What it never offers is the
group's own finding.

## What this phase builds

1. **A report can be switched on for the public, one report at a time.** `CaseReport` gains
   `IsPublicSummaryVisible`. It is off by default and means nothing until the report is
   **Published** and the case itself is **public** — three separate conditions, none of which
   implies another, because publishing a report to a client is not a decision about the world.
2. **What the public sees is the executive summary and the conclusion**, and nothing else. Not the
   sections, not the evidence files, not the field sessions. Those carry the working detail of an
   investigation in somebody's home; the summary and the conclusion are the parts written to be
   read.
3. **Everything goes through `CaseProseRedactor`** on every request, with the case's own roster —
   the same substitution the case title and timeline already get, so a private engagement's names
   are replaced in the report exactly as they are everywhere else. Nothing is snapshotted.
4. **The leak check runs before it goes out.** The same `PublicTitleLeakCheck` that guards the case
   title (item 176) is offered against the summary and conclusion text, so a group is told when the
   words it is about to publish contain the client's name or street.
5. **One resolver, two surfaces.** `CasePublicReport` decides what a case publishes, and both the
   public case page and the CMS `EmbeddedCases` slot ask it — so a group's own page and the case's
   page can never disagree about what was released.

## Not in this phase

The report's PDF stays client-only: a document laid out for the person whose house it is, carrying
addresses and full names, is not a thing to hand a visitor because a summary was switched on.

## Key files

- `Ben.Data.Source/Entities/BenDataModel.CaseReport.cs` — `IsPublicSummaryVisible`, plus migration
  `AddCaseReportPublicSummary`. One nullable-free boolean, default false.
  **Reaches the live database only at deploy — Ben runs it.**
- `Ben.Data.WebApi/Services/CasePublicReport.cs` — the one resolver both surfaces ask
- `Ben.Data.WebApi/Controllers/Public/PublicCaseController.cs` — the case's own public page
- `Ben.Data.WebApi/Controllers/Cms/CmsEmbed.cs` — the cases block in the page builder
- `Ben.Data.WebApi/Controllers/Entities/CaseReportController.cs` — the switch, and the
  `public-summary-leak-check` endpoint
- `Ben.Data.WebApi/Services/PublicTitleLeakCheck.cs` — its sentences now name the field they are
  about, so a summary warning does not tell somebody to go and edit a title
- `Ben.Web.Website.Library/Organization/Cases/ReportBuilder.razor` — the switch and its check
- `Ben.Web.Website.Library/Organization/Public/OrgPublicCaseDetail.razor` — *What we found*
- `Ben.Data.WebApi/SeedData/DevelopmentDataSeeder.cs` — one seeded case shows its finding

## Verified

| check | result |
|---|---|
| `dotnet build Ben.slnx` | 0 errors, 0 warnings |
| Ben.Web.Tests | 4,517 passed, 0 failed (16 added) |
| Ben.Service.RepositoryService.Tests | 319 passed, 0 failed |
| Each new test against the un-fixed code | the switch, the Published condition, the empty-report guard, the newest-wins rule and the redaction each removed in turn: 5 failed; the leave-alone rule inverted: 1 failed; the tag stripping removed: 1 failed; the field-naming removed: 1 failed |
| Playwright `PublicCaseReport` | 3 passed, 0 failed, 0 skipped |
| The whole Playwright suite on a fresh database | 459 passed, 7 failed, 41 skipped in 24 minutes. All seven are the pre-existing set recorded in `ProjectNotes/AudioEditor-Audit-2026-09-06.md`, and they reproduce on master |
| By hand, all four states through the API | draft with the switch off, draft with it on, published with it on, and switched back off — the public endpoint carried the finding in exactly one of them |
| By hand, on screen signed out | the case page shows *What we found* with the summary and the conclusion set apart, and the pages of cases without one show no section at all |
| By hand, the editor | the switch sits under the two fields it publishes; the leak check reports "Nothing found in the summary or conclusion", and names the street when one is put into the prose |

## Two things this turned up

**The client method had the wrong route.** `/api/organizations/…/reports/…` where the controller
declares `/api/orgs/…`. The unit tests could not see it — they call the controller directly — and
it surfaced as a 404 in the browser. The editor reported it honestly rather than showing "nothing
found", which is the one wrong answer a leak check can give.

**A skip that was really a failure.** The first browser tests guarded with
`if (h1 count == 0) Assert.Ignore(...)`, which fired on a page that had simply not finished
rendering — reporting "no such case" for a case that was there. Replaced with a wait, which fails
honestly if the case is ever genuinely missing.

## Data created while verifying

All of it in the throwaway `IsHauntedDb_p3` and `IsHauntedDb_p3b`, dropped at the end: one report
on a seeded case, and one `phase3.author@example.com` administrator. Nothing was written to any
other database.
