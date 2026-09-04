# Delete a person (SuperAdmin)

Ben noticed the SuperAdmin users list had no delete at all, and asked for one that shows a count
per section before it commits — the way deleting a group already does.

## The thing worth knowing first

`AppUsers` is the principal of **335 foreign keys**, and **124** of those are a required
`CreatedByAppUserId` on tables like case notes, timeline entries and group messages. A true row
delete means deleting every one of those rows too, which is a group's record of its own work
written by somebody who has since left.

So this is not a row delete, and does not pretend to be. **It is both things at once, and the
screen says which one you are getting before you press anything:**

- **Destroyed** — everything only ever theirs: personal field sessions (no investigation), the
  files under them, memberships, sign-in history, messages received, follows, blocks, contact rows,
  external logins.
- **Kept, name stripped** — anything authored for a group. Those records stay and are re-attributed
  to a former member.
- **The account row itself** goes when nothing else refers to it. A signup that never did anything
  vanishes completely. A real member's row survives, emptied.

Ben chose this shape on 2026-09-04 over a hard delete that would have taken group history with it.

## Ben's other two calls

- **One refusal only: the last SuperAdmin.** There is no way back from a platform nobody can
  administer.
- **Owning a group and holding a paid seat are notices, not bars.** A SuperAdmin can appoint a new
  owner afterwards; being told is what matters. The paid-seat notice was added on Ben's follow-up,
  and says plainly that nothing here cancels the subscription, so the card keeps being charged.

Note the deliberate asymmetry with self-service closure, which still **refuses** an owner. Those
are different acts by different people: a member leaving cannot strand a group, a SuperAdmin
clearing up can be trusted to fix it.

## Shape

- `AppUserPurge` — `PreviewAsync` and `PurgeAsync`.
- `AdminAppUserPurgeController` at `api/admin/users/{id}/purge`, `[Authorize(SuperAdmin)]`.
- `AdminDeleteUser.razor` at `/admin/delete-user` and `/admin/delete-user/{id}`, mirroring
  `AdminDeleteGroup.razor`. A page, not a dialog: what this destroys is a dozen counts and three
  paragraphs, and none of it fits in a box people click through.
- The users grid gains a **trash icon** (icon only, `Title` for the tooltip — no `aria-label`, per
  item 112) that opens the screen with the account preselected. A nav entry does the same for
  arriving deliberately.
- `AccountClosureService.AnonymiseAsync` was **extracted** from `CloseAsync` and is now shared. Two
  copies of the anonymisation rules would drift, and the copy that drifts is the one that leaves a
  credential behind.

## The reference census, and why it is LINQ

`RowWillSurvive` is computed by **asking the EF model** — every foreign key into `AppUsers`,
counted — rather than from a list somebody maintains. A table added next year is covered on the day
it is added. That is the lesson the organization purge learned twice, most recently when BenCo's
deletion was refused on `InvestigationDutyAssignments`.

The first version issued a parameterised `COUNT` per key. It was faster, and it **could not run on
the in-memory provider at all**, so every unit test of the preview died inside the census before
reaching a single decision. A census only exercisable against SQL Server is a census nothing
checks. It is now `EF.Property` over `db.Set<T>()` via reflection, which both providers translate.

## Verification

Unit: 4,007 in `Ben.Web.Tests`, zero failures — 19 new. Every guard run against broken code first:

| Break | Test that failed |
|---|---|
| A swept table left in the census | `Every_table_the_purge_empties_is_also_excluded_from_the_census` |
| A census exclusion nothing empties | `The_census_never_excludes_a_table_the_purge_does_not_actually_empty` |
| Census always finds nothing | `Work_written_for_a_group_is_counted_as_kept_not_destroyed` |
| Grid delete button removed | `The_delete_screen_is_linked_from("AdminUsers.razor")` |

That last guard is worth a note: **it passed twice while the button was gone.** The first version
matched the delete page's own `@page` directive; the second matched the nav entry I had just added.
Both were true statements about the route and neither was about the thing asked for. It now names
the two files that must link in.

Live, against a running stack: the endpoints answer **401** anonymously and **403** to an ordinary
signed-in account, and the page bounces a non-SuperAdmin to the home page.

**Not verified here:** the SuperAdmin-authenticated path. The SuperAdmin password lives only in
Ben's environment, so `AdminDeleteUserTests` (`TestCategory=DeleteUser`) has not been run. Those
four tests drive the grid button, the two count columns, the row-outcome sentence and the typed
confirmation — deliberately stopping short of pressing the button, because the suite has been
pointed at the live database before and a test that actually deleted somebody would be
indistinguishable from the accident this screen exists to prevent.

## Documentation

- `site-administration.md` gains **Deleting a person**: the two halves, whether the row survives,
  the two warnings and the one refusal.
- `your-profile.md` gains **Deleting your account**. Self-service closure shipped 2026-08-28 and
  was never documented anywhere — found while writing the above.
