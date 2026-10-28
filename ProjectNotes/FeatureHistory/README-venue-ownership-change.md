# Venues: asking for a change of ownership after a place is claimed

Ben, 10/28/2026: "On locations, after they are claimed, there should still be a button or link at
the bottom allowing someone to submit a request to consider ownership change. For instance, someone
entered The Thomas House and they claimed it, but then the Coles click the claim ownership link and
say they are the ones who own The Thomas House. I know the Coles and they do own it, so I would need
to send the current claimant a letter notifying them the owner has claimed the location and then
give the Coles the account to manage their property, and the current claimant needs to be notified
the ownership has changed."

Website + API work. Planned here; it goes on its own branch after the audio/EVP plan is through,
like the LiDAR website phase. The LiDAR plan's scan custody follows the venue automatically, so this
is a prerequisite for a fair handover there too.

## What exists

- The verified venue is `OrganizationVenueProfile.VerifiedUtc != null`, one per place
  (`VenueGrants.VerifiedVenueAtAsync`).
- A claim is `VenuePlaceClaim` (`ClaimantRole`, `Evidence`, `State`, a code to a `PlaceContact`,
  `ObjectionsCloseUtc`, objection fields, decision fields). States: Pending, Proved, Approved,
  Refused, Withdrawn, Contested.
- The claim link on the place page (`PlaceView.razor:285-297`, `#place-claim-line`) shows **only
  when `_venue is null`**. Once a place is claimed there is no door at all.
- `VenueClaims.ApproveAsync` refuses when another group is verified there: "… is already confirmed
  as the venue there. Remove that first if it is wrong." That refusal reaches only a SuperAdmin on
  `/admin/venue-claims`, and the removal is a manual step with no letter to anyone.
- Letters already go out from `VenueClaimController` (a code to the contact; objections to
  interested groups via `_messages.SendAsync` and `_email.SendAsync`).
- Admin screens: `SuperAdmin/AdminVenueClaims.razor`, `ClaimSummary.razor`; the group's side:
  `Manage/Venue/OrgVenueClaim.razor`, `OrgVenue.razor`, `VenueClaimObjection.razor`.

## The design

A **change-of-ownership request** is an ordinary `VenuePlaceClaim` made while another group is
verified, flagged as a challenge, decided by a SuperAdmin, with letters at every step. No new
entity: one column and two states.

### Data (one migration)

- `VenuePlaceClaims.ChallengesOrganizationId` (guid?, no FK): the verified venue org at the time
  the claim was made. Non-null = this is a change-of-ownership request.
- `VenuePlaceClaims.CurrentVenueToldUtc` (datetime2?): when the current venue was written to.
- `VenuePlaceClaims.CurrentVenueReplyNote` (nvarchar(2000)?), `CurrentVenueRepliedUtc`.
- `VenueClaimState` gains `ChallengeOpen = 6` (the current venue has been told and has the reply
  window) and `Transferred = 7` (approved as a change; the old venue was moved aside).
- `OrganizationVenueProfiles.VerifiedUtc` is nulled on transfer and
  `OrganizationVenueProfiles.TransferredAwayUtc` (datetime2?) + `TransferredToOrganizationId`
  (guid?) record where it went, so the old group's venue page can say so.

### Rules

1. **The door is always there.** On a claimed place the line reads *"The Thomas House is run by
   The Cole Family on this site. Not right? **Ask us to look at who owns it**."* (`#place-ownership-line`,
   one link per group the reader administers). Signed-out visitors see the same sentence with a
   sign-in link. Nothing on a place that is a private residence (claims do not apply there today).
2. **Proof is required up front.** The request form is the claim form with the evidence box
   mandatory and a role picked (`ClaimantRole`), plus the same contact code step when the place has
   a public contact the challenger can receive a code at. A request with no evidence is refused with
   the sentence.
3. **The current venue is told the day the request is made** (letter A, below), with a **14-day
   reply window** (`VenueClaims.ChallengeReplyWindow = 14 days`, state `ChallengeOpen`). They can
   reply with a note (`CurrentVenueReplyNote`) or withdraw their own venue (which decides it).
4. **A person decides** — a SuperAdmin on `/admin/venue-claims`, in a new **Ownership requests**
   tab. Nothing is automatic: `VenueClaimJob` never approves a challenge, and the reply window
   closing only marks it "ready to decide". The admin sees both groups' evidence side by side, the
   place's history (events, rooms, sessions by each group), and the contact code result.
5. **Transfer** (`VenueClaims.TransferAsync`): the old profile's `VerifiedUtc` → null,
   `TransferredAwayUtc` / `TransferredToOrganizationId` set; the new org gets its profile verified
   (`ApproveAsync` minus the "already confirmed" refusal, which is what a transfer is); every
   `OrganizationVenueGrant` the old venue made is revoked (`RevokedUtc`); scan custody (LiDAR plan)
   follows because it reads the verified venue; **the old group keeps its own events, rooms, cases
   and files** — it loses the venue role, not its work. Audited.
   **Why this matters** (Ben, 10/28/2026): the owner decides what is public. The verified venue
   reviews place files (`PlaceFileReviewController.Reviewable`), approves archive media, and — in
   the LiDAR plan — holds scan custody and the *Show the walkthrough on the place page* switch.
   The Coles would want The Thomas House public; a homeowner would not. All of those read the
   verified venue, never a copied flag, so a transfer moves every one of them at once, and the
   old venue's earlier "show on the place page" choices are re-asked of the new owner: on transfer
   every scan's `ShownOnPlacePage` is reset to off, and the new venue is told so in letter D.
6. **Refusal** keeps everything as it was; the challenger gets letter D with the admin's note.
7. **One open request per place at a time.** A second group asking while one is open is told so
   and may add its own request to the queue (state Pending, `ChallengesOrganizationId` set), which
   the admin sees beside the first.

### Letters (email + the site message, same text; plain, no mechanics)

- **A. To the current venue, when a request is made:** "Somebody has asked us to look at who runs
  The Thomas House. [Group] says [role, their words]. You have until [date] to reply on your venue
  page. If you don't, a person here will decide from what both sides have said."
- **B. To the challenger, when the request is made:** "We've told the group that runs The Thomas
  House on this site, and we'll decide after [date]. We'll write when we have."
- **C. To the old venue, on transfer:** "The Thomas House is now run on this site by The Cole
  Family. Your events, rooms, cases and files are still yours; the venue page and its approvals now
  go to them. If you think this is wrong, reply to this message." (Ben's words: "the current
  claimant needs to be notified the ownership has changed.")
- **D. To the challenger, on the decision:** transfer → "The Thomas House is yours to manage on
  this site. Here is your venue page."; refusal → "We looked, and we're leaving The Thomas House with
  [group]. [note]".
- **E. To the current venue when they let the window pass:** none beyond A; the decision letter C
  or nothing.

## Screens

- `PlaceView.razor`: the ownership line (rule 1); `OrgVenueClaim.razor` reused for the request
  with `?challenge=1` (title "Ask for a change of ownership", evidence mandatory, a banner naming
  the current venue).
- `Manage/Venue/OrgVenue.razor`: a **Someone has asked about ownership** card while a challenge is
  open — the challenger's role and evidence, the date, a reply box, **Reply**, and **This isn't our
  venue** (withdraws the venue; decides the challenge in the challenger's favour without an admin).
  After a transfer: "This place was handed to [group] on [date]."
- `SuperAdmin/AdminVenueClaims.razor`: **Ownership requests** tab; `ClaimSummary.razor` shows both
  sides; **Transfer to [group]** / **Keep with [group]** with a note.

## API

| Route | Who | Does |
|---|---|---|
| `GET api/organizations/{orgId}/venue-claims/start?place=` | org admin | Already exists; now also answers when a venue exists, with `currentVenue` and `challenge: true`. |
| `POST api/organizations/{orgId}/venue-claims` `{ placeId, role, evidence, challenge: true }` | org admin | Refuses without evidence; sets `ChallengesOrganizationId`; sends A and B; state `ChallengeOpen`. |
| `POST api/organizations/{orgId}/venue/challenges/{claimId}/reply` `{ note }` | the current venue's admins | Records the reply. |
| `POST api/organizations/{orgId}/venue/withdraw` | the current venue's admins | Nulls `VerifiedUtc`; any open challenge for the place is approved by transfer; letters C/D. |
| `POST api/admin/venue-claims/{claimId}/transfer` `{ note }` | SuperAdmin | `TransferAsync`; letters C and D. |
| `POST api/admin/venue-claims/{claimId}/refuse` | SuperAdmin | Exists; now sends D for a challenge. |

## Tests

- `VenueClaimsTests`: a challenge on a claimed place is accepted with evidence, refused without;
  `ApproveAsync` still refuses a plain claim where a venue exists; `TransferAsync` flips the two
  profiles, revokes grants, keeps the old group's events/rooms/cases; `VenueClaimJob` never
  approves a challenge; a venue withdrawing decides an open challenge; one open request per place.
- Controller tests for each route's who-may; the letters are sent to the right people (the fake
  mail sink), once each.
- Playwright: Dana's group claims the seeded Thomas House; the Cole Family (seeded as a second group)
  asks for a change from the place page; Dana sees the card and replies; the SuperAdmin transfers;
  both groups' venue pages read right; the place page names the Coles; Dana's events are untouched.
- Help: `organization-administration.md` "When somebody else claims your venue" + "Asking for a
  change of ownership"; `site-administration.md` "Ownership requests".
