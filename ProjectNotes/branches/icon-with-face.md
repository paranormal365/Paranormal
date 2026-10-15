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
