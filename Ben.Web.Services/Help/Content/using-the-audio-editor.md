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
- **Scan for EVP:** scans at the sensitivity you last chose. The arrow beside it picks Low, Medium
  or High, or opens the fine-tuning.
- **EVP Markers:** the markers you kept and the candidates waiting for you.
- **Zoom out, fit, zoom in.**

Everything else is in three menus at the right of the toolbar:

- **View:** what's drawn, such as the timeline, the spectrogram's labels, the mel scale, the voice
  band and silence shading.
- **Listen:** the playback speed, and the listening tools.
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

## The spectrogram

**Spectrogram** draws the recording's frequencies over time, under the waveform. It's the most
useful tool for finding a voice: speech has a recognizable shape, and once you've seen a few, you'll
spot them in the hiss much faster than you can hear them.

![The spectrogram, with the voice band marked](/help/media/using-the-audio-editor/spectrogram.png)
*The same recording shown as frequencies, using the mel scale and the Viridis colors. Most of this
recording's energy is low; a voice shows up as brighter marks in the band from a few hundred hertz
upward.*

Its settings are in the **Display** tab:

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

Edits work on recordings up to about half an hour long. For anything longer, you'll see a message
instead, because the whole file has to be held in memory while it's edited. Cut out the part you
want first — a clip saved from a region can be edited like any other file. The EVP scan has no
such limit; long recordings are exactly what it's for.

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
