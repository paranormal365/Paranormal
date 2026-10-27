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

## 2.0.0 · 2026-10-27

- Review blind: a setting on your profile hides each candidate's readings until you keep or dismiss
  it, so you decide by what you hear.
- A marker you place where a scan found nothing is noted as something the scan missed, so it can
  learn from that too.
- Scan several recordings for EVPs at once from the Media Library. Any that can't be scanned are
  listed with the reason; the rest are scanned one after another.
- EVP scans: a page listing your scans from the last week, each recording with its progress and
  what it found, to open in the editor or stop.
- Rescan with these settings, at the foot of Fine-tune.
- Each candidate now has a second reading, Voice-like: how much the sound is shaped like a human
  voice. Sort by it, or hide lower readings while you review; nothing is hidden unless you ask.
- Field sessions can be scanned for EVPs: by whoever recorded one, the group it was sent to, an
  event's organizers and confirmed staff, and the venue of a public place it was published to. Each
  works on their own copy of the recordings, or their group's, and the session itself never changes.
- A candidate heard after a question asked during the session is noted with the question.
- An EVP saved from a group's copy belongs to the group, and says which session and recording it
  came from.
- Why? on each candidate: its score in three parts, each with a sentence, and what was measured.
- The detector lane shows what the scan saw along the whole recording, and the level lane how loud
  it is.
- A strip above the waveform marks every candidate across the recording; click one to hear it.
- A live meter shows what you're hearing, and B (or Listen ▸ Hear the original) switches the
  listening tools off and on to compare.
- Keyboard shortcuts: Space plays, the arrows move, N and P go through the candidates, K keeps, D
  dismisses, Z zooms, F fits, L loops, and ? lists them all.
- Loop plays a candidate, a marker or the selection round and round.
- Zoom to the selection, or to one candidate from its row.
- Candidates and kept markers are marked on the spectrogram, and the spectrogram can be made taller.
- The overview strip shows the whole recording under the waveform while you're zoomed in.
- Turn parts down: draw a volume line over the waveform, hear it as you go, and save it as a new
  file.
- EVP scans run in the background. The button shows how far a scan has got, the waveform is dimmed
  where it hasn't reached and turns solid as it goes, and Stop ends it early. You can leave the
  editor while it runs and pick it up when you come back.
- Every setting the EVP scan uses can be adjusted in Fine-tune, in four groups: what counts as a
  sound, the pitch range it listens in, how it judges the room's background, and what reaches the
  list. The spectrogram's voice band follows the range.
- Save scan settings as presets of your own, and choose one as your default. A recording starts
  from the settings you last scanned it with.
- When a scan finds more than fit, a few places go to lower-scoring sounds, marked as samples, so
  your rulings teach the scanner about more than its favorites.
- Clips, edits and case mixes are saved as FLAC: every sound kept exactly, in a smaller file than
  WAV. After saving, the editor shows the size and what it would have been as WAV. A WAV is still
  one tick away for programs that can't open FLAC.
- A FLAC clip or a phone recording can go into a case mix.
- Recordings made on the phone (.m4a) can be scanned for EVPs, clipped and edited, the same as MP3
  and WAV.
- When the site cannot read a phone recording, the scan says so instead of saying the recording is
  broken.
- Saving a clip or an edit counts toward a free account's storage. If it would go past it, you are
  told before anything is saved.
- The editor remembers more of how you left a recording: the zoom, the playback speed, the open
  tab, the voice band, scrubbing or selecting, silence detection, and every spectrogram setting.
- A zoom no longer resets when the spectrogram is turned on or the window is resized.
- The editor's panels, menus and sliders are in the site's colors in light and dark, and the marks
  on the waveform (your regions, clips, candidates, markers, silence) follow the theme too.
- Sliders across the site are in the site's colors rather than a leftover blue.
- A tidier toolbar. The buttons you use most are on it, and everything else is in three menus:
  View, Listen and Edit. A button that is on looks pressed.
- The panels sit in tabs under the waveform (EVP Markers, Clips, Listen, Display and Edit), so
  opening one no longer pushes the waveform down the page.
- One playback speed. The Listen menu, the speed menu under the waveform, the Listen tab and the
  region explorer all show and set the same one.
- The Display tab has new spectrogram settings: frequency scale (including mel), frequency range,
  brightness, contrast, high-frequency lift, window, and one picture per channel.
- Scan for EVP is on the toolbar, with Low, Medium and High a click away.
- A new spectrogram: sharper, with its own frequency labels, and quick on long recordings because it
  draws only what is in view.
- Zoomed in, the timeline and the spectrogram now move with the waveform. The time marks used to
  stay at the start of the recording.
- The Play button on an EVP candidate or a marker stops at the end of it. It used to carry on to the
  end of the recording.
- A candidate you have listened to says "heard". It counts once you have played its middle, not
  just the edge it shares with the one before it.
- The EVP lab looks the way it should again on the editor's page.
- The editor opens as a page of its own, with an address you can bookmark or share with someone
  who can see the file. **Back** returns to where you opened it.
- Right-click a player for **Quick look**: a bigger player with its own controls, without leaving
  the list.
- A marker's end can be set past 1:40 on longer recordings, and the fade boxes accept any length up
  to the recording's own.
- Clicking "Mark a stretch of audio" ticks its box.
- The speed menu shows the speed the player is actually at.
- With the mel scale on, the spectrogram's frequency labels sit on the frequencies they name.
- The voice band on the spectrogram covers 300 Hz to 3.4 kHz, the same range the EVP scan listens
  in.
- The two buttons called "Silence" have different names now: **Detect silence** shades the quiet
  parts, and **Silence selection** is the edit.
- Buttons and menus use the site's icons in place of emoji, which looked different on every
  computer.

### Administration

- EVP Scans in the administration menu: what the scanner found, what people kept and dismissed, what it
  missed, and how each measurement differs between kept and dismissed.

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
