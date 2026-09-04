# App Store screenshots — 1.0.2

The upload set for **IsHaunted 1.0.2 (build 2)**. Kept beside the 1.0.0 set in `../screenshots/`
with the version in the folder name so the two can never be confused in an upload dialog.

## What is in here today

**Byte-identical copies of the 1.0.0 set** (verified with `diff -rq` on 2026-09-04), placed so
the folder exists and the layout is settled. Every file is dark mode, at the sizes App Store
Connect requires:

| Folder | Device | Size |
|---|---|---|
| `iphone-6.5-dark` | iPhone 17 Pro Max, dark mode | 1242×2688 |
| `ipad-13-dark` | iPad Pro 13-inch, dark mode | 2064×2752 — the required 13" slot |
| `app-preview` | Field Kit demo videos, dark | iPhone 886×1920, iPad 1200×1600, 28 s, H.264 |

## Why these are not final

They show the 1.0.0 screens. Since that build the Field Kit gained the in/out **trimmer** with its
preview on the Send screen (item 210), the review screen gained the **archive verdict**, Settings
gained **My evidence**, and the tab bar became **adaptive** — a solo investigator no longer sees
My Cases or Investigations. `04-field-kit` in particular no longer shows the current screen.

**Item 214 recaptures this set** with demo records that exercise the whole Field Kit: dark mode, a
base level actually set, and a gauge that visibly moves. Replace the files in place when it runs;
keep the names, since App Store Connect slots are filled by hand from these.

## Refreshing

Same procedure as `../screenshots/README.md`, pointed at this folder:

    xcrun simctl keychain <udid> reset
    xcrun simctl ui <udid> appearance dark
    TEST_RUNNER_BEN_SCREENSHOTS=1 xcodebuild -project IsHaunted.xcodeproj -scheme IsHaunted \
      -destination 'platform=iOS Simulator,id=<udid>' \
      -only-testing:IsHauntedUITests/AppStoreScreenshotTests \
      -resultBundlePath /tmp/shots-1.0.2.xcresult test
    xcrun xcresulttool export attachments --path /tmp/shots-1.0.2.xcresult --output-path <dir>

`TEST_RUNNER_*` variables must be **exported in the shell**; passed as `xcodebuild KEY=value`
arguments they do not reach the runner. No profile screenshot from a Debug build — it carries the
DEBUG-only Developer section, and debug UI in a screenshot is a rejection.
