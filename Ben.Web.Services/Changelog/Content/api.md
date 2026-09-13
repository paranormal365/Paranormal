# Service changes

This file is PUBLIC. It is rendered anonymously at `/changes`, so every line has to be safe for a
stranger to read.

**What goes in:** what changed about the service the website and the apps talk to, described by
what it means for the people using them. Availability, what can be stored and for how long, what
the apps can now ask for, and the outside services involved.

**What never goes in:** how a security fix worked, endpoint names, internal names for files,
branches, servers or tables, infrastructure, deployment, anybody's name, any group or case.
Nothing here should help somebody attack the service or identify a person. When a fix is
security-related, say only that it was, and never the shape of it.

**Shape:** `## yyyy-MM-dd` headings, newest first, each followed by `- ` lines. Nothing else is
read.

This stream deliberately does not repeat the website's list. A change people meet as a page
belongs there; this is for the part underneath.

## 2026-09-13

- The list of a person's own hosted event bookings now returns one row per event: the live booking if
  there is one, otherwise the newest.

- Hosted events have dining tables and a seating plan per meal, limited to confirmed parties and to each
  table's chairs. Saving the menus now keeps the meals that were not removed, with their seating.

- Hosted event plans can be started from an earlier plan at the same venue, and venues keep a photo
  library that organizers can offer gallery pictures to.

- A retention rule removes a hosted event's files, gallery and room photo links 90 days after it ends,
  after two warnings to the organizer, who can download chosen files as a zip first. The length is a
  site setting.

- Hosted events take reviews from guests who had a confirmed place, and a scheduled task sends each
  party one thank-you the morning after the event.

- Hosted events can be copied into a new draft on new dates, with the organizer choosing which parts
  come across, and their bookings can be exported as a spreadsheet.

- Seats on a hosted event can be picked without an account. They wait fifteen minutes for the person
  to confirm by a link sent to their email address, then become an ordinary hold. Unconfirmed picks are
  limited per address and per event, and deleted a day later.
- Hosted event bookings now carry a name and a phone number for the organizer. A phone given for a
  booking stays with that booking and is not added to the person's account.

- Hosted events can carry a gallery of the host's own pictures, up to fifty, resized with their
  location removed. Guests' photos from an event's room are never added to it.
- Each event now has a storage allowance, 2,000 MB unless the site changes it, shared by its files,
  its gallery and the photos posted in its room.
- A group's ad can lead to one of its events, and is withdrawn from view once the event is over.

- Sessions on a hosted event, drafted privately and published, with first-come sign-up and a queue.
  Two sign-ups for the last place cannot both succeed. Calendar files are served per session.

- Contact details for places, public or private to a group, and claims to run a place: a code to a
  public address recorded by somebody else, a week for objections, a scheduled task that confirms
  unchallenged claims, and a review queue for everything else.

- Venues on the site: a group's profile of a place it runs, requests from other groups to hold an
  event there, and the permission a yes grants. Publishing at a place with a confirmed venue now
  needs that permission covering every night, and withdrawing it stops the events that rest on it.
- A public read for a confirmed venue's page, and one saying who runs a place as its venue.

- Two scheduled tasks now tell the people who decide an event's bookings about them: one as
  bookings arrive, collecting a rush into a single summary, and one daily or weekly overview. Each
  remembers how far it has told each person, so a letter that fails to go is tried again on the next
  pass rather than recorded as sent.
- A signed-in person can read and change, per group, how often those letters reach them. Only groups
  they actually decide bookings for are listed or accepted.
- The notification summary counts held places as waiting on a decision, includes event helpers who
  may decide, and has two new counts for holds that run out within a day.

## 2026-09-12

- What the kitchen needs for a hosted event can now be asked for one night at a time. A party with
  no night of its own — somebody who has the whole run — counts on every night of it, so no
  service is quietly under-catered.
- Every message the service sends is now written down before it is sent, and a task posts them.
  One that does not go the first time is tried again, with longer waits each time, for about a day
  and a half; one refused outright — an address that does not exist — stops at once and keeps the
  reason. Nothing is lost to a temporary fault any more, and whether a message went can be
  answered afterwards.
- A deployment with no mail configured still records what it meant to send, so switching mail on
  posts the backlog rather than starting from empty.
- The words of a message are kept for thirty days and then cleared; who it went to, when, and
  whether it was accepted are kept for good.

- A confirmed booking carries a pass: an opaque code that admits one party to one event, revocable
  at any time, replaced automatically whenever the booking changes. It is served as a picture so a
  letter, a printed page and a phone all show the same thing.
- Scanning a pass takes a signed-in member of the venue who may decide bookings, and answers with
  the party rather than with a yes or no alone.
- Confirming, turning down or releasing a booking writes to the guest, with the pass and one
  calendar entry per booked night. Mail failures never undo the decision.
- An event's menus and its dietary sheet can be read and written. Menus reach guests whose place a
  venue has confirmed; dietary notes are health information and reach only staff who can decide a
  booking, never a public page or an ordinary member.
- Somebody with no account here can be invited to an event by email, through the same single-use
  link that has always signed guests up to a public event. Accepting it asks the venue for a day
  pass and holds nothing until the venue agrees.
- Overnight events count places per room per night, and day passes against one number for the
  event, so a venue that sleeps eight can no longer be sold forty beds.
- Confirming a booking puts the whole party on the event's calendar entry, which is what the
  public count, the reminder and the apps already read.
- One upload now carries up to 5 minutes of video and 500 MB in total. Recording on the phone is
  not limited in any way — this is only about how much travels at once, and a session can be sent
  in as many goes as it takes.
- An event's plan can be saved with each seat's own identity, so renaming a seat somebody has
  already booked is a rename and not a new seat with an empty one left behind.
- When a plan cannot be saved, the refusal names the seats in the way, in a form a screen can
  point at as well as a person can read.
- A confirmed guest's pass can be emailed to them again.
- A pass that has been withdrawn can still be read by its guest, who is told it was withdrawn
  rather than that it was never issued.
- The calendar entries for an event's nights now carry the venue's own evening rather than a
  universal clock.
- A venue with an event at it can be merged into its duplicate, and the event follows.

## 2026-09-11

- Investigations can span several days, and everything scheduled against them understands that.
- Tour seats can be reserved and released.

## 2026-09-10

- Signing in with an Apple Account is accepted from the website as well as the apps.
- Addresses are resolved through Apple's own mapping service.
- Deleting an account now also tells Apple to forget the sign-in that was attached to it.

## 2026-09-06

- Reports of a case can be submitted by people without an account.
- An export that cannot find its media refuses rather than producing an incomplete file.

## 2026-09-02

- Recordings whose session no longer exists are cleaned up rather than left behind.
- Fixed a save that never completed when the form was empty.

## 2026-08-31

- A free account holds 2 GB. What a subscription holds is set by its plan.
- Deleting a group now removes everything that belonged to it, rather than leaving it stranded.
- Field sessions published to a place are compared with everything else recorded there.
- Email delivery is recorded, so a message that never arrived can be told apart from one that was
  never sent.

## 2026-08-30

- Payments run through Stripe end to end.
- Duplicate places can be found and merged into one.
- Field sessions can be published to a public place archive.

## 2026-08-27

- Somebody can be signed up for a tour on the spot, with no account.

## 2026-08-26

- Large uploads are sent in pieces and resume rather than starting again.
- What a role may do is decided once, for every door.

## 2026-08-24

- The feed, its ranking and its moderation queue.
- Work at a private residence is marked as such, and locations are withheld accordingly.

## 2026-08-22

- Subscriptions, seats, and what happens when one lapses.

## 2026-08-20

- Accounts, permanent @names and two-factor authentication.
