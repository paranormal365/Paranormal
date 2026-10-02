# App Store media — IsHaunted 1.1.2

The first set in the **Signal** look — the website's colors, buttons and cards in the app, in dark.
Nine frames per device and one preview per device, captured through a real scripted night on the
simulator with the same command as before:

```
Ben.iOS/scripts/capture-app-store-media.sh iphone 1.1.2
Ben.iOS/scripts/capture-app-store-media.sh ipad   1.1.2
```

## What is new since 1.0.3

- **Everything is re-captured**, because every screen looks different. 01 Feed, 02 My Cases and
  05 Events are taken fresh by `AppStoreScreenshotTests` (they were carried over from 1.0.0
  before); the Field Kit frames 04 and 10–14 by `FieldKitScreenshotTests`, as before.
- **The feed is arranged first.** A test database's feed is whatever the automated runs left
  ("o60d30c714c7 my own post"). `Ben.Web.Playwright/Capture/StoreShotsArrange.cs` hides the test
  accounts' posts (from the admin page made for it, so they can be put back) and writes four posts
  in their place. Run it before capturing:
  `scripts/run-e2e.sh --keep --filter "TestCategory=StoreShotsArrange"`.
- **Left out on purpose:** Tours (the test database runs the same walk under two test groups, so it
  lists it twice) and, on iPad, Investigations (the demo client has none, so it is an empty state).
  Add them back when a capture runs against data that shows them well.
- **Field Kit 04 shows two "Printers Alley Ghost Walk" sessions under Happening now** — the same
  test data. They disappear six hours after the walk ends; retake 04 then if it matters.

## Sizes, the preview, and what the capture taught

Unchanged from `screenshots-1.0.3/README.md`: iPhone 17 Pro Max captured at 1320 × 2868 and
uploaded at 1242 × 2688; iPad Pro 13-inch as captured; previews cut with `tools/preview.swift` and
given a silent audio track.

## Before uploading

Look at every frame. No Developer section, no wrapped clock, the trimmer frame with an in point
somewhere other than the very start, and the feed opening on the four arranged posts.
