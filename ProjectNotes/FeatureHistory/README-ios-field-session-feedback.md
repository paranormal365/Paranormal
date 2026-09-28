# Field sessions: what Ben found on his iPhone (09/27/2026)

Branch `fix/ios-field-session-feedback`, from develop. Ben tested a real session on his iPhone and
reported, in his words:

1. "when recording audio it is super quiet like the mic is turned way down"
2. "Even when recording video, it doesn't play back when you end the session. The playback of
   audio is super quiet."
3. "It is not super obvious what to do after you end a session and want to start another one."
4. "it asks where you are when you start the session. If you use the map, you should be able to
   determine where they are and if they did not allow you to read their position, then you should
   ask where they are. Also, if there is an investigation at the location they should be able to
   just pick it from the dropdown list."
5. "the photo never shows either when you take it during a session. I would think it would
   display when playing back the session... even on the phone."

Then, as next steps in the same arc:

6. A server lookup for public `.ben` sessions near where you are, and a way to find them by
   looking up a place, tested against the server.
7. A "Watch for Motion" setting: the camera looks for motion in frame while the phone is not
   moving, and keeps recording video while it does. Playback highlights the video and shows
   "Motion detected" at those moments.
8. Photos in playback as small thumbnails whose border glows for a couple of seconds as playback
   passes the moment each was taken.
9. Everything that changes in the phone's player is replicated in the web app's `.ben` player.
10. Help files and change logs updated, and the iPhone and iPad apps tested.

## What was actually wrong

| Report | Cause |
|---|---|
| Quiet recording | The microphone runs in `AVAudioSession` `.measurement` mode (so the sound meter reads the raw room), which also turns off the phone's own gain. The file was written from the same raw tap. |
| Quiet playback | Nothing on the review screens set the audio session, so playback inherited the recorder's record-mode setup. |
| Video never plays back | Two things. Since build 7 (the camera freeze) nothing records video during a session at all; the Video switch only runs the viewfinder. And the replay showed ONE clip per moment, the first covering the playhead, and the session's audio starts at Start, so it would have hidden any video anyway. |
| No clear next step after Stop | Stop pushed the review ON TOP of the dead live screen, and nothing on the review offered another session. |
| "Where are you?" | A free-text box, whatever the phone knows. The investigation list is every upcoming investigation, with no idea where they are. |
| Photos never show | Photos are only pins on the review map, and only when they carry coordinates. Indoors that is usually nowhere at all. |

## Decisions Ben made (09/27/2026)

- **Video: record the whole session.** While a session is recording and the Video channel is on,
  the camera records continuously, with no sound of its own. The session's own audio recording
  keeps running, and playback plays both together. No microphone handover, which is what froze
  build 6.
- **Loudness: a fixed boost, not automatic gain.** The meter keeps the raw input; the file is
  written with a steady +18 dB and a soft limiter. Automatic gain swells the noise floor in
  silence, and that swell is exactly what gets mistaken for a voice.
- **Place name: our places first, then Apple's address.** Reverse geocoding is new for the app, so
  the next App Review notes need a line about it.
- **Investigations: the API sends coordinates** on `api/my-investigations` (additive).
- **Public `.ben`: a sanitized public copy**, built by the server: readings and marks kept,
  positions snapped to the place's public point, media only if approved and from the sanitized
  copies. Plays like any `.ben`, labelled as from the public archive.

## Plan

1. Audio: `RecordingGain` on the file path only; `ReviewAudio` sets `.playback` before review and
   trim playback (never while a session is open).
2. Replay: `ReplayFrame` gains `activeVideo` and `activeAudio` (both play; the video is muted while
   the session's audio runs under it). `ReplayPhotos` puts a photo on screen when the playhead
   reaches it; a thumbnail strip under the player glows for a couple of seconds as each photo's
   moment passes, and tapping one jumps there.
3. Watch for Motion: scene motion is only judged while the phone is still, independent of arming
   the sentry, and marks "Motion detected". Playback highlights the video and shows the sign.
4. Whole-session video, noted with its START time (captures were stamped with the moment they
   were noted, i.e. the end). Send gains "leave the video out", because one upload carries five
   minutes of video.
5. After Stop: the review replaces the live screen, and offers "Start another session".
6. Start sheet: position → nearby IsHaunted place or Apple's address, prefilled and editable;
   typed only when location is off. Investigations near here first, one happening now
   preselected. Public sessions recorded nearby are offered.
7. Server: coordinates on `api/my-investigations`; public archive lookups (near a point, and by
   place search); the sanitized public bundle.
8. Phone: browse public sessions nearby or by place, download the public `.ben`, play it.
9. Web player: both media at once, the photo strip with the glow, the motion sign.
10. Help, change logs, product documentation; BenKit, server and Playwright tests; iPhone and iPad
    simulator walks against a local API on a non-production database.

## Cannot be proven in a simulator

A simulator has no camera and a Mac microphone. Loudness and whole-session video must be checked
on Ben's iPhone before this ships; `RecordingGain.decibels` is the single number to adjust.

## Later asks folded in (09/27/2026)

- **Start with a session still open** saves a recording one and replaces an unstarted one, after
  asking — Ben: "just save it and ask if they want to create a new one".
- **Motion photos**: a detection while the phone is still also takes a photograph.
- **Thumbnails grow a little while they glow; tapping one pauses and shows it full size, tapping
  again carries on playing** — phone and web alike.
- **Refused permissions** (camera, microphone, location, speech, notifications) say so and offer
  Settings wherever something needs them.
- **Upload allowance**: 10 minutes of video and 600 MB per upload (was 5 and 500), with **Send
  without the video**.

## What testing found (and fixed)

Walked on the iPhone 17 Pro simulator, in real Chrome, and through the API, against a local server
on `IsHauntedDb_e2e`, with a session written by the phone's own code
(`TestSessionBundleBuilder.swift`; the short profile is the browser tests' fixture).

- Photo thumbnails overlapped (a `.fill` image sized on a container that clips nothing); the lit
  thumbnail was cut off at the strip's edge; the full-size photo sat on a grey card under the bars.
- The server counted every photo, video and sound note as a mark ("8 marked" for 3). Fixed at the
  door, in the web player's list, and for stored sessions by `FieldSessionMarkerRecountService`.
- Every review opened at 0:00 said "No recording at this moment" — the recorder starts a few
  milliseconds after the clock. A clip within a second of the playhead now covers it, phone and web.
- After **Open Settings**, no permission prompt on that screen appeared again (the alert's state
  was never cleared while the app went to the background).
- EVP mode's Start recording failed silently with the microphone refused; three screens opened a
  black system camera with the camera refused.
- A refused microphone produced a silent recording with no warning (found while building).
- The web player's icons, Play and Pause included, never drew: Bootstrap Icons classes on a site
  that does not load that font. Nine other pages have the same problem — flagged separately.
- Tests: the Field Kit UI suites cascaded from one unstarted session into the new save-and-start
  question; `AppNavigator.startNewSession` answers it. `RealAppSessionReplayTests` had a hard-coded
  room.

Second pass (the trim suite against a local API, the iPad, and the help captures):

- **Send screen**: the "what will be sent" summary and **Send the whole session** sat under the
  preview, a screen below the handles — a List had not even drawn them. They are under the handles
  now. The trim UI tests had only ever been skipped; run against a local API they caught it.
- **iPad New session sheet**: **Open the session**, its only action, was below the fold of the form
  sheet. It is page-sized on iPad now.
- **iPad place name**: a name the phone filled in was sometimes footnoted "your own words" — a flag
  set in a deferred task raced `onChange`. It is a comparison with the suggestion now.
- **Phone photo strip**: the lit thumbnail covered its own time, the time was green on the green
  glow, and the glow smeared over the photos beside it and was cut flat at the top.
- **Web player, dark theme**: the lit photo's time was Bootstrap-info purple on a dark page, the eye
  on Motion detected all but vanished and wrapped above the words, and a grown thumbnail covered its
  neighbours. It glows the phone's green now.
- **Server counts**: confirmed on a restarted API — the recount put both public copies' "8 marked"
  back to 3.
- Test harness: the new help capture photographed Daniel instead of James (a restored session
  beats `-autoSignIn`); it signs out first now, and so does the existing Field Kit capture, which
  had the same flaw. `AboutPrivacyUITests` needs a reset simulator keychain, like the captures.

UI suites run on the iPhone 17 Pro Max against the local API: AboutPrivacy 2, DeleteAccount 3,
FieldKitChannels 3, FieldKit 18, SessionStart 4, SessionTrim 3 — all pass. EverySurface,
CaseMessages, LogOccurrence and CaseReports were not run: they take no API address, so they would
sign in to whatever host the Dev build names.

## Still to check on a real iPhone

- **Loudness.** `RecordingGain.decibels` (18) is the one number to adjust.
- **Whole-session video**: battery, heat and storage over a long session, and backgrounding.
- **Open Settings** lands on the top of Settings in the simulator; on a device it should open
  IsHaunted's own page.

## Before the next App Store submission

- The review notes said the app makes no geocoding call. It now asks Apple for the street address
  of the phone's position on the New session sheet (and Tours already geocoded a typed place). The
  notes need a line saying so; App Privacy already declares location for App Functionality.
- The tour plan's own five-minute limit per recording still applies to groups on that plan.
