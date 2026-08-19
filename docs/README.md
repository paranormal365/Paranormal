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

## What it deliberately leaves out

No business, market or financial information, and no usage figures — only what the software does.
Anything of that sort belongs in a separate document written from real numbers.
