# Item 230: geocoding on the Apple Maps Server API

Branched from `develop` at `9b0b276f` (2026-09-10). Ben: *"Move geocoding to apple."* The last
metered call on the site, after item 228 moved the maps and directions.

## What moved

`AddressGeocodingService` is the one static door the address forms, the place records and the
search box use; eight callers, none of which learned which provider stood behind it. Behind it
now is `AppleMapsGeocoder` on the Apple Maps Server API (`https://maps-api.apple.com/v1/`):

- **Forward** — `v1/geocode?q=…&limitToCountries=US` for a structured address or a free query.
- **Reverse** — `v1/reverseGeocode?loc=lat,lon`, read into the forms' street / city / state /
  zip / country from Apple's `structuredAddress`.
- **Precision** — the address tables already store a `GeocodingResultType`; Apple's answer is
  classified into the same words: `rooftop` (a numbered building), `street`, `place`.
- **Auth** — the same Maps key the website signs MapKit tokens with. A ten-minute auth JWT buys a
  thirty-minute access token from `v1/token`; that is cached, refreshed a minute early, and
  replaced once on a 401. `Es256Jwt` in `Ben.Data.Common` now signs all three Apple tokens.
- **Nothing throws.** A failed lookup answers the same `Empty` the previous provider did, so the
  callers' explanations to the person still hold. Unconfigured means every lookup answers nothing.
- **Retired:** `Geocodio:ApiKey/BaseUrl` everywhere — the API's settings, the deploy script, the
  secrets template, the IIS setup script, the deploy notes. The API's `Maps:*` settings replace
  them and the deploy script carries them from the same `AppleMapsKeyId/Path` the website uses.

## Verified

| Check | Result |
| --- | --- |
| Fixtures | Captured from the real API with the real key (`Ben.Web.Tests/Fixtures/AppleMaps/`), never invented |
| `AppleMapsGeocoderTests` | 10 cases: rooftop address, town, nothing found, reverse, token reuse until near expiry, one 401 retry, refused token, incomplete address not sent, country codes, the silent unconfigured door |
| `AppleMapsLiveTests` (opt-in) | Apple geocoded "Nashville, TN" and named it back from the coordinate |
| Through the running API | `GET api/geocode/search?q=Nashville, TN` answers Apple's coordinates |
| Playwright | `Nearby`, `Home`, `AnonymousClientRequest` categories, which geocode for real |

## Quota

25,000 service calls a day on the developer membership, shared with the website's maps. The
anonymous search endpoint keeps its rate limit for that reason; the wording changed from "paid"
to "shared".
