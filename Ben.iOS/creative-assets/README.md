# App Store creative assets and custom product pages

Apple added these to App Store Connect on 10/05/2026:

- **The Asset Library** holds artwork, screenshots and previews. Each item is reviewed on its own,
  ahead of any app update.
- **The Header and Search Results images** appear on the product page and beside the app in search.
  They're optional and show on iOS 27 and later.
- **Custom product pages** are other versions of the App Store page, each with its own screenshots,
  promotional text and header, and its own link. Send each audience the link that fits them.

Every image here is RGB with no transparency, as Apple requires, and keeps its icon and words inside
Apple's safe areas (a centered 1646 × 661 on the header, 2168 × 1030 on the search image). The phones
fade out before their tab bars, so a renamed tab never makes a picture wrong.

| Page | Header (3840 × 1646) | Search Results (3840 × 2560) |
|---|---|---|
| Main product page | `main/header.png` | `main/search-results.png` |
| Investigators | `investigators/header.png` | `investigators/search-results.png` |
| Haunted nights out | `events/header.png` | `events/search-results.png` |

## The main product page

"Find out what happened here.": the home page's own photograph, kicker and headline. These replace the
10/13 pair, whose screens showed the old **Field Kit** tab. Use them with 1.1.4.

## Custom product pages

### Investigators

For investigators and teams, and for ads or posts about the Paranormal Platform.

- **Name** (only you see it): Investigators
- **Header and search image:** `investigators/`
- **Promotional text** (127 of 170):
  > Record magnetic field, sound, video and location on one timeline. Mark moments as they happen, then
  > play the whole night back.
- **Screenshots**, from `screenshots-1.1.4/iphone-6.5-dark/`, in this order: 11 live session, 13 review,
  12 marks, 10 base set, 14 trim, 04 Paranormal. iPad: the same frames from `ipad-13-dark/`.

### Haunted nights out

For people going to a ghost walk or a hosted event, and for the groups that send them the link.

- **Name:** Haunted nights out
- **Header and search image:** `events/`
- **Promotional text** (108 of 170):
  > Reserve a ghost walk, carry your pass even with no signal, and see the program and the menus for
  > your night.
- **Screenshots:** 05 Events first, then the rest of `screenshots-1.1.4/`. The store set has only one
  events frame. The pass, program and menus pictures above come from the app guide's captures, which
  are a different size and show the old tab name, so they can't be store screenshots. Recapture those
  screens in the 1.1.4 build to give this page its own set.

## Putting them in App Store Connect

1. **Asset Library** (on the app's page in App Store Connect): upload all six PNGs and submit them for
   review. They're reviewed on their own, apart from the app. You can also drop an image straight into a
   page's Header or Search Results tab, as you did on 10/13; it lands in the library either way.
2. **Main page:** on the 1.1.4 version, under **Product Page Information → Header and Search Results**,
   choose `main/header.png` and `main/search-results.png` from the library.
3. **Custom product pages:** **Custom Product Pages → +**, name the page, paste its promotional text,
   add its screenshots, then choose its header and search image from the library. Submit it.
4. Once a page is approved, App Store Connect gives it a link, with `?ppid=` and the page's ID on the end.
   Use that link wherever that audience finds you: an ad, a post, or the website's events pages.

## Rebuilding

```bash
python3 Ben.iOS/creative-assets/build.py
```

That rebuilds every page; add a page's folder name to build only that page. Add `--guides` to draw the
safe areas. Each page's photograph, words and screens are set in `PAGES` at the top of `build.py`.

The photographs are Unsplash photos already licensed for the site, credited in
`ProjectNotes/FeatureHistory/README-hosted-events-235-media.md`.
