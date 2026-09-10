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

Apple's portal also offers a **pre-made "Maps token"** (a CSV holding a non-expiring JWT bound to
`*.ishaunted.com`). Ben downloaded one on 2026-09-10; it is kept beside the keys and **not used**:
the token the site signs itself lives thirty minutes and names the exact origin, which is strictly
better, and the pre-made one would not work on `localhost` anyway.

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
| 1 | **Done.** `Kit/Maps/BenMap` (+ `BenMapPin`, `BenMapViewport`): one `mapkit.js` load and one `init` per page, pins as `MarkerAnnotation`s with glyph/colour/dim/cluster, select → .NET, fit, resize, viewport reporting once per burst of drags, colour scheme following `data-bs-theme` live. The Telerik fallback lives inside it. `MapProviderGuardTests` refuses any provider mention outside `Kit/Maps/`, with a shrinking allow-list for the three components still to cross | Guard test (2); verified through phase 2 rather than a throwaway page — a page that exists only to be tested would have tripped the route crawl |
| 2 | **Done.** `InvestigationsMap` is a thin adapter over `BenMap`: same-spot grouping, dimmed past visits and the public-landmark mark stay here; its `.razor.js` is gone | Playwright `MapViewport` (3): three drags on the group map produce one bounded request (needed the burst rule above — MapKit reports a drag only when its momentum settles); selecting a pin opens the investigation; the home map still narrows its list. Browser: the Bell Witch Cave place page draws Apple's dark map with its one dimmed pin |
| 3 | **Done.** `PublicCaseDiscovery` on `BenMap` with the view as the caller's (`FitToPins` off; the person's own location moves it through Center/Zoom); the ~1 km grouping stays, MapKit gathers overlapping groups further; a haunted case is gold and says so in its subtitle; the module keeps only geolocation. Two races in `BenMap` found and fixed here: a parent re-render during the wrapper's async first render used to mark pins drawn before the map existed (a recreated map came up empty), and a tear-down during that first render used to throw a disposed reference into the circuit (the sort buttons showed "An unhandled error") | Playwright `HomeMap` (10, rewritten for canvas pins through the module's `pins`/`selectPin`) + `MapViewport` (3), all green; browser: the home map recentres on the person and survives a sort |
| 4 | **Done.** `AddressMapPlayer` on `BenMap`: the marker keeps the person's colour and chosen icon (an SVG path drawn white into the pin through `glyphImage`); the region is a `CircleOverlay` of a real radius in metres; a click on empty map reaches `OnMapClicked` as a ground coordinate, and "click to set the edge" works as before. The Telerik fallback draws the same circle from the old GeoJSON helper. `AddressMarkerModel` and the component's `.razor.js` are gone | Playwright `AddressMap` (1 case, through the profile's Add-address editor): one pin on a zoom-2 world view, a circle drawn and removed, a click reported as a coordinate; `HomeMap` + `MapViewport` still green (14). Not covered: the Field Kit player's region circle from a saved config — same code path, no seeded session with one |
| 5 | **Done.** `BenMap.RouteAsync` asks `mapkit.Directions` for a driving route — from an address in words, which the provider looks up itself, or from the person's own location — draws it with A and B pins, and returns distance, time and turn-by-turn steps. `DirectionsMapModal` keeps its form, summary, printable steps and "Open in Maps" (now an Apple Maps link). **Retired:** `DirectionsController` and its call to the public OSRM demo server over plain http, the client's `GetDirectionsAsync`, the three route records, and the modal's metered Geocodio lookup of the starting address | Playwright `Directions` (1 case, a real route asked of Apple: Nashville → the Bell Witch Cave, ~40 miles, steps, drawn then cleared) plus the other three map categories (16 in all). Not covered: the admin Directions button itself — no seeded address carries coordinates, so it does nothing there |
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
