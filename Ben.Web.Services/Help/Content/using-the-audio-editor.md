---
title: Using the Audio Editor
summary: Listening closely to a recording — regions, the spectrogram, EVP scanning, clips and the case mixer.
section: Your Account
audience: SignedIn
order: 53
---

Any audio file on the site comes with a player. Click the waveform to play it, and that is often
all you need. When you want to listen properly — to find the three seconds that matter in a
two-hour recording, and to keep them — right-click the player and choose **Open Full View**.

![The audio editor in full view](/help/media/using-the-audio-editor/editor-overview.png)
*The full view: the waveform across the middle, the toolbar above it, and the panels you open from
that toolbar stacked underneath.*

## The two things to know first

**Nothing you do here changes the recording.** Every edit makes a *new* file and leaves the
original exactly as it was. That is on purpose: a recording is evidence, and evidence you have
altered is worth less than evidence you have not. Everything you make is listed under Saved Clips,
each one saying which part of the original it came from.

**The listening tools change what you hear, not the file.** The equaliser, the high-pass and
low-pass filters, the compressor and the noise gate all work on the sound coming out of your
speakers this moment. Turn them on to hear a whisper more clearly; the recording is not touched.
The edits are the other panel, and those are the ones that produce a file.

How you set those tools up *is* remembered, per recording. Find the filter that lifts a voice out
of a hiss and it will still be there when you come back to that file — which also means that what
you are hearing on reopening is not the recording as it was captured. The panel shows what is on.

## Selecting a stretch

Drag across the waveform to draw a region. The edit panel shows its range, and Cut and Silence act
on it.

You get one drawn region at a time — the panel names exactly one range, so drawing a second
replaces the first. Everything else on the waveform stays where it is: markers, clips you have
already saved, and any silence the detector has shaded. Those are not selections and an edit will
never act on one.

Right-click a region for its own menu: play just that stretch, explore it in its own window, save it
as a file, rename it, or remove it.

## The spectrogram

**Show Spectrogram** draws the recording's frequencies over time above the waveform. It is the
single most useful tool for finding a voice: speech has a shape, and once you have seen a few you
will pick them out of hiss far faster than you can hear them.

![The spectrogram, with the voice band marked](/help/media/using-the-audio-editor/spectrogram.png)
*The same recording as frequencies, on the mel scale with the Viridis ramp. Most of this recording's
energy sits low; a voice shows up as brighter marks in the band from a few hundred hertz upward.*

Three controls sit beside it:

- **Resolution** trades detail in time against detail in frequency. A low number makes a smeared
  picture that shows exactly *when* something happened; a high one shows exactly *what pitch* it
  was, but blurs the timing. 512 or 1024 suits most speech.
- **The colour ramp** changes nothing but how easy it is to see. Try Viridis if Jet's blues and
  reds are hard on your eyes.
- **Mel** squashes the high frequencies and spreads out the low ones, which is roughly how hearing
  works. Speech takes up much more of the picture with it on.

Right-click the spectrogram to turn the frequency labels on and off.

The site remembers all of this for each recording — the spectrogram, its resolution, its colours,
the mel scale, the timeline, and the whole listening chain below. Open that file again next week and
it looks and sounds the way you left it. Settings are only saved for recordings that are yours; on
somebody else's file you can set things up however you like, and the editor will say plainly that it
is not keeping them.

## Silence detection

**Silence** shades the stretches where the recording is close to quiet. It is a way of seeing
where nothing is, so you can skip it — the shading is not a selection, and no edit will act on it.
Adjust the threshold if a room's noise floor is being counted as sound.

## Scanning for EVP

The **EVP Markers** panel scans the recording for short bursts of speech-shaped sound and proposes
them as candidates. It is looking for energy in the voice band that stands out from what surrounds
it, at three sensitivities:

- **Low** — only the obvious. Use it on a long recording to get a short list.
- **Medium** — the usual choice.
- **High** — proposes far more, most of which will be knocks and rustles. Worth it when you are
  fairly sure something is there.

![The candidates a scan produced](/help/media/using-the-audio-editor/evp-candidates.png)
*Each candidate with its score, when it happens and how long it lasts. The four buttons play it,
adjust where it starts and ends, keep it, or dismiss it.*

A scan proposes; it never decides. Every candidate waits for you to **keep** it — which asks for a
label, because a marker with no name is not a finding anybody can use — or **dismiss** it. What you
dismiss is remembered, so a second scan will not propose it again, and what you keep is left alone
by every later scan.

A kept marker's ▶ plays it. If the marker names a single moment rather than a stretch, you hear a
couple of seconds either side of it, which is usually what you need to tell a voice from a bump.

## Making a clip

The point of finding something is being able to hand it to somebody else. Two routes:

- Right-click a region and choose **Create Audio File from Region**.
- From a marker, use the scissors on its row.

Either way you name it, pick a file type, and choose whether to boost the volume. **Leave the boost
on** unless you have a reason not to: an EVP is usually far quieter than everything around it, and
a clip cut at the recording's own level can be almost inaudible.

A clip cannot be made public if the recording it came from is private. If you want it shared,
publish the original first — otherwise clipping would be a way around the recording's own settings,
and the site will say so rather than doing it quietly.

## Editing

The **Edit** panel makes new files from the recording:

| Edit | What it does |
|---|---|
| Cut | Removes the selected region and joins what is either side |
| Silence | Replaces the selected region with silence, keeping the length |
| Normalize | Raises the whole file so its loudest point is just under maximum |
| Gain | Makes the whole file louder or quieter by a set amount |
| Fade | Fades the start in and the end out |
| Reverse | Plays the whole file backwards |
| Speed | Changes the speed without changing the pitch |
| Pitch | Changes the pitch without changing the speed |

Cut and Silence use the region you drew. The other six are about the whole file, whatever is
selected.

Edits are limited to recordings of about half an hour. A longer one is refused with a message
rather than attempted, because the whole file has to be held in memory while it is worked on. Cut
the part you want out first — a clip saved from a region can be edited like any other file. The EVP
scan has no such limit; long recordings are what it is for.

## Exploring a region

**Explore Region** opens one stretch on its own, loading only that audio. Everything inside is
relative to the region: the position readout, any note you attach, and any sub-region you draw.
It is the right place to work on a few seconds without a two-hour waveform in the way.

Notes written here are attached to that stretch of the recording, and appear on the clip if you
save one from it.

## The case mixer

A case with several recordings has an **Audio Mixer** on its page: eight tracks, each with a
volume, a pan, a mute and a solo. Add clips from the case's files, drag them along their lane to
line them up, and press Play to hear the arrangement.

![The case mixer with clips placed](/help/media/using-the-audio-editor/case-mixer.png)
*Two copies of the same recording placed on the first two tracks, each drawn at its real length.
A clip whose length has never been measured is drawn dashed instead, at a placeholder width.*

Play is a preview and touches nothing on the server. **Export Mix** renders the arrangement and
attaches it to the case as a new file; you need permission to add files to the case to do that, and
the Mixer button only appears if you have it.

## What is stored, and where

- **Everything you make is a new file.** It appears in Saved Clips and in your own files, with its
  own visibility.
- **Markers, notes and the record of what you dismissed** belong to the recording, so anybody who
  can see the recording sees them.
- **How you have set the editor up** belongs to the recording too — the spectrogram, its settings,
  and the whole listening chain — and is saved only if the recording is yours.
