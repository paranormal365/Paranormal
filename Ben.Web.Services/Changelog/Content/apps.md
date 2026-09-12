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
