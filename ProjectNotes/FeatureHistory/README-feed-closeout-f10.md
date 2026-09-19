# Close-out — item 186 F10

The arc's last mile: the reminder that keeps the dark launch honest, the walks that keep the
whole feed honest, and the records.

- **The dark-launch reminder**: `FeedModerationSummary` now carries `FeedIsOn` + `FeedPostCount`
  (the two facts, from the same endpoint that already tells the truth about screening), and
  `ActionNeededBanners` renders a SuperAdmin-only banner while the feed has content and is off —
  naming the switch (`/admin/site-settings`) AND the screening posture, so nobody launches on
  manual-only screening unread. Session-dismissable; new content brings it back; three unit
  tests pin the facts (`FeedDarkLaunchTests`).
- **The walks** (`FeedArcTests`, joining `FeedTests` in Category=Feed — 16 green against live
  hosts): media post → image-or-honest-wait + category chip → the type's page; a case-derived
  post showing NO group name until the claim, then name + Group-verified on the card; /go
  counting and redirecting through the closed set (reusing the group's one ad row, withdrawn
  after); the home teaser for a visitor; the new-posts pill surfacing a post made elsewhere
  within one poll cycle. The fixture flips the flag on and restores it, waiting out the site's
  30-second settings cache — the same lesson the capture fixture already documented.
  The hand-built 8×8 PNG helper exists because the ingest pipeline genuinely decodes uploads
  and this project deliberately carries no image library.
- **Records**: Future-Improvements item 186 marked BUILT with the full arc + follow-ons
  (galleries/reposts, ad billing via 143/144, screener tuning surface, video feature
  extraction, iOS APNs + universal links, iOS fixture re-capture note); help coherence pass
  (site-administration's feed switch section now describes the whole arc and the screening
  posture); investor overview updated to "built and dark-launched" and both PDFs regenerated.

**The feed is BUILT and DARK. The switch is `features.public-feed` at /admin/site-settings.**
