# The data App Review sees, and what may be deleted afterwards

Apple's reviewer signs in as `apple@apple.com` and looks around. Everything they can see on the
live site is listed here, with its id, so that removing it later is a decision rather than an
archaeology exercise.

Written 2026-09-09, while 1.0.2 (4) was waiting for review.

## The rule that matters more than the list

**Every future update is reviewed too, using this same account.** So "delete it after they approve"
is only half right: delete the whole set and it has to be rebuilt, correctly, under time pressure,
before the next submission. The useful split is:

| | |
|---|---|
| **Permanent** | the account, its group, and enough content that a reviewer landing cold sees a working product |
| **Disposable** | anything captured to demonstrate one feature — sessions, uploads, posts, extra cases |

Keep the permanent set small, realistic and correct. Then it never needs recreating, and nothing
about the next submission depends on remembering what this one had.

## What exists now

Live database, on the SQL server the site reads.

| What | Id | Note |
|---|---|---|
| Group | `225ef137-9ec3-4e7a-8388-9b5a60466afa` | **Apple-Beta**. The review notes filed with 1.0.2 say *Paranormal365*; they disagree. |
| Reviewer account | `71b299c0-4847-400f-dbb0-08df05adefb1` | `apple@apple.com`, "Apple Test". **Permanent — never delete.** |
| Ben | `3df91e73-938c-449c-b5a3-08df04382bda` | `haveben@msn.com` |
| Admin | `6f82795e-1088-4470-3238-08df043e562f` | `admin@ishaunted.com` |
| Case | `b58ff06e-fe1a-4011-af63-76a42844a2af` | Titled *Test Case in Franklin*. 495 Forrest Park Cir, Franklin TN, private. |
| Investigation | `6260a86d-b0c3-47c4-8912-5c7be2b426c6` | Titled *Investigation*. 2026-09-09 05:00 → 2026-09-19 13:01, so it is running throughout review. |
| Roster | `a3543c78…` (Ben), `5c66fdf3…` (Apple Test) | Both on the investigation above. |

### Changed on 2026-09-09, at Ben's ask

Two records read as test data, which is the same guideline family Apple rejected 1.0 under. Renamed
to match the site's own convention — street, never the house number:

| | Was | Now |
|---|---|---|
| Case | *Test Case in Franklin* | **Forrest Park Circle Residence, Franklin TN** |
| Investigation | *Investigation* | **Initial survey and overnight sessions** |

The investigation also gained a description explaining why the window is ten days. The previous
values are in the session scratchpad as `prod-rename-before.json`; they are also written above, so
nothing depends on that file surviving.

### Added on 2026-09-09 — two public events

The Events surface had **no rows at all**, and a tab that opens on nothing is the "incomplete app"
reading Apple already applied once.

| Event | Id | When (UTC) |
|---|---|---|
| Open investigation night — Bell Witch Cave | `12bd58d5-cd36-48fe-98b4-dd1ba1a24ff3` | 2026-09-17 00:00 → 04:00 |
| Beginners' evening: what an investigation involves | `88fe7ce6-f8c1-40bb-8f81-45948be2414f` | 2026-09-23 23:30 → 2026-09-24 01:30 |

Both are public, neither is attached to a case, and both were **verified through the live
`/api/public/events` endpoint** rather than assumed from the insert. The cave night is attached to
the public Bell Witch Cave place, so it carries a city and an approximate pin on the discovery map;
the library evening carries a text location only, which is what it honestly has.

These two are **disposable**. Delete them once the app is approved, or keep them and move the dates
forward — a group with nothing coming up looks abandoned, and moving a date is cheaper than
inventing content again before the next review.

## Also on the live site, unrelated to review

**The duplicate Bell Witch Cave was merged on 2026-09-09.** Two places carried that name.
`b27248e7-8504-4329-ad2d-51dba75e8384` survives — it has the cave's real street address,
430 Keysburg Rd — and `40000001-0000-0000-0000-000000000001` was merged away.

Done by hand against the database, following exactly what `AdminPlaceMergeController` does: repoint
`Investigations`, `Cases`, `OrgCalendarEvents`, `FieldSessionUploads` and `PlaceRooms`, then delete
the losing row. One calendar event moved; nothing else referenced it. The deleted row is in the
session scratchpad as `prod-place-merged-away.json`.

**Where that duplicate came from is worth knowing:** the id `40000001-...` is hard-coded in
`DevelopmentDataSeeder`, so the development seeder has run against the live database at some point.
If it ever runs again the duplicate returns. Prefer `/admin/place-duplicates` for the next one — it
writes the audit entry that a hand-merge does not.

## Removing the disposable part later

Nothing new has to be built for this. Three SuperAdmin screens already do it, and each previews
what will go before it goes:

- `/admin/delete-case` — a case and its children
- `/admin/delete-group` — a whole group
- `/admin/delete-user` — a person, anonymised rather than dropped where rows still point at them

Delete in that order if the whole set is going. Do **not** delete the reviewer account: every
future submission's review notes name it, and an account that cannot sign in is the fastest
rejection there is.
