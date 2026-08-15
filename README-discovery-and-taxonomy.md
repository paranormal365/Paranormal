# Two findings from the Area 8 scoping pass, fixed

Branch: `feature/discovery-browse-and-taxonomy-proposals`

Neither of these was group types. Both were found while scoping Area 8 and both stand on their own.

## 1. `/find` could not browse

`OrgDiscovery` gated everything on a location query, and the only public list endpoint was
`GET /api/public/organizations/search?lat=&lon=` — which also skipped any group with no area of
operation configured. So the home page's own "Browse All Groups" button led to a page that showed
nothing until you typed an address, and two of the three seeded groups were unreachable through
discovery at all.

**Now:** `GET /api/public/organizations/browse` — paged, anonymous, no location. `/find` loads it
on arrival, a location search replaces it, and "Show all groups" brings it back.

- `OrgBrowseResult` is its own record rather than a reused `OrgSearchResult`, which carries a
  distance and a within-range flag that mean nothing without a search point.
- Coordinates are still never returned. A test asserts that structurally, by reflecting over the
  record's property names, rather than trusting a future edit not to add them.
- The three listings now share one `OrgCard` component, differing only in a `MetaLine` fragment.

## 2. The taxonomy could not be extended by the people using it

`ExperienceType` always had `IsApproved` / `ProposedByOrganizationId`, and the admin screen always
had Approve buttons — but every write path hardcoded `IsApproved = true`, so no unapproved row
could ever exist and that whole half of the screen was unreachable.

**Now:** a group owner or administrator can add a missing type straight from the experience picker
on a timeline entry.

- **Live immediately.** Someone recording tonight's occurrence cannot wait for a word to be
  approved. The new type is selected on the entry they were writing when they typed it.
- **Flagged for review, not queued.** App administrators get a system message through the existing
  notification bell, and the type shows a ★ with Confirm and Reject on the taxonomy page.
- **"Unreviewed" needed no new column.** Approved with a null `ApprovedByAppUserId` and a
  proposing organization *is* unreviewed; SuperAdmin-created types stamp that field on creation, so
  they never appear in the queue.
- **Rejecting deletes usages, never records.** A timeline entry tagged with a rejected type loses
  the tag and keeps its text, author, files and place on the timeline. Someone's account of what
  happened is not deleted because an administrator disliked the label on it. Transactional, so a
  half-applied rejection cannot leave orphaned join rows.
- Categories stay SuperAdmin-only. Filling a gap in a category is small and reversible; inventing a
  top-level branch of the taxonomy is not.
- Adding a name that already exists returns the existing type, case-insensitively — "Knocking" and
  "knocking" are the same thing to everyone except a database.

## Verified live

Browse: 3 groups listed anonymously including the two with no area configured; location search
still works; "Show all groups" restores the list.

Taxonomy: BenCo's owner added "Three Knocks" → visible in the public taxonomy read immediately →
SuperAdmin's inbox carried the notice → ★ plus Confirm/Reject rendered on the taxonomy page while
seeded types showed neither → Confirm cleared the marker → Reject removed it. Non-member 403,
anonymous 401.

Three key tests were confirmed to fail against deliberately broken code before being trusted.

## A Razor trap worth remembering

A child-content parameter named `Meta` is parsed as the HTML `<meta>` void element, so its closing
tag is orphaned and the whole component tag reports as malformed — with errors that point at the
*call site*, several files and a hundred lines away from the cause. Renamed to `MetaLine`.
