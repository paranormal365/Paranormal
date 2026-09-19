# Apple Maps Server API fixtures

Captured 2026-09-10 from `https://maps-api.apple.com/` with the real Maps key (`623JTDWHAQ`), by a
throwaway console program that signed the auth token, exchanged it at `v1/token`, and made each
request below. Never invented: a fixture written from documentation shipped a feature once that
the real API did not match (see the fixtures lesson in the client tests).

| File | Request |
| --- | --- |
| `token-200.json` | `GET v1/token` with the signed auth token. The access token value is redacted; the shape is the contract. |
| `geocode-address-200.json` | `GET v1/geocode?q=430 Keysburg Rd, Adams, TN 37010&limitToCountries=US&lang=en-US` — a numbered building (`rooftop`). |
| `geocode-query-200.json` | `GET v1/geocode?q=Nashville, TN&limitToCountries=US&lang=en-US` — a town (`place`). |
| `geocode-nothing-200.json` | `GET v1/geocode?q=zzqx qqzx nowhere 99999&...` — Apple answers 200 with an empty `results`. |
| `reverse-200.json` | `GET v1/reverseGeocode?loc=36.1627,-86.7816&lang=en-US` — Church Street Park, Nashville. |

To recapture: sign a JWT (`iss` team, `iat`, `exp`) with the Maps `.p8`, `GET v1/token` with it as
a Bearer, then call the endpoints with the returned access token as a Bearer.
