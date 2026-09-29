# iPhone and iPad changes

This file is PUBLIC. It is rendered anonymously at `/changes`, so every line has to be safe for a
stranger to read.

**What goes in:** what changed in the app, as somebody holding the phone would describe it.

**What never goes in:** how a security fix worked, internal names for files, branches or servers,
anybody's name, any group or case, and anything that only means something to whoever wrote the
code.

**Dates are the day the change was made, not the day Apple approved it.** A build reaches people
when App Review says so, which is usually later and never predictable. Where a version number is
known it is worth naming, because that is what somebody can check on their own phone.

**Shape:** `## yyyy-MM-dd · version` headings, newest first, each followed by `- ` lines. Nothing else is
read.

**Versions** (from 2026-09-28): each heading carries the App Store version that brought the change
to phones, after a middle dot — `## 2026-09-16 · 1.0.3` — so it matches Settings → General → About
on somebody's own phone. Several days can share a number, because one build carries a fortnight of
work. A change not yet on the App Store is listed under the number the next build will have. Fixes
move the last number, new things the middle one, a whole new part of the app the first.

## 2026-09-29 · 1.1.1

- The app now talks to the service at its new address. Everything that needs you signed in —
  your cases, investigations, launching or joining a group's session, sending sessions — works
  again after the move; 1.1.0 and earlier can still read the feed and use Field Kit, but ask you
  to sign in again for the rest.

## 2026-09-28 · 1.1.0

- A lead can now start everybody's Field Kit. A tour's guide, an event's organiser or door staff,
  or an investigation's lead taps **Launch a session for your group** in Field Kit. Everybody
  registered is told with a notification and a card in the feed, and Field Kit lists it under
  **Happening now**. Nobody is started automatically: **Join** opens a session already set up for
  the tour, event or investigation and its place, and nothing records until Start.
- The lead's page shows who it reached, a **QR code** to hold up, and anybody asking to join. At a
  public tour or event anybody who scans is straight in; at a private night they sign in and ask,
  and the lead taps **Let in** or **No**. People can record while they wait.
- Sessions pile up on the phone and can be sent whenever you like — that night or days later.
  Each row says **Sent** or **Not sent**, and the send screen already says where it goes, with a
  switch to keep it as your own instead.
- Sending the next ten minutes of a long session no longer replaces the part sent before it.
- The send screen gives the upload allowance as 600 MB, which is what it is; it read 629.1 MB.
- On an iPad, a notification or a link into a page deeper than a section's front screen now opens
  that page rather than stopping at the front.

## 2026-09-28 · 1.1.0

- Times in the app are shown on the clock of wherever the phone is, and follow it when it crosses
  into another time zone. Every time is still stored in universal time; the zone chosen on the
  website sets only the website's clock.
- Events, tours, investigations and your case's visits have a **Local time · My time** switch.
  **My time** — where you are — is where it starts; **Local time** reads them on the clock of the
  place they happen. The app remembers which you chose, and when the two are the same it says so
  instead.
- Times recorded in Field Kit — the session list, the review, the instrument panel and the capture
  bar — read on the phone's clock, and a session file still carries universal time and the zone the
  phone was in when it recorded.
- Your case now lists the group's **visits**, with when each one is, where, and the last moment to
  cancel it. They were sent to the phone but never shown.
- An event whose organiser never named its zone reads in Central time, as it does on the website,
  rather than in UTC.
- Signing up on the phone records the phone's time zone as the website's clock for you, until you
  choose another there.

## 2026-09-27 · 1.1.0

- Sound recordings are louder. The microphone was recorded exactly as the sound meter reads it,
  which left a voice across the room barely audible; recordings now carry a steady boost for the
  whole night, while the meter keeps showing the room as it is.
- Playback on the phone is at full speaker volume, and plays with the ring/silent switch on.
- With **Video** on, a session now records video for the whole session, with its sound coming from
  the session's own recording. Before, the switch only showed a viewfinder, so nothing played back.
- The review plays video and sound together, and shows the photographs taken during the session in
  a strip of thumbnails that glow as playback reaches them. Tap one to see it full size; tap again
  to carry on. Photos taken indoors, with no position, used to appear nowhere.
- A new **Watch for motion** setting: with video on and the phone still, movement in view is marked
  *Motion detected* and photographed, and the review shows the sign at those moments. A phone being carried
  or turned no longer counts as movement in the room.
- After **Stop**, the review says the session is saved and offers **Start another session**, filled
  in the same way. Starting a new session while one is still open saves the open one instead of
  asking you to deal with it first. Switching from one session to another no longer loses the first
  one's marks and photos.
- The New session sheet fills in where you are from your location — a known place nearby, or the
  street address — lists investigations here and today first, and picks the one happening here now.
- The Field Kit screen has **Find public sessions**, which finds sessions other people published,
  near you or by searching a place or town, and plays them on your phone.
- When something needs a permission you turned off — the camera, the microphone, location, speech
  recognition or notifications — the app says which one and offers **Open Settings**. A recording
  with the microphone turned off used to be silent without saying so.
- One upload now carries 10 minutes of video and 600 MB in total, and **Send without the video**
  sends everything else in one go.

## 2026-09-17 · 1.0.3

- The button in a running session is a **Photo** button. It takes a photograph of what the camera
  sees — there and then when the camera is already on, or from a viewfinder that opens and closes
  itself — and never leaves the app. It used to open a camera screen with a video switch, and that
  screen could stop answering while sound was recording; clips are not offered from a running
  session for now.
- A session with sound on carries on when the app is put away: sound and readings keep recording in
  your pocket. With sound off the phone pauses the app a few seconds after you leave it, and the
  readings stop until you are back. The camera pauses either way, and says so in those words rather
  than "something else is using the camera". The stretch you were away is marked on the review —
  *App put away* to *Back in the app* — with a line saying which of those happened.
- A photograph that the camera never delivers is given up on after a few seconds with a sentence,
  instead of leaving the button stuck on "Taking…".

## 2026-09-16 · 1.0.3

- A session somebody sends you as a `.ben` file — by AirDrop, in a message, from Files — opens in the
  app and plays exactly as it did for them: the same trace, map, marks and recordings, with a line on
  the review screen saying where it came from.
- Open a .ben file on the Field Kit screen picks a saved one from Files.
- On the server, not on this phone, further down the Field Kit screen, lists the sessions you sent from
  another device or cleared from this one, and Download brings a whole night back. Older sessions sent
  before session files existed are counted rather than offered a download that would be refused.
- A session file that lost or changed bytes on the way is refused when opened, rather than opened with
  a hole in it. The same session is not imported twice.
- Exporting a session writes one `.ben` file and hands it to the share sheet.
- Sending a sealed session could be refused by the server; it is accepted again.
- Signing in on a phone that still held an old, expired session no longer ends the new session a moment
  later.
- A busy or rate-limited server no longer signs you out when it cannot refresh your session just then.
- The review chart says when no field base level was set, even when a sound base was.
- Replaying a long night is smoother: the chart and the map no longer rework every reading on every
  tick.
- A phone propped against a wall no longer marks "the device was moved" on every sample when its
  resting level sits above a low threshold; a real knock still is one.
- A session recording only where you are — field and sound switched off — still writes its readings.
- Dictation could stop working for the rest of a session after one failed start; it recovers now.
- Filming a clip while sound was recording could leave the microphone with the clip when the clip
  failed to finish; the session takes it back either way.
- Property photos, room photos and case photos are drawn at the size shown rather than decoded at
  full size, so a session with many photos no longer runs the phone out of memory.
- Your pass turns the brightness back down when the app is put away or the phone locked, not only
  when the pass is closed.
- A session too big to go in one file (more than 4 GB of recordings) says so before exporting or
  sending, instead of writing a file that cannot be opened.

## 2026-09-13 · 1.0.3

- On What I'm going to, Pass now opens your pass. It had opened the event's screen, so the pass could only be
  reached from there.
- Hosted events: the event screen shows where your booking stands, lets you let a request or a hold go,
  and opens the event's page inside the app to ask for a place.
- What I'm going to, under Profile, lists every hosted event you have asked for, once each, with its
  dates, venue and pass.
- Your pass for a hosted event shows its code, short code, party size, nights and table. It turns the
  screen up to full brightness, and it still opens with no signal.
- Opening the app with no signal no longer signs you out, and neither does a moment when the server
  cannot be reached.
- Each hosted event has its own screen with the pass, programme, menus, downloads and the event's room,
  showing only the parts that event has.
- You can sign up for programme sessions for as many of your party as are coming, join a waiting list
  when a session is full, and give a place back. Times are shown on the venue's clock.
- Organizers and their helpers can run an event's door from the app. Scanning a pass with the camera
  finds its reservation, and tapping the reservation checks the party in as arrived, all of them or some.
  The door also shows tonight's count, finds a reservation by name or pass code, takes an arrival back,
  and writes down walk-ups.
- The door works with no signal once tonight's list has been opened. A scanned pass finds its reservation
  on the kept list, and arrivals are sent with the time they happened as soon as there is signal.
- Opening the app with no signal now keeps you signed in as yourself, instead of showing the app signed
  out until the server can be reached.
- Posts and photos for an event's room are kept on the phone when there's no signal and sent when it
  returns. The room opens with no signal, as it was last seen, so you can still add to it.
- Photos and videos can be shared to one of your events straight from the Photos app, with a caption,
  the photo notice and the choice to send the organizers a copy.
- Photos from the phone are sent as JPEG, so pictures saved in the iPhone's own format are read correctly.
- In the event's room you can write, add photos and video from your library or the camera, agree to the
  photo notice the first time, send a photo to the organizers, take your own posts down and report
  somebody else's.

## 2026-09-12 · 1.0.3

- Sign in with Apple now works. It was returning to the sign-in screen without explanation when
  an Apple Account had no account here yet; it now asks for a display name and an @name, or offers
  to join an account you already have. Fixed in 1.0.2 build 5.
- The Field Kit asks what to record before the session opens — magnetic field, sound, video and
  location, each with what it costs in battery. Video used to be reachable only from part-way down
  a running session's screen.
- Before sending a session, the app says how much it weighs and how much video is in it. Where a
  window is too heavy, it offers to send the video at a smaller size rather than simply refusing.

## 2026-09-04 · 1.0.2

- A recording can be trimmed on the phone before it is sent, so only the part that mattered
  leaves the device.
- A session can be set up first and started when the room is ready, rather than recording from
  the moment it is created.
- Links to the site open in the app.

## 2026-08-31 · 1.0.1

- The app shows only what applies to you.
- A published session says whether what it recorded was unusual for that place.

## 2026-08-30 · 1.0.0

- Version 1.0 submitted to the App Store.

## 2026-08-29

- Somebody can be blocked, from the app and from the site.
- An account can be deleted from inside the app.

## 2026-08-25

- The Field Kit: magnetic field, sound level, photographs, audio and location, recorded offline
  and reviewed later against one timeline.
