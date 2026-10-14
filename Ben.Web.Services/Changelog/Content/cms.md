# CMS changes

This file is PUBLIC. It is rendered anonymously at `/changes`, so every line has to be safe for a
stranger to read.

**What goes in:** what changed in the CMS — the pages a group builds for its public site, and how
visitors see them — as somebody building one would describe it.

**What never goes in:** how a security fix worked, internal names for files, branches or servers,
anybody's name, any group or case, and anything that only means something to whoever wrote the
code.

**Shape:** one `## x.y.z` heading per version, newest first, each followed by `- ` lines. The
date the version was released can follow a middle dot — `## 2.4.1 · 2026-09-18` — and is shown when
it is there, but the version is what is tracked. Nothing else is read but the `### Administration`
sub-heading below.

**Versions** (Ben, 10/14/2026): a major change moves the first number (1.4.2 → 2.0.0); something
new that is not an overhaul moves the middle one (1.4.2 → 1.5.0); fixes and everyday updates move
the last (1.4.2 → 1.4.3). Every version is one step on from the one before it, and the number is
kept in `CmsRelease.Version`, beside the CMS's code in the website's library. A test fails when the two disagree.

**Unreleased:** what is merged but not yet released goes under one `## Unreleased` heading at the
top. Only SuperAdmins see it on `/changes`. At release that heading becomes the next number, in the same commit that sets the version.

**Administration** (Ben, 10/14/2026): a change to the administration screens, or to the concepts
and ideas behind running the site, goes under a `### Administration` sub-heading inside its version,
after the lines everybody reads. Only SuperAdmins see those lines. A version with nothing else in
it is hidden from everybody else, but still takes its number. No other sub-heading is read.

**Tracked from 10/14/2026.** 1.0.0 is the CMS as it stood then, summarized.

## 1.0.0 · 2026-10-09

- A group builds its own public site from pages, each made of sections, with a built-in walkthrough
  the first time.
- A page's **Published** switch decides whether visitors see it at all, and drafts keep edits out of
  sight until they are ready.
- Pages sit in a menu tree, and **Ordering** arranges the whole site at once. **Ideas** suggests
  pages groups like yours usually have.
- A page can be seen as a visitor will see it before it goes live.
- A page's sections can be saved as a layout and reused for the next page.
- Sections can show the group's cases and investigations, a case's photos, contact details, a file
  gallery and the member roster, with locations shown only as far as the group chooses.
- Pictures come from the group's own library, and links to the group's other pages are one click.
