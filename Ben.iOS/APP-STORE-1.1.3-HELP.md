# Help to restore when iOS 1.1.3 is released

`ios-1.1.3` was merged into master on 10/08/2026 so the app's code and the violet icon live with
everything else, but 1.1.3 is not in the App Store yet, so the help was held back: it should never
describe a screen nobody can open.

## Already done, on branch `release/ios-1.1.3-help` (10/13/2026)

Waiting on that branch, not on master, so nothing shows until it is merged:

- **Is mine the newest?** is back in `Ben.Web.Services/Help/Content/the-mobile-apps.md`, after the
  app guide link.
- **Designed menus:** `the-mobile-apps.md` (During the event: "no menus are published", the new
  **Menus** bullet, the caption "A menu, drawn in the look the hosts chose") and
  `going-to-an-event.md` ("The iPhone and iPad app shows the same menus, in the same look, with the
  same PDF.") are word for word what 91d66541 had.
- The 1.1.3 screenshot is copied over
  `Ben.Web.Website/wwwroot/help/media/the-mobile-apps/iphone-event-menus.png`, and
  `Ben.iOS/help-1.1.3/` is deleted.

## Still to do on release day

1. **The violet icon, from Apple.** The home page's app icon
   (`Ben.Web.Website/wwwroot/static/images/app/app-icon.png`, 256 x 256) and the QR codes' centre
   (`app-store-qr-dark.png`, `app-store-qr-light.png`) still show the green icon. The icon is an Icon
   Composer file (`IsHaunted-iOS26-Button.icon`), drawn with Apple's glass, so a PNG made by hand would
   not match what people see. Once 1.1.3 is live:
   - the icon: the App Store lookup (`https://itunes.apple.com/lookup?id=6806786633`) returns
     `artworkUrl512` - the violet one, as Apple draws it (checked 10/13: the lookup works and names
     the `IsHaunted-iOS26-Button` artwork; it still says 1.1.2 until release). It comes as a SQUARE
     JPG; the current `app-icon.png` has rounded, transparent corners, so round them before saving it
     at 256 x 256.
   - the QR codes: Apple's marketing tools (where the current ones came from) draw them with the new
     icon in the centre. Keep both, dark and light.
2. **Rebuild on the Mac** (the builders need Python and Chrome at its Mac path):
   - the app guide, `docs/ads/build-app-guide.py`, since it draws `app-icon.png`;
   - the product documentation PDF, `docs/build-documentation-pdf.py`.
3. Merge `release/ios-1.1.3-help` into develop and master, then stage and deploy.
