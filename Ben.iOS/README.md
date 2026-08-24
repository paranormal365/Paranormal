# Ben.iOS — the IsHaunted iPhone + iPad app

Native Swift 6 / SwiftUI universal app for the IsHaunted platform. One target
runs on both devices: a `TabView` on iPhone, a `NavigationSplitView` sidebar on
iPad — chosen by size class, so Split View / Stage Manager degrade gracefully.

Full plan: `~/.claude/plans/playful-leaping-tome.md` (Phase 1 in 8 slices).
**Status: Slices 1–7 complete (kernel, auth, feed, participation, notifications, cases, investigations + events).**
**How to run and test it (written for a C# developer): see `TESTING.md`.**

## It cannot interfere with the website

- `Ben.iOS/` is **not referenced by `Ben.slnx`** — `dotnet build`, the website,
  and the WebApi never see it.
- The app is a pure REST client of `Ben.Data.WebApi` (localhost:5252 in Dev).
  The website runs on :5078. No shared ports, processes, or build artifacts.
- Open `IsHaunted.xcodeproj` in Xcode and run the website side-by-side freely.

## Design rules

1. **Website features map to native counterparts.** Calendar → EventKit,
   locations → MapKit, uploads → native camera/PhotosPicker, reports → PDFKit,
   sharing → share sheet. If the website does it in HTML, the app does it the
   iOS way.
2. **One URL space, two front ends.** `DeepLinkParser` (BenKit) maps the
   website's route table (`/feed/{id}`, `/events`, `/my-cases/{id}`,
   `/validate-email/{token}`, `/attending/{token}`, …) onto native screens, for
   both `ishaunted://` links and — once the server hosts an
   `apple-app-site-association` file — `https://ishaunted.com/...` universal
   links.
3. **A refusal must never render as "nothing here."** Every screen goes through
   `LoadStateView`: loading / empty / refused-failed / session-ended /
   rate-limited are visually distinct, porting `LoadResult.cs` +
   `README-refused-vs-empty.md` from the web project.
4. **The C# client patterns are the spec.** `TokenSession` ports
   `WebApiBearerTokenHandler.cs` (single-flight refresh, expiresIn − 30 s);
   response mapping ports `WebApiClient.SendListAsync` byte-for-byte (401 =
   session ended, 403 ≠ session ended, <400-char prose rule).

## Layout

```
IsHaunted.xcodeproj/   minimal shell; buildable-folder refs (adding files ≈ no pbxproj diff)
IsHaunted/             app target: App/ Navigation/ Features/ DesignSystem/ Resources/
Support/Info.plist     ATS = local networking only; ishaunted:// scheme; usage strings
BenKit/                local Swift package — networking, auth, models, config (~90% of code)
BenKit/Tests/          unit tests + fixtures captured VERBATIM from the running dev API
scripts/               test.sh · build.sh · run-sim.sh
```

## Build & verify

```bash
./scripts/test.sh        # BenKit unit tests on the Mac host (no simulator)
./scripts/build.sh       # unsigned simulator build (CI-safe)
./scripts/run-sim.sh     # build + install + launch on iPhone 17 Pro
./scripts/run-sim.sh "iPad Pro 13-inch (M5)"
OPEN_LINK="https://ishaunted.com/events" ./scripts/run-sim.sh   # deep-link on launch
```

Dev loop: run the API (`dotnet run` in `Ben.Data.WebApi`, :5252 — kill stale
hosts with `pkill -f "Ben.Data.WebApi"` first), pick **Dev** in the app's
Profile → API environment. The simulator reaches the Mac's localhost directly.

The `-openLink <url>` launch argument routes like an incoming deep link without
the OS confirmation dialog — used by automation and the future UI test target.

## Verified (Slice 2, 2026-08-24)

- 58/58 unit tests: login outcome mapping (2FA-vs-bad-password from
  ProblemDetails `detail`), the full SessionStore state machine, single-flight
  refresh, quiet stale-token restore, deliberate-sign-out vs interrupt banner.
- Live against the dev API on iPhone 17 Pro: signed in as the ordinary member
  seed (`james.thornton@benco.dev`), `api/me` resolved, and the session
  SURVIVED kill + relaunch via the Keychain alone.
- Lesson recorded: a fully unsigned build (`CODE_SIGNING_ALLOWED=NO`) cannot
  use the simulator Keychain — persistence silently fails. Only `build.sh`
  (compile check) disables signing; `run-sim.sh` and Xcode use ad-hoc signing.

## Verified (Slice 1, 2026-08-24)

- 46/46 BenKit unit tests green (`swift test`), including fixtures captured
  from the live dev API — which locks the camelCase-keys and naked-UTC-date
  assumptions and proves the Swift records match `Ben.Service.Models`.
- Unsigned build succeeds for iPhone + iPad simulators.
- Launched on iPhone 17 Pro and iPad Pro 13-inch (M5): adaptive shell correct
  on both; `GET api/public/events` decoded and rendered identically to the
  website's `/events` page (same three seed events, same local-time rendering).
- Deep link `https://ishaunted.com/events` routed to the native Events screen.

## Verified (Slice 3, 2026-08-24)

- 71/71 unit tests: fresh live fixtures lock the arc's full record surface (categories,
  attribution, badges); the For You de-dupe; 404 → `.featureUnavailable` (a switched-off
  feature is a fact, not an error); the dead-token fall-back to reading as a visitor;
  the mention/hashtag linkifier mirroring the server's tag rule.
- Live on iPhone 17 Pro + iPad Pro (M5): For You/Latest modes, media from the anonymous
  route, linkified tags navigating in-app, the BenCo attribution + Group-verified chip,
  and the flag-off state rendering as "switched off sitewide", with the flag restored dark.

## Verified (Slice 4, 2026-08-24)

- 86 unit tests, plus an opt-in LIVE suite (`BEN_LIVE=1 swift test --package-path BenKit
  --filter LiveFeedWriteTests`) that signs in for real, uploads real multipart media, and
  round-trips a like against the running API — the nearest thing to a finger on the screen
  that does not need one. All 4 live tests pass; they skip silently without the flag.
- Found live and fixed: the feed asked "may this reader post?" before sign-in resolved and
  was answered as a visitor, so the compose button never appeared — the website's own
  `_participationKnown` lesson, in iOS form. The list now re-asks when the session changes.

## Verified (Slice 5, 2026-08-24)

- 97 unit tests. The urgency rule is ported from `Ben.Web.Services.NotificationBadge` at the
  same thresholds and pinned by a test that states the point: fifty items from this morning
  are Fresh, one from last week is Overdue. A live fixture from a real account (90 waiting,
  both item-173 breakdowns) locks the payload, with a test that the roll-up equals what the
  rows can open.
- Live on iPhone: five rows with real counts; the two case rows open the case on its group's
  side; the three with no app screen yet are greyed and inert rather than offering a chevron
  that lies.
- Found live and fixed: opened by deep link at launch, the screen's first load beat sign-in
  and was answered as a visitor. Screens that depend on identity now re-ask when identity
  resolves rather than trusting a parent's observer to win the race.
- Deferred deliberately: foreground polling. The screen reloads on appear, on pull-to-refresh
  and when the session changes; a timer would add battery cost for a badge nobody is watching
  while the app is closed. Real push (APNs) is the honest answer and needs server work.

## Verified (Slice 6, 2026-08-24)

- 107 unit tests. Live fixtures for the list and the detail; the timeline is ordered by when
  things HAPPENED (falling back to when they were written), so somebody logging three months
  of experiences in one sitting doesn't have them read as all happening that evening.
- `AuthenticatedImageLoader` + `APIClient.loadData`: case files sit behind a bearer token, and
  `AsyncImage` issues its own unauthenticated request — it would render every case photo as a
  broken frame. Bounded cache; a failure shows a "couldn't load" glyph rather than a spinner
  that never ends.
- A 404 on a case reads "That case isn't available" — one answer for gone and not-yours alike,
  because "that case isn't yours" would confirm to a prober that the case exists.
- Live on iPhone: the list renders the real case with its reference, status chip and location.
  Also seen working: a 429 during a concurrent e2e run rendered as "Too many requests — try
  again shortly" with a retry, which is the honest-states design doing its job.
- Not yet: logging a new occurrence from the app, and case reports → PDFKit. Reading first.

## Verified (Slice 7, 2026-08-24)

- 117 unit tests. Live fixtures for the roster, the attended list and public events.
- The roster splits by whether an investigation has HAPPENED, and an investigation that has
  started but not ended still counts as upcoming — a roster that buries the one you are
  currently at is useless.
- The attended map draws only visits that have coordinates, and the screen SAYS how many it
  could not draw. A place with no coordinates is a real visit with no pin; dropping it from
  the map is right, dropping it from the list would be losing it.
- A null attendee capacity is UNLIMITED, not zero — treating it as zero would mark every
  uncapped event full. Pinned by a test, along with an overbooked event reporting zero left
  rather than a negative.
- RSVP refusals carry the server's own sentence: "This event is full" sends somebody to
  another date; "Couldn't RSVP" sends them to press the same button again.
- Native counterparts per the design rule: MapKit for where you've been, and EventKit's own
  add-event sheet (so the person picks the calendar and the app never needs full access).
  The calendar entry names the TOWN, because the server's public coordinates are deliberately
  approximate and the app must not imply it knows the venue.

## Slices remaining (Phase 1)

8. Account completeness (register, confirm-email deep link, 2FA setup QR)
9. **Sign in with Apple** (required by Ben, and by App Review once any
   third-party login exists). Client: `SignInWithAppleButton` →
   Apple identity token. Server (one new endpoint, built when the web side is
   quiet): validate the Apple JWT (issuer `appleid.apple.com`, audience = the
   app's bundle id), then link-or-create through the SAME external-login
   pattern `MeController` uses for Microsoft/Entra — provider `"Apple"`, key =
   Apple's `sub` claim — and answer with the standard Identity bearer tokens.
   A brand-new Apple user still needs DisplayName + Handle, so the app collects
   those before calling create. Needs the paid Apple Developer Program for the
   entitlement on real devices.

Later phases: equipment + checkout, org-side management, org messaging,
discovery/places, publications, APNs push (needs server work), universal links
(needs AASA hosting).
