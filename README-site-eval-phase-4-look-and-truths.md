# Site evaluation 2026-09-06 — Phase 4: look, feel and small truths

Branch: `feature/site-eval-phase-4-look-and-truths`. Plan of record:
`ProjectNotes/Site-Evaluation-2026-09-06.md`, *Phase 4 — Look, feel and small truths*.

## The problem

Sixteen findings, none of which stops anybody, all of which are the site telling a small untruth
or wearing its plumbing on the outside. Taken one at a time each is trivial. Taken together they
are what somebody meets in the first ten minutes: a badge that says 6 next to a bell that says 1,
a spam trap shown to the person it will flag, a group page headed "Organization", a time nobody
typed printed to the second.

The evaluation grouped them because they share a shape — **the screen is not saying what is
actually true** — and fixing them one at a time as they are noticed is how they got here.

## What this phase changes

Grouped by the kind of untruth.

### Numbers that disagree with themselves

- **W-CL3.** The sidebar badge, the bell and the notifications page each chose their own subset
  of the eight buckets. One shared row source now feeds all three, so the number is always the sum
  of the rows it stands over.
- **W-H1.** A public case card printed "No votes yet" beside its own tally of three votes.

### Things that are not what they are labelled

- **W-A2.** The group page header says the group's name, and its tabs sit in one scrollable row
  instead of wrapping to two. Created/Updated stop printing the server's timezone and seconds.
- **W-SA3.** The dashboard's empty sentence names what it actually counted.
- **W-M3.** The Members grid's *Functional roles* column, and the *Equipment feedback
  (Administrators)* link offered to people who are not administrators.

### Plumbing on the outside

- **W-V2.** The contact form's honeypot had no CSS anywhere, so every visitor saw the trap field.
  A visitor who filled it in was flagged as spam. This is the one defect in the group.
- **W-V3.** The seeded "Generic / Unbranded" rows are for *choosing*, not for *browsing*; they
  leave the public catalogue.
- **W-CL4 / W-S5.** A person who is only a client stops being offered member chrome.

### Times

One rule, not a sweep. `ToDisplayDateTime` and `ToDisplayTime` drop seconds; second precision
becomes `ToDisplayDateTimeWithSeconds`, used by the audit log and the error log, where a second
means something. Every other call site was a scheduled time nobody typed seconds into.

### Reading and writing

- **W-A14.** A published report is editable with no way to re-publish, so a client keeps reading
  the version they were sent.
- **W-P4.** The leak warning fires before Save, not after it.
- **W-O2.** Group Settings gets section navigation.
- **W-R3 (second half).** The TelerikEditor iframe inherits the site font.
- **AE-2 / AE-3.** The audio editor's toolbar wraps instead of clipping, and its emoji become the
  site's icons.

## Key files

| what | where |
|---|---|
| One time rule | `Ben.Web.Services/DateTimeViewerExtensions.cs` — `ToDisplayDateTime` drops seconds; `ToDisplayDateTimeWithSeconds` is the exception, used by the audit log and the error log |
| One notification list | `Ben.Web.Services/NotificationRows.cs` — the bell, the sidebar and the page all read it |
| The honeypot's hiding place | `Ben.Web.Website.Library/Support/ContactPage.razor.css` (new — the rule did not exist) |
| Tabs in one scrolling row | `Ben.Web.Website.Library/Kit/BenTabs.razor` + `.razor.css` |
| The site's rich-text box | `Ben.Web.Website.Library/Kit/BenEditor.razor` (new) — twelve call sites moved onto it |
| The card's vote tally | `Ben.Data.WebApi/Controllers/Public/PublicCaseDiscoveryController.cs` |
| Re-publish | `CaseReportController.TouchReportAsync`, `CaseReportDetail.HasUnpublishedChanges`, `ReportBuilder.razor` |
| The leak warning, before Save | `Ben.Web.Website.Library/Organization/Cases/CaseDetail.razor` — `CheckLeaksAsync` |
| Settings section index | `Ben.Web.Website.Library/Organization/OrgSettingsManager.razor` |
| The placeholder brand's name, once | `Ben.Data.Common/Constants/EquipmentCatalogNames.cs` (new) |

**No migration.** Nothing in this phase changes the schema, so there is nothing here for the
deploy.

## What each finding turned out to be

Several were not what the evaluation's one-line description said, and two were worth more than it
suggested.

**W-V2 was the one real defect.** The contact form's honeypot carried `class="contact-hp"` and
nothing on the site defined that class — the rule had never been written. So the trap rendered as
a plain labelled "Website" input in the middle of the form, and the guard treats anything in it as
proof the submission was not typed by a person. A visitor who filled in the field the form
appeared to be asking them for was silently classified as spam.

**W-H1 was two tables, not a bad count.** The public case card drew its tally from `EvidenceVotes`
— votes on individual files, reached through the timeline — while the vote widget on the same card
drew from `CaseVotes`, and both were labelled "votes". A case with three people saying "yes,
haunted" and nobody yet arguing about a particular photo read "No votes yet" over "3 votes".
Neither number was wrong. The card's own buttons cast a `CaseVote`, so that is the number that has
to move when somebody clicks one; the list now counts those, and the card draws one tally instead
of two.

**W-V1 was twenty-three pages, not `/events`.** The site's shell puts every page inside a flex
wrapper, and twenty-three pages opened with `margin:auto` — the idiom everybody knows for "centre
this column", which on a flex item centres on both axes. `/events` showed it because it is short
enough to leave space over. There is now a guard test.

**W-CL3 was three surfaces each choosing their own buckets.** The sidebar badged two of the eight
and called itself "Notifications"; the bell badged all eight and listed seven; the page tested all
eight to decide whether to say "you're all caught up" and then listed six — it had no row for an
investigation invitation or a feed mention. So a person whose only waiting item was one of those
was told something was waiting and shown nothing. The fix is not "make the three agree", which is
what the last two fixes here did and is why there were three: the number is now computed from the
rows, from one list all three read.

**W-A14 needed a change nobody asked for.** Only the report row's own Update touched
`DateUpdated`, so a report rewritten section by section — which is how a report is actually
written — still claimed it had not changed since it was published. Any "edited since published"
flag built on that would have been blind to the ordinary case, so every mutating endpoint on the
controller now touches it.

**W-M3's first half was already fixed.** The Members grid's *Functional roles* column showed "—"
for everyone when the evaluation ran; on a fresh database it now shows *Investigator Role* against
every member, because phase 2 gave new members the group's starting role. What was left was that a
dash against an Owner or Administrator reads as "can do nothing" when the truth is the opposite —
they bypass the role check entirely. It now says so. The second half, the *Equipment feedback
(Administrators)* link offered to people who are not administrators, is gone: a link that names
its own audience in brackets is a link that should not have been drawn.

## Left undone, on purpose

**The page-title suffix (part of W-V1).** Nine of the site's 109 page titles end in "— IsHaunted"
and a hundred do not, so the suffix is the exception rather than the rule it was reported as
missing from. Making it a rule means a component and 109 call sites, most of which carry Razor
expressions — a mechanical change large enough to deserve its own decision rather than a clause
inside a look-and-feel phase.

**Most of W-S5.** A pure client's sidebar was reported as carrying Organizations, My
Investigations, Equipment, Media and Community. Two of those are gone: the **Organizations** entry
now appears only for somebody who is in a group, and the member desk on the home page renders only
for somebody who is in a group. The rest stay, and the reason is that the only signal the
navigation has is group membership — and a **solo investigator**, who has no group either, needs
Equipment and Media exactly as much as a member does. Hiding them would trade one wrong audience
for another.

**The audio region context menu** still uses emoji. AE-3 named the toolbar and the transport, both
of which are done; the context menu's items are plain strings and `BenContextMenu` renders text
only, so removing them means teaching a shared component about icons — more than the finding asks.

## Verified

| check | result |
|---|---|
| `dotnet build Ben.slnx` | 0 errors, 0 warnings |
| Ben.Web.Tests | 4,540 passed, 0 failed (36 added) |
| Ben.Service.RepositoryService.Tests | 319 passed, 0 failed |
| Playwright `LookAndTruths` | 9 passed, 0 failed, 0 skipped |
| The whole Playwright suite on a fresh database | 466 passed, 10 failed, 40 skipped in 26 minutes. Seven are the pre-existing set recorded in `ProjectNotes/AudioEditor-Audit-2026-09-06.md`. The other three were this phase's, are fixed, and re-run green — see below |
| Each new test against the un-fixed code | see below |
| By hand, signed out | the honeypot measured at x = −9648; no `Generic / Unbranded` row in the catalogue; `/events` container margin 0px, was 150.672px; times read `03:00 PM` |
| By hand, signed in | sidebar 1 / bell 1 / page listing one row; group page headed "Paranormal365" with 15 tabs sharing one top offset and scrolling; Created reads `09/07/2026 06:29 PM` with no "(server)"; the editor's content font byte-identical to the body font and no iframe; the leak warning appearing on blur with no Save; Re-publish appearing after an edit and the banner reading "Re-published." |

### Three regressions only the browser suite could see

**Two fixtures reached into an iframe that no longer exists.** `AnonymousClientRequestTests` and
`MessagingTests` each carried `Page.FrameLocator(".k-editor iframe").Locator("body")`, which was
right while the editor ran in Telerik's default Iframe mode. Moving every editor to `EditMode.Div`
so the content inherits the site font (W-R3) removed the iframe, and both fixtures then spent
thirty seconds waiting for an element that will never appear, in tests whose subject was a client
request and an org message. The selector now lives once, on `BenTestBase.EditorBody`.

**The leak warning nearly stopped being a stop.** Running the check on blur set the "already
warned about this title" marker, so the next Save went straight through — one click from seeing the
warning to publishing. `PublishLeakWarningTests` caught it. The check now *shows* the warning
without *spending* it: the first Save still stops and asks for a second. That was never what W-P4
was about — the complaint was that the warning arrived only after a save had silently not happened,
at the bottom of a dialog somebody had to scroll. It now arrives while they are still choosing, and
the stop is unchanged.

After both fixes, the four affected fixtures re-ran together on a fresh database: **22 passed, 0
failed, 0 skipped**.

### Each new test seen to fail without its fix

| reverted | failed |
|---|---|
| The row for one bucket removed from `NotificationRows` | `Every_bucket_that_is_counted_is_also_shown`, `Rows_are_ordered_by_what_waits_on_you`, `The_total_is_the_sum_of_the_rows`, `Every_bucket_counted_by_the_badge_has_a_row_beneath_it` |
| The card's tally back to `EvidenceVotes` | `GetAll_CountsVotesOnTheCase_NotOnItsEvidence`, `The_list_and_the_vote_summary_report_the_same_tally` |
| `TouchReportAsync` off `AddSection`, and the re-publish wording | `Editing_a_section_marks_the_report_as_changed`, `Re_publishing_tells_the_client_the_report_was_updated` |
| `BelongsToAnyOrganization` forced false | `Profile_SaysAMemberBelongs_WhateverTheirGroupPermits` (both cases) |
| `margin:0 auto` back to `margin:auto` on one page | `No_razor_file_centres_a_container_on_both_axes`, `A_short_page_is_not_floated_down_the_middle_of_the_window` |
| The honeypot's CSS rule renamed | `The_contact_forms_spam_trap_is_not_shown_to_visitors` |
| `flex-nowrap` off the tab strip | `A_group_page_says_which_group_it_is_and_keeps_its_tabs_on_one_line` |
| One toolbar emoji restored | `The_audio_editors_toolbar_wraps_and_uses_the_sites_icons` |
| `CheckLeaksAsync` off the checkbox and the label | `The_leak_warning_fires_before_save_not_after_it` |

**One browser test does not discriminate on the seeded data.**
`The_sidebar_the_bell_and_the_page_report_the_same_number` passed with the sidebar reverted to its
two-bucket badge, because on a fresh seed those two buckets happen to hold the whole total. The
unit test `NotificationRowsTests` is what actually catches this — it was written first and shown
to fail. The browser test is confirmation that the three surfaces agree on a real page, not proof
of the rule.

## Data created while verifying

All of it in the throwaway `IsHauntedDb_p4`, `_p4b` and `_p4c`, dropped at the end: one report
title edited and re-published, and the audio fixture uploaded to a seeded case by the browser
tests. Nothing was written to any other database.
