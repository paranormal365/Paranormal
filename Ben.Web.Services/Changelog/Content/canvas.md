# Canvas changes

This file is PUBLIC. It is rendered anonymously at `/changes`, so every line has to be safe for a
stranger to read.

**What goes in:** what changed on a case's research boards — the boards a group uses to work out
a place's history — as somebody writing one would describe it.

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
kept in `<Version>` in `Ben.Canvas.Editor/Ben.Canvas.Editor.csproj`. A test fails when the two disagree.

**Unreleased:** what is merged but not yet released goes under one `## Unreleased` heading at the
top. Only SuperAdmins see it on `/changes`. At release that heading becomes the next number, in the same commit that sets the version.

**Administration** (Ben, 10/14/2026): a change to the administration screens, or to the concepts
and ideas behind running the site, goes under a `### Administration` sub-heading inside its version,
after the lines everybody reads. Only SuperAdmins see those lines. A version with nothing else in
it is hidden from everybody else, but still takes its number. No other sub-heading is read.

**Tracked from 10/14/2026.** 1.0.0 is the boards as they stood then, summarized.

## 1.0.0 · 2026-09-18

- A case's **Research** tab holds its boards: cards, notes, pictures, files, recordings, links and
  maps, arranged and connected the way the research went.
- A board is written on your own computer and stays yours until you publish it; members see only
  what has been published.
- A board can start from a blank board, a moodboard, a research plan, a family tree or a
  presentation deck, and everything on a template can be changed.
- Pasting a link makes a card with its picture, and a block with an address in it offers to make a
  map of it.
- A board can bring in what the client wrote, and a card can open another board, with Back.
- Tables, shapes, colored blocks and groups drawn as panels.
