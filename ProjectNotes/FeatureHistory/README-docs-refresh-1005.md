# Documentation refresh: pictures retaken, help and guides brought current (10/05/2026)

Branch `feature/docs-refresh-1005`.

Ben: "run the app in order to grab new images you need. Then update documentation and the help files
to reflect the new site and iOS apps. Update any text or missing documentation in the style of the
current web app" — and "You can run the iOS apps as well to grab images needed."

## The plan

1. **Website help pictures** — every `TestCategory=Capture` shot retaken, dark, on the isolated
   stack (`BEN_E2E_DB=IsHauntedDb_signal scripts/run-e2e.sh --keep`), including the three that had
   failed on data (shopping, tour seats, hosted-event menus and kitchen) and a new one: the app's
   section near the foot of the home page (`getting-started/home-app.png`).
2. **App pictures** — the developer guides' frames on iPhone 17 Pro and iPad Pro 13-inch (M5)
   (`DeveloperDocCaptureTests`, `HelpMediaCaptureTests`), against the same stack, keychain reset,
   `-apiBaseURL` on every launch. The group-session frames (the notification, the feed card, the
   send screen, asking to join, the lead's page) were the last pictures still in the look before
   10/02; they are retaken through a real sandbox push and the role-play steps.
3. **Help text** — `the-mobile-apps.md` still ended with "submitted to the App Store and is awaiting
   Apple's review", under a second "Getting the app" heading, long after the app went live; removed.
   `getting-started.md` gains a short section on the app and the home page's section for it.
4. **Documents** — the product documentation, the seven seat guides and the two app guides rebuilt
   in the site's look (`docs/site_doc_style.py`) from the new pictures; the iPhone guide copied to
   the website.
5. Change log (website 2.15.0, the same day), merge to master and develop, push.

## What is left out, and why

- Nothing about the Profile "check for a new version" screen: it is on `ios-1.1.3`, not in any
  released app.
- `69b-share-to-event` is the share extension inside Photos and can only be driven by hand.

## What happened (10/05/2026)

- **Website:** 54 help captures retaken on `IsHauntedDb_signal`; the store, selling and store
  administration pictures and all seven seat guides on a fresh `IsHauntedDb_docs1005`, because the kept
  database had collected 52 test products that pushed the seeded catalog off the first page.
- **Apps:** every developer-guide frame on both devices; the hosted-event and door frames; the five
  group-session frames (a launch notification, the feed card, the send screen, asking to join, the
  lead's page). The help's room picture finally matches its caption ("with a photo posted from the
  phone"). The home page's three phone screens were refreshed from the new frames.
- **Found and fixed on the way:**
  - A venue's letter about an overnight request with no room yet read "has asked for Waiting to be
    placed"; it says "a place" now (`EventOrganizerMailer.Describe`, pinned by
    `EventOrganizerMailerWordsTests`).
  - British words in demo text that the pictures showed: Colour, Grey, "no lift", "a torch".
  - The Venues flyer's quote is no longer a made-up line credited to a real hotel.
  - The capture code: the photo editor's row lookup, the investor captures' check text and feed
    selector, the iPhone's lazily built "Open the session" button, and the notification prompt over
    the lead's page.
- **Rebuilt:** product documentation, seven seat guides, iPhone and iPad guides, app guide, all
  flyers, the Hosted Events brochure (its letters retaken in the site's mail style), and the site's
  copies of the flyers, thumbnails and guides.

Not retaken: two investor-overview pictures (that document is not in the repository) and
`69b-share-to-event`, which only a person can drive.
