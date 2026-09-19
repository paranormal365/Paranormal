# Editor → feed — item 186 F7

Ben's two mid-build requirements, honored together: video-editor output flows into the feed with
its case lineage intact, and organizations control which posts link back to them.

## The flow

Export a render → the phase-176 destination prompt now offers **Post to the feed** (only when the
server's own `CanPost` says this person may — the button never invites a refusal). The HOST page
owns the confirmation dialog, because case lineage and privacy wording are its knowledge, not the
editor's: an ordinary case is named, a **private-engagement** case (item 184) says so in those
words and requires the explicit tick. The upload then goes through the feed's OWN multipart door —
`POST api/feed/posts` with `SourceCaseId` (+ the consent flag) — so a render is ingested,
stripped, screened, and feature-scored exactly like any other upload. Deliberately **no separate
editor upload path**: the plan's `existingUploadFileId` idea was dropped for this because the
bytes are already in the browser and one door is one door.

## Consent — the one door out of item 184's promise

`FeedPostConsent`: APPEND-ONLY (post id via SetNull — the agreement outlives the post), CaseId,
who agreed, when, and the wording version they saw. No tick ⇒ 400 with the sentence, nothing
lands. The case-access check answers "isn't available" identically for a missing case and a
refused one, so the door confirms nothing to a prober.

## Attribution — Unclaimed shows NOTHING

`OrgMessage.AttributedOrganizationId` + `AttributionState` (Unclaimed=0 default / Claimed /
Declined). The projection looks the org's name up ONLY for Claimed posts — absence is structural,
the same discipline as the private-location records. Group admins (`Organization`/`Update`
permission, the same check the ads use) get `/organizations/{id}/feed-attributions` — queue page
with a door from group Settings — and one-click **Claim** (name + link + "Group verified" badge)
or **Decline** (post stays, no link, forever changeable). A claim writes a `GroupClaim` Confirmed
labelled example into F6's loop — the group vouching IS the strongest label the classifier gets —
once per transition, never on an idempotent re-click.

## Badges → ranking

`Group verified` (claimed) and `Moderator reviewed` (a PERSON approved the media —
`MediaReviewedByAppUserId` set — as opposed to the automatic screener). Both render on the card
and multiply the For You score by ×1.15 each, stacking; pinned by a test that also proves a
double-badged post still loses to a genuinely liked one. A thumb on the scale, not a bypass.

## Verifying

- 11 new tests (`FeedAttributionTests` + the badge-lift test): the consent gate both ways with
  the row's contents checked, lineage-without-media refused, the stranger probe, unclaimed
  emits nothing even to the author, claim → name+badge+example, decline → nothing + post
  untouched, plain-member 404s, re-claim writes no second example.
- Live, flag flipped then restored: post with `SourceCaseId` against a real private-engagement
  case refused without the tick / recorded with it; claim via the org endpoint; the card shape
  checked on the anonymous path.
- The editor prompt/dialog UI walk lands in F10's Playwright batch with the rest of the arc.
