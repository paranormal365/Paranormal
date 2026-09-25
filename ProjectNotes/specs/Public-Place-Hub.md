# The Public Place Hub: one public record per location, many groups' work around it

**Status:** plan, agreed in conversation 2026-09-17, not yet built.
**Branch:** `feature/public-place-hub` (this document is its README; each phase adds to it).
**Supersedes nothing.** Extends `Places-and-Investigation-Sharing.md` (2026-08-15) and the free-lane
rule in `PaidPlan` (2026-08-31). Both are quoted below because this plan is mostly them, finished.

---

## What Ben asked for (2026-09-17, four messages)

1. "Maybe we make a new completely public case which should actually be public for adding files,
   messages etc. There can be many investigations by many groups linked to the public case, and if
   someone wants to create a case at a public case, they are given the option to link to the existing
   case and create their own investigation."
2. "This is also what is created when individuals who are not in a paid tier create a case."
3. "Everything a solo person submits is going to be public by default."
4. "If paid, they can make their work private. By default we should be able to collect information as
   public for unpaid plan."

So: a shared public record per location that anyone may add to; many groups' investigations hang off
it; a group opening a case at a known location is offered the link; and the free lane is public by
default, with privacy the thing a plan buys.

## What already exists (verified in code, 2026-09-17)

Most of this is built. The plan is narrower than the idea sounds.

- **The place is already the shared thing.** `Place` has no `OrganizationId` at all ("nothing here
  assumes one owner", `BenDataModel.Place.cs:19-21`). `Case.PlaceId` (`BenDataModel.Case.cs:40`) and
  `Investigation.PlaceId` (`BenDataModel.Investigation.cs:116`) are both nullable; an investigation
  may hang off a place with no case (`CaseId is not null || PlaceId is not null`, enforced at
  `OrgInvestigationsController.cs:228`).
- **The 2026-08-15 spec already chose this shape**, in these words: *"Introduce a `Place`. Do not model
  case-less investigations as cases with the client fields left empty… Case = a client's problem at a
  place; Public investigation = a visit to a place."* This plan does not reopen that decision.
- **The place page is already cross-group.** `PublicPlaceController` (`api/public/places/{id}`) lists
  every group's published investigations through the one visibility predicate
  (`InvestigationVisibilityFilter.VisibleTo`), the published field-session archive, and event evidence,
  and computes "N investigations by M groups since Y" (`PlaceSummary`). Blazor: `Shared/PlaceView.razor`
  at `/places/{PlaceId:guid}`, with a signed-in view ("Your groups' visits" / "Shared by other groups").
- **The sharing ladder exists.** `InvestigationVisibility` is `GroupOnly` → `PlaceInvestigators`
  ("anyone whose organization has also investigated this place", deliberately not reciprocal) →
  `Public`; `Public` is refused at a private residence. Defaults follow `PlaceKind`
  (`InvestigationVisibilityFilter.DefaultFor`).
- **"Did you mean this place" exists on two of three doors.** `PlaceMatcher.IsProbableMatch` (same
  address AND within 0.1 mile; landmarks by name) behind `GET api/places/candidates`, offered but never
  applied, in `NewInvestigationWindow.razor:145-185` ("Use this place") and `OrgScheduler.razor:383`.
  **`CaseController` contains zero references to `Place`.** A new case gets no place at all until an
  investigation binds one; `Case.PlaceId` is written only by the 2026-08-15 backfill and the admin merge.
- **The free-lane rule is written and shipped**, in Ben's words (`PaidPlan.cs:40-51`): *"free means
  your findings join the public archive, paid means your work is yours."* It gates **retraction, not
  publication** — publishing a session stays a deliberate act, because auto-publishing would put
  unreviewed media live as a side effect. `PaidPlan.CoversAsync(userId)` / `CoversOrganizationAsync(orgId)`
  is the single definition of "paid" (active subscription, not merely present).
- **But two gates are keyed on the wrong question.** `PersonalOrganizations.WhyNotInAPersonalOrganization`
  refuses `CreateCase` and `CreatePrivateInvestigation` for `IsPersonal` organizations — a fact about the
  record, not about the plan. Under Ben's rule a paid solo subscriber may keep work private and may take
  client work; an unpaid group of one must publish. Today the opposite holds on both counts. And the
  flat door (`OrgInvestigationsController.Create`) never asks the solo question at all; only the
  case-nested door does (`InvestigationController.cs:160-171`). The two doors do not answer alike.
- **Nothing mints a personal organization.** `SoloPlanController.Start` (free, idempotent, creates the
  personal org with the full skeleton) has **no caller** on the website or in the app. A person with no
  group cannot open a case or an investigation from anywhere today; they can only record field sessions
  and publish them to a place's archive (the app's `ArchiveActions`, `FieldSessionPublishController`).
- **The feed already has placed, screened, public posts with media — minus the place.** A feed post is
  an `OrgMessage` with `ChannelType = PublicFeed`, nullable `OrganizationId`, `AuthorAppUserId`,
  `MediaUploadFileId` + `MediaReviewState` (NSFW screener, Held pile, three confident refusals pause
  uploads for a day — `FeedMediaAbuse`), reports, hide, likes, replies, scheduling, and `PostedLatitude/
  Longitude/PostedPlaceName` (the author's whereabouts, opted in). **There is no `PlaceId`.** Writing is
  gated by `FeedParticipation.RefusalAsync` ("anyone scrolls, people who belong here post": any active
  membership, or a client). The iPhone app reads and writes the feed (`FeedStore`, `FeedActions`).
- **Case files, messages and notes are structurally private.** `CaseFile`, `CaseMessage`, `CaseNote`
  carry no visibility column and appear in no public controller. `CaseMediaPublication` (the rule from
  item 80) says in so many words that files on the Files tab are never publishable because nothing ever
  recorded consent. Only timeline-entry files with `Visibility == Public` on a published case are served,
  through `PublicCaseMediaController`, sanitized, 404-never-403.
- **"Public case" already means something else.** `PublicCaseController` / `PublicCaseDiscoveryController`
  serve a group's own case once `IsPublic && Status in (Public, Haunted)` — the group's published report,
  with the client's name replaced. This plan does not reuse the phrase for the shared record.

## Decisions

Made with Ben on 2026-09-17 unless marked *(recommended, confirm)*.

- **D1. The Place is the hub. No second object.** The shared public record of a location is the place
  and its page. A group's case stays that group's private working record, pointing at the place. Two
  objects meaning "a location many groups investigate" would drift, exactly as the two copies of the
  media rule did (memory: `feedback_server_guard_needs_a_ui_path`, the place page's second copy).
  *In the UI the word is "place", as everywhere on the site today; "public case" keeps its existing
  meaning (a published report).*
- **D2. Unpaid means public by default; paid buys privacy.** Ben: "if paid, they can make their work
  private. By default we should be able to collect information as public for unpaid plan." Keyed on
  `PaidPlan.CoversOrganizationAsync`, never on `IsPersonal`. Applies alike to a personal organization
  and to a free-band group of one.
- **D3. "By default" means pre-set and not undoable without a plan — not published as a side effect.**
  The recorded reason stands (`PaidPlan.cs:45-51`): publication as a side effect would put unscreened
  media live. So on an unpaid plan: a case at a public place is created `IsPublic = true` and the box
  cannot be unticked (with the sentence); investigations there default to `Public` and cannot be
  narrowed; timeline entries default to `Public` visibility in the composer; sessions publish and cannot
  be retracted (already). A published case still becomes visible when its status reaches `Public`, and
  media still passes review first. Live, conversational public contribution happens at the place (D4).
- **D4. Posts about a place are feed posts with a `PlaceId`.** Not a new comment table. That inherits
  screening, the spam pause, reporting, hiding, likes, replies, the moderator screens, the app's reader,
  and the block list, at the cost of one nullable column. The place page shows them and hosts a composer.
- **D5. Private residences never take place posts.** Publishing what happens inside somebody's home is
  theirs to agree to, and there is still no mechanism for asking (spec open question 4). Refused with a
  sentence; the composer does not render there.
- **D6. Who may post about a place — CONFIRMED by Ben, 2026-09-17**: **any signed-in account**, because
  the free lane *is* public contribution and a person with no group is precisely who it exists for. The
  feed's front page keeps its "people who belong here" rule. Both rules live in `FeedParticipation`,
  named, so the difference is stated once. *Alternative rejected:* minting the personal organization to
  make the person "belong" — it works, but it makes a billing object the key to a moderation rule.
- **D7. A personal organization is an organization — CONFIRMED by Ben, 2026-09-17.** The `IsPersonal`
  refusals (`CreateCase`, `CreatePrivateInvestigation`) are retired. A paid solo investigator may open a
  case and keep it private, subject to the same `PrivateResidenceCases` capability as any group; an
  unpaid one gets D2. This reverses the 2026-08-31 sentence "a solo plan does not take client work" in
  one respect only — Ben's 2026-09-17 rule is the newer one — and the `Discoverable` filter (never a
  directory row, never a nearby pin) is untouched.
- **D8. A new case names its place, and says what kind of place it is.** New Case gains the same "this
  place may already be on file → Use this place" offer as New Investigation, and an explicit Public
  location / Private residence choice with no default (the model's own default, `PrivateResidence`,
  stands for callers that send nothing). Offered, never applied — the dedup design's rule.
- **D9. Additive API only.** 1.0.3 is in App Review. Every request field is trailing and defaulted;
  every record field is trailing; no path changes. The app ignores `PlaceId` on posts until a later build
  tags them.

## The worked example: Cragfont

Ben, 2026-09-17: "Use my Cragfont record in production as an example of a public site where people can
post their own evidence and have their own investigations."

Cragfont is a historic house at Castalian Springs, Tennessee — a museum with opening hours, which is a
`PlaceKind.PublicLocation` in every sense the model means by it, and nobody's home. It is the example
every phase below is written against, and it is also the measure of the gap: **on production today
there is nothing public about it at all.** `GET /api/public/cases` on ishaunted.com returns an empty
list (checked 2026-09-17), so Cragfont exists as one group's private case with a video on it, and a
visitor who has heard it is haunted can find nothing and contribute nothing.

What it should be, phase by phase:

| Phase | Cragfont gains |
|---|---|
| 1 | Ben's group pays, so nothing changes for it. A free group opening its own Cragfont case gets one that is public from the start, with visits there shared with everyone. |
| 2 | The case names a place. Typing "200 Cragfont Rd" on New Case offers the place already on file with **Use this place**, and the case lands with "Cragfont — 3 other groups have investigated there". Its page lists **Published cases here**. |
| 3 | A visitor who went on the evening tour posts a photograph about the place. It is screened like any feed media, it appears on Cragfont's page, and reporting and hiding work as they do everywhere. |
| 4 | Somebody with no group at all presses **Investigate here** on Cragfont's page, gets a private space of their own in one step, and records a visit that is public because they pay nothing. |

The dev seed gets a Cragfont-shaped place with visits from both seeded groups so the page has more
than one group on it (Phase 4), and the help screenshots are taken there.

## Facts that shape the work

- The Blazor place page is `Shared/PlaceView.razor`; the client is `IBenPlacesClient`
  (`GetPublicPlaceAsync`, `FindPlaceCandidatesAsync`, `GetPlaceInvestigationsAsync`, `GetPlaceSummaryAsync`).
- `PublicPlaceResponse(Place, Investigations, Summary, Sessions?, EventEvidence?)` — trailing optional
  members; new sections go on the end.
- Case creation: `CaseController.Create` + `CreateCaseRequest` (server `CaseController.cs:947`, client
  `BenAdminClientRecords.cs:1079`; both must change together); page `Organization/Cases/CaseCreatePage.razor`;
  the accepted-on-create rule and its `PutToTheGroup` field landed 2026-09-17 (commit bb2bee61).
- Placement helper: `Services/Places/InvestigationPlacement.ApplyAsync(db, investigation, placeId,
  newPlace, userId, ct)` — creates or finds the place, geocodes, inherits the case's place, flips
  `IsPrivateEngagement` through `PrivateCaseGate` when a residence is bound. `NewPlaceRequest` is at
  `InvestigationPlacement.cs:138` and mirrored at `BenAdminClientRecords.cs:489`.
- Investigation create doors: `InvestigationController.Create` (under a case) and
  `OrgInvestigationsController.Create` (`CreateOrgInvestigationRequest`, `OrgInvestigationsController.cs:980`).
  Visibility default + `Reject` at `:152-153` and `:254-255`.
- Feed: `FeedController` (`GetFeed` filters: mode/hashtag/author/type; `VisiblePosts`, `VisibleOrMineAwaiting`,
  `ExceptBlockedBy`, `ToRecordsAsync`); records in `Ben.Service.Models/Feed/FeedRecords.cs`
  (`CreateFeedPostRequest` already has ten trailing defaulted fields — add after `PostedPlaceName`);
  media path `FeedController.cs:405-430` (ingest → `MediaReviewState.Pending` → screener); switch
  `SiteSettingKeys.FeaturePublicFeed`, unset = off.
- Admin merge repoints nine tables (`AdminPlaceMergeController.cs:185-250`); `OrgMessages` must join
  the list the moment `PlaceId` exists, or a merge orphans the posts.
- Seeds: the only `PublicLocation` seeded is Bell Witch Cave (`DevelopmentDataSeeder.cs:928-940`); no
  seeded person is without a group (emma is in the second org), so a no-group seat has to be added.
- Playwright seats: sarah (admin), james (member), victor (viewer), daniel (client). Guards that bite
  new markup: `LabelAssociationTests`, `IconNameGuardTests`, `OrphanedHandlerTests`, `NoTelerikDialogTests`,
  `HelpLinkTargetTests`, `CanvasCopyTests` is canvas-only. Source-scan guard for the solo rule:
  `PersonalOrganizationsAreHiddenTests` (Discoverable) — untouched by this plan.
- Windows is production. Nothing here touches the filesystem beyond the feed's existing ingest.

---

## Phase 1 — The rule: unpaid is public by default, paid may keep private — **BUILT 2026-09-17**

The smallest change with the largest effect, and everything after it assumes it.

### What it turned out to need, that the plan did not foresee

**Two readers treated the publish flag as publication.** Everywhere on the site "a public case"
means `IsPublic` **and** a status of `Public` or `Haunted`; `PublicCaseController`'s own summary says
so. Two places asked for the flag alone, and the flag is set long before anybody publishes anything —
so with the new default they would have fired on every case an unpaid account opened, on the day it
opened it. Both were fixed before the default could reach them:

- `FieldSessionUploadController.MayContributeAsync` — the widest of its three doors, "anybody at all
  when the investigation or its case is public". On the flag alone, a stranger could upload
  recordings to, and read the sessions on, the investigations of any case somebody had merely ticked
  a box on. Its own test set the case up with status `Proposed` and asserted the stranger got in, so
  this was asserted behaviour, not an oversight nobody had looked at — which is why the test is now a
  five-case `[Theory]` naming both halves. The deliberate width is unharmed and better reached: an
  unpaid account's landmark visits now default to `Visibility.Public`, which is the primary door.
- `OrgPublicController` — the "public cases" count on a group's public page, which would have
  overstated itself against the list underneath it.

Recorded as its own lesson: the memory note says to grep for a documented rule's predicate anyway.
Sixteen places ask this question and fourteen had it right, which is exactly the ratio that makes a
sweep worth doing rather than trusting the comment. **A named predicate for it is not in this phase**
(`CmsEmbed.cs:337` already asks for one, and half-adopting a helper leaves three shapes rather than
two) — spun off as its own task.

**The two doors really did disagree.** The retired solo rule was asked by `InvestigationController`
and not by `OrgInvestigationsController`, so the same visit was judged differently by which screen
booked it. `PublicByDefaultDoorTests` now asserts every rule through both doors in one test each, so
neither can be made to pass by fixing one controller.

### What shipped

- `PaidPlan.PublicByDefaultAsync`, `WhyCannotKeepCasePrivateAsync`, `WhyCannotNarrowInvestigationAsync` —
  one definition of the rule, worded so it can reach the phone (no plan, no price; 3.1.1).
- `PersonalOrganizations` keeps `Discoverable` and nothing else. `PersonalAction` and
  `WhyNotInAPersonalOrganization` are gone, with a note in their place saying why.
- `InvestigationVisibilityFilter.DefaultFor(place, publicByDefault)` and
  `Reject(visibility, place, publicByDefault, whyNotNarrower)`. The place's refusals still come first
  and still win, so a home is never published because a subscription lapsed. All three call sites use
  the new overloads.
- `CaseController`: `IsPublic` set from the plan on create; un-publishing refused, only at the moment
  it would go off, and never on a private-engagement case.
- `MyOrgPermissionsResponse`/`Item` carry `PublicByDefault` (trailing, default false, so an older
  server leaves every control as it was).
- Locked with their reasons: `#case-public` + `[data-testid=case-public-locked]` on the Edit Case
  page; the narrower options withheld and `[data-testid=investigation-scope-locked]` shown on both
  `InvestigationPanel` and `NewInvestigationWindow`. The window's scope correction is now one
  `KeepScopeLegal()` rather than two half-copies, and it moves in both directions.
- Tests: `PaidPlanTests` (+2), `InvestigationVisibilityTests` (+5), `PersonalOrganizationTests`
  (rewritten to the new truth), `FieldSessionUploadControllerTests` (the theory), and the new
  `PublicByDefaultDoorTests` (11). Suite 6221/0.
- Help: a new "With no plan, work at a public place is public" section under
  organization-administration's plan heading, cross-referenced from working-a-case. Changelog both
  streams.

### Not done in Phase 1, on purpose

No Playwright test for the locked controls: **both seeded groups are on active plans**, so there is
no unpaid group in the e2e database to drive them with. That seat arrives with Phase 4's no-group
person, and the browser tests for the locks go in beside it. The existing case and investigation
fixtures were run to prove the markup changes broke nothing.

### As planned

1. **`PaidPlan`** gains `PublicByDefaultAsync(db, orgId, ct)` (= `!CoversOrganizationAsync`) and three
   sentence-or-null rules, each written once:
   - `WhyCannotKeepCasePrivateAsync(db, orgId, ct)` — "On this plan a case at a public location is public.
     What you record there joins the place's page; a plan is what makes your work yours."
   - `WhyCannotNarrowInvestigationAsync(db, orgId, ct)` — same idea for visibility.
   - The existing `WhyCannotKeepPrivateAsync` (sessions) is unchanged.
   The wording never names a price or where to buy (the sessions sentence reaches the app verbatim;
   these will too, one day — Guideline 3.1.1).
2. **`PersonalOrganizations`**: retire `PersonalAction.CreateCase` and `CreatePrivateInvestigation` and
   the two call sites (`CaseController.cs:301-308`, `InvestigationController.cs:160-171`). Keep
   `Discoverable`/`DiscoverableVia` and the enum type (with the remaining doc) so the hidden-from-
   directories guard is untouched. Rewrite the class remarks: privacy is the plan's, not personal-ness's.
3. **Cases.** In `CaseController.Create`: when the org is public-by-default and the bound place is not a
   private residence (Phase 2 binds it; until then, when no residence is bound), `IsPublic = true`. In
   `Update`: turning `IsPublic` off on such a case returns 400 with the sentence. Timeline entry
   composer (`CaseTimeline.razor`) defaults `Visibility` to `Public` when the case's org is public-by-
   default — the record already carries what the page needs? No: add `PublicByDefault` (bool, trailing)
   to `CaseRecord` so the page and the Edit Case page can say why the box is locked.
4. **Investigations.** Both create doors and the update path: when public-by-default and the place is a
   `PublicLocation`, default `Visibility = Public` (instead of `PlaceInvestigators`) and refuse anything
   narrower with the sentence. At a residence the existing rules stand (paid lane). This also closes the
   door-parity gap: the rule lives in `InvestigationVisibilityFilter` next to `DefaultFor`/`Reject` as
   `DefaultFor(place, publicByDefault)` / `Reject(visibility, place, publicByDefault)`, so neither door
   can forget it.
5. **UI.** Edit Case page: `Make Public` disabled + sentence under it when locked
   (`#case-public-locked`). New Investigation window and the investigation edit form: narrower scopes
   rendered disabled with the sentence (`#investigation-scope-locked`). The sentence renders — a server
   guard needs a UI path.
6. **Tests.** `PaidPlanTests`: unpaid org public-by-default; active plan not; lapsed plan is.
   `CaseControllerTests`: unpaid create → `IsPublic`; update off → 400 with sentence; paid → free choice.
   `InvestigationVisibilityTests`: unpaid at landmark defaults Public; narrowing refused; residence
   unchanged; **both doors** (a parity test that calls each controller). `PersonalOrganizationTests`:
   `A_personal_organization_may_not_open_a_case` and `…investigations_are_public_ones` rewritten to the
   new truth (a paid personal org opens a private case; an unpaid one opens a public one). Every rule
   test seen failing first with the rule broken.
7. **Help.** `organization-administration.md` "Your group's plan": the sentence. `working-a-case.md`
   publishing paragraph. Changelog website + api.

## Phase 2 — A case knows its place, and is offered the one already on file — **BUILT 2026-09-17**

### One thing the plan called for, dropped on contact

**No fallback place derived from the case's own address.** The plan said a request naming neither an
existing place nor a new one should have one built from the case's address, "so no new case is
placeless". That cannot answer the question everything downstream depends on: *what kind of location
is it?* Guessing a residence would designate the case private-lane work permanently and ask the
paid-plan gate, over a question nobody was asked. Guessing a public location could put somebody's
home on a page anyone can read. So the kind is a **required** answer on the New Case page instead —
no default, Open Case disabled until it is given — and a caller that sends nothing leaves the case
unplaced, exactly where every case stood before today. The reasoning is written into
`CasePlacement`'s own remarks so the next person to reach for a fallback finds it.

### What shipped

- `PlaceFactory.CreateAsync` — the trimming, country default, cautious kind default and geocoding
  call, extracted from `InvestigationPlacement` so the three doors that build a place from a
  description build it identically. `InvestigationPlacement` now calls it.
- `CasePlacement.ApplyAsync` — the case-shaped sibling. Binds an existing place or creates a
  described one; runs `PrivateCaseGate` and designates the case private-lane when a residence is
  bound, which now happens **at birth** rather than whenever somebody first schedules a visit;
  never rewrites the case's own address, and fills its coordinates only when it has none.
- `CreateCaseRequest` gains trailing `PlaceId` and `NewPlace` (server + client mirror). The iPhone
  app never creates org cases — it only touches `api/my-cases` — so the website is the one caller.
- `CaseController.Create` restructured: place first, publication decided after it, so an unpaid
  account's case is public only at a `PublicLocation` and never when a residence made it
  private-lane.
- `Kit/Places/BenPlaceCandidates.razor` — the "this place may already be on file / Use this place"
  offer, lifted out of `NewInvestigationWindow` with its debounce, its refusal sentence and its
  "offered, never applied" rule. It watches the parent's address fields rather than taking its own
  input events, keyed so an unrelated re-render does not restart the clock.
- New Case page: the offer, a name field for landmarks, the required kind choice with both
  consequences spelled out, `?place={id}` pre-choosing from a place's own page, and a chosen-place
  banner with **Change**.
- `CaseRecord` gains `PlaceId` and `PlaceName` (the latter mapped off the navigation, which
  `GetById` now includes — the W-A9 lesson); the case page shows a line naming the place, how many
  other groups have investigated there, and a link to it.
- Place page: **Cases written up here**, from a new trailing `Cases` member on
  `PublicPlaceResponse`. Published means the flag AND a Public/Haunted status, written the long way
  rather than borrowing either of the two readers that had it wrong. Titles go through
  `CaseProseRedactor`. A public place also offers **Open a case here** per group the viewer belongs
  to.
- Tests: `PublicByDefaultDoorTests` grew to 16 (landmark, no place, home-at-birth, inline place,
  inline place with no kind, unknown place refused); `PublicPlaceTests` grew to 21 (listed, the
  seven-case published/not theory, elsewhere, redactor, slug fallback, empty-not-null); Playwright
  `CasePlaceTests` (the kind gate, the offer taken, `?place=` pre-choosing). Suite 6226/0.
- Help: "Which place the case is about" on working-a-case, "Cases written up here" under Place
  pages. Changelog both streams.

### Not done in Phase 2

**`GET api/places/{id}/my-cases` and the signed-in "Your groups' cases here" section.** The plan
listed it; it is a convenience next to the rest, and a member already reaches the place from their
own case's banner. Deferred rather than dropped — it belongs with Phase 3, where the place page
grows its signed-in half anyway.

### As planned

1. **Request.** `CreateCaseRequest` gains trailing `Guid? PlaceId = null`, `NewPlaceRequest? NewPlace = null`
   (server + client mirror). `NewPlaceRequest.Kind` is the kind choice; null → `PrivateResidence` (the
   model's default, safe direction).
2. **Server.** `Services/Places/CasePlacement.ApplyAsync(db, case, placeId, newPlace, userId, ct)` —
   the case-shaped sibling of `InvestigationPlacement`, sharing its place-creation and geocoding code
   (extract `PlaceFactory.CreateAsync(newPlace, userId, ct)` so there is one way to make a place). When
   the request names neither, the case's own address becomes a `NewPlace` (as the backfill did) so no
   new case is placeless. Binding a residence runs `PrivateCaseGate` and sets `IsPrivateEngagement`,
   exactly as `InvestigationPlacement.cs:97-109`. Then Phase 1's `IsPublic` decision runs, now with the
   real place.
3. **New Case page.** After the address block: the "Did you mean" offer, lifted from
   `NewInvestigationWindow.razor:145-185` into a Kit component `Kit/Places/BenPlaceCandidates.razor`
   (`Street/City/State/Zip/Name` parameters, debounced `FindPlaceCandidatesAsync`, `OnChosen`), used by
   both windows so the wording and the "offered, never applied" comment live once. A chosen place fills
   and locks the address fields with a "Change" link. Then **What kind of place is this?** — two
   radios, no default, `id="case-place-kind-public"` / `id="case-place-kind-residence"`, each with one
   sentence: public location → "Anyone may add to this place's page; on a free plan your case here is
   public."; private residence → "Somebody's home. Findings stay with your group; this is paid-lane
   work." Open Case is disabled until a kind is chosen (or a place with a kind was picked).
   `?place={id}` in the query pre-chooses a place (the place page's "Open a case here" button).
4. **Landing.** After create, the case page shows a one-time banner (`data-testid="case-place-banner"`):
   "This case is at {place} — {n} other groups have investigated there. See the place page ·
   Schedule your first visit" (the second link opens the Investigations tab's scheduler). Dismissed by
   navigating; nothing stored.
5. **Place page.** New section **Published cases here**: cases with `PlaceId == id && IsPublic &&
   Status in (Public, Haunted)`, as `PublicPlaceCaseRow(CaseId, OrgUrlName, CaseRef, Title, Status,
   PublishedYear)` through `PublicClientName` redaction; trailing member on `PublicPlaceResponse`.
   Signed-in members additionally see **Your groups' cases here** (own org's cases at the place, any
   status) from a new `GET api/places/{id}/my-cases` (membership-scoped).
6. **Tests.** `CaseControllerTests`: existing place bound; new place created and geocoded; residence →
   private-engagement flip and gate; placeless request gets a place from its address; unpaid + public
   place → `IsPublic`. `CasePlacementTests` on SqliteTestDb. `PublicPlaceTests`: published cases listed,
   unpublished not, client name never emitted. Playwright `CasePlaceTests`: typing the seeded cave's
   address on New Case offers it; Use this place locks the fields; Open Case lands with the banner; the
   place page's Published cases section shows a seeded published case; `?place=` pre-chooses.
7. **Help.** `working-a-case.md` New Case paragraph (the kind choice, the offer). Changelog.

## Phase 3 — Posts about a place — **BUILT 2026-09-17**

### What shipped

- Migration `PlacePosts`: `OrgMessages.PlaceId` nullable, `(PlaceId, DateCreated)` filtered index,
  SetNull FK to `Places`. One `AddColumn`, applied to `IsHauntedDb_player`; production via the
  runbook entry added with it.
- `FeedParticipation.PlaceRefusal` — **signed in is enough**, beside the front page's stricter rule
  so the difference is stated once and on purpose. D6, confirmed by Ben.
- Write door: the place must exist and must be a `PublicLocation` (a residence is refused with a
  sentence); a **reply inherits its parent's place** and cannot claim another; the place a reply
  inherits is not re-validated, so a thread survives its place being merged away.
- Read: `GET api/feed?place=`, and `FeedController.LatestForPlaceAsync` — which the place page calls
  rather than writing a query of its own, so hidden posts, unreleased scheduled posts and the
  reader's block list behave identically on both surfaces. `AboutPlaceName` resolved at read, with
  the town as a fallback for a place with no name.
- `PublicPlaceResponse` gains trailing `Posts` and `CanPost`. The place page grows the composer and
  the cards, reusing `FeedComposer` and `FeedPostCard` rather than forking either; a new post goes
  to the top of the list instead of reloading the page.
- `/feed/places/{id}` for "See all", with the place's name as its heading. Promoted cards are kept
  off it, as they are off a tag's and a type's.
- Every feed card shows **at {place}** with a link home when the post has one — deliberately
  distinct from "said at", which is where the author was standing.
- `AdminPlaceMergeController` repoints posts and reports the count. This was the **quiet** failure:
  the FK is SetNull, so without the repoint a merge would succeed and silently strip the place off
  every post about it — the posts surviving with their text and losing the one thing that put them
  on a page. Seen failing before being kept.
- The Phase 2 leftover finished: `GET api/places/{id}/my-cases` and **Your groups' cases here**,
  scoped to the caller's own memberships because another group's unpublished case at the same
  building is their business.
- Tests: `FeedPlacePostTests` (12 — carries the place, residence refused, unknown 404, the
  signed-in-is-enough seam asserted in both directions at once, reply inherits, reply cannot claim
  another, `?place=` filters, unknown filter empty, page carries posts and `CanPost`, home offers
  nobody the box, hidden post gone, threads not replies), plus the merge repoint. Suite 6251/0.
- Help: "Posting about a place" in the-feed, a paragraph in moderating-the-feed, two sections under
  Place pages in working-a-case. Changelog both streams. Runbook entry for the migration.

### Four things only driving it in a browser found

Every one of these compiled, passed its own unit tests, and was wrong on the page.

1. **The anonymous endpoint cannot answer "may you post".** The website calls
   `api/public/places/{id}` through `GetAnonymousAsync` — deliberately, so published means published
   and no second idea of visibility creeps in. So `CanPost` was always false and **the composer never
   appeared for anybody.** Fixed by adding the signed-in `GET api/places/{id}/posts`, which is what
   this page already does for investigations: two endpoints, both reading posts through
   `FeedController.LatestForPlaceAsync`. The anonymous one now returns `CanPost: false` and says in
   its own comment that this is structural, not an answer.
2. **The signed-in load was being overwritten by the anonymous one.** `LoadArchiveAsync` runs after
   it and had been given the posts too, so it clobbered the real answer with the anonymous one. It
   now touches only the archive.
3. **`<FeedComposer>` rendered as a literal `<feedcomposer>` HTML tag.** `Shared/PlaceView.razor`
   had no `@using Ben.Web.Website.Library.Feed`, and Razor emits an unrecognised component as a raw
   element with **no error and no warning**. The div was there, empty, and the page looked like a
   data problem. Worth remembering as its own trap.
4. **Two things in the composer's row have the accessible name "Post"**, so a test clicking by name
   hit the wrong one and nothing happened — which read as the post being refused. The submit button
   now carries `data-testid="feed-composer-post"`.

And one real product bug the same pass found: **the box was offered when the feed was switched off**,
because `CanPost` never asked. Clicking it would have been refused by an endpoint that 404s with the
feed off. Both place-post reads now check `FeaturePublicFeed` first, with a test for it.

### One bug this phase introduced and existing tests caught

`PublicPlaceController` is anonymous and is reached with **no HttpContext at all** in eleven
existing tests. Reading the caller's id through `User.FindFirst(...)` threw a
`NullReferenceException`, which on the live site would have been a 500 on the most public page there
is. Now null-safe the whole way down. Worth recording because the failure was invisible from the
new code's own tests — every one of them supplies a context — and only the old ones found it.

### As planned

1. **Migration `PlacePosts`**: `OrgMessages.PlaceId uniqueidentifier NULL`, FK → `Places` (SetNull),
   index `(PlaceId, DateCreated)` filtered `PlaceId IS NOT NULL`. One `AddColumn` + one index. Apply to
   `IsHauntedDb_player` only; production with the runbook.
2. **Entity/records.** `OrgMessage.PlaceId` + nav. `CreateFeedPostRequest` gains trailing
   `Guid? PlaceId = null`. `FeedPostRecord` gains trailing `Guid? PlaceId = null, string? PlaceName = null`.
3. **Write.** `FeedController.CreatePost`: when `PlaceId` is given — place must exist and be
   `PublicLocation` (D5; "Posts about somebody's home aren't shared here."), participation is
   `FeedParticipation.PlaceRefusalAsync` (D6: signed-in is enough) instead of `RefusalAsync`; a reply
   inherits its parent's place. Media, screening, spam pause, poll, scheduling: unchanged.
4. **Read.** `GetFeed` gains `[FromQuery] Guid? place` (combines like `type`); `ToRecordsAsync` fills
   `PlaceName`. `PublicPlaceResponse` gains trailing `IReadOnlyList<FeedPostRecord>? Posts` (latest 20,
   through `VisiblePosts` and the reader's block list) and `bool CanPost`. The place page section
   **Posts about this place** with the existing feed card (`Feed/FeedPostCard.razor` or whatever the feed
   page renders — reuse, do not fork) and, when `CanPost`, the existing composer with the place fixed
   and a "Posting publicly about {place}" line. "See all" → `/feed/places/{id}` (new route on
   `FeedPage.razor`, the `place` filter). Feed cards everywhere show "at {place}" linking to
   `/places/{id}` when `PlaceId` is set.
5. **Moderation.** Nothing new: hidden posts vanish from the place page through `VisiblePosts`; reports
   land in `api/admin/feed` as today. `AdminPlaceMergeController` repoints `OrgMessages.PlaceId`.
   `moderating-the-feed.md` gains one paragraph.
6. **Switch.** Place posts are part of the feed and follow `FeaturePublicFeed`; the place page renders
   the section only when the feed is on. No second switch.
7. **Tests.** `FeedControllerTests`: post with place stored and returned; residence refused; unknown
   place 404; `?place=` filters; reply inherits; signed-in stranger may post *with* a place and still
   may not without one (D6, both directions); hidden post absent from the place page.
   `AdminPlaceMergeTests` (or the existing merge fixture): posts repointed. `PublicPlaceTests`: posts
   section, `CanPost` for a no-group account. Playwright `PlacePostsTests`: sarah posts on the cave's
   page → card appears with "at Bell Witch Cave"; the no-group seat can post there but not on `/feed`;
   `/feed/places/{id}` lists it; feed card links back.
8. **Help.** `the-feed.md` "Posting about a place". Changelog website + api + apps ("older app builds
   show place posts as ordinary posts").

## Phase 4 — The door for a person with no group — **BUILT 2026-09-17**

### What shipped

- `IBenOrganizationClient.GetSoloPlanAsync` / `StartSoloPlanAsync` — the first callers the
  solo-plan endpoint has ever had. It shipped with the solo tier and sat unreachable: a person with
  no group could record field sessions from the phone and nothing else, because every other feature
  is org-scoped and they had no org.
- Place page, public locations only: **Investigate here** per group the reader belongs to, and
  **Start investigating on your own** for somebody who belongs to nothing. The second explains the
  bargain in a `BenModal` first — a private space of your own, free, nobody else joins it; and what
  you record here is public because you pay nothing — then mints the organization and goes straight
  on to the scheduling window with the place already settled. Straight on, because the button said
  "start investigating" and stopping at "you now have a space" leaves somebody to work out where
  investigations live.
- `NewInvestigationWindow` gains `PrechosenPlaceId`, so arriving from a place's page does not ask
  for an address the person is already looking at.
- **A seat that belongs to nothing**: `wren.ashby@benco.dev` in `DevelopmentRosterSeeder`, with no
  `MemberAsync` call anywhere — that absence is the fixture. `BenTestBase.SoloEmail`/`SoloPassword`,
  `BEN_SOLO_PASSWORD` derived in `seeded-passwords.sh` from the DevData password like Victor's.
  Daniel was the closest existing seat and is no use here: he is group-less but a *client*, and a
  client passes the feed's belonging rule, so he cannot show the wider door at all.
- Help: "Investigating on your own" in getting-started, cross-referenced from the-mobile-apps and
  from the plan section in organization-administration. Changelog.
- Tests: `PersonalOrganizationTests.Somebody_in_no_group_gets_one_of_their_own_and_it_is_public_by_default`
  (deterministic, and the real proof of the door); Playwright `SoloInvestigatorTests` — the door, the
  scope that can only be public, and the post-about-a-place-but-not-the-feed seam.

### The same Razor trap, twice in one afternoon

`<NewInvestigationWindow>` rendered as an empty `<newinvestigationwindow>` element because
`PlaceView.razor` had no `@using Ben.Web.Website.Library.Organization` — the button opened nothing,
with no error anywhere. Exactly the failure `<FeedComposer>` had an hour earlier in phase 3. Both
usings are now on the page with a comment saying why. Recorded as its own memory note
(`feedback_razor_unknown_component_renders_as_html`) because it will happen again.

### Wren's group-less state is a consumable, and the fixture had to learn that twice

`SoloInvestigatorTests` **writes**: the door test mints her a personal organization, after which
"belongs to nothing" is impossible on that database. Two mistakes came out of it, and an existing
guard caught the first.

1. The branches for "she already has a space" were `Assert.Pass`, and
   `PlaywrightTestsCanFailTests` refused the file outright: a browser test that ends itself as
   passed makes a regression in what it covers report green. It is right, and the guard's own
   instruction is the fix — `Assert.Ignore` for a missing precondition. So a database that cannot
   provide the seat now reports honestly as not-run, naming `BEN_E2E_DB` as the way to get it back.
2. Even on a brand-new database two tests ignored themselves, because NUnit runs alphabetically and
   the **scheduling** test spent the seat before the **door** test could use it. The fixture is now
   `[Order]`ed: the two tests that need her group-less run first, then the two that work either way.
   Four passed, none skipped, on a fresh `BEN_E2E_DB`.

The deterministic proof of the door remains the unit test, which needs no seat at all.

### As planned

1. **Website client** gains `StartSoloPlanAsync()` → `POST api/solo-plan` (exists, free, idempotent).
2. **`/my-investigations`** (`Client/MyInvestigations.razor`) and the place page's signed-in header gain
   **Investigate here / Start investigating on your own**: for somebody with no organization it mints
   the personal organization (one sentence first: "This creates a private space for your own
   investigating. On the free plan, what you record at public places is public; a plan lets you keep
   it to yourself."), then opens the New Investigation window with the place pre-chosen and
   `Visibility` fixed to Public per Phase 1. For somebody with groups it asks which group, as the
   scheduler does.
3. **Place page CTAs** (signed-in, `PublicLocation` only): Investigate here · Open a case here
   (members holding Cases.Create; `?place=`) · Post about this place. Three buttons, one row, wrapping
   on phones.
4. **Seed + seat.** `DevelopmentDataSeeder` adds a person with no group ("olivia.chen@benco.dev",
   password from the same gitignored source as the others) and a second `PublicLocation` with
   investigations from both seeded groups, so the place page has more than one group on it.
   `BenTestBase.SoloEmail`/`SoloPassword` (`BEN_SOLO_EMAIL`, `BEN_SOLO_PASSWORD`); `run-e2e.sh` and
   `seeded-passwords.sh` hand it through like the others. Never printed.
5. **Tests.** `PersonalOrganizationTests` already cover minting; add: minting from the web client
   shape; a no-group person's first investigation at a landmark is Public and cannot be narrowed.
   Playwright `SoloInvestigatorTests`: olivia signs in, opens the cave, Investigate here → sentence →
   window with the place fixed and only Public offered → the visit appears under "Your groups' visits"
   and, once published, for a visitor.
6. **Help.** `getting-started.md` new section "Investigating on your own" (free = public, plan = yours);
   `the-mobile-apps.md` cross-reference. Changelog.

## Phase 5 — Finishing — **DONE 2026-09-17**

1. **Changelog catch-up.** Checked all three streams against `git log` since their last dated
   heading. Nothing user-visible from today was missing; the internal commits — deploy notes, the
   plan document itself — are correctly absent. One entry had to be reworded because
   `ChangelogServiceTests` requires an entry to start as a sentence rather than with bold.
2. **Pictures and documents.** Four new screens captured through a new
   `HelpMediaCapture.Capture_Places`: the place page signed in (the load-bearing one — the only page
   that gathers several groups' work at one location), New Case with the offer showing and the kind
   choice, the composer on a place's page with a post already saying "at Bell Witch Cave", and the
   sentence somebody with no group reads first. `HelpMediaReferenceTests` refused the help text until
   the files existed, which is what made this a step rather than an intention. Product PDF and all
   six persona PDFs rebuilt.
   - Two shots went wrong first. Anchoring the New Case shot on the radio input gave a forty-pixel
     sliver, because `around` grows from the element's own box; it proves itself on the question
     instead. The dialog shot proved itself on the word "public", which appears several times on the
     page behind it, so the first match won and nothing was captured; it proves itself on the button.
3. **The walk.** `ProductWalk` gained a public place as a stranger, the same page as a member, a new
   case naming a place, and a whole persona — `Somebody_in_no_group` — which nothing covered, because
   every existing seat is a member, a client, a viewer or an admin and each passes doors Wren does
   not. Every persona walks clean. Three failed on the first pass and neither failure was the arc:
   the capture's `finally` had restored the feed switch to off so `/feed` correctly 404s, and a
   freshly seeded database has no field session to play back.
4. **Full run and merge.** Solution build clean, `Ben.Web.Tests` 6254/0, `Ben.Canvas.Tests` 927/0,
   and the whole Playwright suite on a fresh `BEN_E2E_DB` before merging to develop and master.

### As originally planned

1. **Changelog catch-up** against `git log` since each file's last heading.
2. **Help + pictures + PDFs**: re-shoot the place page (visitor and signed-in), New Case with the
   offer and the kind choice, the locked Make Public box, the place composer; rebuild the product and
   persona PDFs.
3. **ProductWalk** gains: the place page as visitor / member / solo; New Case at a known place;
   posting about a place. Run as every persona on the player copy; read every report.
4. **Full e2e**, then merge to develop and master and push. Runbook entry: the `PlacePosts` migration,
   no switch, "deploy API and website together", and what to check (a post appears on the cave's page).

---

## What this deliberately does not do

- **No shared case object, no case owned by several groups.** `Case.OrganizationId` stays non-null;
  every `CaseOrgAccess` check stays true. Sharing is the place.
- **No public case files/messages/notes.** Those tables stay private. The public "files at a place"
  are feed posts' media (screened) and the field archive (reviewed), which already have consent and
  review built in.
- **No curation of places** (spec open question 3). `Place.IsApproved` stays inert; the dedup offer and
  the admin merge are the tools. Revisit if junk places appear.
- **No reciprocity change** on `PlaceInvestigators` (open question 1).
- **No client-consent mechanism for residences** (open question 4) — which is why D5 refuses them.
- **No app work in this branch.** The app keeps working unchanged (D9); tagging a post to a place from
  the phone is the next app build, noted in `APP-STORE-1.0.4` when it starts.

## Verification

1. `dotnet build Ben.slnx` warning-free for touched projects; `dotnet test Ben.Web.Tests` green; every
   new rule test seen failing first with its rule broken (memory: `feedback_verify_test_discriminates`).
2. `scripts/run-e2e.sh --filter <fixture>` per phase; full run on the finished branch (last full run:
   716 tests, 33 min). Playwright runs with `dotnet vstest`, never `dotnet test`.
3. Migration applied to `IsHauntedDb_player` only; hosts on 5252/5078/5180/5125 restarted after each
   rebuild; never during a run.
4. Walk on the player copy as visitor, sarah, james, victor, daniel and olivia (memory:
   `feedback_test_as_ordinary_member`; the place page's public half must be traced as a visitor —
   `feedback_author_sees_what_visitor_cannot`).
5. iOS: decode a feed page and a public place response captured from the real API with the new fields
   present (fixture from the real API, never invented) in BenKit's tests; nothing renumbered.

## Open for Ben

- D6 (who may post about a place) and D7 (paid solo may open cases) are recommendations. Both are one
  predicate each; say the word and they flip.
- The sentences in Phase 1 are drafts. They will reach the phone one day, so they say the rule and stop.
- Whether an unpaid group's **existing** cases at public places become public. This plan says **no**:
  the rule applies to cases created after it ships, exactly as the member cap never evicted anybody
  (`PaidPlan.cs:73-76`). Flipping history public without asking would be the side-effect publication
  D3 rules out.
