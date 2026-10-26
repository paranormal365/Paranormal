# Audio Editor changes

This file is PUBLIC. It is rendered anonymously at `/changes`, so every line has to be safe for a
stranger to read.

**What goes in:** what changed in the audio player and editor — listening to a recording, finding
and keeping the part that matters — as somebody using it would describe it.

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
kept in `AudioEditorRelease.Version`, beside the editor's code in the website's library. A test fails when the two disagree.

**Unreleased:** what is merged but not yet released goes under one `## Unreleased` heading at the
top. Only SuperAdmins see it on `/changes`. At release that heading becomes the next number, in the same commit that sets the version.

**Administration** (Ben, 10/14/2026): a change to the administration screens, or to the concepts
and ideas behind running the site, goes under a `### Administration` sub-heading inside its version,
after the lines everybody reads. Only SuperAdmins see those lines. A version with nothing else in
it is hidden from everybody else, but still takes its number. No other sub-heading is read.

**Tracked from 10/14/2026.** 1.0.0 is the editor as it stood then, summarized.

## Unreleased

- Recordings made on the phone (.m4a) can be scanned for EVPs and clipped, the same as MP3 and WAV.
- When the site cannot read a phone recording, the scan says so instead of saying the recording is
  broken.
- Saving a clip counts toward a free account's storage. If the clip would go past it, you are told
  before anything is saved.

## 1.0.0 · 2026-10-02

- Every audio file on the site has a player, and **Open Full View** opens the editor for listening
  closely.
- Nothing done in the editor changes the recording: every edit makes a new file, and each saved clip
  shows which part of the original it came from.
- Listening tools — an equalizer, high-pass and low-pass filters, a compressor and a noise gate —
  change only what you hear, and are remembered for each recording.
- Selecting a stretch, the spectrogram, silence detection and exploring a region.
- Scanning for EVP, and a smarter detector that explains how it works. Each find can be ruled on,
  and the rulings are kept.
- The case mixer plays a case's recordings together.
