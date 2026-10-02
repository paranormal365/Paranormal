# Signal — making the whole site look like one thing

Ben chose **Signal** on 2026-10-01, after the three direction mock-ups in `docs/design/`:

> *"I like signal and I like the images. I think I would like the logo appearing somewhere and the
> IsHaunted.com may need to pop more… A consideration is you will need to also fix the styles for
> Telerik Blazor and the canvas and video editor. Also the administration side. I like the purple
> there. You will need to fix the css and site as well as the documentation and help files and the
> flyers you just created should fit into the look of the new site. The whole site should be
> cohesive."*

## Two findings that decide the approach

**1. The template is already variable-driven; its dark skin is not.** `smartapp.min.css` (563 KB,
vendored) reads **478 distinct `--bs-*` custom properties**. `themes/night.min.css` (55 KB) carries
**445 hard-coded hex values**. So the colour of the site does not live in the template — it lives
in the one swappable theme file that `App.razor` loads as `<link id="theme-style">`.

That means the honest move is **not** to override 445 baked values from on top. It is to write our
own skin at that swap point and leave the template alone, exactly as the night skin does now.
`smartapp.min.css` is never edited, so it stays updatable.

**2. Telerik already follows Bootstrap.** `theme/telerik-night.css` remaps the Kendo variables onto
`--bs-*` rather than onto literal colours, and it says so at the top: *"this file has no explicit
dark-mode block: the variables it reads are themselves theme-dependent."* So once the skin sets
`--bs-primary` and its kin to Signal, **the Grid, Scheduler, Editor, Map and pickers come with it**
for free, and phase 3 is corrections rather than a rewrite. That was the piece I expected to be
most expensive and it is close to the cheapest.

The architecture, then:

```
smartapp.min.css          vendored, never touched
  └── theme/signal.css    OURS. light + dark in one file, keyed off data-bs-theme.
        │                 Sets --bs-* and our own --ben-* tokens.
        ├── telerik-signal.css      already reads --bs-*; corrections only
        ├── kit/ben-kit.css, ben-store.css, 57 scoped .razor.css   → use tokens, drop literals
        └── the two editors' 77 stylesheets                        → chrome tokenised
```

## The logo

The mark is a white ghost in a **green** ring with amber windows (`#72A73F`, `#27602E`, `#FED46E`).
Signal's accents are violet and cyan. Dropped in at full colour it is the only green thing on the
page and reads as a sticker — `docs/design/preview/11-signal-brand-b-dark.png` shows this plainly,
which is why it is worth looking at rather than arguing about.

Three options are built (`docs/design/build-brand-options.py`): **A** wordmark forward with the mark
in white, **B** A plus the full-colour mark over the hero, **C** the mark in a gradient app-icon tile.

**Recommended: C, and no logo moment in the hero.** It makes `IsHaunted.com` pop — heavy wordmark,
`.com` in the accent gradient — and ties the mark to the accent **without altering the brand file**:
the mark is turned white with a CSS filter, one line and reversible. The full-colour mark stays
exactly as it is for the favicon, the app icon, the iOS listing and the flyers.

If the drawn logo should be featured, the right place is a page about the site, not the hero of a
photograph-led design. Re-tinting the ring to violet would be the most cohesive answer of all, and
it is a **brand change with App Store consequences** (icon, favicons, printed flyers), so it is
Ben's to make deliberately rather than mine to slip in.

## Phases

Each gets its own branch and `ProjectNotes/FeatureHistory/README-signal-phase-N.md`, as every arc
here does. Sizes: S ≈ half a day, M ≈ 1–2 days, L ≈ 3+.

### Phase 1 — The skin, and one page proved end to end (L)

`wwwroot/theme/signal.css`: both modes in one file, keyed off `data-bs-theme` the way the template
and Bootstrap already are. Tokens for colour, type scale, radius, shadow and spacing. The shell:
bar, sidebar, footer, buttons, fields, cards, badges, tables, alerts.

Also **a styleguide page** (`/styleguide`, gated), showing every primitive and every Telerik widget
side by side in both modes. Nothing else in this plan can be checked honestly without one — the
current site has no page where a change can be seen against everything it affects at once.

Done when the home page and one administration page are right in **both** modes, by eye and in a
screenshot, and the shell has no literal colour left in it.

### Phase 2 — The public site (L)
Home, find, place, event, tour, store, feed, publications, help. Per page, both modes.

### Phase 3 — Telerik (M)
`telerik-signal.css` from the existing bridge. Grid header and row treatment, popup surfaces, focus
rings, the Editor, the Scheduler, the pickers. Checked on the styleguide, then on the real screens
that use each one.

### Phase 4 — Administration (M)
Ben likes the purple there, and Signal's accent **is** violet, so admin and the public site
converge rather than diverge. Dashboards, grids, the settings pages, the logs.

### Phase 5 — The canvas and video editors — DONE 2026-10-01

**Correction to the first version of this plan**, which said editor changes "must be written to
survive a re-vendor". They need not: both `Ben.Video.VENDORED.md` and `Ben.Canvas.VENDORED.md` say
the copies diverge on purpose and work happens here, with no upstream merge. Editing them directly
is fine.

Done as a consolidation rather than a restyle. The skin split into `signal-tokens.css` (values
only) and `signal.css` (the website's use of them); both editor hosts load byte-for-byte copies of
the tokens and of `telerik-signal.css`, replacing their hand-copied Night palettes and the canvas
host's older Telerik bridge. `SignalCopiesStayInStepTests` fails the build if a copy drifts.

### Phase 6 — Everything written and printed (M, mostly machine time)
- **210 help screenshots and recordings** recaptured through the existing harness
  (`BEN_CAPTURE=1`, `TestCategory=Capture`). This is a long run and it writes into the tree, so it
  comes **after** the UI is settled or it gets done twice.
- The persona and product PDFs rebuilt from those screenshots (`docs/build-*.py`).
- **The seven flyers restyled to Signal** and republished (`docs/ads/build-ads.py`,
  `build-flyer.py`, `publish-to-site.py`). Note these are already in use — new PDFs mean the
  emailed and printed ones drift, and the App Store Marketing URL guide too.

### Phase 7 — The sweep (M)
Contrast audit in both modes (`VisualAuditWalk`, `SiteWideAuditTests`), the full Playwright suite,
and a light/dark parity pass. Expect the visual tests that assert colours to need updating — that
is the change landing, not a regression, and each one gets looked at rather than re-baselined
wholesale.

## What this will cost that is easy to miss

- **The screenshots dominate the tail.** 210 files, a long capture run, then three PDF builds.
- **Dark was the designed mode; light is the one that finds bugs.** Both mock-up faults so far were
  light-only — a dark amber kicker invisible on a night photograph, and a card gap that was a
  `.foot` class collision hidden by an invisible dark border. Every phase is checked in both.
- **The flyers are already out in the world.** Restyling them is right for cohesion and it does
  mean the ones already emailed no longer match.
- **`wwwroot/flyers` is 22 MB** and ships on every deploy. Worth shrinking the six audience ads
  while they are being rebuilt anyway.
