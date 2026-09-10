# Item 228: Apple Maps (MapKit JS) in place of OpenStreetMap tiles

Branched from `develop` at `84a853e7` (2026-09-10). Ben's ask: *"Could we use Apple Maps in the
website instead of OpenStreetMap, or does that cost money?"* — it costs nothing beyond the Developer
Program membership already paid for Sign in with Apple and the iOS app, and the tile server the
site uses today is one whose usage policy tolerates us rather than serves us.

## What the portal needs (Ben, now)

**A Maps ID and a key with the Maps service enabled. Not a Services ID.** A Services ID is Sign in
with Apple's identity for the web (item 227); MapKit JS never sees it.

1. **Identifiers → Maps IDs → +**: `maps.com.ishaunted` — created 2026-09-10, name "AppleMaps". This is the `id` the token names.
2. **Keys → +**: name it for the website, tick **Maps**, choose that Maps ID, download the `.p8`.
   Apple hands the file over once. Note the **Key ID** shown next to it.
3. Nothing else: no redirect URLs, no domain file, no App ID grouping. Team ID stays `5778H75249`.

**Done 2026-09-10:** key "IsHaunted Apple Maps", Key ID `623JTDWHAQ`, the `.p8` kept at
`~/.ishaunted/AuthKey_623JTDWHAQ.p8` (mode 600) on Ben's machine. The website reads
`Maps:TeamId`, `Maps:KeyId` and `Maps:PrivateKeyPath` — a **path**, so the key itself is never in
configuration, let alone the repository, which is public. A missing path means "not configured"
and the Telerik map; a malformed key throws at startup, because that is a deployment mistake.
Production needs the same three values and the file placed on the server.

## What is there today, exactly

| Component | Hosts | What it draws | Provider |
| --- | --- | --- | --- |
| `Shared/InvestigationsMap` | OrgInvestigations, MyFieldSessions, MyProfile (map tab), PlaceView, MyInvestigations | pins with a click | OSM tiles via `TelerikMap` |
| `Shared/PublicCaseDiscovery` | Home | clustered pins, "near you" from the browser's location | OSM tiles via `TelerikMap` |
| `Manage/Maps/AddressMapPlayer` | FieldKitPlayer, AddressFieldsWithMap, AddressMapModal | one pin, a radius circle, click to move the pin | OSM tiles via `TelerikMap` |
| `Manage/Maps/DirectionsMapModal` | AdminUserDetail | a route polyline, two pins, printable steps | OSM tiles via `TelerikMap`; route from the **public OSRM demo server over plain http** |

Geocoding is separate: eight server call sites go through `AddressGeocodingService` to Geocodio,
which is metered. This plan leaves that alone; a later item can move it to the Apple Maps Server
API, whose geocoding sits inside the same free daily quota.

## The one fact that shapes the design

**Apple does not serve raster tiles.** `TelerikMap`'s tile layer wants a `{z}/{x}/{y}` URL and
there is none to give it; Apple's map is only reachable through the MapKit JS library, which
renders the map itself. So this is not a URL swap. Each of the four components is rebuilt on a
new `Kit/BenMap` that wraps MapKit JS, in the same way `Kit/` wraps Telerik widgets today — one
place knows the library, everything else passes pins.

What MapKit JS gives back for the trouble: annotation clustering built in (Home's clusters are
hand-rolled today), a `Directions` service that replaces the OSRM demo server, dark mode that
follows the site's theme, and attribution it draws itself. Quota is 250,000 map views and 25,000
service calls a day; over that, Apple is asked for more, not paid.

## Phases

Each phase ends with `dotnet build Ben.slnx`, `dotnet test Ben.slnx`, the relevant Playwright
category against the running site, and its own commit. No `git add -A`.

| Phase | Work | Verified by |
| --- | --- | --- |
| 0 | **Done.** `MapKitSigningOptions` + `MapKitTokenService` (hand-rolled ES256, 30-minute life, `origin` claim, raw r‖s signature); `GET /auth/mapkit-token` (404 until configured, `no-store`); `Kit/Maps/MapsOptions` with `Provider` and `Effective`, Apple honoured only when a token can be signed | 9 unit tests; a token fetched from the running site decoded to the expected header and claims; **Apple accepted it**: `mapkit.init` reported `Initialized` and a map of Nashville with a marker drew over the home page (MapKit JS 5.81.65) |
| 1 | `Kit/BenMap`: loads `mapkit.js` once, initialises with the token endpoint, exposes Center/Zoom/Height, Pins (with a per-pin template and click callback to .NET), FitToPins, Resize, Dispose, theme-following colour scheme. Source-scan guard: `mapkit.` appears nowhere outside `Kit/` | Guard test; a Playwright page with the component alone; the pin click reaches .NET |
| 2 | `InvestigationsMap` on `BenMap`, behind the provider switch | Playwright on OrgInvestigations, PlaceView and the profile map tab; the pin click still navigates |
| 3 | `PublicCaseDiscovery` on `BenMap`: MapKit's `clusteringIdentifier` replaces the hand-rolled clusters; browser geolocation as today | Playwright `HomeMap` category; declined geolocation still shows the map (the NearbyDiscovery lesson) |
| 4 | `AddressMapPlayer` on `BenMap`: `CircleOverlay` for the radius, single-tap moves the pin, coordinates flow back as they do now | Playwright on the address editor and the Field Kit player; a placed pin saves the same coordinates |
| 5 | Directions on `mapkit.Directions`: route, ETA and steps drawn client-side; `DirectionsController` and the OSRM dependency retired; print view kept | Playwright on AdminUserDetail; no request leaves for `router.project-osrm.org` |
| 6 | Remove the Telerik path and the provider switch; delete the four `.razor.js` tile templates and every OSM attribution string; help documents that mention maps re-read and their screenshots recaptured; item 228 closed in the backlog | Full suite; `HelpMediaCapture`; a grep proving `tile.openstreetmap.org` is gone |

Phase 0 and 1 land before anything visible changes. Phases 2–5 are one component each and can
ship separately behind the switch. Phase 6 is when the old path goes.

## Deliberately unchanged

- **Geocoding stays on Geocodio.** Moving it is a separate decision with its own quota arithmetic.
- **The iOS app**, which is already on native MapKit.
- **The Field Kit's stored coordinates**: pins move between providers; data does not.

## Not verifiable here

MapKit JS refuses to initialise without a real key, so Phase 0's browser check waits on the
portal step above. Everything from Phase 1 on can be exercised locally once the key exists;
`localhost` is an acceptable origin for a MapKit token.
