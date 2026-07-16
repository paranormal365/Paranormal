#!/usr/bin/env python3
"""
Converts all .md files in ProjectNotes/Project-Analysis/ to Bootstrap-dark HTML
in ProjectNotes/Project-Analysis-Html/.

Usage:  python3 generate_html.py
Run from:  /Users/ben/Source/Ben
"""

import os
import re
import pathlib
import markdown
from markdown.extensions.tables import TableExtension
from markdown.extensions.fenced_code import FencedCodeExtension

SRC_ROOT = pathlib.Path("ProjectNotes/Project-Analysis")
DST_ROOT = pathlib.Path("ProjectNotes/Project-Analysis-Html")

# ── Navigation structure ──────────────────────────────────────────────────────

NAV = [
    ("Ben.Data.Common",                  ["Summary","Interfaces","Helpers","Enums","Constants","Services"]),
    ("Ben.Data.Source",                  ["Summary","Context","Entities-Core","Entities-User","Entities-Org","Entities-Upload"]),
    ("Ben.Data.WebApi",                  ["Summary","Controllers-Base","Controllers-Security","Controllers-Admin","Controllers-Entities"]),
    ("Ben.Service.Mappings",             ["Summary","Profiles"]),
    ("Ben.Service.Models",               ["Summary","Records-Admin","Records-Entities"]),
    ("Ben.Service.RepositoryService",    ["Summary","Interfaces-Generic","Services"]),
    ("Ben.Service.RepositoryService.Tests", ["Summary"]),
    ("Ben.Service.Security",             ["Summary","Interfaces"]),
    ("Ben.Web.Library",                  ["Summary","Services","Components"]),
    ("Ben.Web.Tests",                    ["Summary"]),
    ("Ben.Web.WebApp",                   ["Summary","Services-Interfaces","Services-Implementations","Components-Pages"]),
]

# Colour band per project (Bootstrap text colours)
PROJECT_COLOURS = {
    "Ben.Data.Common":                   "text-info",
    "Ben.Data.Source":                   "text-primary",
    "Ben.Data.WebApi":                   "text-success",
    "Ben.Service.Mappings":              "text-warning",
    "Ben.Service.Models":                "text-warning",
    "Ben.Service.RepositoryService":     "text-danger",
    "Ben.Service.RepositoryService.Tests":"text-secondary",
    "Ben.Service.Security":              "text-danger",
    "Ben.Web.Library":                   "text-info",
    "Ben.Web.Tests":                     "text-secondary",
    "Ben.Web.WebApp":                    "text-success",
}

# ── Shared head + custom CSS ──────────────────────────────────────────────────

CUSTOM_CSS = """
  :root { --bs-body-bg: #0d1117; }
  body  { background-color: var(--bs-body-bg); color: #c9d1d9; }
  .navbar { background-color: #161b22 !important; border-bottom: 1px solid #30363d; }
  .navbar-brand { color: #58a6ff !important; font-size: 1.15rem; }
  .breadcrumb-item a, .breadcrumb-item.active { color: #8b949e; font-size: .9rem; }
  /* sidebar */
  #sidebar { position: sticky; top: 1rem; max-height: calc(100vh - 5rem); overflow-y: auto; }
  #sidebar .nav-link { color: #8b949e; font-size: .875rem; padding: .2rem .75rem; border-left: 2px solid transparent; }
  #sidebar .nav-link:hover, #sidebar .nav-link.active { color: #58a6ff; border-left-color: #58a6ff; }
  /* content */
  .page-header { border-bottom: 2px solid #30363d; padding-bottom: .75rem; margin-bottom: 1.5rem; }
  .page-header h1 { color: #e6edf3; font-size: 1.75rem; }
  .section-card { background-color: #161b22; border: 1px solid #30363d; border-radius: 8px; padding: 1.5rem; margin-bottom: 1.5rem; }
  .section-card h2 { color: #e6edf3; font-size: 1.2rem; padding-bottom: .5rem; border-bottom: 1px solid #30363d; margin-bottom: 1rem; }
  .section-card h3 { color: #c9d1d9; font-size: 1.05rem; margin-top: 1.2rem; }
  .section-card h4 { color: #8b949e; font-size: .95rem; margin-top: 1rem; }
  /* tables */
  .table { --bs-table-bg: transparent; --bs-table-striped-bg: rgba(255,255,255,.025); color: #c9d1d9; }
  .table thead th { color: #58a6ff; border-bottom: 2px solid #30363d; white-space: nowrap; }
  .table td, .table th { border-color: #21262d; vertical-align: top; }
  .table-responsive { border: 1px solid #30363d; border-radius: 6px; }
  /* code */
  pre { background: #010409 !important; border: 1px solid #30363d; border-radius: 6px; padding: 1rem; overflow-x: auto; }
  code { color: #e3b341; font-size: .85em; }
  pre code { color: #c9d1d9; }
  /* inline code in tables */
  td code, th code { background: rgba(110,118,129,.2); padding: .1em .35em; border-radius: 3px; }
  /* blockquote / note boxes */
  blockquote { background: rgba(121,192,255,.05); border-left: 3px solid #1f6feb; padding: .75rem 1rem; border-radius: 0 6px 6px 0; margin: 1rem 0; }
  blockquote p { margin: 0; color: #a5d6ff; }
  /* badges */
  .badge-project { font-size: .8em; font-weight: 500; vertical-align: middle; }
  /* source link */
  a.src-link { color: #6e7681; font-size: .8em; font-style: italic; }
  a.src-link:hover { color: #58a6ff; }
  /* toc */
  #toc a { color: #8b949e; font-size: .875rem; text-decoration: none; display: block; padding: .15rem .5rem; border-radius: 4px; }
  #toc a:hover { background: rgba(255,255,255,.05); color: #58a6ff; }
  /* back to top */
  #back-top { position: fixed; bottom: 1.5rem; right: 1.5rem; opacity: .7; }
  #back-top:hover { opacity: 1; }
"""

def nav_html(current_project: str, from_index: bool = False) -> str:
    """Build the Bootstrap navbar with project dropdowns."""
    prefix = "" if from_index else "../"
    items = []
    for proj, pages in NAV:
        colour = PROJECT_COLOURS.get(proj, "text-light")
        active = "active" if proj == current_project else ""
        short = proj.replace("Ben.", "").replace(".Tests", " Tests")
        # build dropdown items
        dd_items = []
        for page in pages:
            label = page.replace("-", " ").replace("Summary", "📋 Summary")
            dd_items.append(
                f'<li><a class="dropdown-item" href="{prefix}{proj}/{page}.html">{label}</a></li>'
            )
        dd_html = "\n".join(dd_items)
        items.append(f"""
          <li class="nav-item dropdown">
            <a class="nav-link dropdown-toggle {active} {colour}" href="#" role="button"
               data-bs-toggle="dropdown">{short}</a>
            <ul class="dropdown-menu dropdown-menu-dark">
              {dd_html}
            </ul>
          </li>""")
    return "\n".join(items)


def html_template(title: str, project: str, breadcrumb: str, body_html: str,
                  from_index: bool = False, toc_items: list = None) -> str:
    prefix = "" if from_index else "../"
    colour = PROJECT_COLOURS.get(project, "text-light")
    toc_html = ""
    if toc_items:
        links = "\n".join(f'<a href="#{item[1]}">{item[0]}</a>' for item in toc_items)
        toc_html = f"""
      <div id="sidebar" class="col-lg-2 d-none d-lg-block">
        <div class="ps-2 pt-2">
          <div class="text-muted small fw-semibold mb-2 text-uppercase" style="font-size:.7rem;letter-spacing:.08em">On this page</div>
          <nav id="toc">{links}</nav>
        </div>
      </div>"""
        content_col = "col-lg-9"
    else:
        content_col = "col-12"

    return f"""<!DOCTYPE html>
<html lang="en" data-bs-theme="dark">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>{title} — Ben Project Analysis</title>
  <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" rel="stylesheet">
  <style>{CUSTOM_CSS}</style>
</head>
<body>
<!-- ── Navbar ──────────────────────────────────────────────────────────────── -->
<nav class="navbar navbar-expand-xl sticky-top">
  <div class="container-xxl">
    <a class="navbar-brand" href="{prefix}index.html">🔷 Ben Analysis</a>
    <button class="navbar-toggler border-0" type="button" data-bs-toggle="collapse" data-bs-target="#navMain" aria-label="Toggle navigation">
      <span class="navbar-toggler-icon"></span>
    </button>
    <div class="collapse navbar-collapse" id="navMain">
      <ul class="navbar-nav flex-wrap gap-0">
        {nav_html(project, from_index)}
      </ul>
    </div>
  </div>
</nav>

<div class="container-xxl py-4">
  <!-- breadcrumb -->
  <nav aria-label="breadcrumb" class="mb-3">
    <ol class="breadcrumb">
      <li class="breadcrumb-item"><a href="{prefix}index.html" class="text-decoration-none text-secondary">Home</a></li>
      {breadcrumb}
    </ol>
  </nav>

  <div class="row">
{toc_html}
    <div class="{content_col}">
      {body_html}
    </div>
  </div>
</div>

<!-- back to top -->
<a id="back-top" href="#" class="btn btn-sm btn-outline-secondary rounded-pill">↑ Top</a>

<script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js"></script>
</body>
</html>"""


# ── Markdown → HTML conversion ────────────────────────────────────────────────

md_processor = markdown.Markdown(
    extensions=[
        TableExtension(),
        FencedCodeExtension(),
        "nl2br",
        "sane_lists",
    ]
)

def md_to_html(md_text: str) -> str:
    """Convert markdown to HTML using python-markdown."""
    md_processor.reset()
    html = md_processor.convert(md_text)
    # Fix table classes — add Bootstrap table classes
    html = html.replace("<table>", '<div class="table-responsive"><table class="table table-bordered table-striped table-hover table-sm">')
    html = html.replace("</table>", "</table></div>")
    # Style theadscols
    html = html.replace("<thead>", '<thead class="table-dark">')
    # Wrap code blocks in section-friendly pre
    return html


def fixup_links(html: str, current_folder: str) -> str:
    """
    - Convert .md hrefs to .html
    - Keep cross-folder relative paths correct
    - Source code links (../../../...) remain unchanged but styled differently
    """
    # .md → .html for internal analysis links
    html = re.sub(r'href="([^"]*?)\.md"', lambda m: f'href="{m.group(1)}.html"', html)
    # Source code links (deep relative paths with .cs / .razor) — add class
    html = re.sub(
        r'href="(\.\.\/\.\.\/\.\.\/[^"]+\.(cs|razor|json|md|sh))"',
        r'href="\1" class="src-link" target="_blank"',
        html
    )
    return html


def extract_toc(md_text: str) -> list:
    """Extract h2/h3 headings for the sidebar TOC."""
    items = []
    for m in re.finditer(r'^(#{2,3})\s+(.+)', md_text, re.MULTILINE):
        level = len(m.group(1))
        text = m.group(2).strip()
        # Build the anchor ID python-markdown uses (lowercase, spaces→-)
        anchor = re.sub(r'[^\w\s-]', '', text.lower()).strip()
        anchor = re.sub(r'[\s]+', '-', anchor)
        indent = "&nbsp;&nbsp;&nbsp;" if level == 3 else ""
        items.append((indent + text, anchor))
    return items


def wrap_sections(html: str) -> str:
    """Wrap h2-led content blocks in section cards."""
    # Split on <h2 (keeping the tag)
    parts = re.split(r'(?=<h2)', html)
    if len(parts) <= 1:
        return f'<div class="section-card">{html}</div>'
    result = [f'<div class="section-card">{parts[0]}</div>'] if parts[0].strip() else []
    for part in parts[1:]:
        result.append(f'<div class="section-card">{part}</div>')
    return "\n".join(result)


def convert_file(src: pathlib.Path, dst: pathlib.Path, project: str, page_name: str):
    """Read a .md file and write a .html file."""
    md_text = src.read_text(encoding="utf-8")

    # Extract page title from first H1
    title_match = re.search(r'^#\s+(.+)', md_text, re.MULTILINE)
    page_title = title_match.group(1).strip() if title_match else page_name

    toc_items = extract_toc(md_text)
    body_html_raw = md_to_html(md_text)
    body_html = fixup_links(body_html_raw, project)
    body_html = wrap_sections(body_html)

    colour = PROJECT_COLOURS.get(project, "text-light")
    breadcrumb = f"""<li class="breadcrumb-item"><a href="{project}/Summary.html" class="text-decoration-none {colour}">{project}</a></li>
      <li class="breadcrumb-item active">{page_name}</li>"""

    full_html = html_template(
        title=page_title,
        project=project,
        breadcrumb=breadcrumb,
        body_html=body_html,
        from_index=False,
        toc_items=toc_items if len(toc_items) > 2 else None,
    )
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_text(full_html, encoding="utf-8")
    print(f"  ✓  {dst.relative_to(DST_ROOT)}")


# ── Index page ────────────────────────────────────────────────────────────────

def build_index():
    project_cards = []
    for proj, pages in NAV:
        colour = PROJECT_COLOURS.get(proj, "text-light")
        short = proj.replace("Ben.", "").replace(".Tests", " Tests")
        links_html = " ".join(
            f'<a href="{proj}/{p}.html" class="btn btn-sm btn-outline-secondary me-1 mb-1">{p.replace("-"," ")}</a>'
            for p in pages
        )
        # short description per project
        descs = {
            "Ben.Data.Common": "Shared contracts, helpers, enums, and constants. No project dependencies — the base of the dependency tree.",
            "Ben.Data.Source": "EF Core DbContext, all 31 entity classes, migrations, and design-time factory. Single source of truth for the BenDb schema.",
            "Ben.Data.WebApi": "ASP.NET Core Web API — all endpoints, controllers, seeders, and authentication setup. Runs on port 5252.",
            "Ben.Service.Mappings": "AutoMapper profiles mapping EF Core entities to DTO records. Registered via assembly scan.",
            "Ben.Service.Models": "Immutable C# record DTOs returned by API endpoints and consumed by Blazor components.",
            "Ben.Service.RepositoryService": "Repository pattern implementation with organisation security, audit logging, and geocoding services.",
            "Ben.Service.RepositoryService.Tests": "xUnit tests for repository services using EF Core InMemory provider. 119 tests.",
            "Ben.Service.Security": "Organisation-level permission enforcement layer — middleware attribute, policy checks, and security enums.",
            "Ben.Web.Library": "Shared Blazor component library (Telerik UI). SuperAdmin panels and user detail pages.",
            "Ben.Web.Tests": "xUnit tests for the WebApp service layer using Moq. 82 tests.",
            "Ben.Web.WebApp": "Blazor Server application — primary UI. Communicates with WebApi over HTTP. Runs on port 5078.",
        }
        desc = descs.get(proj, "")
        project_cards.append(f"""
      <div class="col-md-6 col-xl-4">
        <div class="section-card h-100 d-flex flex-column">
          <h5 class="{colour} mb-1">{proj}</h5>
          <p class="text-secondary small mb-3" style="flex:1">{desc}</p>
          <div>{links_html}</div>
        </div>
      </div>""")

    cards_html = f'<div class="row g-3">{"".join(project_cards)}</div>'

    body = f"""
    <div class="page-header mb-4">
      <h1 class="text-white">🔷 Ben — Project Analysis</h1>
      <p class="text-secondary">Architecture reference for all 11 projects in the Ben solution.</p>
    </div>

    <div class="section-card mb-4">
      <h2>Solution Architecture</h2>
      <p>The Ben solution is structured as a clean layered architecture:</p>
      <ul>
        <li><strong>Data layer:</strong> <code>Ben.Data.Common</code> → <code>Ben.Data.Source</code> → <code>Ben.Data.WebApi</code></li>
        <li><strong>Service layer:</strong> <code>Ben.Service.Mappings</code>, <code>Ben.Service.Models</code>, <code>Ben.Service.RepositoryService</code>, <code>Ben.Service.Security</code></li>
        <li><strong>UI layer:</strong> <code>Ben.Web.Library</code> → <code>Ben.Web.WebApp</code></li>
        <li><strong>Tests:</strong> <code>Ben.Service.RepositoryService.Tests</code>, <code>Ben.Web.Tests</code></li>
      </ul>
      <p class="mb-0 text-secondary small">Use the navbar dropdowns above to jump directly to any file.</p>
    </div>

    <h2 class="mb-3 text-white">Projects</h2>
    {cards_html}
    """

    breadcrumb = '<li class="breadcrumb-item active">Home</li>'
    html = html_template(
        title="Ben Project Analysis",
        project="",
        breadcrumb=breadcrumb,
        body_html=body,
        from_index=True,
    )
    idx_path = DST_ROOT / "index.html"
    idx_path.parent.mkdir(parents=True, exist_ok=True)
    idx_path.write_text(html, encoding="utf-8")
    print(f"  ✓  index.html")


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    print(f"\nConverting markdown → HTML")
    print(f"  Source: {SRC_ROOT}")
    print(f"  Dest:   {DST_ROOT}\n")
    build_index()
    for proj, pages in NAV:
        print(f"\n  [{proj}]")
        for page in pages:
            src = SRC_ROOT / proj / f"{page}.md"
            dst = DST_ROOT / proj / f"{page}.html"
            if src.exists():
                convert_file(src, dst, proj, page)
            else:
                print(f"  ⚠  MISSING: {src}")
    total = sum(len(p) for _, p in NAV) + 1
    print(f"\n  Done — {total} files written to {DST_ROOT}\n")


if __name__ == "__main__":
    main()
