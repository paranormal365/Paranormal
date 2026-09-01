# Generated documents

## IsHaunted-Product-Documentation.pdf

Every in-app help document, reproduced **verbatim**, as one printable PDF.

To regenerate after changing anything in `Ben.Web.Services/Help/Content/`, run both steps from the
repository root:

```bash
python3 docs/build-documentation-pdf.py && "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless --no-pdf-header-footer --print-to-pdf=docs/IsHaunted-Product-Documentation.pdf "file://$PWD/docs/ishaunted-documentation.html"
```

### Screenshots

The documents embed screenshots, and the build resolves them to paths relative to `docs/`, so
Chrome prints the real pixels. Two sources, by audience:

| Document audience | Files live in | Referenced as |
|---|---|---|
| Everyone, signed-in, group members | `Ben.Web.Website/wwwroot/help/media/` | `/help/media/slug/x.png` |
| Group and site administrators | `Ben.Web.Services/Help/Media/` | `help-media:slug/x.png` |

Administrator screenshots are embedded in the assembly rather than served, and the in-app help
inlines them as data URIs — the same reason the help text is embedded: a file under wwwroot is
served to anyone who guesses its name.

To re-capture them after a UI change, with the stack running:

```bash
BEN_CAPTURE=1 dotnet test Ben.Web.Playwright -p:IsTestProject=true --filter TestCategory=Capture
```

The build refuses if a document references a screenshot that is not on disk, and
`HelpMediaReferenceTests` fails if a reference is missing, orphaned, or on the wrong side of the
audience split.

**The script writes `docs/ishaunted-documentation.html`, beside itself** — not to the working
directory. Rendering some other copy of that file is how the PDF silently went a section out of
date once: the build succeeded, Chrome succeeded, and the output was the previous version.

Sanity check afterwards: the PDF's byte size should change whenever the help did.

### Verifying it

`Read` cannot rasterize a PDF here (no poppler), and the Browser pane will not screenshot a
`file://` page. Screenshot the **HTML** instead:

```bash
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless --screenshot=check.png --window-size=794,1123 "file://$PWD/docs/ishaunted-documentation.html"
```

Do **not** try to grep the PDF for text. Chrome subsets its fonts and writes glyph indices, so even
text that is plainly present will not match — a search there returns false negatives, not answers.

## The developer handover documents

Eight documents for handing the project to another developer — two for the apps, six for the
website, **one per user type**, each named for its audience:

| Document | Who it is for |
|---|---|
| `IsHaunted-iOS-iPhone.pdf` / `IsHaunted-iOS-iPad.pdf` | Somebody picking up the native app |
| `IsHaunted-Web-Visitor.pdf` | The site with no account |
| `IsHaunted-Web-Client.pdf` | Someone who asked a group for help |
| `IsHaunted-Web-Member.pdf` | An ordinary member of a group |
| `IsHaunted-Web-Viewer.pdf` | A member who may look and change nothing |
| `IsHaunted-Web-Owner.pdf` | A group's owner or administrator |
| `IsHaunted-Web-Superadmin.pdf` | Runs the platform |

**One document per seat rather than one with six chapters.** The permission model is real: the site
is a different application from each of those seats, and a reader only needs their own. More to the
point, an administrator passes every permission check by role, so a surface broken for everybody
else looks perfect from that seat — which is why each document is CAPTURED while signed in as that
person rather than described from the code.

### Rebuilding them

Website — one run per seat, with all three hosts up:

```bash
for p in visitor client member viewer owner superadmin; do
  BEN_PERSONA=$p BEN_PERSONA_OUT="$PWD/docs/web-media" BEN_BASE_URL=http://localhost:5078 \
    dotnet vstest Ben.Web.Playwright/bin/Debug/net10.0/Ben.Web.Playwright.dll \
    --TestCaseFilter:"FullyQualifiedName~PersonaDocCaptureTests"
done
python3 docs/build-persona-documentation.py
```

Apps — once per device, from `Ben.iOS/`. **The flag must be a shell environment variable, not a
build setting**, or the test silently skips and reports a pass in under a second:

```bash
TEST_RUNNER_BEN_DOC_SHOTS=1 xcodebuild -project IsHaunted.xcodeproj -scheme IsHaunted \
  -destination 'platform=iOS Simulator,id=<udid>' \
  -only-testing:IsHauntedUITests/DeveloperDocCaptureTests \
  -resultBundlePath /tmp/doc.xcresult test
xcrun xcresulttool export attachments --path /tmp/doc.xcresult --output-path docs/ios-media/<device>
python3 docs/build-ios-documentation.py iphone
```

Set the simulator to dark first: `xcrun simctl ui <udid> appearance dark`. The website captures
force dark by emulating `prefers-color-scheme`, which is the path `ben-boot.js` already falls back
to — the site choosing dark for itself rather than a test writing a stored preference.

### Everything in them is simulated

Seeded accounts, seeded cases, and generated media. `scripts/generate-media-posters.py` gives every
stored file a poster so no post renders as a grey box: video gets an atmospheric frame with a
semi-transparent play button, and audio gets **its own real waveform** — decoded with `afconvert`
and drawn in the site's WaveSurfer bar style. Headless WaveSurfer was tried first and does not
work: `decodeAudioData` never resolves there, so its loader hangs until the screenshot is taken.

Every frame is captioned `SIMULATED`, and each document says so on its first page, so nothing can
be mistaken for a real investigation.

**The builders name any section whose screenshot is missing** rather than shipping a silent gap.
`53-session-review` is currently unfilled on both devices: the capture cannot reliably reach the
review screen after a live session, and the text describes it without a picture.

## What it deliberately leaves out

No business, market or financial information, and no usage figures — only what the software does.
Anything of that sort belongs in a separate document written from real numbers.
