"""The website's look, for the documents printed from docs/ (10/05/2026).

Ben: "make them styled like the site." The product documentation, the seven seat guides and the two app
guides were plain white pages in the system font. These are the site's own pieces, in its dark mode:

- Public Sans, the site's typeface, loaded from the site's own font files (wwwroot/fonts) rather than a
  look-alike — the `url(/fonts/...)` in fonts.css is rewritten to reach them from docs/.
- The dark-mode values of themes/signal-tokens.css, copied by name, so a page of a PDF and a page of the
  site read as one thing; and the screenshots, all captured in dark mode, sit on a page of their own color.
- A cover built like the site's PageHero: a photograph in a rounded card, a cyan kicker, a big white
  title, the brand tile; then the facts as the site's cards.

The flyers (docs/ads) keep their own design — Ben likes them, and they are not touched by this.
The dark page reaches the paper's edge because the color is on @page, not on the body (tested 10/05:
an html background leaves the margins white).
"""
import html as _html
import os

DOCS = os.path.dirname(os.path.abspath(__file__))
WWWROOT = os.path.abspath(os.path.join(DOCS, "..", "Ben.Web.Website", "wwwroot"))


def _rel(from_dir, target):
    return os.path.relpath(target, from_dir).replace(os.sep, "/")


def font_faces(html_dir):
    """The site's @font-face rules, pointing at the site's font files from where the HTML is written."""
    with open(os.path.join(WWWROOT, "fonts", "fonts.css"), encoding="utf-8") as f:
        css = f.read()
    return css.replace("url(/fonts/", f"url({_rel(html_dir, os.path.join(WWWROOT, 'fonts'))}/")


BASE = """
:root {
  --bg: #0C1017; --sunken: #090D13; --surface: #151C28; --raised: #1B2433;
  --ink: #E9EDF4; --body: #C6CEDA; --muted: #98A3B4; --faint: #6C7889;
  --line: rgba(255,255,255,.09); --line-strong: rgba(255,255,255,.18);
  --accent: #7C5CFF; --accent2: #22D3EE; --link: #A78BFA; --soft: rgba(124,92,255,.16);
  --grad: linear-gradient(120deg, #7C5CFF, #22D3EE);
  --glow: 0 8px 24px rgba(124,92,255,.40);
}
@page { size: 8.5in 11in; margin: .62in .66in .72in; background: #0C1017;
  @bottom-center { content: counter(page); font-family: 'Public Sans', sans-serif; font-size: 8pt; color: #6C7889; } }
@page cover { margin: 0; @bottom-center { content: none; } }
* { box-sizing: border-box; }
html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
body { margin: 0; background: #0C1017; color: var(--body);
  font-family: 'Public Sans', system-ui, -apple-system, sans-serif; font-size: 10.2pt; line-height: 1.6; }

h1, h2, h3, h4 { color: var(--ink); font-weight: 700; letter-spacing: -.028em; line-height: 1.12;
  break-after: avoid-page; page-break-after: avoid; }
h1 { font-size: 30pt; margin: 0 0 .1in; }
h2 { font-size: 16.5pt; margin: .42in 0 .12in; }
h2::before { content: ""; display: block; width: .38in; height: 3px; border-radius: 2px; background: var(--grad);
  margin-bottom: .12in; }
h3 { font-size: 12.5pt; margin: .26in 0 .08in; }
p { margin: 0 0 .11in; }
ul, ol { margin: 0 0 .12in; padding-left: .24in; }
li { margin-bottom: .05in; }
li::marker { color: var(--link); }
a { color: var(--link); text-decoration: none; }
b, strong { color: var(--ink); font-weight: 650; }
em { color: var(--ink); }
.kicker { font-size: 8pt; font-weight: 700; letter-spacing: .2em; text-transform: uppercase; color: var(--accent2); margin-bottom: .06in; }
/* A solid violet, not the site's gradient-clipped text: Chrome prints background-clip:text as a solid
   block over the last letters (seen on the product cover, 10/05). */
.accent-text { color: #A78BFA; }

code, .mono { font-family: ui-monospace, 'SF Mono', Menlo, monospace; font-size: 8.7pt; color: var(--ink);
  background: var(--raised); border: 1px solid var(--line); padding: 0 4px; border-radius: 4px; }
pre { font-family: ui-monospace, 'SF Mono', Menlo, monospace; font-size: 8.6pt; line-height: 1.5; color: var(--ink);
  background: var(--surface); border: 1px solid var(--line); border-radius: 10px; padding: .12in .16in; white-space: pre-wrap; }
pre code { background: none; border: 0; padding: 0; }

blockquote { margin: 0 0 .14in; padding: .1in .16in; border-left: 3px solid var(--accent); border-radius: 0 10px 10px 0;
  background: var(--soft); color: var(--body); }
blockquote p:last-child { margin-bottom: 0; }

table { width: 100%; border-collapse: separate; border-spacing: 0; margin: .06in 0 .16in; font-size: 9pt;
  border: 1px solid var(--line); border-radius: 10px; overflow: hidden; page-break-inside: avoid; }
th { text-align: left; padding: .07in .1in; background: var(--raised); color: var(--muted); font-size: 7.6pt;
  font-weight: 700; letter-spacing: .08em; text-transform: uppercase; border-bottom: 1px solid var(--line-strong); }
td { padding: .07in .1in; border-bottom: 1px solid var(--line); vertical-align: top; background: var(--surface); }
tr:last-child td { border-bottom: 0; }

/* No drop shadow: on a dark page it adds nothing, and Chrome draws a shadow split by a page break as a
   dark bar at the foot of the page before (seen on the first build, 10/05). */
img { max-width: 100%; height: auto; border-radius: 10px; border: 1px solid var(--line-strong); page-break-inside: avoid; }
/* A screen and the words about it travel together. */
.shot { break-inside: avoid; page-break-inside: avoid; }
figure { margin: .14in 0 .2in; text-align: center; page-break-inside: avoid; }
figcaption { color: var(--faint); font-size: 8.4pt; margin-top: .07in; }

/* ── the cover: the site's PageHero, on paper ─────────────────────────── */
.site-cover { page: cover; position: relative; width: 8.5in; height: 11in; overflow: hidden; break-after: page;
  page-break-after: always; background: radial-gradient(90% 50% at 100% 0%, #1E1650 0%, #0C1017 65%); }
.site-cover .hero { position: absolute; left: .45in; right: .45in; top: .45in; height: 6.4in; border-radius: .3in; overflow: hidden;
  background-size: cover; background-position: center; box-shadow: 0 24px 60px rgba(0,0,0,.55); }
.site-cover .hero::after { content: ""; position: absolute; inset: 0;
  background: linear-gradient(180deg, rgba(12,16,23,.25) 0%, rgba(12,16,23,.15) 35%, rgba(12,16,23,.92) 100%),
              linear-gradient(90deg, rgba(30,22,80,.55) 0%, rgba(30,22,80,0) 60%); }
.site-cover .brand { position: absolute; top: .38in; left: .45in; z-index: 2; display: flex; align-items: center; gap: .12in;
  color: #fff; font-weight: 700; font-size: 13pt; letter-spacing: -.01em; }
.site-cover .tile { width: .5in; height: .5in; border-radius: .13in; display: grid; place-items: center; background: var(--grad);
  box-shadow: var(--glow); }
.site-cover .tile img { width: 68%; height: 68%; border: 0; border-radius: 0; box-shadow: none; filter: brightness(0) invert(1); }
.site-cover .brand span { color: var(--link); }
.site-cover .words { position: absolute; left: .45in; right: .6in; bottom: .48in; z-index: 2; }
.site-cover h1 { font-size: 40pt; line-height: 1.02; color: #fff; margin: .06in 0 .14in; }
.site-cover .lede { font-size: 13pt; line-height: 1.45; color: rgba(255,255,255,.88); margin: 0; max-width: 5.6in; }
.site-cover .facts { position: absolute; left: .45in; right: .45in; top: 7.1in; display: grid; grid-template-columns: repeat(3, 1fr); gap: .14in; }
.site-cover .fact { background: var(--surface); border: 1px solid var(--line); border-radius: .16in; padding: .15in .18in; }
.site-cover .fact b { display: block; font-size: 7.4pt; letter-spacing: .16em; text-transform: uppercase; color: var(--muted); font-weight: 700; }
.site-cover .fact span { display: block; font-size: 12pt; color: var(--ink); font-weight: 650; margin-top: .04in; letter-spacing: -.01em; }
.site-cover .about { position: absolute; left: .45in; right: .45in; top: 8.25in; background: var(--surface); border: 1px solid var(--line);
  border-left: 3px solid var(--accent); border-radius: .16in; padding: .16in .22in; font-size: 9.4pt; line-height: 1.55; color: var(--body); }
.site-cover .about b { color: var(--ink); }
.site-cover .foot { position: absolute; left: .45in; right: .45in; bottom: .38in; display: flex; justify-content: space-between;
  align-items: center; font-size: 8pt; color: var(--faint); letter-spacing: .06em; }
.site-cover .foot::before { content: ""; position: absolute; left: 0; right: 0; top: -.14in; height: 2px; border-radius: 2px; background: var(--grad); opacity: .7; }
"""


def css(html_dir):
    """Everything a document needs: the site's fonts, then the site's dark look."""
    return font_faces(html_dir) + BASE


def cover(html_dir, *, kicker, title_html, lede, photo, facts, about_html, foot_left="ishaunted.com", foot_right=""):
    """The site's PageHero as a cover page. `photo` is a file name in docs/media/stock."""
    e = _html.escape
    photo_url = _rel(html_dir, os.path.join(DOCS, "media", "stock", photo))
    logo_url = _rel(html_dir, os.path.join(WWWROOT, "static", "images", "is-haunted-logo.svg"))
    fact_html = "".join(f'<div class="fact"><b>{e(k)}</b><span>{e(v)}</span></div>' for k, v in facts)
    return f"""
<section class="site-cover">
  <div class="hero" style="background-image:url('{photo_url}')"></div>
  <div class="brand" style="top:.82in;left:.9in"><div class="tile"><img src="{logo_url}" alt=""></div><div>IsHaunted<span>.com</span></div></div>
  <div class="words" style="left:.9in;right:1in;bottom:auto;top:{6.85 - 2.55}in">
    <div class="kicker">{e(kicker)}</div>
    <h1>{title_html}</h1>
    <p class="lede">{e(lede)}</p>
  </div>
  <div class="facts">{fact_html}</div>
  <div class="about">{about_html}</div>
  <div class="foot"><span>{e(foot_left)}</span><span>{e(foot_right)}</span></div>
</section>"""
