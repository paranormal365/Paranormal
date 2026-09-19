# Request review & voting — "Under Review" stops being a status and becomes a decision

Ben, 2026-08-26: *"What happens when someone chooses 'Under Review' for a requested
investigation? It should be marked as a link in an internal message that the person needs to
vote on taking on a case."* Plus the follow-up rule: *"Any group who accepts the case first
wins."*

## What "Under Review" did before

Flipped an enum. `CaseController.UpdateRequestStatus` advanced the application's status and
returned 204. No message, no vote, no way for a reviewing member to see the client's photos —
and the accept flow cancelled only PENDING rival applications, so a group mid-review at another
table kept a live application to a request that was already someone's case, and was never told.

## The flow now

1. **Marking Under Review messages the group.** Eligible members (owners/admins + holders of
   `Case.Read`, direct or via role — the same grant the page checks, per the dead-end-clicks
   policy) get an internal message linking `/organizations/{org}/request-review/{request}`.
   Transition-only: re-saving the status does not re-spam.
2. **The review page shows everything the client submitted.** Description, address, client
   demographics (never the name), and every attached file with previews — text, photos, any
   type. `FileAudienceAccess` gained a clause: a file attached to a ClientRequest is viewable by
   active members of any org holding a LIVE application (not Rejected/Cancelled). The clause
   sits BEFORE the owns-no-cases short-circuit, because a group deciding whether to take its
   first case owns no cases — the flow test caught the first draft never being reached.
3. **Members vote.** `ClientRequestReviewVote`, one ballot per member per application
   (unique index), re-voting updates. Advisory by design: the tally informs whoever holds the
   accept grant; it accepts nothing on its own.
4. **First group to accept wins.** The polite check refuses a second accept; the genuine race is
   refereed by a unique filtered index (`UX_ClientRequestOrganizations_OneAcceptedPerRequest`) —
   at most one Accepted application can ever exist per request, so two same-instant accepts
   cannot create two cases for one home.
5. **Everyone hears the outcome.** Losing groups' reviewers: "no longer available." The client:
   "{Group} has taken on your investigation" naming their contact person when a manager was
   chosen, linking `/my-cases/{id}` where the Messages tab reaches the group.

`RequestReviewNotifier` holds all three messages and the one recipient rule.

## Trying it

The seeder now plants a **contested request**: Alice Nguyen's attic-hatch story offered to both
BenCo and MCSS, photo attached. Mark it Under Review in one group's Pending Requests, watch the
bell, vote on the review page, then Accept — and check the other group's messages.

## Found along the way

- The accept-cancels-only-Pending bug above.
- The refusal page conflated "signed out" with "not available" — opposite next steps for the
  reader; there is a distinct signed-out card now (found live when a host restart ate the session).
- The dev DB lost `features.publications` in the rebuild — restored; the route crawler is what
  noticed.
- The route crawler resolves `ClientRequestId` from the same org it crawls as, so the new page
  joins the parameterised walk.

## Tests

`RequestReviewFlowTests` — 11 tests: message send + no-respam, review door (grant/live
application/lost race), ballot rules, file access both ways, accept cancels mid-vote rivals and
messages losers + client, second accept refused with exactly one case in the world. 3387 pass.
