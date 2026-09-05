# Second sweep — what a hunt for more issues turned up

2026-09-04, after the case-lifecycle walkthrough and the client-funnel fix. Everything below was
run against a throwaway database (`IsHauntedDb_hunt`) and its own uploads directory, both destroyed
afterwards; the four real databases were confirmed still standing. No production data was read or
written.

Nine findings. Two are serious, and one of those is already fixed on a branch.

---

## 1. A group message can run script in every reader's browser — FIXED on a branch

**Proven, not inferred.** A member posted a broadcast whose body was
`<p>Kickoff meeting Friday.</p><img src=x onerror="window.__xssProof=1">`. Another member opened
it, and the marker was set: `window.__xssProof === 1`. The script ran in the reader's authenticated
session, on the site's own origin, in the ordinary Messages tab.

Three things line up to allow it:

- `OrgMessageController.Send` stored `request.Body.Trim()` with no cleaning. The CMS, publications,
  the calendar and public events all sanitise on the way in through `ICmsMarkupSanitizer`; messages
  were the surface that never did.
- `MessageBody.razor` renders a message body as `MarkupString` when `Html` is set, which
  `OrgMessages` and the notifications page both set. The component's own comment says a sanitiser
  belongs there "when one is added".
- There is no `script-src` policy to fall back on. `Program.cs` sets only `frame-ancestors 'none'`,
  deliberately, with a note that a real policy is separate work.

Reach: any active member of a group, against every recipient of that message, including its owner
and administrators. The public feed is **not** affected — `FeedText` refuses `MarkupString` on
purpose, and says so in a comment.

**Fixed on `fix/message-html-injection`** (not merged). Bodies are sanitised on write with the
sanitiser the CMS already uses; two tests assert the handler and the `<script>` are gone and the
paragraph survives, and both fail against the old code. Re-verified live: the same payload now
stores as `<p>Second kickoff.</p><img src="x">`.

**One thing that branch does not do.** Bodies stored before it still hold whatever was posted, and
they are still rendered as markup. A live database needs a one-time clean of existing `OrgMessage`
rows before this is really closed. That is your call, so I left it.

## 2. Notification bodies interpolate typed text raw — FIXED on the same branch

`UserMessage.MessageBody` is rendered as markup so that platform notices can bold a group's name.
`NotificationText.Safe` exists precisely to encode the fragments people type before they go in, and
its own remarks say to use it for "display names, equipment names, decline reasons". Five files
used it. Five did not:

- the membership decline note and the group name, shown to the applicant
- the experience type a member names, shown to **site administrators** — the widest reach here, a
  group member's typed string landing in a SuperAdmin's rendered notification
- both group names in a merge notice
- an ad's headline and its decline reason

All four are now encoded, with a test on the decline note that fails against the old code. The
support-ticket path was already correct and is the pattern the others now follow.

## 3. The paywall is one click away from off

The billing page offers **Subscribe** to a brand-new group, quotes `Free — month $0.00`, and the
button works: the banner says "You're subscribed", and the plan reads **Active**.

What happens underneath is that a one-member group prices into the Free band at zero, so `payable
== 0`, so checkout takes the branch written for 100%-off trial coupons — a real subscription, no
card, no Stripe. Every paywall on the site then asks `PaidPlan.CoversOrganizationAsync`, sees
Active, and stands aside.

Demonstrated on the throwaway stack: after that one click the group opened membership applications
and accepted **five** members. No charge, and no overflow seat was offered either, because the Free
tier carries no per-extra-member price, so `OverflowSeats` returns before it can offer one. The
same Active row also lifts the 2 GB storage cap, private field sessions and private event evidence,
which all read the same rule.

Two smaller things sit inside this one:

- The banner says "the coupon covered this period in full" when no coupon was entered.
- The ladder and the rule disagree about what free means. The Free tier's band is 1–3 members;
  `PaidPlan.WhyCannotAddMemberAsync` says the second member needs a plan.

**ANSWERED 2026-09-05: production was never exposed.** The live price list is readable without a
database connection — it is what the pricing page is served from — and every band on it is priced:

| Band | Members | Monthly |
|------|---------|--------:|
| Small Group | 1–3 | $20 |
| Standard Group | 4–10 | $40 |
| Large Group | 11–25 | $60 |
| Enterprise | 26+ | $100 |

There is no $0 band, so the zero-payable path was never reachable on ishaunted.com. The hole was
real on a **fresh** database, whose seeded ladder carried Free 1–3 at $0 — which is where it was
demonstrated, and which the seeder no longer creates. The advisory added to the price-bands editor
stays silent against the live list, because nothing there is priced at nothing.

## 4. The refusal that should sell a plan blames the wrong thing

Ticking **Accepting Membership Applications** on a free group and saving shows:

> Save failed. The URL name may already be in use, or you may not have permission.

The server's own sentence is much better and is thrown away: *"Working with other people is part of
a paid plan — a free group is just you. Everybody already here stays; adding somebody new needs a
plan."* The owner is told they might lack permission on their own group, pointed at a name clash
that does not exist, and given no mention of a plan and no link to billing — which is two clicks
away under Settings.

The cause is structural: `WebApiClient.PutAsync` and `PostAsync` return `default` on any
non-success, so the body never reaches the page, and the page writes a guess. Seven UI sites guess
a reason this way. The client already has the shape that fixes it — `ApplyForMembershipAsync`
returns `(Result, Error)` — so this is adoption, not invention.

## 5. Signing in loses where you were going

Twice, with two different new accounts: sign in, get sent to the welcome wizard, click "Skip for
now", and land on the home page rather than the page that was asked for. Navigating to a deep link
while the wizard is pending has the same effect — the wizard replaces the destination and never
gives it back.

## 6. Nothing on the group calendar says how to add an event

The Calendar tab has no create button. Events exist and matter — public events, walking tours,
evidence from attendees — and the only way in is knowing to double-click an empty slot. The editor
that opens is good; the door is invisible.

## 7. `/logout` is a 404

Signing out works from the profile menu. The obvious URL renders "Page not found" rather than
signing out or redirecting.

## 8. One membership door skips the plan check

`PUT /api/organizations/{id}/security/users/{userId}/membership` creates a membership with proper
authorisation (owner or administrator) but no `WhyCannotAddMemberAsync` call, unlike the two doors
that have one. No site UI uses this route, so it is an API-only inconsistency rather than a live
bypass — but the gate is meant to be about the group, not about which door was used.

## 9. Small things, in one place

- **35 distinct compiler warnings only appear on `-t:Rebuild`.** An ordinary `dotnet build` prints
  0 because nothing recompiles. Mostly XML-comment defects, two xUnit analyzer rules, and a real
  `CS8604` possible-null on `IFeedMediaScreener.ScreenAsync`'s `storagePath`.
- **Equipment make list is not narrowed by category** — picking Audio Recorder still offers
  Manfrotto tripods and BaoFeng radios, though the model list that follows *is* filtered. A
  category lives on the model rather than the brand, so the narrowing is derivable: the makes that
  have a model in the chosen category. A branch named `feature/equipment-make-category-filter`
  already exists and is contained in develop, so this may be a decision rather than an omission.
- **After sharing gear with a group, nothing on the card says so.** The state is only visible by
  reopening the dialog.
- **The equipment lending rules held up.** An item shared with a group but with all three borrow
  audiences unticked correctly refused: "The owner isn't lending this out." Worth recording that
  this one was tested and is right.

---

## What I would do first

1. Merge `fix/message-html-injection`, then decide about cleaning the message bodies already stored.
2. Decide what Subscribe should do when the quote is zero and no coupon was used. The narrow fix is
   to refuse a zero-payable checkout unless a coupon actually priced it that way; the wider question
   is whether a $0 tier should be subscribable at all.
3. Carry the server's refusal text through `PutAsync`/`PostAsync` and adopt it at the seven guessing
   sites. Finding 4 is the one that costs money every time it happens.
