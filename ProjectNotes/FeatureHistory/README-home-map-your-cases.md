# The home map shows the cases you can already see

Ben, 2026-09-09: *"plot the viewer's cases on the home map. plot ones where the end user has access
to them or they are already public."*

The discovery map returned `IsPublic` cases and nothing else, so a member whose group had work on
it opened the front door to an empty map. On the live site that was literally every signed-in
person: the one case there is a private residence.

## What it does now

`GET /api/public/cases` stays `[AllowAnonymous]`, and a visitor with no token gets **exactly** what
it always returned. A caller who sends one additionally gets the cases they could already open
elsewhere.

The client change is one word: the adapter now calls `GetAsync` instead of `GetAnonymousAsync`.
`Auth()` attaches a bearer only when a token exists, so the same method serves a member and a
stranger without a branch.

## The three doors, each the one its own screen already uses

Not looser restatements of them — the same calls, so this endpoint can never be the generous one:

| Who | Gate | Where that gate already lives |
|---|---|---|
| A group's people | `HasAccessAsync(Case, Read)` | `CaseController.CanReadAsync` |
| The client who asked | the originating `ClientRequest.AppUserId` | `MyCaseController` |
| A co-client | a `CaseClientAccess` row | `MyCaseController` |

**Membership alone is not enough**, and that is the test worth keeping: a plain Member with no
grant on Cases sees nothing, and the same person sees the case the moment the read is granted — so
the emptiness is the gate working rather than the seed being wrong. A mutant that asks "are they a
member" passes every other test in the file and fails that one.

SuperAdmin sees everything, as everywhere else. **Proposed cases are nobody's pin**, on either
side: a case nobody has agreed to yet is not a place, and `MyCases` hides it too.

## Coordinates stay approximate, including your own

One code path for everybody, so there is no branch on which a real address could escape. A member
who needs the address has the case page; a map is for finding, not for navigating to somebody's
door. Tested: your own case comes back at a grid-cell centre like everyone else's.

## And it inherits the viewport

This landed after item 223, so the widened set is already bounded by what is on screen, capped, and
reported. A group with three hundred cases does not turn the front page into a download.
