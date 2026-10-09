---
title: Using the Video Editor
summary: Turning raw footage into something worth watching — import, trim, title, and export.
section: Your Account
audience: SignedIn
order: 57
---

The video editor gives you a full editing suite in your browser. Nothing is uploaded while you
work: your footage is downloaded to your own computer, then edited and rendered there. Only the
finished video goes back to the server, and only if you choose to send it.

![The video editor, open and empty](/help/media/using-the-video-editor/editor-overview.png)
*The editor: the picture at the top, the timeline below it, and the media and properties panel on
the right.*

You can resize all three parts. Drag the divider above the timeline to give the timeline more or
less room; drag the panel's left edge to widen it; collapse the panel with the button in its
header, and bring it back from the toolbar. The editor remembers your layout for next time.

## Where to find it

- **My Videos** — your own workspace, for anything not tied to a case.
- **A case's Video tab** — the same editor, opened for that case, for work your group will attach
  to it. Projects saved there belong to the case: everyone who can open the case sees the full list
  and can open any project in it. Only the person who saved a project can overwrite, publish or
  delete it, so the list shows who made each one.

There's also a standalone version of the editor at
[ishaunted.com/editors/video](https://ishaunted.com/editors/video/). It's the same editor with the
same features — multiple tracks, titles, transitions, effects, saved projects and more — and it
keeps your footage even further from the server: media goes straight from the site to your
computer without passing through the web server at all. Sign in there with the button in its
toolbar, and it lists your uploaded media, saves projects to the server and publishes finished
videos just like the site does. The main difference is what happens before you sign in: see
[Working signed out](#working-signed-out) below.

The **Standalone editor** link on the My Videos page takes you there without signing in again. The
link carries a one-time pass that's valid for one minute and never leaves your browser's address
bar, and the editor uses it up as it opens. If the project you had open was saved to the server, it
opens there too. Nothing else is carried over: the standalone editor gets its own sign-in rather
than a copy of the one you're using here.

## Start the engine

The first thing to press is **Initialize**. The editor does its video work with a copy of ffmpeg
that runs inside your browser. That engine is a large download, so it isn't fetched until you ask
for it. You can open a project just to look at it without the engine, but editing needs it.

The chip next to the toolbar shows its progress: *Not loaded*, then *Loading ffmpeg…*, then
**Ready**. Importing, previewing and exporting all wait until it's Ready.

Once it's Ready, the **Initialize** button disappears to make room for the tools you'll actually
use. It comes back only if the engine fails — and that's the one time you should press it again.

## Bringing in footage

Everything you bring in goes into the **Media** panel first. That's your source material for this
project, which isn't the same thing as your edit. A clip stays there whether or not it's on the
timeline, and its card tells you which: *on timeline*, or *on timeline ×2* if you've used it twice.
A card that says **media missing** means its file is no longer in this browser's storage; see
*Where your footage lives*.

Press **+** on a card to place it at the playhead. You can place the same source as many times as
you like, and trimming one copy doesn't affect the others. **Remove from media** removes the card;
anything already on the timeline stays exactly as it is.

One shortcut: the first thing you bring into an empty project is placed on the timeline for you,
since that's almost always what you want.

There are two ways to bring footage in.

**From your machine** — the **Open** button picks files straight from your disk. They never touch
the server.

**From the server** — the **Server** tab lists media you already have on the site: your own
uploads, and anything shared with you through a group or a case.

![The Server tab listing media held on the site](/help/media/using-the-video-editor/media-library.png)
*Everything you have access to, with its size. Clicking a file downloads it to this browser.*

Importing shows one row per file, and you can cancel any row while it's still working.

### Finding the right file

The list above the files narrows down what the tab shows:

| | What you get |
|---|---|
| **All media** | Everything you have access to — your uploads, and anything shared with you. |
| **My files** | Only what you uploaded yourself. |
| **By case** | One case's media. A second list appears so you can pick the case. |

Each row shows **whose file it is**, and which case it belongs to, if any. Two people can easily
upload recordings with the same name, and **All media** shows everyone's files together.

If you pick a case that had more than one visit, a third list appears so you can narrow it down to
a single night's footage instead of everything from the case. Leave it on **The whole case** to see
it all.

These filters only narrow what you could already see. They can't give you access to someone else's
footage, and choosing a case you're not part of simply shows nothing.

The list is loaded once. If you upload something somewhere else while the editor is open, press the
**refresh** button next to the filter lists to load it again. A video you publish from the editor
refreshes the list automatically.

Bringing a file over takes **two clicks**, on purpose:

1. **Click the file once.** It downloads to your browser and is stored there. The card then says
   **Click to add**. Nothing has been added to your video yet — this step only downloads the file,
   and it doesn't need the engine.
2. **Click it again.** Now it's decoded, put in the matching bin — Video, Audio or Images — and
   placed on the timeline. This step does need the engine, so start it first.

Downloading a large file and adding it to your edit are separate decisions. This way you can
download several files while you think, and place them later.

If audio tracks are turned off in your editor, an audio file still goes into the **Audio** bin, and
the import row explains why it wasn't placed. It's there whenever you want it.

![The import summary, listing what came in](/help/media/using-the-video-editor/import-complete.png)
*Each import reports what it found — length and frame size — and stays until you dismiss it.*

### How much footage the editor can take

The editor keeps your footage in the browser's own storage, and does all its work on your own
computer. Both have limits, and it's worth knowing them before you plan a long edit.

Measured on a 2026 MacBook Pro in Chrome, with a few clips on the timeline:

| | What happened |
|---|---|
| Storage the browser offered | About 6 GB |
| A 284 MB clip | Imported in about 50 seconds, stored at its original size |
| A 348 MB clip | Imported in about a minute, stored at its original size |
| Rebuilding the preview after adding one | About a minute |
| Switching that timeline to **Live** | About 3 seconds |

What this means for you:

**Footage is stored at full size.** An imported file takes up as much space as the file itself, so
6 GB is roughly how much footage a project can hold before the browser runs out of room. The Media
panel shows how much you've used.

**The bigger the footage, the longer you wait.** Each edit rebuilds the rendered preview, and with
a few hundred megabytes of footage that takes about a minute. That's what **Live** is for while
you're cutting: it plays your footage directly and is ready in seconds, whatever the file size.

**For anything bigger, use the Sidecar.** The browser's video engine has its own limited memory,
and very large or very long footage is where it runs out. The
[native helper](#the-native-helper-sidecar) does the same work outside the browser, using your
computer's full memory and all of its processor cores.

If a file is simply too big to import, trim it first in any tool that can cut without re-encoding.
That gets you the part you want at a fraction of the size.

### Landing on an occupied spot

If a clip would land where something already is, the editor asks you what to do instead of
guessing:

![Choosing between inserting and overwriting](/help/media/using-the-video-editor/insert-or-overwrite.png)
*Insert pushes what's already there further along. Overwrite replaces the part that overlaps.*

**Insert (Make Room)** is the safe choice: nothing is lost, and everything after the insertion
point moves later. **Overwrite** is what you want when you're deliberately replacing a section.

## The timeline

![Two clips on the timeline](/help/media/using-the-video-editor/timeline-two-clips.png)
*Two camera angles, one after the other. The pink line is the playhead.*

- **Tracks** stack: video tracks on top of each other, audio below. A clip on a higher track covers
  the one beneath it at the same moment.
- **Drag a clip** to move it. Drag its **edges** to trim it — this moves where the clip starts and
  ends; the file itself isn't changed.
- **Fit** zooms out to show the whole project across the window; the zoom slider next to it lets
  you zoom in for frame-accurate work.
- **TC** switches the ruler between timecode and frame numbers.
- **Ripple** decides what happens to everything after a trim or delete: when it's on, the gap
  closes and later clips move back; when it's off, the gap stays.

### Keyboard

You can control most of the timeline from the keyboard. The full list is in the editor itself:
**File → Keyboard shortcuts**, or press <kbd>?</kbd>.

The ones to learn first:

| Key | Does |
|---|---|
| <kbd>Space</kbd> | Play or pause |
| <kbd>←</kbd> / <kbd>→</kbd> | Step one frame back or forward |
| <kbd>Home</kbd> / <kbd>End</kbd> | Jump to the start or the end |
| <kbd>S</kbd> | Split the selected clip at the playhead |
| <kbd>M</kbd> | Drop a marker at the playhead |
| <kbd>Ctrl</kbd>/<kbd>⌘</kbd> + <kbd>D</kbd> | Duplicate whatever is selected, clips and annotations alike |
| <kbd>Delete</kbd> | Remove whatever is selected |
| <kbd>Ctrl</kbd>/<kbd>⌘</kbd> + <kbd>Z</kbd> | Undo (add <kbd>Shift</kbd> to redo) |
| <kbd>Escape</kbd> | Clear the selection |

On a Mac, <kbd>⌘</kbd> works everywhere <kbd>Ctrl</kbd> does.

When a title, callout or piece of clip art is selected, the arrow keys nudge it around the frame
instead of stepping through frames.

### Marking a moment

**Marker** adds a labeled point at the playhead. Markers are for you and anyone reviewing with
you — a way to say "here" without cutting anything. They're saved with the project.

### Saving a single frame

**Save Frame** saves the picture at the playhead to your computer as a PNG. It's taken from the
clip's own footage at full resolution, not from the preview, so it's as sharp as the source allows.
It doesn't include your titles or callouts, which is usually what you want for a frame you're
sharing as evidence.

## Working on a clip

Select a clip, and the panel's **Properties** tab shows its settings.

![A clip's properties](/help/media/using-the-video-editor/clip-properties.png)
*Trim, speed, volume, split and delete, all for the selected clip.*

- **Apply Trim** sets exactly where the clip starts and ends, for when dragging its edge isn't
  precise enough.
- **Apply Speed** slows a moment down or speeds a long stretch up. Speed changes how much of the
  timeline the clip takes up — at double speed it becomes half as long — so the timeline always
  shows how long the finished video will really be. Speeding up a clip leaves a gap after it; close
  the gap by dragging the next clip back, or leave it and the export fills it with black.
- **Apply Volume** sets the clip's level. Audio clips also have a volume envelope you can drag on
  the timeline itself, for fading within a single clip.
- **Set In** and **Set Out** trim the clip to the playhead's position, so you can trim to what
  you're actually watching instead of typing a timecode. Both are available while the playhead is
  over the clip.
- **Split** cuts the clip in two at the playhead.
- **Link Nearby Audio** links a separately recorded sound file to the picture it goes with, so
  moving one moves the other.
- **Mute** on the right-click menu silences a clip's own sound without changing its volume setting.

### Hiding part of the picture

A clip often has one thing in it that can't be shown: a face, a license plate, the house number by
the door. **Hide an area** covers a rectangle of the picture with a blur or a mosaic, so you can
still use the rest of the clip.

Areas are drawn on the clip, so they move with it if you move or trim it, and the preview shows
each one where the finished video will cover it. In the preview, the area has a dashed outline to
show that it's only an editing marker: the browser's blur isn't the one used in the final render,
and the render's blur is stronger.

An area's size and position are measured as a proportion of the frame rather than in pixels, so it
still covers the same thing if you export at a different resolution. If an area is too small to
render, the export lists it in its warnings rather than quietly leaving it out — check the picture
before you share it.

### Putting a clip somewhere other than the whole frame

By default, a clip fills the whole frame. **Place this clip** lets you change that:

- **Two cameras at once.** Put a clip on a second video track and place it in a corner, or place
  both clips at half width to show them side by side.
- **Footage shot sideways.** **Turn upright** rotates a phone clip a quarter turn.
- **Something at the edge you don't want.** **Cut off the edges** trims a portion off any side —
  handy for removing a recorder's timestamp bar or a neighbor's window. Unlike hiding an area,
  which covers something up, cutting removes it from the video completely.

The picture keeps its own proportions inside whatever box you give it, so placing a clip never
stretches it.

## Sound

Audio clips sit on their own tracks below the picture and work like everything else on the
timeline: drag one to move it, and drag its edges to trim it. The waveform drawn on the clip shows
the part of the recording that the clip actually plays.

Select a sound clip, and the **Properties** tab offers:

- **Volume**, plus an envelope you can drag on the clip itself for fading within a single clip.
- **Left** and **Right** separately, for a recording where one channel is louder than the other.
- **Fade in** and **fade out**, limited to half the clip. Both are also on the clip's right-click
  menu on the timeline, where they set a one-second fade without opening the panel.
- **Mute this clip**, which silences it but keeps its volume setting.

### Cleaning up a recording

A recording made in a house at two in the morning is mostly room tone, refrigerator hum and the
recorder's own background hiss.

- **Reduce hiss** helps a voice stand out from all that. The higher you set it, the more it
  removes, but past about three-quarters it starts to make speech sound watery. Turn it up until
  the noise stops bothering you, and no further.
- **Even out the level** brings the clip to a standard loudness. Turn it on for every clip in a
  reel made from several recordings, so nobody has to adjust the volume between them.

Both are applied when the video is rendered, not to the file you imported, so nothing is lost and
you can change your mind.

### Music under a voice

Music and room tone are usually set at a level that suits the parts where nobody is talking, so
they're too loud the moment a voice comes in.

Open an audio track's menu and choose **Duck others under this**. Everything else — including the
video's own sound — gets quieter whenever that track is playing, and comes back up when it stops.
It saves you from drawing a volume envelope around every line by hand, and redrawing it every time
the timing changes.

### Separating a clip's own sound

Right-click a video clip and choose **Separate Audio** to put its sound on its own track, where you
can trim and move it separately. The new audio clip keeps the trim, speed and volume the video
had, so it starts out lined up exactly as before.

## Joining two clips

Where one clip meets the next, the cut is instant unless you add a transition.

- Hover over the join between two clips and a small dashed circle appears on it. Click it and pick
  a style — **Fade**, **Dissolve**, **Wipe left**, **Circle open**, **Fade through black** and a
  dozen more.
- You can also drag a style straight from the **Transitions** tab of the Assets panel onto the
  join.
- A transition overlaps the two clips: they play at the same time for the length of the
  transition, so the finished video is shorter than the two clips added together. The timeline
  shows this — the second clip moves back to meet the first.
- Drag either end of a transition on the timeline to make it longer or shorter. Right-click it to
  change the style or remove it.
- Removing a transition gives the overlapping time back to both clips.

A transition belongs to the two clips it joins. If you delete, trim or move either clip, the join
is gone, so the transition is removed too — and **Undo** brings it back along with your edit.

## Layers above the picture

The timeline can have more than one video track. A clip on a higher track plays over the one
beneath it for as long as it runs. Everything on the timeline keeps its place in the finished
video, gaps included: if you leave a gap between two clips, the export shows black for exactly that
long, just as the timeline does.

Titles, callouts and clip art are stacked in the order you added them, whatever kind each one is.
The newest is on top, and that's how it renders.

## Titles and callouts

**+ Text** adds a title. It gets its own block on the timeline, so you edit its timing just like a
clip's — drag it to move it, and drag its edges to change how long it's on screen.

![A text overlay on the timeline](/help/media/using-the-video-editor/text-overlay.png)
*A title works like any other clip: it starts and ends where you put it.*

**Callout** adds a shape — rectangle, ellipse, arrow — for pointing at something in the frame.
Callouts can be moved, resized and rotated, and their movement can be animated over time.

![A callout on the timeline](/help/media/using-the-video-editor/callout.png)
*Callouts are for drawing attention to a spot in the picture.*

Both get their color, font and border settings from the properties panel, and both can move across
the frame while they're on screen. Every change takes effect immediately and can be undone,
including changes to the text — there's no separate Apply step.

A title stays on one line unless you tell it otherwise. **Wrap long lines** breaks it at a width
you choose, which is easier than typing the line breaks yourself and still works if you change the
font size.

Right-click any clip, title, callout or piece of artwork and choose **Duplicate**, or press
<kbd>Ctrl</kbd>/<kbd>⌘</kbd> + <kbd>D</kbd>, to make an identical copy. The copy is placed just
after the original and is completely separate, so editing one doesn't change the other. It's an
easy way to make three matching callouts without building each one from scratch.

## Preview and export

**Preview** renders the real, full-quality video in its own window on the page, so you can check
the finished result before you commit to it. You can stop it at any point, and if it stops showing
progress, the window tells you and offers to restart the video engine.

Meanwhile, the editor keeps its own rough preview up to date in the background as you work — which
is why the status chip sometimes says it's busy just after an edit. That preview keeps your place:
it continues from where you were instead of jumping back to the start after every change. It also
plays your audio tracks, so you can hear how the music sits against the picture while you're still
editing.

### Live and Rendered

Above the picture you can choose between two ways of watching your timeline. Each one is good for a
different job.

**Rendered** is the background preview described above: the editor makes a small video of your
whole timeline and plays it. It shows everything — transitions, effects, titles, callouts — because
it's made the same way as the export. It takes a moment to catch up after each edit, and the longer
the timeline, the longer that takes.

**Live** plays your original footage directly, switching between files as the playhead moves across
them. A cut you've just made shows up immediately, with no waiting, which is what you want while
you're cutting. It plays only the picture and the sound: transitions appear as plain cuts, and
effects, titles and callouts aren't shown. A clip whose media isn't on this computer plays as
black, and the player tells you how many clips that affects.

Use Live for cutting, and Rendered for checking. The export always matches Rendered.

### If the engine stops

The video engine runs inside your browser and can occasionally stop — usually on a very large file.
When that happens, the status chip says so and a **Restart engine** button appears next to it. Your
project is safe; only the step that was in progress is lost. Usually the editor restarts the engine
for you.

If the message says the file is more than the browser can handle, restarting won't help: that's a
limit of the browser itself. Use a shorter selection, a smaller export resolution, or the native
helper described below, which doesn't have that limit.

![The Render and Export dialog](/help/media/using-the-video-editor/export-dialog.png)
*Presets for common uses, with every setting available underneath if you want it.*

**Export** renders the final video. Start from a preset — Web HD, High Quality, 720p, Mobile or
WebM — and change only what you care about: format, codec, quality, resolution and frame rate.
**Export Now** renders right away; **Add to Queue** lines it up so you can keep working, even while
another export is already running.

A few settings are worth knowing about:

- **Source resolution** keeps the size of your first clip instead of resizing anything. Choose it
  when you're editing 4K or phone footage and want the export to match what the camera recorded.
- **Frame rate** is 30 by default. Lower it only if you need a smaller file.
- The available codecs depend on the format, because not every codec works with every file type.
  Pick the format first.

If something on the timeline couldn't be included — a clip whose media isn't loaded, or a piece of
artwork that couldn't be read — the export still finishes and lists what it left out. Check that
list before you share the file.

You choose where the finished video goes:

- **To your machine** — the file is saved on your computer and never leaves it.
- **To the server** — the finished video is uploaded and becomes a regular file in your media
  library, ready to attach to a case or publish. Publishing a render from a case project also adds
  it to that case's Files tab, so the rest of the group can find it without opening the editor.
  Publishing again replaces the earlier version instead of keeping both.

If you close that question without choosing, you're asked whether you meant to throw the video
away. Nothing is deleted unless you say so.

Rendering is hard work for your computer. A short project finishes quickly; a long one with
overlays takes minutes. Both are faster with the native helper below.

## The native helper (Sidecar)

Everything above runs inside the browser's sandbox, which is safe but slow. It renders using one
processor core, within a memory limit set by the browser, which is why a long project can crawl.

The **Sidecar** is a small app you install on your own computer to remove both limits. While it's
running, the editor hands the heavy work — decoding, rendering, exporting — to the Sidecar instead
of doing it in the browser tab. The Sidecar can use all of your processor's cores and isn't held
back by the browser's memory limit. You get the same video, much faster.

Titles, callouts, artwork, transitions and hidden areas are still drawn in the browser even when
the Sidecar is running. Those parts are quick; it's the decoding and encoding that's slow, and
that's what the Sidecar takes over.

![The native acceleration panel](/help/media/using-the-video-editor/sidecar-panel.png)
*The panel that opens from the toolbar chip: whether a helper is installed, and whether this
browser is paired with it.*

**It's completely optional.** Without the Sidecar, everything still works — the editor simply uses
the in-browser engine.

### Installing and pairing

1. **Install it** on the computer you edit on. On Windows, it's in the **Microsoft Store** as
   *IsHaunted.com SideCar*. It's free and signed by Microsoft, so there are no warnings to click
   through. For a Mac, or for Windows without the Store, [the sidecar downloads page](/editors/video/downloads/)
   has the installers and instructions.
2. **Turn it on**, if it isn't already running. On Windows, the chip's panel has a **Turn on**
   button. When your browser asks whether to open the sidecar, allow it.
3. **Pair this browser with it.** On Windows, the first time the sidecar starts, it opens a small
   window with a **six-digit code**: press **Copy code** and paste it into the panel. If you've
   closed that window, or you're on a Mac, click **Show a pairing code** in the panel instead.
   That's it: after that, the sidecar has no window, and you turn it on and off from the chip.

The chip on the toolbar shows the status at a glance: *No sidecar* (none found), *Pair sidecar*
(found, but not paired yet) or *Native* (paired and in use).

**Why a code?** The Sidecar only accepts connections from your own computer, and it won't take work
from a web page until that page proves you're actually using it. The code is how you prove it. Each
code works once and expires after ten minutes, and pairing one browser doesn't unpair another.

**Pairing is per browser and per site address.** If you edit in a different browser, or reach the
site at a different address, you'll need to pair again.

## Saving your work

The editor saves your project automatically, a couple of seconds after you stop editing. The
project name at the top of the toolbar shows a `*` while there are unsaved changes. If you try to
close the tab with unsaved work, or while a render is still running, the browser asks you to
confirm first.

**Save to Server** stores the project — the arrangement, the trims, the titles and everything
else — in your account, so you can pick it up on another computer. **Saved Projects** lists your
saved projects.

A project isn't the video itself. It's the recipe: which clips, in what order, cut where. The video
only exists once you export it.

### Where your footage lives

Everything you import is copied into the browser's own storage, which is why your clips are still
there when you reopen a project. The media panel shows how much of that storage is in use.

When the editor starts, it clears out any footage that no project uses anymore, so deleting a
project eventually frees up the space. It only does this once it has the full list of projects, so
it never removes anything based on a guess.

### Opening a project somewhere else

A clip you brought in from the **Server** tab remembers which file it came from, so if you open the
project on another computer or in another browser, the footage is downloaded again automatically.
Small files just download. For anything larger than about 50 MB, you're asked first, and choosing
**Later** is fine: the project still opens, and those clips wait until you're ready.

There are two things it can't do. A clip you imported directly from your own computer can't be
downloaded again, because that file only exists where you put it. And if the file on the server
has been replaced since you saved the project, the clip stays missing rather than being quietly
linked to different footage — editing against the wrong footage is worse than a clip that's
clearly missing.

In either case, right-click the clip and choose **Replace Media…** to pick the file yourself. Your
choice is saved, so it's still there the next time you open the project.

A clip with missing footage stays on the timeline with all its trims, titles and edits intact,
marked with a warning triangle.

**It also stops the export**, and tells you so. When you open the export window, the first thing
you see is the reason, with the names of the affected clips. The editor won't render a video with a
hole where that footage should be, so Export Now and Add to Queue stay turned off until you
reconnect the clip or remove it. Otherwise the export would reach the missing clip partway through
and stall there.

## Working signed out

You can use the standalone editor without signing in, and quite a lot works that way: open files
from your computer, edit them, render the result and save it back to your computer. All of that
happens locally.

Signing in connects it to the site. The sign-in button is at the right-hand end of the toolbar.
Until you sign in:

- the **Server** tab tells you to sign in and offers the same button, instead of showing an empty
  list (which would wrongly suggest you hadn't uploaded anything);
- **Save to Server** isn't available, because there's nowhere to save to;
- after an export, the destination choice still shows the server option, grayed out, so you can see
  it's there and what it needs.

So you can edit locally as much as you like, and sign in when you want to use your own uploaded
footage or keep the result somewhere other than this computer. If your sign-in expires partway
through, the editor tells you and keeps the rendered video — just sign in again and upload it
again.

## When something looks wrong

- **The editor says "Not loaded" and nothing imports.** Press **Initialize** and wait for *Ready*.
- **Export is grayed out.** It needs the engine to be Ready and at least one clip on the timeline.
  If the chip says it's busy, a background render is running; Export becomes available when that
  finishes.
- **A file downloaded but did not appear.** Downloading and placing are two separate clicks — click
  the card a second time.
- **Nothing is listed on the Server tab.** In the standalone editor, sign in first. On the site,
  the tab lists files you own or have been given access to; a file someone else hasn't shared with
  you won't be there.
