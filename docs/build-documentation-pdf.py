#!/usr/bin/env python3
"""Builds the end-user documentation into a single printable HTML file.

Content comes verbatim from Ben.Web.Library/Help/Content — the same files the in-app help
serves. Nothing is paraphrased, so the document cannot drift from what the product says.
"""
import re, pathlib, datetime, html
import markdown

ROOT = pathlib.Path("/Users/ben/Source/Ben")
CONTENT = ROOT / "Ben.Web.Library/Help/Content"
OUT = pathlib.Path(__file__).parent / "ishaunted-documentation.html"

AUDIENCE_LABEL = {
    "everyone": "Public — no account needed",
    "signedin": "Signed-in users",
    "organizationmember": "Group members",
    "organizationadministrator": "Group owners &amp; administrators",
    "appadministrator": "Platform administrators",
}


def parse(path):
    raw = path.read_text().replace("\r\n", "\n")
    if not raw.startswith("---\n"):
        return None
    end = raw.index("\n---", 4)
    fm, body = raw[4:end], raw[end + 4:].lstrip("\n")
    fields = {}
    for line in fm.split("\n"):
        if ":" in line:
            k, v = line.split(":", 1)
            fields[k.strip().lower()] = v.strip()
    if "title" not in fields:
        return None
    # The first heading duplicates the front-matter title in a couple of files.
    body = re.sub(r"^#\s+.*\n+", "", body, count=1)
    return {
        "slug": path.stem,
        "title": fields["title"],
        "summary": fields.get("summary", ""),
        "section": fields.get("section", "General"),
        "audience": fields.get("audience", "").lower(),
        "order": int(fields.get("order", 500)),
        "body": body,
    }


docs = sorted(
    (d for d in (parse(p) for p in CONTENT.glob("*.md")) if d),
    key=lambda d: (d["order"], d["title"]),
)

md = markdown.Markdown(extensions=["tables", "sane_lists"])

sections = []
for d in docs:
    md.reset()
    d["html"] = md.convert(d["body"])
    if not sections or sections[-1][0] != d["section"]:
        sections.append((d["section"], []))
    sections[-1][1].append(d)

today = datetime.date.today().strftime("%-d %B %Y")

toc_rows = []
for name, items in sections:
    toc_rows.append(f'<li class="toc-section">{html.escape(name)}<ul>')
    for d in items:
        label = AUDIENCE_LABEL.get(d["audience"], "")
        toc_rows.append(
            f'<li><a href="#{d["slug"]}"><span class="toc-title">{html.escape(d["title"])}</span>'
            f'<span class="toc-aud">{label}</span></a>'
            f'<div class="toc-summary">{html.escape(d["summary"])}</div></li>'
        )
    toc_rows.append("</ul></li>")

body_parts = []
for name, items in sections:
    for d in items:
        label = AUDIENCE_LABEL.get(d["audience"], "")
        body_parts.append(f"""
<section class="doc" id="{d['slug']}">
  <div class="doc-head">
    <div class="doc-kicker">{html.escape(name)}</div>
    <h1>{html.escape(d['title'])}</h1>
    <p class="doc-summary">{html.escape(d['summary'])}</p>
    <div class="doc-aud">Written for: <strong>{label}</strong></div>
  </div>
  {d['html']}
</section>""")

CSS = """
@page { size: A4; margin: 20mm 18mm 18mm; }
@page { @bottom-center { content: counter(page); } }

* { box-sizing: border-box; }
html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
body {
  font-family: "Charter", "Iowan Old Style", Georgia, serif;
  font-size: 10.5pt; line-height: 1.55; color: #1a1a1a; margin: 0;
}
h1, h2, h3, .doc-kicker, .toc-aud, .cover-meta, th {
  font-family: "Helvetica Neue", Helvetica, Arial, sans-serif;
}

/* ── Cover ─────────────────────────────────────────────────────────── */
.cover { height: 247mm; display: flex; flex-direction: column; page-break-after: always; }
.cover-top { flex: 1; display: flex; flex-direction: column; justify-content: center; }
.cover-rule { width: 54px; height: 4px; background: #6d28d9; margin-bottom: 26px; }
.cover h1 { font-size: 40pt; line-height: 1.05; margin: 0 0 10px; letter-spacing: -0.02em; }
.cover .lede { font-size: 14pt; color: #444; margin: 0 0 34px; max-width: 118mm; line-height: 1.45; }
.cover-meta { font-size: 9.5pt; color: #666; letter-spacing: 0.04em; text-transform: uppercase; }
.cover-meta div { margin-bottom: 5px; }
.status {
  border: 1px solid #d8d3ea; background: #f7f5fd; border-radius: 4px;
  padding: 14px 18px; font-size: 9.5pt; line-height: 1.5; color: #332b52;
}
.status strong { display: block; margin-bottom: 4px; font-family: "Helvetica Neue", sans-serif; }

/* ── Contents ──────────────────────────────────────────────────────── */
.contents { page-break-after: always; }
.contents h2 { font-size: 20pt; margin: 0 0 22px; }
.toc { list-style: none; padding: 0; margin: 0; }
.toc-section {
  font-family: "Helvetica Neue", sans-serif; font-size: 8.5pt; font-weight: 700;
  letter-spacing: 0.09em; text-transform: uppercase; color: #6d28d9;
  margin: 22px 0 8px; padding-bottom: 5px; border-bottom: 1px solid #e6e2f2;
}
.toc-section:first-child { margin-top: 0; }
/* The section header is uppercase; its nested list must not inherit that — the first
   render turned every document title and summary into shouting. */
.toc ul {
  list-style: none; padding: 0; margin: 0;
  text-transform: none; letter-spacing: normal; font-weight: 400; color: #1a1a1a;
}
.toc ul li { margin: 0 0 11px; }
.toc a { text-decoration: none; color: #1a1a1a; display: flex; align-items: baseline; gap: 10px; }
.toc-title { font-size: 11.5pt; font-weight: 600; font-family: "Helvetica Neue", sans-serif; }
.toc-aud { font-size: 8pt; color: #888; margin-left: auto; white-space: nowrap; }
.toc-summary { font-size: 9.5pt; color: #555; margin-top: 1px; }

/* ── Documents ─────────────────────────────────────────────────────── */
.doc { page-break-before: always; }
.doc-head { border-bottom: 2px solid #1a1a1a; padding-bottom: 14px; margin-bottom: 22px; }
.doc-kicker {
  font-size: 8.5pt; font-weight: 700; letter-spacing: 0.09em;
  text-transform: uppercase; color: #6d28d9; margin-bottom: 7px;
}
.doc h1 { font-size: 25pt; margin: 0 0 7px; letter-spacing: -0.015em; }
.doc-summary { font-size: 11.5pt; color: #444; margin: 0 0 9px; }
.doc-aud { font-size: 9pt; color: #777; }

.doc h2 {
  font-size: 13.5pt; margin: 26px 0 9px; padding-top: 3px;
  page-break-after: avoid;
}
.doc h3 { font-size: 11.5pt; margin: 20px 0 7px; page-break-after: avoid; }
p { margin: 0 0 11px; }
ul, ol { margin: 0 0 12px; padding-left: 20px; }
li { margin-bottom: 5px; }
li > ul, li > ol { margin-top: 5px; }
strong { font-weight: 700; }
code {
  font-family: "SF Mono", Menlo, monospace; font-size: 9pt;
  background: #f2f0f7; padding: 1px 4px; border-radius: 3px;
}
blockquote {
  margin: 0 0 12px; padding: 9px 15px; border-left: 3px solid #c4bce0;
  background: #f9f8fc; color: #3d3550; font-style: italic;
}
blockquote p:last-child { margin-bottom: 0; }

table {
  width: 100%; border-collapse: collapse; margin: 0 0 14px;
  font-size: 9.5pt; page-break-inside: avoid;
}
th {
  text-align: left; padding: 7px 9px; background: #f2f0f7;
  border-bottom: 1.5px solid #c4bce0; font-size: 8.5pt;
  text-transform: uppercase; letter-spacing: 0.05em;
}
td { padding: 7px 9px; border-bottom: 1px solid #e8e5ef; vertical-align: top; }
tr:last-child td { border-bottom: none; }

/* Keep a heading with what follows it wherever the renderer allows. */
h1, h2, h3 { break-after: avoid-page; }
"""

doc = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<title>IsHaunted.com — Product Documentation</title>
<style>{CSS}</style></head>
<body>

<div class="cover">
  <div class="cover-top">
    <div class="cover-rule"></div>
    <h1>IsHaunted.com</h1>
    <p class="lede">The complete product documentation — every screen, rule and safeguard,
      written for the people who use it.</p>
    <div class="cover-meta">
      <div>Product Documentation</div>
      <div>{len(docs)} documents &middot; {len(sections)} sections</div>
      <div>{today}</div>
    </div>
  </div>
  <div class="status">
    <strong>About this document</strong>
    This is the in-product help, reproduced in full and unaltered — the same text the
    application serves to its users. It describes the software as built. It contains no
    business, market or financial information, and no usage figures.
  </div>
</div>

<div class="contents">
  <h2>Contents</h2>
  <ul class="toc">{''.join(toc_rows)}</ul>
</div>

{''.join(body_parts)}

</body></html>"""

OUT.write_text(doc)
print(f"wrote {OUT} ({len(doc):,} bytes, {len(docs)} documents)")
for name, items in sections:
    print(f"  {name}: {', '.join(d['title'] for d in items)}")
