---
title: Using the Audio Editor
summary: Listening closely to a recording — regions, the spectrogram, EVP scanning, clips and the case mixer.
section: Your Account
audience: SignedIn
order: 53
---

Every audio file on the site comes with a player. Click the waveform to play it — often that's all
you need. When you want to listen closely — to find the three seconds that matter in a two-hour
recording, and keep them — right-click the player and choose **Open in editor**.

The editor opens as a page of its own. Its address belongs to that recording, so you can bookmark it
or send it to someone who can see the file. **Back** returns you to where you opened it from.
To listen in a bigger player without leaving the list, choose **Quick look** instead.

![The audio editor](/help/media/using-the-audio-editor/editor-overview.png)
*The editor: the toolbar above, the waveform in the middle, and its panels in tabs underneath.*

## Finding your way around

The toolbar holds what you use most:

- **Select / Scrub:** whether dragging across the waveform draws a region or moves the playhead.
- **Clear Regions:** removes the regions you drew. Markers and saved clips stay.
- **Spectrogram:** shows the recording's frequencies under the waveform.
- **Scan for EVP:** scans with the settings you last chose. The arrow beside it picks Low, Medium
  or High, or opens the fine-tuning, which is ticked when you've adjusted the settings yourself.
- **EVP Markers:** the markers you kept and the candidates waiting for you.
- **Zoom out, fit, zoom in,** and **zoom to the selection**, which makes the stretch you drew fill
  the window.
- **Loop:** plays a candidate, a marker or the selection round and round instead of stopping at its
  end. Good for hearing a faint sound several times without reaching for the mouse.
- **?** lists the keyboard shortcuts.

Everything else is in three menus at the right of the toolbar:

- **View:** what's drawn, such as the timeline, the spectrogram's labels, the mel scale, the voice
  band, silence shading and the **overview strip**: the whole recording in a thin strip under the
  waveform, with the part in view outlined, so you can drag around a long recording while zoomed in.
- **Listen:** the playback speed, the listening tools, and **Hear the original**, which plays the
  recording as it is, round every listening tool, so you can hear what the filters change (B on the
  keyboard). An "Original" tag shows while it's on.
- **Edit:** making a clip, exploring a stretch, and every change that makes a new file.

Under the waveform, the panels sit in tabs: **EVP Markers**, **Clips**, **Listen**, **Display** and
**Edit**. **Hide panels** folds them away when you want the room.

## The two things to know first

**Nothing you do here changes the recording.** Every edit creates a *new* file and leaves the
original untouched. This is deliberate: a recording is evidence, and altered evidence is worth less
than untouched evidence. Everything you create is listed in the **Clips** tab, and each one shows which
part of the original it came from.

**The listening tools change what you hear, not the file.** The equalizer, the high-pass and
low-pass filters, the compressor and the noise gate only affect the sound playing through your
speakers right now. Turn them on to hear a whisper more clearly; the recording itself isn't changed.
Edits are in their own tab and menu, and those are the ones that create a file.

Your listening-tool settings *are* remembered for each recording. If you find a filter that lifts a
voice out of the hiss, it will still be on when you come back to that file. That also means that
when you reopen a file, you aren't hearing the recording exactly as it was captured. The **Listen**
tab shows which tools are on.

**There is one playback speed.** Set it from **Listen** in the toolbar, the speed menu under the
waveform, or the **Listen** tab. All three show the same speed, and so does the window you explore
a region in. It only changes how the recording plays here. **Edit ▸ Change speed** makes a new file
at a different speed.

## Selecting a stretch

Drag across the waveform to draw a region. The **Edit** tab shows its range, and **Cut** and
**Silence selection** act on it.

You can have only one drawn region at a time, so drawing a second one replaces the first. Everything
else on the waveform stays put: markers, clips you've already saved, and any silence the detector
has shaded. None of those are selections, and an edit will never act on them.

Right-click a region to open its menu: play just that part, explore it in its own window, save it
as a file, rename it, or remove it.

## Keyboard shortcuts

Once you're working through a list of candidates, the keyboard is quicker than the mouse. The keys
work anywhere on the editor's page, except while you're typing in a box.

| Key | What it does |
|---|---|
| Space | Play or pause. With Loop on and a selection, play the selection round. |
| ← → | Back or on a second; with Shift, five. |
| [ then ] | Start a selection at the playhead, then end it there. |
| N, P | Next or previous candidate: it's highlighted in the list and played. |
| K | Keep the highlighted candidate. |
| D | Dismiss the highlighted candidate, and move on to the next. |
| Z | Zoom to the selection, or to the highlighted candidate. |
| F | Fit the whole recording. |
| L | Loop on or off. |
| B | Hear the original, without the listening tools, and back again. |
| Esc | Clear the selection, or put the volume line away. |
| ? | The list of shortcuts. |

## The spectrogram

Beside the hint above the waveform, a small meter shows what you're hearing while it plays: the
frequencies, and the level, with the loudest moment held for a second.

**Spectrogram** draws the recording's frequencies over time, under the waveform. It's the most
useful tool for finding a voice: speech has a recognizable shape, and once you've seen a few, you'll
spot them in the hiss much faster than you can hear them.

![The spectrogram, with the voice band marked](/help/media/using-the-audio-editor/spectrogram.png)
*The same recording shown as frequencies, using the mel scale and the Viridis colors. Most of this
recording's energy is low; a voice shows up as brighter marks in the band from a few hundred hertz
upward.*

The candidates and the markers you kept are marked on the spectrogram too, as tinted bands, so a
sound in the list can be found in the picture.

Its settings are in the **Display** tab, including its **height**: make it taller to see pitches
further apart. The waveform keeps its own height, and the editor remembers your choice.

- **Resolution** trades detail in time for detail in frequency. A low number gives a blurrier
  picture that shows exactly *when* something happened; a high number shows exactly *what pitch* it
  was, but blurs the timing. 512 or 1024 works well for most speech.
- **The color ramp** only changes how easy the picture is to read. Try Viridis if Jet's blues and
  reds are hard on your eyes.
- **Frequency scale** spaces the frequencies out. **Mel** compresses the high frequencies and
  spreads out the low ones, which is roughly how human hearing works; with it on, speech fills much
  more of the picture. It is also in the **View** menu.
- **Frequency range** shows only part of the range, for example 0 to 4000 Hz to look closely at a
  voice. Leave the top at 0 to see everything.
- **Brightness** and **Contrast range** help a quiet recording: raise the brightness, or narrow the
  range to hide more of the background hiss.
- **Lift high frequencies** makes the upper parts of a voice easier to see.
- **Window** changes how each slice of sound is measured. Leave it on Hann unless you know you want
  another.

**View ▸ Voice band** marks 300 to 3,400 Hz, the range the EVP scan listens in. **View ▸
Spectrogram labels** shows or hides the frequency labels.

The site remembers all of this for each recording: the spectrogram and its settings, the
timeline, the voice band, the listening tools, the playback speed, how far you were zoomed in, and
which tab was open. Open the file again next week and it looks
and sounds the way you left it. Settings are only saved for your own recordings. On someone else's
file you can set things up however you like, but the editor will tell you that it isn't saving them.

## Silence detection

**View ▸ Detect silence** shades the parts of the recording that are close to silent, so you can see where
nothing is happening and skip it. The shading isn't a selection, and no edit will act on it. If a
room's background noise is being counted as sound, change the threshold in the **Display** tab.

## Scanning for EVP

The **EVP Markers** tab scans the recording for short bursts of speech-like sound and suggests
them as candidates. It looks for energy in the voice range that stands out from the sound around
it, at three sensitivity levels:

- **Low** — only the obvious ones. Use it on a long recording to get a short list.
- **Medium** — the usual choice.
- **High** — suggests many more, and most will be knocks and rustles. Worth it when you're fairly
  sure something is there.

![The candidates a scan produced](/help/media/using-the-audio-editor/evp-candidates.png)
*Each candidate shows its score (with a bar for easy comparison), when it happens and how long it
lasts. The four round buttons play it, adjust where it starts and ends, keep it, or dismiss it.*

A scan only makes suggestions; you make the decisions. Each candidate waits for you to **keep** it
or **dismiss** it. Keeping one asks you for a label, because an unnamed marker isn't a finding
anyone can use. Dismissed candidates are remembered, so a later scan won't suggest them again, and
later scans never touch the markers you've kept.

Press ▶ on a kept marker to play it. If the marker is a single moment rather than a stretch, you
hear a couple of seconds on either side, which is usually what you need to tell a voice from a bump.

### Stood out, and voice-like

Each candidate has two readings, each out of 100:

- **Stood out**: how far the sound rose above the room around it. This is the scan's own score.
- **Voice-like**: how much the sound is shaped like a human voice, measured by a second model after
  the scan finds it. A knock or a hum reads low; a voice, or something shaped like one, reads high.

Neither one says a sound *is* a voice or an EVP. They help you decide what to listen to first. Sort
the list by **Stood out**, **Voice-like** or **Time**. Use **Hide below voice-like** to shorten the
list while you work; at 0 it hides nothing, and it only changes what you see, never what the scan
found. A candidate shows "not measured" when it was found before voice-likeness was measured, or on
a server without the model; scan again to measure it.

### Why a candidate was found

Every candidate has a **Why?** button (the ⓘ on its row). It opens its score in three parts:

- **Stood out**, up to 50 points: how far it rose above the room around it. 20 dB or more counts in
  full.
- **In the listening range**, up to 30: how much of its sound was in the range the scan listens in.
- **Length**, up to 20: speech-length sounds score highest; a click or a long stretch of talking
  scores less.

Under that are the measurements it was made from: how loud the room was, its loudest moment, how
much its level moved (speech rises and falls, a hum holds steady), and how hiss-like it was. The
score is how much a sound stood out, never how likely it is to be a voice.

**View ▸ Detector lane** shows the same thing along the whole recording, under the waveform: the
level in the listening range, the room's background, the dashed line a sound had to cross, and
shading wherever the scanner's gate opened. A shaded stretch with no candidate on it was too short
to keep. **View ▸ Level lane** shows how loud the recording is at every moment, with the silence
line when silence detection is on.

Above the waveform, a thin strip marks every candidate and kept marker across the whole recording;
stronger candidates are bolder. Click one to go to it and hear it.

### While a scan runs

A scan runs in the background, so you can keep listening and working while it goes. The button
shows how far it has got, and the waveform is dimmed where the scan hasn't reached yet, turning
solid as it goes. **Stop** ends it early; any candidates from an earlier scan stay where they were.

You can leave the editor during a scan. It carries on, and when you come back the editor picks it
up where it has got to. One recording is scanned at a time, and you can have up to three scans
waiting or running.

### Scanning several recordings at once

In the **Media Library**, press **Scan several for EVP…**, click the recordings you want (only
recordings can be chosen), then **Scan for EVP**. Choose the settings, as in Fine-tune, and start.
They are scanned one after another as one scan. Any recording that can't be scanned is listed with
the reason, and the rest go ahead.

**EVP scans** (the link at the top of the Media Library) lists your scans from the last week: each
recording, how far it has got, and what it found, with **Open in editor** on each and **Stop** while
one runs. After changing a setting in Fine-tune, **Rescan with these settings** scans the recording
again straight away.

### Adjusting the scan

**Fine-tune** opens every setting the scan uses, in four groups. Each box explains itself when you
hold the pointer over it.

- **What counts as a sound:** how far above the background a sound must rise, how far it must drop
  before it counts as over, the shortest and longest sound, how close two sounds must be to join
  into one, and how much audio to keep either side.
- **Where it listens:** the range of pitches it pays attention to. 300 to 3,400 Hz is the
  telephone range, where most speech sits. Lower the bottom for a deep voice, or lower the top to
  shut out hiss. The voice band on the spectrogram follows this range.
- **The room's background:** how many seconds either side count as "the room", and how quiet a part
  of that counts as its background. A shorter stretch keeps up with a room that changes, such as a
  furnace starting; a longer one is steadier.
- **What reaches the list:** the most candidates one scan may suggest, the lowest score worth
  showing, how much of the list goes to lower-scoring sounds when a scan finds more than fit, and
  how much a new sound must overlap one you've already kept or dismissed to count as the same.

When a scan finds more than fit, a few places go to lower-scoring sounds spread through the
recording, and each is marked **sample**. They're there on purpose: your rulings on sounds the
scanner wasn't sure of teach it the most.

**Save as preset…** keeps the settings under a name of your own, such as "Basement". Your presets
are yours alone. Pick one from the **Preset** list to use it again, or make it your default, so a
recording you haven't scanned before starts from it. A recording you have scanned starts from the
settings you last used on it.

### A smarter EVP detector

Your choices also help the scanner improve, and **How it works** on the panel's banner explains how.
Each scan saves a few measurements of every sound it finds, such as how far it rose above the
background and how much of it was in the voice range. Each keep or dismiss is saved alongside those
measurements, along with whether you played the candidate first. No audio is copied, and nothing
records who made the choice. A future version will use this to rank candidates more the way
investigators do. It will learn which sounds people tend to keep, not whether a sound is
paranormal. If you delete the recording, its measurements and choices are deleted with it.

![How the smarter detector works](/help/media/using-the-audio-editor/evp-smarter-detector.png)
*What a scan and a review save, and what they don't.*

## Making a clip

Once you've found something, you'll want to share it. There are two ways to make a clip:

- Draw a region and choose **Edit ▸ Make a clip from the selection**, or right-click the region
  and choose **Create Audio File from Region**.
- From a marker, use the scissors on its row.

Either way, you name the clip, pick a file type, and choose whether to boost the volume. **Leave the
boost on** unless you have a reason not to: an EVP is usually much quieter than everything around
it, and a clip at the recording's original level can be almost impossible to hear.

A clip can't be made public if the recording it came from is private. If you want to share the
clip, publish the original first. Otherwise, clipping would be a way around the recording's own
privacy setting, so the site tells you instead of quietly doing it.

## Editing

The **Edit** menu and the **Edit** tab make new files from the recording. Cut, Silence selection,
Normalize and Reverse run as soon as you choose them. The others open the **Edit** tab, where you
choose how much:

| Edit | What it does |
|---|---|
| Cut | Removes the selected region and joins the parts on either side |
| Silence selection | Replaces the selected region with silence, keeping the length |
| Normalize | Raises the whole file so its loudest point is just under maximum |
| Gain | Makes the whole file louder or quieter by a set amount |
| Fade | Fades the start in and the end out |
| Reverse | Plays the whole file backwards |
| Speed | Changes the speed without changing the pitch |
| Pitch | Changes the pitch without changing the speed |

Cut and Silence selection use the region you drew. The other six apply to the whole file, whatever is
selected.

### Turning parts down

**Edit ▸ Turn parts down…** puts a line across the waveform for the volume. Drag it down where
something is too loud, such as a door slam right beside a faint sound. Double-click the line to add
a point, and double-click a point to remove it. Playing follows the line as you draw it, so you hear
the result before you keep it. **Save as a new file** keeps it; the original isn't changed. The line
can only turn sound down; to bring a quiet stretch up, turn the rest down and then use Normalize.

Edits work on recordings up to about half an hour long. For anything longer, you'll see a message
instead, because the whole file has to be held in memory while it's edited. Cut out the part you
want first — a clip saved from a region can be edited like any other file. The EVP scan has no
such limit; long recordings are exactly what it's for.

## How clips and edits are saved

Clips, edits and case mixes are saved as **FLAC**. FLAC keeps every sound exactly as it was, in a
smaller file than WAV, and it plays in every current browser. After each save, the editor tells you
the size and what it would have been as WAV, and the line stays at the top of the **Clips** tab.

If you need a WAV for a program that can't open FLAC, tick **Save as WAV instead of FLAC** in the clip
window or the **Edit** tab. It sounds the same; the file is just bigger. The site never saves a
format such as MP3 that throws sound away, because the quiet sounds it drops are the ones an EVP
search is listening for.

Recordings from the phone app (.m4a) can be scanned, clipped and edited like any other.

## Exploring a region

**Edit ▸ Explore the selection** (or **Explore Region** on a region's right-click menu) opens a
single stretch on its own, loading only that audio. Everything inside is
measured from the start of the region: the position readout, any note you add, and any smaller
regions you draw. It's the best place to work on a few seconds without a two-hour waveform in the
way.

Notes you write here are attached to that part of the recording, and appear on the clip if you save
one from it.

## The case mixer

A case with several recordings has an **Audio Mixer** on its page: eight tracks, each with volume,
pan, mute and solo controls. Add clips from the case's files, drag them along their tracks to line
them up, and press Play to hear the result.

![The case mixer with clips placed](/help/media/using-the-audio-editor/case-mixer.png)
*Two copies of the same recording on the first two tracks, each shown at its real length. A clip
whose length has never been measured is drawn with a dashed outline at a placeholder width.*

Play is only a preview and doesn't change anything on the server. **Export Mix** renders the
arrangement and adds it to the case as a new file. You need permission to add files to the case to
do this, and the Mixer button only appears if you have it.

## What is stored, and where

- **Everything you make is a new file.** It appears in Saved Clips and in your own files, with its
  own visibility setting.
- **Markers, notes and the list of what you dismissed** belong to the recording, so anyone who can
  see the recording sees them too.
- **Your editor setup** belongs to the recording as well — the spectrogram, its settings, and all
  the listening tools — and is saved only if the recording is yours.
