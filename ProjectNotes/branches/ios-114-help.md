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

## Still to do before merging

1. **Recapture the page's phone pictures.** The current ones show "Field Kit" in the tab bar. They
   must be retaken on the documentation database, as on 10/05
   (`ProjectNotes/FeatureHistory/README-docs-refresh-1005.md`): iPhone 17 Pro and iPad Pro 13-inch,
   `HelpMediaCaptureTests` and the group-session frames. Not the App Store capture database: its
   frames from 10/15 have cluttered test sessions, an empty public-sessions list and an empty door.
2. Rebuild the product PDF and the app guides, and copy the iPhone guide to the website.
3. Merge to develop and master once 1.1.4 is released, then deploy the website.
