# App Store update: IsHaunted 1.1.4 (build 14)

**Submitted 10/15/2026, build 14** — archived with Xcode 27.1 RC (27A9275, iOS 27.1 SDK) for iPhone Duo.
Release is set to manual. What was entered in App Store Connect is in §4.

1.1.4 brings three things:
- the session review now plays back as the website's player does;
- Field Kit is renamed the Paranormal Platform;
- the build is clean of compiler warnings.

| | |
|---|---|
| Version | **1.1.4** (`MARKETING_VERSION`) |
| Build | **13** (`CURRENT_PROJECT_VERSION`). The highest ever archived or uploaded was 12 (1.1.3). |
| Replaces | 1.1.3 (12), in review since 10/14/2026 |
| Server work first | **No.** Nothing here asks the API for anything new. |
| Screenshots | **Recaptured** in `screenshots-1.1.4/`: every store screenshot shows the tab bar, and its middle tab now reads **Paranormal**. |
| Subtitle | **The Paranormal Platform** (23 of 30), replacing "Paranormal field investigation". |

## 1. What changed

- **Session review, the same as the website's player** (10/14):
  - tapping a photo goes to the moment it was taken;
  - the mark being passed lights up;
  - the map follows the marker, with **Recenter**;
  - the arrow turns smoothly and stays right when the map is turned;
  - Play where nothing was recorded starts at the next recording;
  - a base level set partway through counts from there;
  - with no compass or course, the arrow follows the direction walked.

  See `ProjectNotes/branches/session-player-engine.md` and the shared rules in
  `ProjectNotes/specs/replay-semantics.md`.
- **The Paranormal Platform** (Ben, 10/14): Field Kit's name everywhere people read it. The tab and the
  iPad sidebar read **Paranormal**, and the screen's title reads **Paranormal Platform**. Code names
  and deep links (`ishaunted://field-kit/…`) are unchanged.
- **No compiler warnings.** The 1.1.3 archive had eight, and `scripts/warnings.sh` now reports none.

## 2. Ship it

Apple reviews one version at a time, so 1.1.4 can be uploaded now but goes **Add for Review** only
once 1.1.3 is approved or rejected.

1. **Before archiving:** run `scripts/build.sh` and then `scripts/warnings.sh`. Fix anything it lists
   (APP-STORE.md §5).
2. **Store screenshots: done** (10/14), in `screenshots-1.1.4/` — nine frames per device and a preview
   each, with **Paranormal** on the tab bar. Look through them once; `screenshots-1.1.4/README.md`
   says what was left out and why.
3. Product → **Archive** on the IsHaunted scheme. In Organizer, choose the **IsHaunted 1.1.4 (13)**
   archive, then **Distribute App** → App Store Connect → **Upload**. Leave "Manage version and build
   number" **off**.
4. App Store Connect → IsHaunted → **+ Version** → **1.1.4**. Set the subtitle to **The Paranormal
   Platform**, then replace the screenshots with the new ones.
5. **What's New in This Version**:

   > Field Kit is now the Paranormal Platform, on the Paranormal tab.
   >
   > Reviewing a session: tap a photo to go to the moment it was taken, and the mark you are passing
   > lights up. The map follows the marker, the arrow turns smoothly, and Recenter brings it back after
   > you move the map. Pressing Play where nothing was recorded starts at the next recording.

6. Select build **13**, then **Add for Review**.

## 3. When it's live

Follow `APP-STORE-1.1.4-HELP.md`:
1. Put back the held-back session-review help.
2. Apply the prepared Paranormal Platform rename of `the-mobile-apps.md`.
3. Recapture that page's phone screenshots.
4. Rebuild the product PDF and the app guides.

If Apple rejects it for code, the next build is **14**. An uploaded build number is never reused.

## 4. As submitted (10/15/2026)

- **Build:** 14 (13 was archived with Xcode 27.0 and never submitted).
- **Subtitle:** The Paranormal Platform (was "Paranormal Toolkit").
- **Promotional text:** Now made for iPhone Duo: open it and the whole investigation fills the inner screen, with every control for the night down the side.
- **What's New:** iPhone Duo; ask a question and listen; add details to marks; Field Kit is now the Paranormal Platform; the session review changes; menus and alerts easier to read.
- **Description:** opens with the Paranormal Platform; new "Made for iPhone Duo" and "Events" sections; ask-and-listen and add-details in "In the field".
- **Review notes:** Field Kit → Paranormal Platform (formerly Field Kit); a paragraph on the iPhone Duo controls and Add details.
- **Media:** `screenshots-1.1.4/main/` — iPhone 6.5" (8 + preview), iPad 13" (8 + preview), iPhone Duo (5).
