# Categories + the learning loop — item 186 F6

Ben asked for two things this phase delivers together: posts categorized "best they can" with the
backend checking the label is honest, and "code that learns as we go with building our paranormal
database". The honest version of the second is a feedback loop, and this branch builds all of it:
the label, the measurements, the score, the nudge, the labelled-example store, and the re-fit.

## One deliberate deviation from the written plan

The plan called for a new `FeedCategory` table seeded with eight entries. The repo already has the
platform-wide **experience taxonomy** (`ExperienceCategory` → `ExperienceType`: Apparition, Shadow
Figure, Voices / Whispering, EMF Spike…) used by client requests, case timelines and evidence —
with its own admin surface, approval flow, and the item-#90 history of being the taxonomy of
record. Feed posts now categorize against **that**, via `OrgMessage.FeedExperienceTypeId`. Every
labelled example therefore accumulates against the same taxonomy the rest of the paranormal
database uses, which is exactly the asset Ben described — and a second parallel category list is
exactly how taxonomies rot.

## What this builds

- **The label**: composer offers "What does this show?" once media is attached (grouped native
  select over `GET api/experience-categories/with-types`, always optional). The card shows the
  chip; clicking it lands on `/feed/types/{id}` — a shareable page like a tag's. Server filter:
  `GET api/feed?type={id}`.
- **The measurements**: `FeedMediaFeatureSet`, one row per media post, filled at post time from
  the ingest pipeline's own metadata row (duration, audio stream, dimensions, camera, capture
  hour) plus image luminance measured via SkiaSharp. Columns exist for what nothing measures yet
  (EVP hits, motion) so filling them later is a backfill, not a migration.
- **The score**: `CategoryMatchScore` = sigmoid(w·x+b) over the encoded features
  (`FeedFeatures`), weights from the newest `FeedTypeWeightSet` for the type, else hand-written
  priors by parent category (`CategoryMatchScoring`). The priors are deliberately humble:
  Audible-without-audio nudges; Physical/Olfactory/Psychological can never nudge on priors,
  because a camera cannot witness a cold spot — both facts are pinned by tests.
- **The nudge** (score < 0.30): an AUTHOR-ONLY banner offering one-click recategorize
  (`PUT api/feed/posts/{id}/experience-type`). Never blocks, never visible to others — same
  doctrine as the awaiting-review note. Accepting it writes a Mismatch example for the old label
  and a Confirmed for the new: the poster's half of the loop.
- **The ranking signal**: `FeedRanking` multiplies engagement by `0.75 + 0.25·score` (null =
  1.0). A certain mismatch sinks like a post with a quarter fewer eyes; it does not vanish.
- **The examples**: `FeedLabelledExample` — APPEND-ONLY, `FeedLearningService.AddExampleAsync`
  the only writer, features snapshotted as JSON so an example outlives its post (SetNull FK).
  Sources: Moderator (queue one-clicks "Is what it says" / "Category is wrong"), PosterCorrection
  (the nudge), and — reserved for F7 — GroupClaim.
- **The re-fit**: `WeightRefitJob` on the existing 5-minute scheduler, self-gated to nightly per
  type, requiring ≥20 examples with ≥5 of each label. Hand-rolled logistic regression
  (`LogisticFit`: deterministic GD, L2, hash-based 20% holdout) appends a NEW versioned
  `FeedTypeWeightSet` with its holdout accuracy; a fit that measures worse is logged loudly but
  never auto-reverted — the history makes reverting a person's one-row decision.

## Migration

`AddFeedCategoriesAndLearning` — all additive (2 nullable columns on OrgMessages, 3 new tables,
indexes). Applied to the shared dev DB; safe under the running production build.

## Verifying

- Unit (22 new in `FeedLearningTests` + updated builders): feature encoding (unknown-audio ≠
  no-audio, night-hour edges, duration cap, JSON round-trip), prior humility (the never-nudge
  categories, the daylight-apparition case), fit convergence + determinism, ranking floor, luma
  golden values, re-fit versioning/self-gating/minimums. Full suite green.
- Live, flag flipped on then restored: member posts a photo with a type → chip in the response,
  score written; recategorize writes examples; moderator judgment writes an example; type filter
  returns the post.
