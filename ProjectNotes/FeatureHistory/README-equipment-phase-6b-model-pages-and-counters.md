# Equipment Phase 6b — Make/Model Pages, a Unified Item Page, and Interest Counters

Branch: `equipment-phase-6b-model-pages-and-counters` · Backlog item **#55**, second of four
Phase-6 branches. Stacked on `equipment-phase-6a-photo-pipeline-and-gap-fixes`, which is still
unmerged — merge 6a first.

## Why

Phase 1 built a catalog of makes and models, but a make and model had nowhere to go: it was a row
in a search result and nothing more. Meanwhile the same recorder owned by nine people was nine
unrelated records, so nobody could see that nine of them exist, six are offered for borrowing, and
one owner had already found the manufacturer's spec sheet.

This branch gives a product a page, gives a piece of equipment a page, and starts counting whether
anybody is looking.

## The three surfaces

### `/equipment-models/{id}` — one product, pooled across every owner

Public, anonymous-readable. Shows how many are owned, how many are offered for borrowing, the
distinct manufacturer links their owners added, and their photos gathered together.

Everything on it is **anonymous by construction**. `CatalogPhotoRecord` carries a photo id, a
caption and a sort order — no item id, no upload-file id, nothing about an owner. A reflection test
enforces that, because the guarantee should survive a future field being added carelessly: a shape
that cannot carry an identifier cannot leak one, no matter how a later filter is written.

The one exception is `LinkedItemId`, and it is computed **per viewer, server-side**: set only when
this particular caller may open that particular item. The page renders a link where it is present
and a plain image where it is not. It never works the rule out for itself.

### `/equipment/{id}` — one piece, to whoever is entitled to it

Personal gear had no detail page at all, and group gear had one reachable only under an
organization path — so the model page had nowhere to send a viewer for two thirds of what it lists.
One `[AllowAnonymous]` endpoint now answers for all of them, resolving the audience once:

| Audience | Sees |
|---|---|
| SuperAdmin | everything, including counters |
| Owner | ownership, serial, condition, holder — **not** counters |
| Group Administrator/Owner (their gear) | as above, **plus** counters |
| Equipment-permission holder | ownership, serial, condition, holder |
| Group member / member of a group it is shared with | ownership, but no serial |
| Public (listed, not retired) | the piece, with no mention of anyone |
| Anyone else | **404**, not 403 — an id must not be probeable |

Absence is **structural**: `Ownership?`, `Management?` and `Counters?` are nested optional records,
so a viewer who may not see the serial receives a payload with no slot for it rather than one
carrying six nulls that a later change might start filling in. A client that ignored every flag
would still learn nothing extra.

Counters follow the **membership role**, not the Equipment permission — a group may hand its
equipment role to somebody who is not an administrator, and Ben's audience for interest numbers is
administrators. The test that proves this is worth reading: the same person, with the permission and
without the role, gets `Counters == null`.

### Interest counters

Two lifetime totals on the item: page views and manufacturer-link clicks. `POST …/viewed` and
`…/link-clicked`, both `[AllowAnonymous]`, both **204 whatever happens** — including for an id that
does not exist, so the endpoint cannot be used to test whether one does. Retired gear stops
counting.

The client fires the link-click **after** the anchor's own navigation, never instead of it. A
counter must not cost the reader the thing they actually asked for, and a failed POST is invisible
by design.

Nothing is recorded about *who* looked. The numbers are totals only.

## Per-photo catalog opt-out

`ExcludeFromCatalog` is set on each photo, not each item, with a tick in the photo strip. A clean
product shot may be exactly what belongs on a model page while the one taken in someone's living
room is not — that is a per-photo judgement, and forcing it to be all-or-nothing would have made
people withhold both.

Hiding a photo keeps it on the item; it only leaves the pooled set. Captions are shown publicly, and
the help doc now says so.

The photo-visibility rule is one named method with five branches and a truth-table test class, so
the two byte-serving routes and the model page cannot drift into three subtly different answers.

## Also in this branch

- **Manufacturer link** (`WebsiteUrl`) on personal and group items, normalized server-side to an
  absolute `http`/`https` URL or rejected. Aggregated distinctly on the model page.
- **Navigation wiring**: catalog cards, `My Equipment` cards, and both organization grids now link
  to the item page; catalog and My Equipment also link the make/model through to its page.
- **`OrgEquipmentEditor` keeps its own photo list.** The parent's reload does not hand that window a
  fresh record, so the strip previously showed a stale tick after any photo change. It now re-reads
  through the unified item endpoint — which already serves equipment managers everything they may
  see, so there is no second projection to keep in step.
- **The photo strip renders for a custodian with no photos**, so there is somewhere to add the first
  one.

## Verification

Full solution build, **0 warnings, 0 errors**. Full suite: **4,334 passing, 0 failing**
(Web 2,056 · Video 1,787 · Repository 306 · Sidecar 185).

Every privacy guard here was proven by discrimination before being cited — the audience matrix was
verified by forcing `isCustodian = true`, which failed three tests; the counter rule by granting the
permission without the role. A test that passes against the broken code proves nothing.

**Still to do by hand** (Ben's own click-through, signed in):
- Open a model page in a private window and read the **JSON**, not just the UI — no owner traces.
- Confirm a click-through appears only when signed in with permission, and not otherwise.
- Confirm counters increment, and are absent from a plain member's payload.
- Confirm the rate-limit policy covers the two anonymous counter routes.

## One open question for Ben

Photos of items that are **not** publicly listed currently pool onto the model page by default
(anonymously, and unlinked for anyone not entitled to the item). The alternative is to make pooling
opt-in.

It is a one-line default flip **now** and an awkward migration once people have uploaded photos
under one rule. Worth deciding before this merges.

## Next

6c — owner FAQ and anonymous ask-the-owner questions. 6d — mutual loan feedback and ratings.
