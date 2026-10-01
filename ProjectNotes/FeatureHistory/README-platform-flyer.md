# Branch: feature/platform-flyer

Ben, 2026-10-01: *"Could you create a front and back advertisement flyer I could email to someone
to make them interested in the site. It needs to be flashy and enticing with images of the site
the logo and things like the editor the evp detector the investigations the group the billing page
the tours the events the apps how they integrate into the site. It should summarize all that it
does with the best of everything."* Then: *"Create it as a front and back pdf"* and *"On 8x11"*.

## What it is

`docs/ads/IsHaunted-Flyer.pdf` — two pages, 8.5 × 11 inches portrait (US Letter, so it prints with
no scaling and duplexes front-to-back), 4.1 MB, which attaches to an email without trouble.
`docs/ads/build-flyer.py` builds it; `docs/ads/flyer-everything.html` is what Chrome prints.

```bash
python3 docs/ads/build-flyer.py --png
```

## Why a seventh flyer

`docs/ads/build-ads.py` already makes six, one per **audience** — enthusiasts, groups, ghost walks,
venues, event hosts, and a general one. Each is photograph-led with a single screenshot, because
each is answering *"is this for me?"*.

This one answers a different question: *what does it actually do?* That is a capability showcase,
so it gets a layout of its own rather than contorting the six. The pipeline is the same — US
Letter, real screens, Chrome print-to-PDF, the same palette and furniture — and `build-ads.py` is
untouched, so the six existing PDFs cannot be disturbed by work on this one.

## What is on it

**Front.** Logo and a night hero; *"Everything the night needs, in one place."*; a browser showing
the map of haunted places overlapped by the iPhone's Field Kit; three pillars (explore free /
investigate properly / fill the house); three larger screens — a ghost walk, a case, the EVP
detector mid-scan; and the call to action.

**Back.** Twelve capability tiles, each with its own screenshot: the feed, the map, cases and
evidence, investigations, the EVP detector, audio tools, the video editor, the photo editor,
research boards, your group, ghost walks, hosted events. Then the part Ben asked for that a feature
list cannot say — **how the phone and the website are one thing** — as five steps with real screens
and arrows: record it in the field → it lands on the case → scan it for voices → cut the moment out
→ the client reads it. Then the plans band over the group's billing page, and the address.

## Nineteen real screens, no mockups

Every screenshot is a photograph of the running site, from the help-media harness, the admin help
captures, or the persona walks under `docs/web-media`. The build **refuses to print** if any
referenced file is not on disk, because a missing image prints as a silent gap rather than an error.

Two things were learned making it look right, and both are written into the script:

- **A whole 1400-pixel page drawn under two inches wide is a grey smudge.** Each tile carries a
  zoom and a crop anchor so it shows a corner of its screen at a size a reader can make out.
- **Cropping a full-page capture to the top-left shows the navigation sidebar and two notification
  banners, not the product.** Four tiles were doing exactly that; they are anchored centre-right
  now. The video editor's overview is mostly an empty black preview canvas, so that tile uses the
  timeline capture instead — which is what the caption promises anyway.

## Keeping it emailable

The help captures are retina, 2880 × 1800, twenty-odd of them, and Chrome embeds every one at full
resolution: the first PDF was 7.2 MB. `small()` resamples each into `docs/ads/.cache/` (gitignored,
rebuilt on demand) at the width it is actually drawn, as quality-82 JPEG — **4.1 MB**, with no
visible difference at the printed size.

## Prices

Taken verbatim from `build-ads.py` rather than restated, so the seven advertisements cannot drift
apart: free to join · groups from $19.99 a month · ghost walks $29 a month per tour · hosted events
$99 each · investigations of public places free on every plan.
