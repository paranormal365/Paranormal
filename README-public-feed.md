# The public feed — phase 8

A short-form public feed: anyone signed in can post, follow people, mention them with `@name` and
tag posts with `#tag`. Backlog item on the nine-phase plan; the last big build before publications.

**Off by default.** Everything is behind `features.public-feed`, which a SuperAdmin turns on. The
API 404s wholesale when it is off — not 403, because a disabled feature should not be discoverable
by the shape of its refusal.

## Already merged, before this branch

The back end landed alongside the accounts work, because `@mentions` needed real `@names` first.

- **Schema** — `OrgMessage` with `ChannelType.PublicFeed` (nullable OrganizationId and parent-based
  threading were already exactly a feed post and its replies), plus `HiddenUtc`/`HiddenByAppUserId`,
  and four new tables: `UserFollow`, `OrgMessageMention`, `OrgMessageHashtag`, `OrgMessageReport`.
- **`FeedController`** — seven endpoints: paged feed (`all` / `following`, optionally by tag), one
  thread, a profile, create post, report, follow, unfollow.
- **`FeedTextParser`** — finds `@names` and `#tags`. In `Ben.Data.Common` because two sides need the
  same answer: the API fills the tables when a post is written, the website turns the same text into
  links when it is read. Two parsers would drift, and the way they would drift is a post whose
  visible links disagree with the notifications it sent. 25 tests.

## What this branch builds

**Server**
- Admin moderation: `GET /api/admin/feed/reports` and resolve (dismiss or hide).
- Mention notifications, joining the existing summary buckets.
- `FeedController` tests — the parser has them, the controller does not.

**Client and UI**
- `IBenFeedClient` slice on `IBenAdminClient`, with its adapter half.
- `/feed` — composer on top, then the posts. Plain textarea: short-form is the point, and a
  rich-text editor would invite the wrong thing.
- `FeedPostCard` — linkifies through the `MessageBody` seam built in phase 5, so mentions and tags
  become links in one place rather than three.
- `/feed/tags/{tag}`, follow buttons, and an administrator's moderation queue.
- `FeatureGate` and a nav entry, both behind the flag.

**Help** — a feed page and a moderation page, with `HelpLink`s, in this branch rather than after.

## Decisions already made, not to be relitigated

- **Anyone signed in may post.** Ben's call, and it is what makes moderation part of the feature
  rather than an optional extra.
- **Reports never hide anything by themselves**, and no number of them does. Hiding is an
  administrator's act — otherwise a group who dislike a post could remove it between them, which
  moderates whoever is least popular rather than whatever breaks the rules.
- **Hidden, not deleted.** A deleted post takes its replies, its reports and the record of the
  decision with it, so the next administrator asking "what happened here" finds nothing.
- **A mention resolves to exactly one account**, via the permanent `@name`. Stored as an id, so a
  display-name change never repoints an old mention at somebody else.

## Verifying

```bash
dotnet test Ben.Web.Tests --filter "FullyQualifiedName~Feed"
dotnet test Ben.Web.Playwright -p:IsTestProject=true --filter "TestCategory=Feed"
```

The site runs on **5078** from `Ben.Web.Website`, the API on **5252**, each from its own project
directory. The feed flag must be **on** for its tests to see anything — they turn it on themselves
and put it back.
