#!/usr/bin/env python3
"""Builds the end-user documentation into a single printable HTML file.

Content comes verbatim from Ben.Web.Services/Help/Content — the same files the in-app help
serves. Nothing is paraphrased, so the document cannot drift from what the product says.
"""
import re, os, pathlib, datetime, html
import markdown

# Derived from this file's own location rather than hard-coded, so the build follows the repo
# instead of one machine's checkout path.
ROOT = pathlib.Path(__file__).resolve().parent.parent
CONTENT = ROOT / "Ben.Web.Services/Help/Content"
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

# A moved content folder used to fail silently: glob on a missing directory yields nothing, the
# build succeeds, Chrome succeeds, and the PDF comes out empty. Refuse instead. (The help moved
# from Ben.Web.Library to Ben.Web.Services when the old WebApp was retired, and this script kept
# pointing at the old path.)
if not docs:
    raise SystemExit(f"No help documents found under {CONTENT} — has the content folder moved?")

# ── Screenshots ───────────────────────────────────────────────────────────────
# The documents reference their screenshots two ways, by audience: a public document points at
# /help/media/… under the site's wwwroot, and an administrator document uses the help-media:
# scheme, whose files are embedded in Ben.Web.Services and never served. Chrome renders this HTML
# from docs/, so both become paths relative to that folder — printing keeps the pixels, and the
# HTML stays small enough to open in an editor.
PUBLIC_MEDIA   = ROOT / "Ben.Web.Website/wwwroot/help/media"
EMBEDDED_MEDIA = ROOT / "Ben.Web.Services/Help/Media"
OUT_DIR        = pathlib.Path(__file__).parent

missing_media = []

# ── Print copies ──────────────────────────────────────────────────────────────
# Chrome embeds an image in the PDF exactly as it finds it, and the help screenshots are 2× PNGs.
# Recaptured on the Signal skin — photographs behind the page heroes, gradients, which PNG cannot
# squeeze — the same 30 pages came to 105 MB, past GitHub's 100 MB limit on a single file
# (2026-10-02). The PDF is printed from JPEG copies instead, no wider than a printed page needs;
# the help pages themselves keep their PNGs. The copies live in a git-ignored folder.
PRINT_MEDIA = OUT_DIR / ".print-media"
PRINT_WIDTH = 1800   # px — a Letter page at 300 dpi is 2550 wide, and these sit inside its margins
PRINT_QUALITY = 82


def print_copy(path):
    if path.suffix.lower() != ".png" or not path.exists():
        return path
    try:
        from PIL import Image
    except ImportError:
        return path   # no Pillow: print the PNG, as before
    rel = path.relative_to(ROOT)
    out = (PRINT_MEDIA / rel).with_suffix(".jpg")
    if not out.exists() or out.stat().st_mtime < path.stat().st_mtime:
        out.parent.mkdir(parents=True, exist_ok=True)
        im = Image.open(path).convert("RGB")
        if im.width > PRINT_WIDTH:
            im = im.resize((PRINT_WIDTH, round(im.height * PRINT_WIDTH / im.width)), Image.LANCZOS)
        im.save(out, "JPEG", quality=PRINT_QUALITY, optimize=True, progressive=True)
    return out


def resolve_media(body, slug):
    def public(m):
        rel = m.group(1)
        path = PUBLIC_MEDIA / rel
        if not path.exists():
            missing_media.append(f"{slug} → {path}")
        return f"]({os.path.relpath(print_copy(path), OUT_DIR)})"

    def embedded(m):
        rel = m.group(1)
        path = EMBEDDED_MEDIA / rel
        if not path.exists():
            missing_media.append(f"{slug} → {path}")
        return f"]({os.path.relpath(print_copy(path), OUT_DIR)})"

    body = re.sub(r"\]\(/help/media/([^)]+)\)", public, body)
    body = re.sub(r"\]\(help-media:([^)]+)\)", embedded, body)
    return body


md = markdown.Markdown(extensions=["tables", "sane_lists"])

sections = []
for d in docs:
    md.reset()
    d["html"] = md.convert(resolve_media(d["body"], d["slug"]))
    # Grouped by section, NOT by runs of consecutive sections. The order values interleave
    # (Getting Started is 10, then the client chapters, then Getting Started again at 45+),
    # and matching only against the PREVIOUS document split "Getting Started" into two
    # identical headings in the contents. A section keeps the position of its first document
    # and collects every later one, so the order field still decides sequence.
    existing = next((s for s in sections if s[0] == d["section"]), None)
    if existing is None:
        existing = (d["section"], [])
        sections.append(existing)
    existing[1].append(d)

if missing_media:
    raise SystemExit(
        "Help documents reference screenshots that are not on disk:\n  "
        + "\n  ".join(missing_media)
        + "\nRe-capture them with BEN_CAPTURE=1 (see HelpMediaCapture).")

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

import sys as _sys
_sys.path.insert(0, str(pathlib.Path(__file__).parent))
import site_doc_style  # the website's look (10/05/2026)

# The site's look, plus this document's own pieces: the contents and each article's header.
CSS = site_doc_style.css(str(pathlib.Path(__file__).parent)) + """
/* ── Screenshots ─────────────────────────────────────────────────────── */
.doc img { display: block; margin: .12in auto .05in; }
/* The italic line an author puts under an image is its caption. */
.doc img + em, .doc p > img + em { display: block; margin-bottom: .16in; font-style: normal;
  font-size: 8.4pt; color: var(--faint); text-align: center; }

/* ── Contents ──────────────────────────────────────────────────────── */
.contents { break-after: page; page-break-after: always; }
.contents h2 { font-size: 24pt; margin: 0 0 .22in; }
.toc { list-style: none; padding: 0; margin: 0; }
.toc-section { font-size: 7.8pt; font-weight: 700; letter-spacing: .18em; text-transform: uppercase; color: var(--accent2);
  margin: .24in 0 .08in; padding-bottom: .05in; border-bottom: 1px solid var(--line); }
.toc-section:first-child { margin-top: 0; }
/* The section header is uppercase; its nested list must not inherit that. */
.toc ul { list-style: none; padding: 0; margin: 0; text-transform: none; letter-spacing: normal; font-weight: 400; }
.toc ul li { margin: 0 0 .1in; }
.toc a { color: var(--ink); display: flex; align-items: baseline; gap: .1in; }
.toc-title { font-size: 11pt; font-weight: 650; }
.toc-aud { font-size: 7.6pt; color: var(--faint); margin-left: auto; white-space: nowrap; }
.toc-summary { font-size: 9pt; color: var(--muted); margin-top: 1px; }

/* ── Each article opens like a page on the site ───────────────────── */
.doc { break-before: page; page-break-before: always; }
.doc-head { position: relative; margin: 0 0 .26in; padding: .26in .3in .24in; border-radius: .2in; overflow: hidden;
  background: radial-gradient(120% 140% at 100% 0%, rgba(124,92,255,.28), transparent 60%),
              radial-gradient(90% 120% at 0% 100%, rgba(34,211,238,.14), transparent 60%), var(--surface);
  border: 1px solid var(--line); }
.doc-head::before { content: ""; position: absolute; inset: 0 0 auto 0; height: 3px; background: var(--grad); }
.doc-kicker { font-size: 7.8pt; font-weight: 700; letter-spacing: .2em; text-transform: uppercase; color: var(--accent2); margin-bottom: .06in; }
.doc h1 { font-size: 26pt; margin: 0 0 .06in; }
.doc-summary { font-size: 11pt; color: var(--body); margin: 0 0 .06in; }
.doc-aud { font-size: 8.4pt; color: var(--muted); }
"""

doc = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<title>IsHaunted.com — Product Documentation</title>
<style>{CSS}</style></head>
<body>

{site_doc_style.cover(str(pathlib.Path(__file__).parent), kicker="Product documentation",
    title_html='Everything <span class="accent-text">IsHaunted</span> does.',
    lede="The complete product documentation — every screen, rule and safeguard, written for the people who use it.",
    photo="i3-walking-to-house.jpg",
    facts=[("Documents", str(len(docs))), ("Sections", str(len(sections))), ("Updated", datetime.date.today().strftime("%m/%d/%Y"))],
    about_html="<b>About this document.</b> This is the in-product help, reproduced in full and unaltered — the same "
               "text the application serves to its users. It describes the software as built. It contains no business, "
               "market or financial information, and no usage figures.",
    foot_right="Product documentation")}

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
