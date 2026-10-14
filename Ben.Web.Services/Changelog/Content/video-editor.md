# Video Editor changes

This file is PUBLIC. It is rendered anonymously at `/changes`, so every line has to be safe for a
stranger to read.

**What goes in:** what changed in the video editor, on the site and at its standalone address, as
somebody editing would describe it.

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
kept in `<Version>` in `Ben.Video.Editor/Ben.Video.Editor.csproj`. A test fails when the two disagree.

**Unreleased:** what is merged but not yet released goes under one `## Unreleased` heading at the
top. Only SuperAdmins see it on `/changes`. At release that heading becomes the next number, in the same commit that sets the version.

**Administration** (Ben, 10/14/2026): a change to the administration screens, or to the concepts
and ideas behind running the site, goes under a `### Administration` sub-heading inside its version,
after the lines everybody reads. Only SuperAdmins see those lines. A version with nothing else in
it is hidden from everybody else, but still takes its number. No other sub-heading is read.

**Tracked from 10/14/2026.** 1.0.0 is the editor as it stood then, summarized.

## 1.0.0 · 2026-10-01

- A full editing suite in the browser, under **My Videos** and on a case's Video tab, and as a
  standalone editor at its own address. Footage is edited on your own computer; only the finished
  video goes back, and only if you send it.
- Several tracks, trimming, splitting and joining clips with transitions, titles and callouts,
  artwork, hidden areas and placing a clip anywhere in the frame.
- Sound on its own tracks: cleaning up a recording, music under a voice, and separating a clip's own
  sound.
- Marking a moment, saving a single frame, and keyboard shortcuts for the timeline.
- A live preview and a rendered one, and exports that go to the server or to your own computer.
- Projects save on their own as you work, and can be saved to the server and opened somewhere else.
- The Sidecar, when it is installed, takes over the slow work and uses the whole computer.
