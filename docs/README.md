# Generated documents

## IsHaunted-Product-Documentation.pdf

Every in-app help document, reproduced **verbatim**, as one printable PDF.

To regenerate after changing anything in `Ben.Web.Services/Help/Content/`, run both steps from the
repository root:

```bash
python3 docs/build-documentation-pdf.py && "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless --no-pdf-header-footer --print-to-pdf=docs/IsHaunted-Product-Documentation.pdf "file://$PWD/docs/ishaunted-documentation.html"
```

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
