# App Store creative assets and custom product pages

Apple added these to App Store Connect on 10/05/2026:

- **The Asset Library** holds artwork, screenshots and previews. Each item is reviewed on its own,
  ahead of any app update.
- **The Header and Search Results images** appear on the product page and beside the app in search.
  They're optional and show on iOS 27 and later.
- **Custom product pages** (up to 70) are other versions of the App Store page, each with its own
  screenshots, promotional text and header, and its own link. Send each audience the link that fits them.

Every image here is 3840 × 1646 (header) or 3840 × 2560 (search), RGB with no transparency as Apple
requires, with its icon and words inside Apple's safe areas (a centered 1646 × 661 on the header,
2168 × 1030 on the search image). The phones fade out before their tab bars, so a renamed tab never
makes a picture wrong.

## What the pictures may show

Everything on an App Store page has to match what the iPhone app does (App Review Guideline 2.3).
The phones show only real app screens. The researchers, audio and video pages also show a laptop with
the website's own tool, labeled **On IsHaunted.com**, because that is where that work is finished.
That appears only in the header and search images, never in the screenshots. If App Review refuses one
of those images, only that image is held back, not the app.

## The pages

| Folder | Page | For | Promotional text (170 at most) |
|---|---|---|---|
| `main/` | Main product page | everyone | (the version's own) |
| `explorers/` | Explorers | anyone starting out on their own | Record a night at a haunted place, share what you found, and follow the people and groups near you. |
| `investigators/` | Investigators | investigators | Record magnetic field, sound, video and location on one timeline. Mark moments as they happen, then play the whole night back. |
| `groups/` | Investigation groups | groups and their teams | Your group's cases and visits, the people to contact, and the sessions your team records, all in your pocket. |
| `researchers/` | Researchers | research on a case's boards | Notes, photos, marks and readings stamped with the time and place, ready for your research boards on IsHaunted.com. |
| `audio/` | Audio and EVP | audio specialists | Ask a question, then wait. EVP sessions record every question and mark where you asked, then you finish in the audio editor on IsHaunted.com. |
| `video/` | Video | video editing | Record video with every reading, trim it on the phone before it goes, and finish it in the video editor on IsHaunted.com. |
| `public-places/` | Haunted places | public sites and their visitors | Find the sessions people published at haunted places near you, then add your own night to the place for everyone to see. |
| `events/` | Haunted nights out | guests at walks and hosted events | Reserve a ghost walk, carry your pass even with no signal, and see the program and the menus for your night. |
| `event-hosts/` | Event hosts | groups and venues hosting an event | Scan passes and check in parties at the door, even with no signal, while guests carry their pass, program and menus. |
| `ghost-walks/` | Ghost walk tours | tour groups and their guides | Guests reserve your walk, join the session your guide starts, and send what they recorded to your group. |

The page name is only for you; no one else sees it.

## Screenshots for each page

Screenshots must be real app screens at a store size, so they come from `screenshots-1.1.4/` (01 Feed,
02 My Cases, 04 Paranormal, 05 Events, 10 base set, 11 live session, 12 marks, 13 review, 14 trim).
Put the frames that matter to the page first:

| Page | Order |
|---|---|
| Explorers | 01, 11, 13, 04, 05 |
| Investigators | 11, 13, 12, 10, 14, 04 |
| Investigation groups | 02, 13, 11, 01, 05 |
| Researchers | 12, 13, 11, 14, 02 |
| Audio and EVP | 11, 13, 12, 10 |
| Video | 14, 12, 13, 11 |
| Haunted places | 04, 13, 11, 01 |
| Haunted nights out | 05, 01, 04 |
| Event hosts | 05, 01, 02 |
| Ghost walk tours | 05, 04, 11, 13 |

Several pages want screens the store set doesn't have yet: the door, the pass, the program, the menus,
EVP mode, a case's detail, and joining a walk's session. The app guide has them, but at another size and
showing the old tab name. Recapturing them in 1.1.4 at the store size gives those pages their own sets.

## Putting them in App Store Connect

1. **Asset Library** (on the app's page in App Store Connect): upload the images and submit them for
   review. You can also drop an image straight into a page's Header or Search Results tab; it lands in
   the library either way.
2. **Main page:** on the 1.1.4 version, under **Product Page Information → Header and Search Results**,
   choose `main/header.png` and `main/search-results.png`. They replace the 10/13 pair, whose screens
   showed the old **Field Kit** tab.
3. **Each custom product page:** **Custom Product Pages → +**. Name it, paste its promotional text, add
   its screenshots in the order above, choose its header and search image, and submit it.
4. Once a page is approved, App Store Connect gives it its own link (it ends with `?ppid=` and the page's
   ID). Use that link wherever that audience finds you: an ad, a post, a group's own page, or the
   website's events pages.

## Rebuilding

```bash
python3 Ben.iOS/creative-assets/build.py
```

That rebuilds every page; name a page's folder to build only that one, and add `--guides` to draw the
safe areas. Each page's photograph, words, phone screens and website capture are set in `PAGES` at the
top of `build.py`.

The photographs are Unsplash photos already licensed for the site, credited in
`ProjectNotes/FeatureHistory/README-hosted-events-235-media.md`. The website captures are the help
pages' own, from `Ben.Web.Website/wwwroot/help/media/`.
