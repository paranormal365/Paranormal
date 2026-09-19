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

## 2026-09-19

- Sign-ins through a Microsoft account are recorded, once per twelve hours of use. They
  never were: a Microsoft session is a token validated on every request rather than a
  moment somebody signs in, so there was no obvious thing to record.
- The users list can be asked when each account last signed in and how many times.

## 2026-09-18

- A board that links to another board is checked when it is published: the service refuses to publish
  one whose target has not been published, and says which. A picker that only offers published boards
  stops the state being created; holding the rule here stops it being reached any other way.
- Reading a board by its published copy is now a request of its own, so following a link to another
  board never reaches its author's unpublished draft.

## 2026-09-17

- Money figures are worked out and filed more carefully. Several sums rounded a fraction of a cent
  the wrong way, or rounded each item before adding rather than once at the end, and one rounded
  the amount by one convention while taxing it by another — so a record could disagree with itself.
  Amounts on existing records are unchanged; this is about what is written from now on.
- Tax charged for event credits is the tax that is filed. It was being worked out again when the
  payment came back, so a change to a tax rule or to a group's address in between could leave a
  receipt saying something different from the card statement.
- A renewal attempt is now recognised as the same attempt when it is retried across a date
  boundary, so a retry cannot become a second payment for one period.
- A subscription period's agreed price is no longer altered by anything bought mid-period. The extra
  purchase is recorded as its own charge and payment, which is where it belongs.
- A group's capabilities and permission areas now come from a plan that is actually standing, so
  they match what the checks enforce. A plan that has ended no longer supplies them.
- A place of your own in a group is no longer retried indefinitely when a payment has failed; it
  ends, and you are told once.

- A place tells a stranger less about a private residence. Its street address, postcode and exact
  position are no longer returned to an anonymous caller, and its name is withheld as well, because
  a home is often named after the family in it; the city, the state and an approximate position
  still come back so a page can say roughly where it is. Asking as a signed-in caller now requires
  a reason to know — one of your groups has worked there, you added the place, or you administer
  the site — and otherwise answers the same as it would to anybody. A public location is
  unaffected.
- A link preview for a case, and a case's vote tally, follow the same rules as every other public
  case surface: substituted names on a private engagement, and nothing at all once a case stops
  being published.
- Nearby search answers with public locations as well as groups and events, for those that have
  published work at them. Optional addition; a caller that ignores the new list behaves as before.
- A published case says which public location it is about, when it has one.
- Reading a client message thread no longer depends on the group's plan being current. Writing
  still does. A lapse leaves everything already recorded readable, which is what it always said.
- What a caller may do in a group now includes whether the group is read-only, so a client can say
  so before offering a control that will be refused.
- An account can ask how much of its storage allowance is used, and is told there is no cap when a
  group's plan covers it.
- A case answers who agreed to publish its footage to the feed, and when.
- A feed post can name a public location it is about, and the feed can be narrowed to one. A place's
  page returns its latest posts and says whether the reader may add one. Both the field and the
  filter are optional additions, so an app that sends neither behaves as before; an older app shows a
  place's posts as ordinary posts. Posting about a private residence is refused.
- A place also answers with the caller's own groups' cases there, whatever their status.
- A case can be created naming the shared place it concerns, either one already on file or one
  described with it, and a place's public page now also lists the cases published there. Both fields
  are optional additions, so an app that sends neither behaves exactly as before.
- What an account may keep private is now decided by its plan, not by whether it works alone. An
  account with no plan has its cases and its visits at public locations shared by default and cannot
  narrow them; an account with a plan chooses. Work at a private residence, and anything already
  private, is unaffected.
- A case counted as public, or opened for anyone to add recordings to, now means a case that has
  actually been published. Two older checks read the publish setting without asking whether the case
  had been published, which is what every other answer on the service has always meant by it.
- A video, a recording or an image on a case can be asked for a byte range, so it plays and can be seeked in the page instead of only being downloadable. The same applies to a case's public page, a place's archive, an event's evidence and a group's files.
- Opening a case now accepts it, for anybody who may change a case's status. A new optional field on the request asks for the group's decision instead, which leaves the case proposed as before. Older apps that do not send it get the accepted behaviour.

## 2026-09-16

- The public archive lists and serves recordings that live inside a session's single file, by the file row's id; publishing such a session is no longer refused, and its recordings are screened like any other before they show.
- Research boards on a case have their own addresses: a group's boards, one board, and publishing one. A board nobody has published is not in the list for anybody but the person writing it.
- The older research pages and their attachments have been removed, along with their addresses. Files those pages referred to are untouched: they are the case's files and stay on the case's Files tab.
- The canvas is no longer behind a site switch. It answers signed-in callers wherever the service is running.

## 2026-09-14

- A client's list of cases now says which request each case was accepted from, as a new field that older apps ignore.
- A group's Viewers are refused every change, with a sentence saying why, whatever roles or permissions they hold. The permissions a person has in a group now say whether they are a Viewer.
- Files are now saved whole or not at all. A save interrupted part way, such as a thumbnail being made when the
  person leaves the page, no longer leaves an empty file that is then served in place of the picture.
- Case descriptions are cleaned of anything that could run as code before they are stored, and a description
  that holds no words once its formatting is removed is stored as no description.
- Case notes are stored and returned as cleaned HTML. A note sent as plain text is kept as paragraphs and line
  breaks, and notes written before this change are converted once, the same way.
- Case messages accept a formatted copy as well as plain text. A message sent formatted is stored with its plain
  text written from it, so every message still has a plain-text body; messages now return the formatted copy
  when there is one.
- Research notes can be pages made of blocks, with a private draft, publishing, and files and links kept beside
  them. Every block is cleaned before it is stored, a draft saved twice by the same request is stored once, and
  a save from an out-of-date copy, or over somebody else's unpublished changes, is refused with a sentence
  instead of overwriting.
- The case timeline includes published, dated research pages, marked as read-only with a way to open the page.
  Older apps see them as research entries.
- Link cards are made by reading the linked page once, only when a signed-in person posts or adds the link, never
  when somebody reads it. The page's picture is copied small onto our own storage so readers never load it from
  the other site, and a card no research page keeps is removed after seven days. Each person may ask for a limited
  number of new cards a minute, and addresses that cannot be read safely get a plain card with the address.
- A new site setting takes plans and member seats off sale. While it is off, starting a checkout for either is
  refused with a short sentence, and the public features answer says so.

## 2026-09-13

- SuperAdmin endpoints list every hosted event, return the events dashboard figures, show what removing an
  event would do, remove it, and answer appeals.
- Hosted events have a new Removed state. It counts as called off, is not shown on the public site, and
  can't be published, restored or un-cancelled by the organizer.
- Organizers can read their event's removal and appeal it once.
- Restoring an event now only acts on an archived event. It no longer turns a called-off event back into
  a draft.

- Hosted events have endpoints to write to their guests: one to list the letters sent, one to count who a
  letter would reach, and one to send it by email and site message. Only people who may decide bookings
  can use them, and an event can have ten letters a day.
- A new summary endpoint returns a hosted event's booking counts, places left, arrivals and review
  average for the people who may read its bookings.
- Hosted events carry optional access notes, returned on the public event record.
- Asking for a place, changing a booking, posting to an event's room, reporting, reviewing and signing
  up for a session now share the stricter hosted booking rate limit.
- Publishing, calling off and restoring events, booking decisions, pass changes and a venue's
  withdrawal are now written to the audit log.

- A new endpoint lists the hosted events the signed-in person may run the door at, whether as a member of
  the organizing group, an accepted helper, or one of the venue's people where the venue lent its staff.
- Letting a party in at the door, by name or by scanning, accepts an optional arrival time, so an arrival
  recorded without a signal keeps the time it happened. A future time, or one long before the night, is
  taken as now, and an earlier arrival replaces a later one already recorded.

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
