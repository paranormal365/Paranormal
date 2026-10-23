# help/ios-115-live (10/23/2026)

iOS 1.1.5 was released on 10/23/2026. This branch applies the help held back for it
(`Ben.iOS/APP-STORE-1.1.5-HELP.md`, now marked done). Branched from `origin/master` at 2dc16845.
Not merged, not pushed.

## Done here

- **Help text** (`Ben.Web.Services/Help/Content/the-mobile-apps.md`, *EVP question and answer*): the
  paragraph from the release sheet, word for word, between the first paragraph and the iPhone Duo
  one. The rest of the section was checked against the three-step button (Ask a question → Done
  asking — listening → Ask the next question):
  - The iPhone Duo paragraph said "press it while you ask"; the side-column microphone starts the
    asking, so it now says "press it when you start to ask", and it says the strip across the top
    names the next step on a button of its own and shows the readings (`CallAndResponseBanner`).
    **Done listening** is still the Duo strip's own word for stopping, so it stays.
  - The picture's caption said "one large control, and the wait timing itself"; it is now "EVP mode
    while listening: green, with how long the room has had to answer", which is what it shows.
- **Developer guide text** (`docs/build-ios-documentation.py`, the EVP section) said the screen had
  "two controls"; it now describes the one button, red asking, green listening and the readings.
- **Capture** (`DeveloperDocCaptureTests`): EVP mode is now photographed listening. It presses Ask a
  question, types "Is anyone here with us?", presses Done asking — listening, waits nine seconds,
  then takes `47-evp-mode`, and also an unused `47a-evp-asking` frame (red, with the keyboard up).
  It then presses Stop listening before Done, so the rest of the run is unchanged.

## Pictures retaken

A new database, `IsHauntedDb_ios115help1023`
(`BEN_E2E_DB=IsHauntedDb_ios115help1023 scripts/run-e2e.sh --keep --filter "TestCategory=StoreShotsArrange"`).
iPhone 17 Pro (iOS 26.5) and iPad Pro 13-inch (M5) (iOS 27.2), dark, keychain reset, the app uninstalled
and freshly installed with its permissions granted, `-apiBaseURL http://localhost:5252`, signed in as
the seeded client. The status bar was set to 9:41.

- `docs/ios-media/iphone/47-evp-mode.png` and `docs/ios-media/ipad/47-evp-mode.png`: listening, green,
  0:10 and 0:09 on the clock, with the question, and magnetic field and sound readings above it.
- `Ben.Web.Website/wwwroot/help/media/the-mobile-apps/iphone-evp.png`: the iPhone frame at 414×900,
  as on 10/17.
- `Ben.Web.Website/wwwroot/static/images/app/phone-47-evp-mode.jpg`: the home page's right-hand phone
  (588×1280 JPEG).
- `Ben.Web.Website/wwwroot/help/media/getting-started/home-app.png`: the home page's app section,
  which shows that phone. Retaken signed out at 1440×900, 2×, dark, from the `#the-app` section, the
  same way `HelpMediaCapture.Capture_GettingStarted` takes it. It was done with a small script that
  uses the Playwright driver, so the rest of that capture test was not rerun.

Not retaken: `iphone-controls.png` and `ipad-meter.png`. They show the live page with nothing being
asked, so they have no ask-and-listen strip. Only the EVP frames were copied from this run. The run's
other frames were not committed.

Every committed picture was looked at. None shows test leftovers, "Field Kit", the old icon, raw
field names, or a real person's photo or details.

## Rebuilt

- Product documentation PDF (`docs/build-documentation-pdf.py` + headless Chrome).
- iPhone and iPad developer guides (`docs/build-ios-documentation.py iphone|ipad`), with the iPhone
  one copied to `Ben.Web.Website/wwwroot/guides/`. Frames that were already missing are still
  missing (iPhone `58-launch-lead-page`; iPad `54`–`57`).
- Customer app guide (`docs/ads/build-app-guide.py --png`), which also writes the site's copy.
- The flyers and brochure were not rebuilt, because none of them uses the EVP picture.

## Change log

One line under `## Unreleased` in `webapp.md`, as for the 1.1.4 help.
