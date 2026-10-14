# Sidecar changes

This file is PUBLIC. It is rendered anonymously at `/changes`, so every line has to be safe for a
stranger to read.

**What goes in:** what changed in the Sidecar, the small app that does the video editor's heavy
work on your own computer, as somebody who installed it would describe it.

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
kept in `<Version>` in `Ben.Video.Sidecar/Ben.Video.Sidecar.csproj` and `SidecarRelease.Version`, which a test keeps equal; the editor tells people with an older one to update. A test fails when the two disagree.

**Unreleased:** what is merged but not yet released goes under one `## Unreleased` heading at the
top. Only SuperAdmins see it on `/changes`. At release that heading becomes the next number, in the same commit that sets the version.

**Administration** (Ben, 10/14/2026): a change to the administration screens, or to the concepts
and ideas behind running the site, goes under a `### Administration` sub-heading inside its version,
after the lines everybody reads. Only SuperAdmins see those lines. A version with nothing else in
it is hidden from everybody else, but still takes its number. No other sub-heading is read.

**A version reaches people when its installers are up for both Mac and Windows**; until then the
editor does not suggest it.

## 1.1.3 · 2026-09-25

- On Windows, the Sidecar no longer opens a console window that stopped it when closed. A new
  install shows one small window with its pairing code and a **Copy code** button, and after that
  you turn it on and off from the editor.
- WebM clips no longer read as 0 seconds long.

## 1.1.2 · 2026-09-20

- On Windows, the editor can turn the Sidecar on and off.
- Exports use the best video encoder the computer has, rather than assuming one. VP9 exports come
  out at the quality asked for.
- In the Microsoft Store as *IsHaunted.com SideCar*.

## 1.1.1 · 2026-09-19

- On Windows, the Sidecar keeps running instead of stopping fifteen quiet minutes after you sign in
  to the computer.

## 1.1.0 · 2026-09-19

- The Sidecar starts when the editor looks for it and stops a while after the editor closes,
  instead of running all day.
- It no longer keeps a processor busy or grows to gigabytes of memory while it waits.
- The editor says when a newer Sidecar is available, and where to get it.

## 1.0.0

- The video editor's decoding, rendering and exporting run on your own computer's cores, outside
  the browser's limits, once the Sidecar is installed and paired with a six-digit code.
- Installers for Mac and Windows.
