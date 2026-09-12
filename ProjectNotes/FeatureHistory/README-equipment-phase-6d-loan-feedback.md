# Equipment Phase 6d — Mutual Loan Feedback and Ratings

Branch: `equipment-phase-6d-loan-feedback` · Backlog item **#55**, last of four Phase-6 branches.
Stacked on 6a, 6b and 6c — merge in order.

Migration: `AddEquipmentLoanFeedback` (applied to dev SQL; `scripts/create-database.sql`
regenerated).

## Why

Ben's ask, in his words: *"when someone requests to borrow something, the loaner should be able to
see any past comments or ratings for the requester for any previously borrowed equipment. So we know
they are trustworthy and respectful with equipment."*

Somebody deciding whether to hand over a £400 recorder should be able to see that the last three
lenders got it back on time and clean. The mirror of that — what borrowers thought of a lender — is
the same feature pointed the other way, and closes item #55.

## The one rule

**The subject never sees it.** Every read excludes them, and there are **no notifications on this
feature at all** — telling somebody that feedback about them exists is most of the way to showing it
to them. That absence is deliberate and is the reason it is called out here rather than left as a
gap somebody later "fixes".

Both exclusions were verified by deleting them and watching the guards fail:

| Deleted | Test that fails |
|---|---|
| the `CanReviewCheckoutAsync` gate on borrower-feedback | `ABorrowerCannotReadTheirOwnFile` |
| `if (item.OwnerAppUserId == userId) return NotFound()` | `ALenderCannotReadWhatBorrowersSaidAboutThem` |

## The deliberate asymmetry

The two directions differ in exactly one way, and it is on purpose.

**Lender about borrower → attributed.** This is lender-to-lender context, and an unattributed
warning is hard to weigh: you want to know whether it came from someone who lends constantly or
someone who has lent once. `BorrowerFeedbackRecord` carries `AuthorDisplayName`, and a test asserts
that it *does* — so the asymmetry stays a decision rather than becoming an accident somebody tidies
away.

**Borrower about lender → unattributed.** A borrower saying a lender was unreasonable has more to
lose by being named than a lender does. `LenderFeedbackRecord` has no author field at all.

Flipping either is one projection field, if Ben changes his mind once he has seen it in use.

## Ratings

Optional, 1–5, beside the free text — somebody who wants to say something but not put a number on it
should not be forced to. The aggregate is the part worth reading carefully:

- **No average below three ratings.** One sour rating rendered as "2.0" reads as a verdict when it
  is one voice. Below the threshold the panel says *"1 rating — too few to average"* and shows the
  comments, which is the more honest thing to read at that sample size anyway.
- **The count always travels with the average** ("4.6 across 9 ratings"), so a reader can weigh it.

The threshold is `LoanFeedbackSummaryRecord.MinimumRatingsForAverage`, one constant rather than a
number repeated in two panels.

## Product reviews

The one part of this table that is ever public. A **borrower** may review the gear itself, separately
from the person, and it appears on the make/model page without their name. A lender cannot — a lender
reviewing their own gear on its public page would be an advertisement, and that is a 400.

Reviews follow the same public-only rule as the FAQ aggregate: only from publicly-listed copies. The
test unlists the item mid-run and watches the review leave with it — the review is about the
product, but its presence would still say that somebody nearby owns one.

## Moderation

`/organizations/{OrgId}/equipment-feedback`, a page (per the standing rule), for group
**Administrators and Owners** and SuperAdmin — not the Equipment permission, which is about looking
after kit rather than adjudicating between people.

It is the only surface that names both sides, because acting on a complaint means knowing who wrote
what about whom. Removal is a hard delete, audited by the platform's own interceptor, and scoped:
even a moderator can only remove rows touching their own group.

The client call deliberately returns **null on refusal** rather than an empty list. "Nothing here
yet" and "not yours to moderate" are different things to tell somebody, and the usual `?? []` would
have collapsed them into the wrong one.

## Endpoints

| Route | Who |
|---|---|
| `POST api/equipment/checkouts/{id}/feedback` | either party, `Returned` only, once per side |
| `GET …/feedback-state` | either party — drives whether the form appears |
| `GET …/borrower-feedback` | whoever may review **this** request; excludes this loan's own row |
| `GET api/equipment/items/{id}/lender-feedback` | anyone who may see the item **except** whoever lends it |
| `GET api/equipment-catalog/models/{id}/reviews` | anonymous; publicly-listed copies only |
| `GET/DELETE api/organizations/{id}/equipment-feedback[/{feedbackId}]` | group Administrators/Owner, SuperAdmin |

Reading someone's history is **scoped to the request being decided**, not offered as a general
lookup. You can read it because they have asked you for something, not because you were curious.

## Verification

Full solution build, **0 warnings, 0 errors**. Full suite: **4,367 passing, 0 failing**
(Web 2,089 · Video 1,787 · Repository 306 · Sidecar 185) — 14 new.

The help-link test caught two anchors I had referenced before writing their sections, which is
exactly what it is for.

**Still to do by hand** (Ben, two accounts):
- Return a loan, leave feedback from both sides, confirm neither sees the other's.
- Confirm the approver sees prior borrower feedback on the next request, and that the borrower
  viewing that same request sees nothing.
- Confirm a borrower's product review appears on the model page, unattributed.
- Confirm the moderation page refuses a plain member and lists both names for an Administrator.

## This closes item #55

Six phases: personal inventory → per-group sharing → group-owned gear and the Equipment permission →
the checkout lifecycle → condition photos and history → model pages, counters, FAQs, questions and
feedback.

Deliberately left for later, recorded in `ProjectNotes/Future-Improvements.md`: rental for money
(#85's territory), SuperAdmin cross-group equipment browse, review pagination, time-series counters,
and future-dated reservations.
