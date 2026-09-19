# Feels alive — item 186 F9

The polish that makes the feed read as a living room rather than a database listing. Three
pieces, all UI, no schema:

- **Faces.** `UserAvatar` (items 162/163's viewer-aware machinery — consent and defaults decided
  server-side) on every `FeedPostCard`, and large on the feed profile header. Threads and
  profiles inherit it through the card.
- **The new-posts pill.** A `PeriodicTimer` (45 s) re-checks the feed's chronological head while
  the page is open. On All/Following the pill's click prepends exactly the new posts, keeping
  scroll position; on For You — where "new" has no stable position — it re-ranks the page.
  Sticky, so it's reachable from anywhere in the scroll. A failed poll is a quiet skip.
- **The home teaser.** `FeedTeaser` on the landing page: top three For You posts with avatars +
  "Join the conversation". Anonymous-visible by design — the funnel's first step. Renders
  NOTHING when the feed is dark, unreachable, or empty (the feed's own 404 answers null here);
  a teaser for a dead room would be worse than none.

## Verifying

All three are rendering behavior: build green, full unit suite green, and the visual pass done
live in the browser with the flag flipped on then restored (teaser present on / and absent with
the flag off; avatars on cards; the pill exercised by posting from a second session). The
scripted two-context pill walk joins F10's Playwright batch.
