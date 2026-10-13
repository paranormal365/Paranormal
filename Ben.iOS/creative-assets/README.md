# App Store creative assets

For App Store Connect → the version → **Product Page Information → Header and Search Results**
(added by Apple in October 2026; optional, shown on iOS 27 and later, reviewed apart from the app).

| File | Tab | Size |
|---|---|---|
| `header.png` | Header | 3840 × 1646 (21:9) |
| `search-results.png` | Search Results | 3840 × 2560 (3:2) |

Both are RGB with no transparency, which Apple requires.

They match the website: the home page's photograph, its kicker and headline ("One home for the
paranormal", "Find out what happened here.") in Public Sans, the Signal night color and violet-to-cyan
gradient, the violet app icon, and real app screens from `screenshots-1.1.2/`.

Apple crops both differently per device. What must be seen (icon and words, and on the search image
the two Field Kit phones) is inside the centered safe areas: 1646 × 661 on the header, 2168 × 1030 on
the search image. The outer phones are for wide screens.

Rebuild after changing anything: `python3 Ben.iOS/creative-assets/build.py` (add `--guides` to draw the
safe areas). The photograph is under the Unsplash License, credited in
`ProjectNotes/FeatureHistory/README-hosted-events-235-media.md`.
