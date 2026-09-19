# Test posts on the feed — a way down for what an e2e run leaves behind

Branch: `feature/admin-test-posts`, cut from `develop` at `5065e9e`.

## The problem

Development and production share one database, so a Playwright run's feed posts land on
ishaunted.com's front page. On 2026-09-02 the first page of the live feed was 184 lines of
"Playback check" and "e2e post" — 172 from 08/31, 12 from 09/01 — and the four curated posts the
App Store screenshots show were at ranks 196–199. The feed has no delete, and the only route to
hiding a post was a member reporting it and a moderator upholding the report, one post at a time.

## What this adds

**`/admin/test-posts`** (Administration → Test Posts, SuperAdmin only): every public-feed post by
a seeded account, newest first, with checkboxes, check-all, **Hide selected** behind a confirm, and
**Unhide selected**. Hidden, not deleted: hiding is what a moderator already does, every feed
query already filters on `HiddenUtc`, and it is reversible from the same page.

**What counts as a test post** is a fact about the author, not a guess about the text: the
account's email is on a domain only the seeder uses (`benco.dev`, or `example.com`, which is
reserved and can belong to nobody). Matching on words like "test" would eventually hide a real
person's post that happened to say it. The four curated posts are by the same accounts, so they
are on this list too — which is why it has checkboxes rather than one "hide them all" button.

API: `GET api/admin/feed/test-posts`, `POST …/hide`, `POST …/unhide` (`{ ids }`). Hiding a
top-level post takes its visible replies with it and says how many. An id that is not a seeded
account's post refuses the whole batch with a sentence and changes nothing — this door has no
report and no second pair of eyes, so it must not trust the caller's list.

## How it is proved

- `AdminTestPostControllerTests` (4): only seeded authors are listed, whatever a real person's post
  says; hide takes replies and unhide puts back only the ids given; a real person's id refuses the
  batch whole and leaves the seeded post untouched; an empty choice is refused.
- `TestPostsTests` (browser, 3): SuperAdmin sees the list or the all-clear and check-all selects
  every row; a member is sent away; and — behind `BEN_TEST_POSTS_HIDE=1` — one post is hidden, a
  visitor's feed no longer carries it, and it is put back.

Verified on the side database `IsHauntedDb_player` with three seeded posts: all three browser
tests pass, the hide round trip included.

## On production

Deploy, open Administration → Test Posts, check-all, **untick the four curated posts** (AverageBen's
welcome, Sarah's night walk, James's EVP, Sarah's cold spot), Hide. Nothing is deleted; anything
hidden by mistake is one click back.
