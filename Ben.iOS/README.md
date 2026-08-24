# Ben.iOS — the IsHaunted iPhone + iPad app

Native Swift 6 / SwiftUI universal app for the IsHaunted platform. One target
runs on both devices: a `TabView` on iPhone, a `NavigationSplitView` sidebar on
iPad — chosen by size class, so Split View / Stage Manager degrade gracefully.

Full plan: `~/.claude/plans/playful-leaping-tome.md` (Phase 1 in 8 slices).
**Status: Slice 1 (scaffolding + kernel) complete and verified live.**

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

## Verified (Slice 1, 2026-08-24)

- 46/46 BenKit unit tests green (`swift test`), including fixtures captured
  from the live dev API — which locks the camelCase-keys and naked-UTC-date
  assumptions and proves the Swift records match `Ben.Service.Models`.
- Unsigned build succeeds for iPhone + iPad simulators.
- Launched on iPhone 17 Pro and iPad Pro 13-inch (M5): adaptive shell correct
  on both; `GET api/public/events` decoded and rendered identically to the
  website's `/events` page (same three seed events, same local-time rendering).
- Deep link `https://ishaunted.com/events` routed to the native Events screen.

## Slices remaining (Phase 1)

2. Auth core (Keychain, sign-in + 2FA challenge, api/me, session-ended banner)
3. Feed read-only (modes, cursor+de-dupe, media, feature-gate-404 state)
4. Feed participation (composer, camera, multipart upload, likes/replies)
5. Notifications (60 s foreground polling, summed bucket badge, messages)
6. My Cases (occurrences + photo attach, authed thumbnails, reports → PDFKit)
7. Investigations + Events (RSVP, attended MapKit map, EventKit add-to-calendar)
8. Account completeness (register, confirm-email deep link, 2FA setup QR)

Later phases: equipment + checkout, org-side management, org messaging,
discovery/places, publications, APNs push (needs server work), universal links
(needs AASA hosting).
