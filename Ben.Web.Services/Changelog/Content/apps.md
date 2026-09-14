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

**Shape:** `## yyyy-MM-dd` headings, newest first, each followed by `- ` lines. Nothing else is
read.

## 2026-09-13

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

## 2026-09-12

- Sign in with Apple now works. It was returning to the sign-in screen without explanation when
  an Apple Account had no account here yet; it now asks for a display name and an @name, or offers
  to join an account you already have. Fixed in 1.0.2 build 5.
- The Field Kit asks what to record before the session opens — magnetic field, sound, video and
  location, each with what it costs in battery. Video used to be reachable only from part-way down
  a running session's screen.
- Before sending a session, the app says how much it weighs and how much video is in it. Where a
  window is too heavy, it offers to send the video at a smaller size rather than simply refusing.

## 2026-09-04

- A recording can be trimmed on the phone before it is sent, so only the part that mattered
  leaves the device.
- A session can be set up first and started when the room is ready, rather than recording from
  the moment it is created.
- Links to the site open in the app.

## 2026-08-31

- The app shows only what applies to you.
- A published session says whether what it recorded was unusual for that place.

## 2026-08-30

- Version 1.0 submitted to the App Store.

## 2026-08-29

- Somebody can be blocked, from the app and from the site.
- An account can be deleted from inside the app.

## 2026-08-25

- The Field Kit: magnetic field, sound level, photographs, audio and location, recorded offline
  and reviewed later against one timeline.
