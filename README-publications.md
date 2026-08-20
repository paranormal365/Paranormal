# Publications — phase 9

The last of the nine phases. A group can run a **publication**: a chronological series of long-form
posts that people subscribe to. The "paranormal Substack" Ben described.

**Off by default** behind `features.publications`. The API 404s wholesale when it is off.

## What it is, and what it is not

A publication is **not** an organisation's CMS pages. Pages carry site structure — an About page, a
Services page — and are edited in place. Publication posts are **chronological, subscribable, and
never edited into each other**: what somebody read last month stays what they read. Hence new
tables rather than reuse of `OrganizationPage`.

It is also not the feed. The feed is short-form, public, and by any signed-in person; a publication
is long-form, authored by a group, and read by people who chose to follow it. `TelerikEditor` is
right here, and wrong there — long-form is its home.

## Free now, paid-ready

Monetisation (#85) is not built and no billing exists. But the shape has to allow for it, because
retrofitting a paywall means changing what is already published.

- `PublicationPost.RequiredTier int?` — **null means free**, which every post is today. **Nothing
  writes a non-null value.**
- `PublicationSubscription.Tier int?` — the same, on the other side.
- The public reader already withholds the body of a tiered post and serves the excerpt instead. That
  path is written and tested now, while it costs nothing, rather than being bolted on later against
  live data.

## The anonymous path is the product

A publication nobody can read without an account is a newsletter with no readers. The public
controller is `[AllowAnonymous]`, and **its tests sign in as nobody** — per the standing rule here
that an author always sees what a visitor cannot. That rule was earned: it has caught this exact
class of bug more than once.

## What this branch builds

**Schema** — `Publication`, `PublicationPost`, `PublicationSubscription`, one migration.

**API**
- Org-scoped authoring: permission-gated CRUD, plus publish and unpublish.
- `/api/public/publications…`, anonymous, serving only published posts.
- Subscribe, unsubscribe, and the caller's own subscriptions.

**UI**
- Authoring on an `OrganizationView` tab — pages, not modals, per the house rule for anything that
  builds long-form content.
- `/publications` directory, `/publications/{urlName}` with a subscribe button, and
  `/publications/{urlName}/{postUrlName}` as the reader.

**Help and screenshots**, in this branch rather than after — a feature is not done until the help
covers it.

## Decisions taken

- **A draft is a post with no `PublishedUtc`.** One nullable column rather than a status enum: there
  are exactly two states and the timestamp is wanted anyway.
- **A slug is generated once and never regenerated.** Renaming a post must not break a link somebody
  shared — the rule item #89 established for organisation URLs, applied here from the start rather
  than after somebody's link dies.
- **Bodies are sanitised on save**, through the existing `CmsMarkupSanitizer`, not on render. Storing
  what was submitted and cleaning it at every read means every future reader depends on remembering
  to clean it.

## Verifying

```bash
dotnet test Ben.Web.Tests --filter "FullyQualifiedName~Publication"
dotnet test Ben.Web.Playwright -p:IsTestProject=true --filter "TestCategory=Publications"
```

Site on **5078**, API on **5252**, each from its own project directory. The flag must be on for the
tests to see anything; they turn it on and put it back.
