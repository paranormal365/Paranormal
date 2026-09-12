# Video editor — Phase 1: the layout

Branch: `feature/video-editor-phase1-layout` (on top of Phase 0)

## Why

The editor opened with the picture as a 38-pixel strip and 700 pixels of empty timeline beneath it,
and the Media & Properties panel floated over the timeline's own Ripple, Callout and Marker
buttons. Both were the first thing anybody saw. See `ProjectNotes/VideoEditor-Audit-2026-09-05.md`,
findings F3 and F4.

## What this branch does

1. **The timeline has a height; the picture takes the rest.** The timeline's root asked for
   `height: 100%`, which as a bare flex child of the editor made its flex-basis the *whole editor*
   — the preview then shrank in proportion to that basis and kept about a third of what it was
   given, at any window size. It now lives in a row that carries the height, and the preview row is
   simply `flex: 1`.
2. **The timeline sizes itself to its tracks**, and stops as soon as you drag the seam yourself.
3. **The seam grows the timeline.** `ResizableDivider` gained a `Target` parameter so a drag can
   resize the element *after* it.
4. **The panel is docked, not floating.** A column beside the picture: nothing overlaps, Properties
   gets the full height of the editor and scrolls, the width is draggable, and the panel collapses
   from its own header and reopens from the toolbar.
5. **The layout is remembered** — width, height, collapsed state and tab — in localStorage, applied
   before the first paint so the editor does not snap into shape after loading.
6. **The preview stage is the composition's shape.** It was whatever box flexbox left over, with
   the video letterboxed inside it; the callout, title and control-point overlays are positioned
   against that box, so they drifted from the frame whenever the two disagreed.
7. **One height rule for the site's editor pages.** My Videos used `100vh` and overshot the app bar;
   the case page had already worked that out and hard-coded the fix with a comment. Both now use
   `.ben-editor-page`.
8. **`/video-editor` redirects to My Videos.** A third host for the editor with no header, no
   project list, no link from anywhere, and no height container to resolve against.
9. **A window under 900px says so** instead of hiding the media panel and leaving no way to import.
10. **A missing seeded password skips the tests that need it** instead of failing them.

## Verifying

```
dotnet build Ben.slnx
dotnet test Ben.Video.Tests
dotnet test Ben.Web.Tests
```

then, with the WebAssembly host running:

```
dotnet run --project Ben.Wasm.Video --urls http://localhost:5180
```

```
dotnet test Ben.Web.Playwright -p:IsTestProject=true --filter "FullyQualifiedName~WasmEditorTests"
```

## What was verified on screen

At 1440×900, 1280×800 and 375×812:

| | Before | After |
|---|---|---|
| Preview stage | 1280×38 | 754×424, exactly 16:9 |
| Timeline row | ~700px, mostly empty | 261px, sized to its tracks |
| Panel | floating, over the timeline controls | docked at 341px, nothing overlapping |
| Ripple / Callout / Marker | covered | visible |
| Properties form | clipped at 420px | scrolls inside the panel |
| Seam drag | resized the preview, which then re-shrank | grows the timeline, and is remembered across a reload |
| A phone | media panel hidden, no way to import, no explanation | "This editor needs a wider window" |

Five new Playwright tests cover the height split, the aspect ratio, the panel not overlapping, the
remembered layout, and the narrow-window message. They pass; the five that sign in skip without a
password.
