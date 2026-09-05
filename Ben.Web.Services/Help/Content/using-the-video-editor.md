---
title: Using the Video Editor
summary: Turning raw footage into something worth watching — import, trim, title, and export.
section: Your Account
audience: SignedIn
order: 57
---

The video editor puts a full editing suite in your browser. Nothing is uploaded while you work:
your footage is downloaded to your own machine, edited there, and rendered there. Only the finished
video goes back to the server, and only if you ask it to.

![The video editor, open and empty](/help/media/using-the-video-editor/editor-overview.png)
*The editor: preview at the top, timeline below, media and properties in the panel on the right.*

## Where to find it

- **My Videos** — your own workspace, for anything not tied to a case.
- **A case's Video tab** — the same editor, opened against that case, for work your group will
  attach to it.

There is also a standalone editor that runs on its own, at
[ishaunted.com/editors/video](https://ishaunted.com/editors/video/). It is the same editor with the
same features — multiple tracks, titles, transitions, effects, saved projects and all — and it
keeps your footage even further from the server: media goes straight from the site to your machine
without passing through the web server at all. The one difference worth knowing is what happens
before you sign in: see [Working signed out](#working-signed-out) below.

## Start the engine

The first thing to press is **Initialize**. The editor does its video work with a copy of ffmpeg
that runs inside the browser, and that engine is a large download — so it is not fetched until you
say so. Opening a project to look at it costs nothing; editing needs the engine.

The chip beside the toolbar tells you where it is up to: *Not loaded*, then *Loading ffmpeg…*, then
**Ready**. Import, preview and export all wait for Ready.

Once it says Ready the **Initialize** button goes away — it has done its one job, and the space goes
to the tools you will actually use. It comes back if the engine ever fails, which is the one case
where pressing it again is the right move.

## Bringing in footage

There are two ways in.

**From your machine** — the **Open** button takes files straight off your disk. Nothing about them
touches the server.

**From the server** — the **Server** tab lists media you already have on the site: your own
uploads, and anything shared with you through a group or a case.

![The Server tab listing media held on the site](/help/media/using-the-video-editor/media-library.png)
*Everything you can reach, with its size. Clicking a file downloads it to this browser.*

### Finding the right file

The list above the files narrows what the tab shows:

| | What you get |
|---|---|
| **All media** | Everything you can reach — your uploads, and anything shared with you. |
| **My files** | Only what you uploaded yourself. |
| **By case** | One case's media. A second list appears; pick the case. |

Picking a case that had more than one visit adds a third list, so you can narrow to a single
night's material rather than everything the case has ever produced. Leave it on **The whole case**
to see all of it.

The scope only ever narrows what you could already see. It is not a way of reaching somebody
else's footage, and choosing a case you have no part in shows nothing rather than refusing.

Bringing a file over is deliberately **two clicks**:

1. **Click the file once.** It downloads to your browser and is kept there. A tick appears on the
   card. Nothing has been added to your video yet — this step is just fetching, and it does not
   need the engine.
2. **Click it again.** Now it is decoded and placed on the timeline.

The split exists because downloading a large file and committing it to your edit are different
decisions. You can pull several files down while you think, and place them later.

![The import summary, listing what came in](/help/media/using-the-video-editor/import-complete.png)
*Each import reports what it found — length and frame size — and waits for you to dismiss it.*

### Landing on an occupied spot

If a clip would land where something already sits, the editor asks rather than guessing:

![Choosing between inserting and overwriting](/help/media/using-the-video-editor/insert-or-overwrite.png)
*Insert pushes what is already there along. Overwrite replaces the part that overlaps.*

**Insert (Make Room)** is the safe answer: nothing is lost, everything after the insertion point
shifts later. **Overwrite** is what you want when you are deliberately replacing a section.

## The timeline

![Two clips on the timeline](/help/media/using-the-video-editor/timeline-two-clips.png)
*Two camera angles, one after the other. The pink line is the playhead.*

- **Tracks** stack: video on top of video, audio below. A clip on a higher track covers the one
  beneath it at the same moment.
- **Drag a clip** to move it. Drag its **edges** to trim — the clip's ends move, the file is
  untouched.
- **Fit** sizes the whole project to the width of the window; the zoom slider beside it takes you
  in closer for frame-accurate work.
- **TC** switches the ruler between timecode and frame numbers.
- **Ripple** decides what happens to everything downstream when you trim or delete: with it on, the
  gap closes and later clips move back; with it off, the gap stays.

### Keyboard

Most of the timeline can be driven from the keyboard, and the full list is in the editor itself:
**File → Keyboard shortcuts**, or press <kbd>?</kbd>.

The ones worth knowing straight away:

| Key | Does |
|---|---|
| <kbd>Space</kbd> | Play or pause |
| <kbd>←</kbd> / <kbd>→</kbd> | Step one frame back or forward |
| <kbd>Home</kbd> / <kbd>End</kbd> | Jump to the start or the end |
| <kbd>S</kbd> | Split the selected clip at the playhead |
| <kbd>M</kbd> | Drop a marker at the playhead |
| <kbd>Delete</kbd> | Remove what is selected |
| <kbd>Ctrl</kbd>/<kbd>⌘</kbd> + <kbd>Z</kbd> | Undo (<kbd>Shift</kbd> as well to redo) |
| <kbd>Escape</kbd> | Clear the selection |

On a Mac, <kbd>⌘</kbd> works everywhere <kbd>Ctrl</kbd> does.

With a title, callout or piece of clip art selected, the arrow keys nudge it around the frame
instead of stepping frames.

### Marking a moment

**Marker** drops a labelled point at the playhead. Markers are for you and anyone reviewing with
you — a way to say "here" without cutting anything. They travel with the project.

## Working on a clip

Select a clip and the panel's **Properties** tab describes it.

![A clip's properties](/help/media/using-the-video-editor/clip-properties.png)
*Trim, speed, volume, split and delete, all for the selected clip.*

- **Apply Trim** sets exactly where the clip starts and ends, when dragging its edge is not precise
  enough.
- **Apply Speed** slows a moment down or runs a long stretch faster.
- **Apply Volume** sets the clip's level; audio clips also carry a draggable volume envelope on the
  timeline itself, for fading within a single clip.
- **Split** cuts the clip in two at the playhead.
- **Link Nearby Audio** ties a separately-recorded sound file to the picture it belongs with, so
  moving one moves the other.

## Titles and callouts

**+ Text** adds a title. It gets its own chip on the timeline, so its timing is edited exactly like
a clip's — drag it to move it, drag its edges to change how long it shows.

![A text overlay on the timeline](/help/media/using-the-video-editor/text-overlay.png)
*A title is a clip like any other: it starts and ends where you put it.*

**Callout** adds a shape — rectangle, ellipse, arrow — for pointing at something in the frame.
Callouts can be moved, resized and rotated, and their movement can be animated over time.

![A callout on the timeline](/help/media/using-the-video-editor/callout.png)
*Callouts are for drawing attention to a spot in the picture.*

Both take colour, font and border settings from the properties panel, and both can move across the
frame while they are on screen.

## Preview and export

**Preview** renders the real thing at full quality in a separate window, so you can check the
finished result before committing to it. Meanwhile the editor keeps its own rough preview up to
date in the background as you work — which is why the status chip sometimes says it is busy shortly
after an edit.

![The Render and Export dialog](/help/media/using-the-video-editor/export-dialog.png)
*Presets for the common cases, and every setting underneath them if you want it.*

**Export** renders the final video. Start from a preset — Web HD, High Quality, 720p, Mobile or
WebM — and adjust only what you care about: format, codec, quality, resolution and frame rate.
**Export Now** renders immediately; **Add to Queue** lines it up so you can keep working.

You choose where the result goes:

- **To your machine** — the file is saved locally and never leaves it.
- **To the server** — the finished video is uploaded and becomes an ordinary file in your media
  library, ready to attach to a case or publish.

Rendering is real work. A short project finishes quickly; a long one with overlays takes minutes,
and both are faster with the native helper below.

## The native helper (Sidecar)

Everything above runs inside the browser's sandbox, which is safe but slow: the browser will not
use your machine's video hardware, and it works with one hand tied behind its back on long
projects.

The **Sidecar** is a small application you install on your own computer to lift that limit. When it
is running, the editor hands the heavy work — decoding, rendering, exporting — to it instead of
doing it in the browser tab. The result is the same video, produced considerably faster, and long
projects stop straining the browser's memory.

![The native acceleration panel](/help/media/using-the-video-editor/sidecar-panel.png)
*The panel behind the toolbar chip: whether a helper is installed, and whether this browser is
paired with it.*

**It is entirely optional.** With no Sidecar installed, everything still works — the editor uses
the in-browser engine, exactly as it does now.

### Installing and pairing

1. **Install it** on the computer you edit on — download it from
   [the sidecar downloads page](/editors/video/downloads/), which carries the install steps for Windows
   and Mac. It runs quietly in the background and starts with your machine.
2. **Pair this browser with it.** The Sidecar shows a **six-digit code**; type that into the
   editor's pairing panel. That is all.

The chip on the toolbar tells you the state at a glance: *No sidecar* (none found), *Pair sidecar*
(found, not yet paired) or *Native* (paired and in use).

**Why a code at all.** The Sidecar listens only on your own machine, and it refuses to take work
from a page until that page has proved it is one you are actually using. The code is how you say
so. It is single-use and expires after ten minutes, and pairing one browser does not unpair
another.

**Pairing is per browser and per site address.** Editing from a different browser, or reaching the
site by a different address, means pairing once more.

## Saving your work

**Save to Server** stores the project — the arrangement, the trims, the titles, all of it — under
your account, so you can pick it up on another machine. **Saved Projects** lists what you have.

A project is not the video. It is the recipe: which clips, in what order, cut where. The video
itself only exists once you export.

## Working signed out

The standalone editor can be used without signing in, and quite a lot works that way: open files
from your machine, edit them, render the result and save it back to your machine. All of that is
local.

Signing in is what connects it to the site. Until you do:

- the **Server** tab has nothing to list, because listing your media is a request the site must be
  able to attribute to you;
- **Save to Server** and publishing a finished render have nowhere to go, for the same reason.

So: edit locally as much as you like, sign in when you want your own footage or want to keep the
result somewhere other than this computer.

## When something looks wrong

- **The editor says "Not loaded" and nothing imports.** Press **Initialize** and wait for *Ready*.
- **Export is greyed out.** It needs both a Ready engine and at least one clip. If the chip says it
  is busy, a background render is running; it will free up when that finishes.
- **A file downloaded but did not appear.** Downloading and placing are two separate clicks — click
  the card a second time.
- **Nothing is listed on the Server tab.** In the standalone editor, sign in first. On the site,
  the tab lists what you own or have been given access to; a file someone else has not shared with
  you will not be there.
