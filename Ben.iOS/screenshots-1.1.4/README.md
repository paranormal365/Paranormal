# App Store media — IsHaunted 1.1.4

Recaptured because every frame shows the tab bar, and its middle tab now reads **Paranormal**
(Field Kit is the Paranormal Platform, Ben 10/14/2026). Same look and same frames as 1.1.2, taken
with the same commands:

```
scripts/run-e2e.sh --keep --filter "TestCategory=StoreShotsArrange"
Ben.iOS/scripts/capture-app-store-media.sh iphone 1.1.4
Ben.iOS/scripts/capture-app-store-media.sh ipad   1.1.4
```

## What changed in the capture

- **The arrange step is not optional.** Captured without it, the first frame was the test runs'
  leftovers ("First visit for me 91216954d947423aa", a gray picture).
- **It now clears the whole feed, not its first page.** The iPad's feed is tall enough to scroll past
  the four store posts, and it showed two of the SuperAdmin's posts from weeks earlier ("posted from
  elsewhere #ta…") that the first page never reached. The iPad's feed now ends on the group session
  card, which reads "the Paranormal Platform in the IsHaunted app".
- **The preview needs ffmpeg**, which is gitignored; in a worktree the script now uses the main
  checkout's.

## Left out on purpose (as in 1.1.2)

- **Tours**: the test database lists the same walk under two test groups ("Printers Alley After Dark"
  and "… 0928-155010").
- **iPad Investigations**: the demo client has none, so it is an empty state.

## Before uploading

Look at every frame: no Developer section, no wrapped clock, the trimmer with its in point after the
start, the feed opening on the four arranged posts, and **Paranormal** on the tab bar and in the iPad
sidebar. Sizes and the preview's silent audio track are unchanged from `screenshots-1.0.3/README.md`.
