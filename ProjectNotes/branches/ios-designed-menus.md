# feature/ios-designed-menus

Ben, 10/13/2026, after the designed menus and their fonts were merged: *"come back and update the iOS
to work with the new stuff."*

## What the app did before

The app read `GET api/public/hosted-events/{id}/menus`, the old meal-by-meal list, and drew it as plain
rows: one course after another with tag badges. That endpoint was kept working for the released app by
filling it from the published menus (`EventMenuLegacyShape`), but it drops nearly everything the new
menus carry: the design, prices, what is included, allergens, the throughout-the-event menus and
the PDF. It also only answered a confirmed lead booker, while published menus are public on the
website.

## What it does now

- **The menus screen** (`MenusView`) reads `published-menus`, anonymously, and draws each menu in its
  own design (`DesignedMenuView`): paper and inks, frame, ornament (the same shapes as the website's
  SVG and the PDF), heading and body typefaces, dotted leaders to prices, marks, allergens, the key and
  the credit line. At the top, **Included with the event:** says what the event's price covers.
  **PDF** under each menu opens the server's PDF in Quick Look, to save or share.
- **The words** come from `EventMenuWording` in BenKit, a port of the server's
  `EventMenuPresentation`, so the app, the website and the PDF say the same thing.
- **The fonts** (`MenuFontLibrary`) are fetched, not bundled. `GET api/public/menu-designs` (new)
  returns the 25 designs and the 44 font files; only the files a design uses are fetched from
  `GET api/public/menu-fonts/{file}` (new), kept in Caches and registered for the process. A design
  added on the server reaches the app without an update. Until a face arrives, the menu is drawn in
  the system face of the same kind (serif, sans or typewriter).
- **The event's page** (`EventDetailView`) shows, for a hosted event with published menus, the
  included line and **See the menus**, for anyone, signed in or not.
- **The event hub** counts published menus ("2 menus") instead of meals.
- **On iPad** a menu stays a menu's width (640 points), rather than the width of the pane.

## Removed

The app's old menu call and its records (`loadMenus`, `HostedEventMenus`, `HostedEventMenu`,
`HostedEventMenuItem`), three tests and the `hosted-menus` fixture. **The API endpoint stays**: build
1.1.2 and earlier still call it.

The website guard `WhatTheAppDrawsAsTextTests` had an entry for a `Notes` field that only the old app
menu record had. With the record gone, the guard asked for its entry to go too.

## Found along the way

- **`.task` on an empty `Group` never runs.** The event page's menus link first used
  `Group { if !menus.isEmpty { … } }.task { load }`; with nothing on screen to appear, the load never
  started, so the link never showed. A zero-height `Color.clear` gives `.task` something to hang on.
- **A long item name wrapped** because the dotted leader took the width first. The name, marks and
  price now have layout priority; the leader takes what's left.
- **`CTFontManagerRegisterGraphicsFont` is deprecated in iOS 18.** Fonts are registered by file URL
  (`CTFontManagerRegisterFontsForURL`), and a file that doesn't read as a font is deleted so the next
  launch fetches it again.
- **`xcodebuild -destination "platform=iOS Simulator,name=iPhone 17 Pro"`** does not resolve here and
  fails quietly behind a `grep`; `run-sim.sh`'s `find … | head -1` then installs whichever old build it
  finds first. Build with `-destination id=<udid>` and a known `-derivedDataPath`.

## Tests

- BenKit: `EventMenuTests`, ten tests over fixtures captured from the real local API
  (`published-menus.json`, `menu-designs.json`): decoding, the words, the closest font, an unknown
  ornament, the anonymous calls, the addresses. BenKit 569/569.
- .NET: `Ben.Web.Tests` 7998 passed, 0 failed (the route guard counts the iOS callers of the two new
  routes).
- Run on the iPhone 17 Pro and iPad Pro 13-inch simulators against the local API, signed out: the
  event page's link, all five demo menus in their designs and fonts, and the PDF in Quick Look.

## Docs

Help (`the-mobile-apps.md`, `going-to-an-event.md`), the help screenshot `iphone-event-menus.png`, the
apps changelog (listed under 1.1.3, Ben's call on 10/13), the iPhone and iPad guides with new menus pictures, and the product documentation PDF.

## Not done

No archive or upload: that waits for Ben. The build number and `MARKETING_VERSION` are unchanged.
