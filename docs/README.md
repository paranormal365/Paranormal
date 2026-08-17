# Generated documents

## IsHaunted-Product-Documentation.pdf

Every in-app help document, reproduced **verbatim**, as one printable PDF.

Regenerate after changing anything in `Ben.Web.Library/Help/Content/`:

```
python3 docs/build-documentation-pdf.py
```

Then render the HTML it writes to PDF with headless Chrome:

```
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless \
  --no-pdf-header-footer --print-to-pdf=docs/IsHaunted-Product-Documentation.pdf \
  file://$PWD/ishaunted-documentation.html
```

The script reads the same markdown files the app serves and does not paraphrase, so the PDF
cannot drift from what the product actually tells its users. Front matter drives the ordering,
the section grouping and the per-document audience label.

**It contains no business, market or financial information, and no usage figures** — only what the
software does. Anything of that sort belongs in a separate document written from real numbers.
