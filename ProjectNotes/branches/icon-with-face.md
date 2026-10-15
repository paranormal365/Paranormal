# feature/icon-with-face (10/15/2026)

Ben: "Replace the icon with the new one with the face added back across the site and documentation
and note it in the change log." The violet icon's ghost was the original logo painted all white, so its
eyes and mouth vanished; they are near-black again, as in the original logo.

## The one source

- App icon: `Ben.iOS/IsHaunted/Resources/IsHaunted-iOS26-Button.icon` (Icon Composer). Its ghost,
  `Assets/is-haunted-logo-white.svg`, has the face (paths 2, 3, 4: mouth and eyes, `#040404`).
  Ships in iOS 1.1.4 build 15.
- Rendered by Icon Composer's own tool, so the site shows exactly what the phone shows:
  `"/Applications/Xcode.app/Contents/Applications/Icon Composer.app/Contents/Executables/ictool"
  Ben.iOS/IsHaunted/Resources/IsHaunted-iOS26-Button.icon --export-image --output-file icon.png
  --platform iOS --rendition Default --width 512 --height 512 --scale 2`

## Website

- `favicon.png`, `favicon-16.png`, `static/images/app/app-icon.png`: the rounded icon.
- `apple-touch-icon.png`, `icon-192.png`, `icon-512.png`: the same, edge to edge (iOS rounds them and
  drops alpha). `apple-touch-icon.png` also heads every email.
- `app-store-qr-dark.png` / `-light.png`: the new icon in the center; both still scan to the same
  App Store link (checked with Apple's Vision barcode reader).
- Header tile: `static/images/is-haunted-mark.svg` (the white ghost with its face) instead of the color
  logo washed white by `filter: brightness(0) invert(1)`, which is what removed the face.
- Sign-in page and the empty-page marks: the app icon instead of the old green logo; the dark circle
  that propped up the pale ghost on light cards (`.ben-mark-on-light`) is gone.
- The canvas and video editors' browser tabs: the IsHaunted icon instead of Blazor's placeholder.

## Documents

`docs/site_doc_style.py` (product documentation, seat guides, iOS guides, sensor builder's guides),
`docs/ads/build-ads.py`, `build-flyer.py` and the app guide, all rebuilt and republished to the site.
App Store header and search art and the In-App Event art rebuilt. Not changed: the animated video ad
(`docs/ads/ishaunted-ad`) and the design explorations in `docs/design`, which draw the original
full-color logo, face included.

## Still to do in App Store Connect

Upload the rebuilt header and search art to the 1.1.4 version and the seven custom pages, and the
event art to the iPhone Duo event draft. The Halloween Night event is already in review with its old
card art; leave it.

## Second pass, 10/15/2026 (feature/icon-everywhere)

Ben: "Update the icon everywhere it is used ... Don't worry about the ads. They can keep the old green
version."

- **Ads, flyer and the hosted-events brochure put back** to the green logo, as they were before this
  work (PDFs on the site too). New ads will be made with the new icon.
- **Help screenshots:** 102 website screenshots (`wwwroot/help/media`, `Help/Media`) show the site
  header, whose tile had the faceless ghost. Rather than retake them all, the face was painted onto the
  ghost in each one, at exactly the place and size the site now draws it: the tile found by its
  violet-to-cyan colour near the top left, squared, the mark at 68% of it, the face layer rendered from
  the same SVG. Every one was checked by eye before and after.
- **`getting-started/home-app.png`** showed the old green app icon and old QR in the home page's app
  section: both replaced in place; the QR in the screenshot still scans.
- Product documentation and the seven seat guides rebuilt again, since they embed those screenshots.
- Not touched: `docs/media/hosted-events` (used only by the ads and the investor overview) and the
  untracked Thomas House files.
- App Store Connect: 1.1.4 resubmitted with build 15 (icon with the face), the new header and search
  art and the fixed iPad preview; the seven custom pages and the iPhone Duo event draft have the new
  art. The app's icon shown at the top of App Store Connect and on the App Store changes by itself when
  1.1.4 is released.

## Third pass, 10/15/2026: the final sweep

A check over every tracked image found what the first pass missed:
- **65 seat-guide screenshots** in `docs/web-media` (the first scan never looked there): the face
  painted on, each checked by eye; the seven seat guides rebuilt.
- **The iPhone notification screenshot** (`help/media/the-mobile-apps/iphone-launch-notified.png` and
  `docs/ios-media/iphone/54-launch-notified.png`): the old green app icon on the notification and on the
  home screen, replaced with the new one; the iPhone guide rebuilt and copied to the site. The picture
  still says "Field Kit" in the notification's text, which the server no longer sends; it is on the
  1.1.4 help branch's list of pictures to retake.
- The home-page help screenshot's QR: the first patch let the old icon's dark pixels through as
  specks; set in again through a plain rounded mask, and it still scans.

What the check still reports, and why it is fine: the 32 px favicons (the face is too small to see at
that size), the In-App Event cards (the face is there; the background fooled the check), and
`static/images/ghost glass 699x640.png`, which no page uses.
