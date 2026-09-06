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
*The editor: the picture at the top, the timeline below it, and media and properties in the panel
on the right.*

The three parts are yours to size. Drag the seam above the timeline to give the timeline more room
or hand it back to the picture; drag the panel's left edge to widen it; collapse the panel with the
button in its header and bring it back from the toolbar. The editor remembers all of it for next
time.

## Where to find it

- **My Videos** — your own workspace, for anything not tied to a case.
- **A case's Video tab** — the same editor, opened against that case, for work your group will
  attach to it. Projects saved there belong to the case: everybody who can open the case sees the
  whole list and can open any of them. Only the person who saved a project can overwrite, publish
  or delete it, so the list says who made each one.

There is also a standalone editor that runs on its own, at
[ishaunted.com/editors/video](https://ishaunted.com/editors/video/). It is the same editor with the
same features — multiple tracks, titles, transitions, effects, saved projects and all — and it
keeps your footage even further from the server: media goes straight from the site to your machine
without passing through the web server at all. Sign in there, from the button in its toolbar, and
it lists your uploaded media, saves projects to the server and publishes finished renders just as
the site does. The one difference worth knowing is what happens before you sign in: see
[Working signed out](#working-signed-out) below.

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

Everything you bring in lands in the **Media** panel first. That is your material for this project,
and it is not the same thing as your edit: a clip sits there whether or not it is on the timeline,
and the card tells you which — *on timeline*, or *on timeline ×2* if you have used it twice.

Press **+** on a card to place it at the playhead. The same source can be placed as many times as
you like, and trimming one placement leaves the others alone. **Remove from media** takes the card
away; anything already on the timeline stays exactly as it is.

The one shortcut: the first thing you bring into an empty project is placed for you, because nobody
picks a video and then wants to look at an empty timeline.

There are two ways in.

**From your machine** — the **Open** button takes files straight off your disk. Nothing about them
touches the server.

**From the server** — the **Server** tab lists media you already have on the site: your own
uploads, and anything shared with you through a group or a case.

![The Server tab listing media held on the site](/help/media/using-the-video-editor/media-library.png)
*Everything you can reach, with its size. Clicking a file downloads it to this browser.*

Importing shows a row per file, and each row can be cancelled while it is still working.

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

The list is fetched once. If you upload something elsewhere while the editor is open, press the
**refresh** button beside the scope lists to fetch it again. A render you publish from here
refreshes the list on its own.

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
| <kbd>Ctrl</kbd>/<kbd>⌘</kbd> + <kbd>D</kbd> | Duplicate what is selected, clips and annotations alike |
| <kbd>Delete</kbd> | Remove what is selected |
| <kbd>Ctrl</kbd>/<kbd>⌘</kbd> + <kbd>Z</kbd> | Undo (<kbd>Shift</kbd> as well to redo) |
| <kbd>Escape</kbd> | Clear the selection |

On a Mac, <kbd>⌘</kbd> works everywhere <kbd>Ctrl</kbd> does.

With a title, callout or piece of clip art selected, the arrow keys nudge it around the frame
instead of stepping frames.

### Marking a moment

**Marker** drops a labelled point at the playhead. Markers are for you and anyone reviewing with
you — a way to say "here" without cutting anything. They travel with the project.

### Saving a single frame

**Save Frame** writes the picture under the playhead to your machine as a PNG. It comes from the
clip's own footage at full resolution rather than from the preview, so it is as sharp as the source
allows — and it does not carry your titles or callouts, which is usually what you want from a frame
you are going to share as evidence.

## Working on a clip

Select a clip and the panel's **Properties** tab describes it.

![A clip's properties](/help/media/using-the-video-editor/clip-properties.png)
*Trim, speed, volume, split and delete, all for the selected clip.*

- **Apply Trim** sets exactly where the clip starts and ends, when dragging its edge is not precise
  enough.
- **Apply Speed** slows a moment down or runs a long stretch faster.
- **Apply Volume** sets the clip's level; audio clips also carry a draggable volume envelope on the
  timeline itself, for fading within a single clip.
- **Set In** and **Set Out** trim the clip to where the playhead is, so you can trim to what you
  are actually watching instead of typing a timecode. Both are available while the playhead is over
  the clip.
- **Split** cuts the clip in two at the playhead.
- **Link Nearby Audio** ties a separately-recorded sound file to the picture it belongs with, so
  moving one moves the other.
- **Mute** on the right-click menu silences a clip's own sound without changing its level.

### Hiding part of the picture

A clip often has one thing in it that cannot go out: a face, a number plate, the house number by
the door. **Hide an area** covers a rectangle of the picture with a blur or a mosaic, so the rest
of the clip can still be used.

Areas are drawn on the clip, so they travel with it if you move or trim it, and the preview shows
each one where the finished video will obscure it. The marker in the preview has a dashed edge to
say it is an editing marker: the browser's blur is not the one the render uses, and the render's is
stronger.

An area is measured as a share of the frame rather than in pixels, so it still covers the same
thing if you export at a different resolution. If an area is somehow too small to render, the
export says so in its warnings rather than quietly leaving it out — check the picture before
sharing it.

### Putting a clip somewhere other than the whole frame

By default a clip fills the frame. **Place this clip** lets you say otherwise:

- **Two cameras at once.** Put a clip on a second video track and place it in a corner, or place
  both at half width for a side-by-side.
- **Footage shot sideways.** **Turn upright** rotates a phone clip a quarter turn.
- **Something at the edge you do not want.** **Cut off the edges** trims a share off any side,
  which is how a recorder's timestamp bar or the neighbour's window comes out of shot. Cutting
  removes it from the file completely, unlike hiding an area, which covers it.

The picture keeps its own proportions inside whatever box you give it, so placing a clip never
stretches it.

## Sound

Audio clips sit on their own tracks below the picture and behave like everything else on the
timeline: drag one to move it, drag its edges to trim it, and the shape drawn on the chip is the
part of the recording that clip actually plays.

Select a sound and the **Properties** tab offers:

- **Volume**, and a draggable envelope on the chip itself for fading within a single clip.
- **Left** and **Right** separately, for a recording where one channel is hotter than the other.
- **Fade in** and **fade out**, limited to half the clip.
- **Mute this clip**, which silences it without losing the level it is set to.

### Cleaning up a recording

A recording made in a house at two in the morning is mostly room tone, fridge hum and the
recorder's own noise floor.

- **Reduce hiss** lifts a voice out of that. It goes further as you turn it up, and past about
  three-quarters it starts to make speech sound watery — so turn it up until the noise stops
  bothering you and no further.
- **Even out the level** brings the clip to a common loudness. Worth switching on for every clip in
  a reel cut from several recordings, so the volume does not need changing between them.

Both are applied when the video is rendered, not to the file you imported, so nothing is lost and
you can change your mind.

### Music under a voice

Music and room tone are usually set at a level chosen for the stretches with nobody talking, and
the moment a voice comes in they are too loud.

Open an audio track's menu and choose **Duck others under this**. Everything else — including the
picture's own sound — drops in level whenever that track is playing and returns when it stops. It
is the alternative to drawing a volume envelope around every line by hand and redrawing it whenever
the timing moves.

### Separating a clip's own sound

Right-click a video clip and choose **Separate Audio** to put its sound on its own track, where it
can be trimmed and moved independently. The new clip carries the trim, the speed and the level the
picture had, so it starts out lined up exactly as it was.

## Layers above the picture

The timeline can hold more than one video track. A clip on a track above the first plays over the
one beneath it for as long as it runs, and everything on the timeline — the gaps included — keeps
its place in the finished file. If you leave a gap between two clips, the export holds on black for
exactly that long, the same as the timeline shows.

Titles, callouts and clip art stack in the order you added them, whatever kind each one is: the
newest sits on top, and that is how it renders.

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
frame while they are on screen. Every change is made as you make it and every one can be undone,
including the words themselves — there is no separate Apply step.

A title runs on one line unless you tell it not to. **Wrap long lines** breaks it at a width you
choose, which is easier than typing the breaks yourself and survives a change of font size.

Right-click any clip, title, callout or piece of artwork and choose **Duplicate**, or press
<kbd>Ctrl</kbd>/<kbd>⌘</kbd> + <kbd>D</kbd>, to make another one just like it. The copy lands just
after the original and is entirely separate, so editing one leaves the other alone — which is how
you make three matching callouts without building each from scratch.

## Preview and export

**Preview** renders the real thing at full quality in a separate window, so you can check the
finished result before committing to it. It can be stopped at any point, and if it stops reporting
progress the window says so and offers to restart the video engine.

Meanwhile the editor keeps its own rough preview up to date in the background as you work — which
is why the status chip sometimes says it is busy shortly after an edit. That preview keeps your
place: it carries on from where you were rather than jumping back to the start after every change,
and it plays your audio tracks, so you can hear how the music sits against the picture while you
are still editing.

### If the engine stops

The video engine runs inside the browser and can occasionally stop — most often on a very large
file. When it does, the status chip says so and a **Restart engine** button appears beside it. Your
project is untouched; only the step that was in progress is lost. The editor usually restarts it
for you.

If the message says the file is more than the browser can hold, restarting will not help: that is a
limit of the browser itself. Use a shorter selection, a smaller export resolution, or the native
helper described below, which does not have the limit.

![The Render and Export dialog](/help/media/using-the-video-editor/export-dialog.png)
*Presets for the common cases, and every setting underneath them if you want it.*

**Export** renders the final video. Start from a preset — Web HD, High Quality, 720p, Mobile or
WebM — and adjust only what you care about: format, codec, quality, resolution and frame rate.
**Export Now** renders immediately; **Add to Queue** lines it up so you can keep working, including
while another export is already running.

A few of those settings are worth knowing:

- **Source resolution** keeps the size of your first clip instead of resizing anything. Choose it
  when you are cutting 4K or phone footage and want the export to be what the camera recorded.
- **Frame rate** defaults to 30. Lower it only if you need a smaller file.
- The codecs on offer change with the format, because not every codec fits in every container. Pick
  the format first.

If something on the timeline could not be included — a clip whose media is not loaded, a piece of
artwork that could not be read — the export still finishes and tells you what it left out. Read
that list before you share the file.

You choose where the result goes:

- **To your machine** — the file is saved locally and never leaves it.
- **To the server** — the finished video is uploaded and becomes an ordinary file in your media
  library, ready to attach to a case or publish. Publishing a case project's render also puts it on
  that case's Files tab, so the rest of the group can find it without going through the editor.
  Publishing again replaces it rather than leaving both.

Closing that question without answering it asks whether you meant to throw the render away. Nothing
is deleted unless you say so.

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

The editor saves the project for you, a couple of seconds after you stop editing. You do not have
to think about it, and the name at the top of the toolbar carries a `*` while there is anything not
yet written. If you close the tab with unsaved work, or with a render still going, the browser asks
before letting you.

**Save to Server** stores the project — the arrangement, the trims, the titles, all of it — under
your account, so you can pick it up on another machine. **Saved Projects** lists what you have.

A project is not the video. It is the recipe: which clips, in what order, cut where. The video
itself only exists once you export.

### Where your footage lives

Everything you import is copied into the browser's own storage, which is what lets you reopen a
project and find the clips still there. The media panel shows how much of that storage is in use.

When the editor starts it clears out any footage no project is using any more, so deleting a
project eventually gives the space back. It only does this once it can see the full list of
projects, so nothing is removed on a guess.

### Opening a project somewhere else

A clip you brought over from the **Server** tab remembers which file it came from, so opening that
project on another machine, or in another browser, fetches the footage back on its own. Small
downloads just happen. Anything larger than about 50 MB asks first, and **Later** is a real answer:
the project still opens, and the clips wait.

Two things it will not do. A clip you imported straight off your own machine cannot be fetched
back, because that file only exists where you put it. And if the file on the server has been
replaced since you saved the project, the clip stays missing rather than being quietly relinked to
different footage — editing against the wrong material is worse than a clip that says it has none.

For either of those, right-click the clip and choose **Replace Media…** to point it at the file
yourself. The replacement is kept, so it is still there the next time you open the project.

A clip whose footage is missing stays on the timeline with all its trims, titles and edits intact,
and is left out of any render until its file is back.

## Working signed out

The standalone editor can be used without signing in, and quite a lot works that way: open files
from your machine, edit them, render the result and save it back to your machine. All of that is
local.

Signing in is what connects it to the site. The button is in the toolbar, at the right-hand end.
Until you use it:

- the **Server** tab says so and offers the same button, rather than showing an empty list — an
  empty list would mean you had uploaded nothing, which is a different thing;
- **Save to Server** is not offered, because it has nowhere to go;
- after an export, the destination prompt still shows the server option, greyed out, so you can
  see it exists and what it needs.

So: edit locally as much as you like, sign in when you want your own footage or want to keep the
result somewhere other than this computer. If your sign-in expires part-way through, the editor
says so and keeps the render — signing in again and uploading once more is the whole fix.

## When something looks wrong

- **The editor says "Not loaded" and nothing imports.** Press **Initialize** and wait for *Ready*.
- **Export is greyed out.** It needs both a Ready engine and at least one clip. If the chip says it
  is busy, a background render is running; it will free up when that finishes.
- **A file downloaded but did not appear.** Downloading and placing are two separate clicks — click
  the card a second time.
- **Nothing is listed on the Server tab.** In the standalone editor, sign in first. On the site,
  the tab lists what you own or have been given access to; a file someone else has not shared with
  you will not be there.
