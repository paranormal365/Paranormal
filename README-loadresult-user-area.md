# Item 120, slice 5 — the user area

**13 methods, 14 consumers**, ratchet **45 → 34**.

## The slice that reached past the adapter

`GetAllUsersAsync` and `GetOrgUserDirectoryAsync` do not call the generic `GetAsync` — they
delegate to dedicated methods on `WebApiClient` itself, whose own `?? []` sat **outside the
ratchet's scan**. The ratchet counts `BenAdminClientAdapter.*.cs`, which is the right place for it
to look and not the only place the bug lives. Both are converted.

Worth remembering for the remaining slices: the ratchet measures one file pattern, not the whole
client.

## A defect in item 133's own work, found by using it

Reshaping a result by hand — `Ok(result.Items.Select(…))` — silently drops `SessionExpired`. Both
places doing that had dropped it, including the organization roster written the same day: a
signed-out reader was told to "try again", which is advice that cannot work, instead of to sign in.
`Reason` had been carried across in both, because a missing sentence is visible; a missing bool is
not.

`LoadResult.Map` now carries the whole outcome and changes only the shape. Four tests, and the one
that matters is verified to discriminate — reverting `Map` to the hand-rolled behaviour fails it.

## Surfaces and decorations

Converted with failure rendering: the four self-service cards (addresses, emails, phones, links)
and the SuperAdmin user grid. Somebody shown an empty list of their own phone numbers adds a
duplicate of what is already there; a SuperAdmin shown an empty user grid goes looking at the
database.

Recorded as decorations, with reasons: `AdminUserDetail`'s five lookup-type dropdowns,
`OrgCmsPageEdit` and `OrgCmsEditor`'s directory lookups (ids to display names), and the nav's user
count badge — a refused fetch leaves the badge off rather than showing zero users, which would be
the same lie in a smaller space.

`GetMyPhotosAsync` is the third method this conversion has found that is declared, implemented and
called by nothing.
