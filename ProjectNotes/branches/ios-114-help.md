# feature/ios-114-help (10/15/2026)

The help held back until iOS 1.1.4 is in the App Store (`Ben.iOS/APP-STORE-1.1.4-HELP.md`), prepared
early so release day is a merge, not a rewrite. **Do not merge until 1.1.4 is released.**

## Done here

`Ben.Web.Services/Help/Content/the-mobile-apps.md`:
- Field Kit → the Paranormal Platform, with the **Paranormal** tab (the prepared patch, applied 3-way).
- Reviewing what you recorded: the mark being passed lights up; Play where nothing was recorded starts
  at the next recording; tapping a photo goes to its moment; the map follows the marker, with Recenter.
- **Adding details to a mark** (text only, until the session is sent).
- **On iPhone Duo**: the ☰ menu and the investigation column.
- EVP on iPhone Duo: the side-column microphone, typing the question, Done listening.

## Finished 10/17/2026 (`feature/ios-114-help-finish`) — ready to merge

iOS 1.1.4 was approved and released on 10/16/2026. This branch is `feature/ios-114-help` with develop
merged in (no conflicts) and the list below done. **Ready to merge** to develop, then master, then
deploy the website.

- **Pictures retaken** on a new database, `IsHauntedDb_ios114help1017` (`scripts/run-e2e.sh --keep
  --filter "TestCategory=StoreShotsArrange"`), never the App Store capture database, iPhone 17 Pro
  and iPad Pro 13-inch (M5), dark, keychain reset and a fresh install before each run:
  - All 28 pictures on *The iPhone and iPad Apps* (`help/media/the-mobile-apps/`). Every tab bar and
    sidebar says **Paranormal**; the notification reads "Tap to join it in the Paranormal Platform."
    with the new icon. `HelpMediaCaptureTests` on both devices; the meter, controls and EVP pictures
    are the developer-guide frames at 414×900, the iPad meter and lead's page at 675×900, as before.
  - The group-session frames, played on three simulators with `RolePlayUITests` against a world built
    over the API: Sarah Mitchell's walking-tour group "Printers Alley Walks", a public walk starting
    in twenty minutes with Daniel's seat approved (the feed card, the send screen), and a private
    "Cellar night at the mill" (Wren asking by the lead's code; Wren and Hazel asking on Sarah's
    iPad). The notification is `xcrun simctl push` with exactly what `FieldLaunchService` sends, with
    the test runner uninstalled so its icon isn't on the home screen.
  - By hand on the simulator, because no test drives them: the room with two posts waiting with no
    signal (a cold start pointed at a dead address), the Photos share sheet, and the iPhone's case
    messages for the developer guide.
  - Both developer guides' frames (`DeveloperDocCaptureTests` and the help captures, renamed into
    `docs/ios-media/<device>/`), the home page's three phone screens, and
    `getting-started/home-app.png`, the home page's app section, which showed them.
  - Daniel's pass was withdrawn after the door pictures, so the pictured code is a dead one.
- **Rebuilt:** the product documentation PDF, the iPhone and iPad developer guides (the iPhone one
  copied to `wwwroot/guides`) and the customer app guide (`docs/ads/build-app-guide.py`, which writes
  the site's copy too). The flyers, the six audience sheets and the Hosted Events brochure also
  carry the app's pictures, so they were rebuilt (`build-ads.py`, `build-flyer.py`) and put on the
  site (`publish-to-site.py`).
- **Capture fix:** the door's reservation sheet now lists the kitchen's notes and the party before
  **Check in**, so `testCaptureTheDoor` scrolls the sheet until the button is there.
- American spelling checked on the page; a line under `## Unreleased` in the web app's change log.

Not retaken: the iPad's Photos share-sheet frame (`ipad/69b-share-to-event.png`), which shows no
tab bar and no test data.

## Still to do before merging (done 10/17, above)

1. **Recapture the page's phone pictures.** The current ones show "Field Kit" in the tab bar. They
   must be retaken on the documentation database, as on 10/05
   (`ProjectNotes/FeatureHistory/README-docs-refresh-1005.md`): iPhone 17 Pro and iPad Pro 13-inch,
   `HelpMediaCaptureTests` and the group-session frames. Not the App Store capture database: its
   frames from 10/15 have cluttered test sessions, an empty public-sessions list and an empty door.
   Include `iphone-launch-notified.png`: its notification still says "Tap to join it in Field Kit"
   (the server now says "the Paranormal Platform"); its icons were updated to the new one on 10/15.
2. Rebuild the product PDF and the app guides, and copy the iPhone guide to the website.
3. Merge to develop and master once 1.1.4 is released, then deploy the website.
