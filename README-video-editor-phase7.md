# Video editor phase 7 — portable projects

Branch: `feature/video-editor-phase5-persistence` (phase 7 continues on it).
Plan: `ProjectNotes/VideoEditor-Audit-2026-09-05.md`, phase 7.
Follows `README-video-editor-phase6.md`.

Phase 5 made a project survive a reload. Phase 6 made it reach the server. This phase makes it
survive being opened somewhere else.

Five commits, `ba473ba3` to `572efc07`. Closes F14 and callouts-14.

## The problem

Help said, and had said for a long time, that a project saved to the server can be picked up on
another machine. It could not. A clip persisted its original file name and its stored extension,
and the restore read *this* browser's storage by clip id — so a project opened on a second machine,
or after the browser's storage was cleared, came back with every clip marked missing.

The offered repair was "Replace Media…", one clip at a time. It did not work either, in two
separate ways described below.

## Slice 1 — the project records where its media came from

Three fields on every timeline item: which server file the media came from, how large it was, and a
hash of it. A clip placed from the Server tab records all three.

The hash is optional by design and that is the interesting part. The browser's digest has no
streaming form, so hashing means holding the whole file in memory at once, on the single thread the
whole editor runs on — and the footage this product is built for runs to hundreds of megabytes.
It is taken under 64 MB and left null above.

`MediaFingerprint` therefore keeps three things apart that are easy to collapse into two:

| verdict | means | what it allows |
|---|---|---|
| Matches | everything recorded agrees | use it |
| Unknown | nothing was recorded to compare | use it |
| Differs | something recorded disagrees | refuse |

Accepting Unknown is deliberate. A project saved before any of this existed records neither size
nor hash, and refusing on "cannot tell" would leave every older project permanently unable to find
its own media — which is the exact state the feature exists to end.

### What the parity test could not see

The three new fields round-tripped correctly. They also round-tripped correctly with the save
mapper's line deleted, which is the finding.

The parity check compares a saved item with a restored one, so a property the fixture leaves at its
default agrees with itself whatever the mapper does. The fixture's own doc comment warned about
exactly this and nothing enforced it. `Every_fixture_gives_each_property_a_distinctive_value` now
does, and on its first run it found four exclusion keys naming `TrackItem` for properties declared
on the clip types — keys that had therefore been matching nothing at all.

## Slice 2 — the project fetches its media back

`MediaRelinkService` runs at the end of the restore. It consults the library cache first, so a file
used before in this browser comes back with no network. Otherwise it fetches the same way the
Server tab does: the browser itself where the host can say where from, through the host otherwise.

A file that does not match is not used. Nothing in the whole path can make a project worse — every
failure leaves the clip exactly as it was, which is what lets it run unattended.

`MediaRelinkPlan` decides whether to ask first: under 50 MB it just happens, over it asks, and a
clip whose size was never recorded counts as a reason to ask rather than as zero. An unbounded
download starting in silence is the outcome that rule exists to prevent.

## Slice 3 — Replace Media keeps the replacement, and clip art heals itself

Re-linking wrote the browser's session filesystem and nothing else, so the replacement lasted
exactly as long as the tab. Somebody who patiently re-linked eight clips found all eight missing
again next time. It writes to storage now, and undo puts back everything it changed rather than
only the session path. An image clip was silently not handled at all: its path was left alone while
the clip was marked as having media.

A clip keeps its recorded server file only when the replacement really is that file — otherwise a
later re-fetch would quietly overwrite the replacement with the footage it was chosen instead of.

Clip art had footage's problem and none of the repair. Its asset source is documented as the key to
re-downloading the file and nothing had ever used it: at export the layer was left out, the preview
drew nothing, and the timeline chip looked entirely normal, because clip art's missing-media flag
was never set either. Artwork that can be fetched is fetched; artwork that cannot is marked missing,
so the chip carries the warning it always had a place for.

## What the screen found that the tests did not

Two things, both after everything above was green.

- **"Replace Media…" had no file picker.** The menu item clicked `#bv-relink-input` and that
  element was on no page in the project, so it did nothing whatsoever. Its handler had been
  written, maintained and audited this week for a control that was never rendered. Nothing could
  have caught it: the id is a string on both sides, so the build is happy and the failure is
  silence. `EditorMarkupGuardTests` now fails with the message this deserved, and was checked
  against the un-fixed markup.
- **Re-fetching leaked a copy of the file on every reload.** A project saved without a media bin is
  given one on open, seeded from the timeline with a fresh id per entry, so each reload fetched
  media for a brand-new id. One reload of a one-clip project left three copies of the same file in
  storage. Bin entries never needed their own copy and now share the clip's.

## Verified on screen

Standalone host at 1440×900, storage cleared, with a stand-in media library so the path could be
exercised without writing to any real server.

- Place a clip from the Server tab, and the saved project carries the file's id, its size and its
  hash.
- Delete both the clip's copy and the library cache copy, then reload. One download, exactly two
  files in storage, the chip is no longer missing, and the footage plays.
- Do the same with a **different** file behind the same id. It is fetched, refused on size, and the
  clip stays missing with no copy written.
- Switch the library off entirely and use **Replace Media…**. The clip comes back, and it is still
  there after a reload with the library still off.

## Not done in this phase

- **Locating a file by name.** Camtasia offers to search a folder for missing media. The
  right-click replacement covers the same ground one clip at a time; a folder search needs the File
  System Access API and a directory permission prompt, which is a feature of its own.
- **Re-fetch progress per clip.** The prompt says how much and then says "Downloading…". For the
  sizes that reach the prompt a per-file bar would be worth having, and it belongs with the import
  window's own progress rather than bolted onto this.
- **A hash for large files.** Deliberate, and explained above. If the sidecar ever hashes
  out-of-process the ceiling could go.
