# Refused uploads go to a person — unless the person is spamming (item 217)

**Branch:** `feature/feed-refusal-spam-pause`

## Ben's ask, 2026-09-04

> Before it denies it, can it just submit it to admin, superadmin or moderator for approval
> instead of outright denial? ... Unless the person is spamming it.

And, just before: the NSFW screener is only checking for nudity, right? The site is ghost
hunting; scary stuff must be allowed.

## What was already true

Nothing was ever denied outright. The screener's two upper bands both set `Held`, which is the
Held pile on **Administration → Content → Feed Media**, where Approve publishes. The post is always
created; the author is only told the upload is being checked. And the model
(`Falconsai/nsfw_image_detection`) is a two-class normal/nsfw classifier trained on pornography —
it has no notion of violence, gore or horror.

## What this branch adds

- **`OrgMessage.MediaScreenerScore`** (double?, migration `AddOrgMessageScreenerScore`): the
  classifier's probability stored as a number, set by the create path and the pending sweep. The
  spam rule counts from it rather than parsing the note, which would stop working the day the
  wording changed. `FeedMediaVerdict` gained `Score`.
- **`FeedMediaAbuse`** — the rule: three posts by one author inside 24 hours that the screener
  scored at or above the block threshold (0.85) *and that are still Held* pause that author's
  media uploads. Checked in `FeedController.CreatePost` **before ingest**, so a paused account
  cannot fill the disk. Text posts are unaffected. The message says nothing about which check
  tripped.
- **Why "still Held"**: a moderator approving one of the three changes its state, so the pause
  lifts at once — the rule reads what the queue decided. Borderline scores never count.
- **Why a window, not a flag**: nothing is written to the account; the pause ends on its own when
  the oldest refusal ages out, so there is no switch to forget.
- **Queue badge**: `FeedMediaReviewItem.AuthorRefusalsLast24h`; the moderation page shows
  "Uploads paused — N refusals today" at three and "N refusals today" at two.

## Tests

`FeedControllerTests` (item 217 region): a confident refusal is held with its score, not denied;
three pause the fourth (and no fourth file or post exists); a paused account still posts text;
two do not pause; borderline never counts; a refusal older than a day no longer counts; a
moderator's approval lifts the pause; manual screening never pauses. `NsfwScreenerTests`: the
verdict carries the score. Suite 4,051/0. The pause guard was proven to discriminate: with the
check removed, the three-refusals test fails.

No Playwright test: the isolated e2e stack has neither the 87 MB model nor a fixture the
classifier would refuse, so nothing in a browser could exercise the pause honestly.

## Docs

`moderating-the-feed.md`: the Waiting/Held distinction, "scary is fine", and the pause rule.

## Deployment

`scripts/create-database.sql` regenerated; the migration must be applied to the site's database
before the API is deployed (the startup log says DATABASE IS BEHIND otherwise).
