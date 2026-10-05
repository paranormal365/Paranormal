# The app on the home page, and a guide for customers (10/05/2026)

Ben: include the App Store badge, logo and QR codes on the home page "with the link to the app in the
app store", plus "images and information about the app store app as a feature", in Signal; link to
documentation, reworked "flashy and professional like the flyers". Then: "place the info ... near the
bottom, but put a scroll to advertising link near the top", and "use dark and light in the right modes".

- `HomeAppFeature` (Ben.Web.Website.Library/Shared): near the foot of Home for everybody, `#the-app`.
  The app icon, a pitch, six features, a fan of three real iPhone screens, Apple's badge (black on the
  light page, white on the dark) and Apple's QR code (light/dark the same way; hidden on phones), links
  to the help and the guide. The store link keeps `data-testid="home-app-store"`.
- `HomeHero`: a pill near the top, "Field Kit for iPhone & iPad — see what it does ↓", scrolling to
  `#the-app`. It doesn't name the site (the hero never does; HomePageTests guards it).
- Assets in `Ben.Web.Website/wwwroot/static/images/app/`: Apple's files as supplied (the QR codes open
  https://apple.co/4y7fKo7 → the IsHaunted store page), the icon at 256px, three screens as JPEG. The event
  pass is left out of the marketing: its QR code is a live pass on the capture database.
- `docs/ads/build-app-guide.py` → `docs/IsHaunted-App-Guide.pdf` and `/guides/IsHaunted-App-Guide.pdf`:
  six Letter pages in the flyers' style. The old `/guides/IsHaunted-iOS-iPhone.pdf` is the developer
  handover and stays as it is; the App Store's Marketing URL could move to the new guide with 1.1.3.
- "Car park" → "parking lot" in two help articles; AmericanSpellingTests now refuses it (proved
  against the old text first).
