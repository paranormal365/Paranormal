# URL integrity: organization addresses, aliases, and equipment slugs

Closes backlog item **#89**. Two things were listed as remaining — equipment model slugs, and
alias-and-redirect for changed slugs. The alias work turned up two faults nobody was looking for.

## What was actually wrong

The task was "renaming breaks shared links". True, and the smallest of three problems on
`Organization.UrlName` — the one column in this product that people type by hand.

| | Before | Now |
| --- | --- | --- |
| Characters | anything: `ghost squad`, `a/b`, `../admin` | lowercase, digits, single hyphens, no reserved words |
| Uniqueness | checked on create, **never on rename**, no index behind either | checked on every path, unique index in the database |
| Renaming | broke every shared link, silently | old address kept as an alias and still resolves |

The uniqueness gap is the serious one. All **seventeen** lookup sites are first-match queries, so
two organizations holding one address meant `/o/ghost-squad` served whichever row came back first —
and a group could rename onto another group's address deliberately and take their public traffic.

There were also **three** creation paths, not two: the admin endpoint, the org endpoint, and
`RegisterOrganizationAsync` in the repository layer, which knew about none of this. They now share
one helper in `Ben.Data.Source`, the only project all three can see. Two endpoints writing one
column under different rules is exactly how the original collation bug happened.

## Aliases

An old address resolves to the organization and reports that it did, so the page can move the
browser to the current address — the link works, and what gets copied onward is the one that will
still be right tomorrow.

**Aliases are never reassigned.** An address a group has held stays theirs for good. Handing
`/o/ghost-squad` to a different group would point somebody's saved link at strangers, which is worse
than the link being dead: a dead link says "gone", a captured one says something false.

Cases, investigations and events need none of this — all three generate a slug once and return early
if one exists. **If any of them ever becomes editable, it needs this on the same day.**

## Equipment slugs

`/equipment/{make}/{model}` replaces `/equipment-models/{guid}`, the last page still wearing a GUID.

**This slug is regenerated on rename — the opposite of every other slug here, on purpose.** A case
or an organization freezes its address because somebody chose and shared it. The catalog is the
site's own vocabulary and its rename path exists to correct mistakes; a page for a make fixed from
"Sansung" to "Samsung" that still answered only to `/equipment/sansung` would keep the error in the
most visible place there is. A catalog link shared before a correction dies — accepted, because
these addresses are new and nothing has been shared yet.

Model slugs are unique **within the make**, matching how the names are: two manufacturers may both
make an "X1". The GUID route stays and redirects to the readable address, because every list in the
app still links by id — without that, the readable route would exist and nothing would reach it.

Existing rows are backfilled by the seeder in C#, not by SQL in the migration, so there is one
definition of how a name becomes a slug. A SQL approximation would quietly disagree with `UrlSlug`
on accents, punctuation and length, and a row whose address does not follow the rule is worse than a
row with no address.

## Verification

- Clean solution build, **0 warnings, 0 errors**; full suite **green (4,621)**.
- Every guard broken deliberately and watched to fail — shape validation, alias-blocks-reuse,
  alias-recorded, alias-resolution, slug uniqueness, and per-make slug scoping.
- Both migrations **applied to the real dev SQL Server**. The organization migration is written
  defensively (normalize case, then suffix later holders of a shared address) rather than against one
  machine's data; the unique index built without needing it, so there were no duplicates in practice.
- `scripts/create-database.sql` regenerated.

A mistake worth recording: the dedupe CTE referenced `d.Rn` where `d` was the `Organizations` table,
not the CTE. It failed loudly at the database. Had it been a silent no-op instead, the index would
simply have failed later on somebody else's data.

## Not done

- **Nothing here has been click-tested by a human.** The canonical redirects in particular are worth
  a look: the logic is straightforward, but "arrive on an old address and land on the new one" is a
  browser behaviour, and no test in this repo exercises a browser.
- **Lists still link to equipment models by id.** The redirect covers it, at the cost of an extra
  navigation. Threading the slugs through the item records would remove that; not worth it yet.
- Reserved words for organization addresses are a fixed list. There is a source-scan test for CMS
  page slugs that catches new routes stealing a word; nothing equivalent guards this list, because
  these do not collide with real routes today — they are refused for looking official.
