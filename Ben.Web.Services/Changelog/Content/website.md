# Website changes

This file is PUBLIC. It is rendered anonymously at `/changes`, so every line has to be safe for a
stranger to read.

**What goes in:** what changed for the person using the site, in their words. One line per change.

**What never goes in:** how a security fix worked, internal names for files, branches, servers or
tables, infrastructure and deployment, anybody's name, any group or case, and anything that would
only mean something to whoever wrote the code. If a line would help somebody attack the site or
identify a person, it does not belong here.

**Shape:** `## yyyy-MM-dd` headings, newest first, each followed by `- ` lines. Nothing else is
read. Add today's heading at the top when you ship something people can see.

The history before 2026-08-22 is summarised rather than listed: the site was not yet open, and a
day-by-day account of building it would say nothing to anyone using it now.

## 2026-09-12

- Events have a **Bookings** page. It opens on what is waiting on you — every party nobody has
  answered, soonest deadline first — rather than on a list of everybody you would have to search.
- A party holding places says how long is left, and one button gives them longer without you
  having to decide anything yet. If holds have already run out you can give them all back at once.
- Opening a party asks where they are actually staying, starting from what they asked for. Moving
  a party of three out of a double and into the suite is one change of a dropdown, and if they do
  not fit the refusal names the room and how many more it sleeps.
- The house below shows one night at a time: settled places solid, ones somebody is still holding
  hatched, and ones you have held back marked as not on offer.
- On a seating plan, the number of seats you pick is the size of your party. Holding one seat for
  four people used to be possible and could never then be confirmed.
- Where an event sells its places by seat, guests now pick their own on the plan and what they
  choose is held for them until the venue answers. Everybody else sees those squares as waiting on
  an answer and cannot take them.
- If two people reach for the same seat at the same moment, one of them gets it and the other is
  told which seat went, by name, with the rest of their choice still theirs.
- A hold lasts as long as the venue said. When it runs out the places go back and the guest is told
  — they stay on the venue's list and are welcome to choose again.
- A venue can hold a room or a seat back, either for one night or for the whole run, and say
  whether it is out of use or being used by the venue itself. What a guest sees is which of those
  two it is, never the venue's own note about it.
- A family can now take two rooms, and a party of three can take three seats. Before, a booking
  quietly kept only one of them.
- Guests' names, addresses and dietary notes on the booking board now need permission to read.
  Any member of the group could see them.
- Events now move themselves along: on the site, then on now when the first night starts on the
  venue's own clock, then over when the last one ends, then filed away a fortnight later. Nobody
  has to remember to do any of it.
- When an event is filed away, anybody still waiting on an answer is told it has passed rather than
  left waiting for ever, and the organizer is told what was closed on their behalf.
- Calling an event off more than 48 hours before it starts gives the event credit back, and the
  event goes back to needing one — so it is never both paid for and refunded. Inside 48 hours the
  credit stays spent, and putting the event back up then costs nothing. The card says which of the
  two will happen before you press anything.
- An event that has already happened can no longer be taken back to a draft. It ran, and it stays
  on the record.
- An event with a minimum number gets Yes-it's-on and No-call-it-off buttons, and a reminder a week
  and a day before the date you said you would decide by. Calling it off for want of numbers works
  exactly like any other cancellation.
- An event now says what state it is in, in one word, everywhere it appears: draft, live, on now,
  over, archived or called off. It used to be worked out separately on each screen from three
  different flags, and they did not always agree.
- Before you can publish an event, a checklist says exactly what is missing and links to the place
  you fix it. The publish button refuses in the same words, so there are no surprises after you
  press it.
- An event records who agreed it may happen at its venue: your own place, or somebody you arranged
  it with directly and when they said yes. Asking a venue that is on this site is coming later, and
  the page says so rather than leaving the option out.
- You can now say what a day costs, when bookings close, the fewest people that make the event
  worth running, and the date by which you will decide. All of these were readable and none of them
  could be set.
- You choose whether guests ask for a place and you put them somewhere, or pick their own on the
  plan and hold it until you answer. Switching is refused once anybody is booked or waiting,
  because it would change what their booking means.
- Un-calling-off an event brings it back as a draft, so putting it in front of people again is a
  decision you make on purpose. Publishing it a second time costs nothing.
- An event now has a plan you draw. A venue that lets rooms picks them from a list and places them
  on a floor; an evening sold by the seat gets whole blocks of rows added at once, with a centre
  aisle put in wherever you want one. Rows skip I and O, because on a printed ticket they read as
  1 and 0.
- Sections on a plan carry a name, a colour and a price, and the price is printed once for the
  section rather than on every seat. Prices are shown to guests and never charged here.
- The plan works on a phone, where it is there to be read and adjusted a room or a seat at a time,
  and says so rather than pretending a four-hundred-seat house is an afternoon's work on a phone.
- Site administrators can see every email the site has meant to send: when it was written, whether
  the mail server took it, how many times it has been tried and what went wrong. A message that
  failed can be put back in the queue, one at a time or all at once after a problem is fixed.

- A green tick now marks anything the site has actually proved — a confirmed email address,
  two-factor being on — and says what was proved and when if you hover it. An amber one means the
  opposite and always says what to do about it.
- Adding an email address to your profile now confirms it. The link is sent as soon as you save,
  rather than waiting for you to find a button, and the card tells you what it did.
- An email address has to be confirmed before it can be made your primary one, which was already
  true of making it public.
- Events that run overnight take bookings. A venue ticks which of its rooms an event is using, and
  guests ask for a room on named nights or a day pass for the day. A party is one booking with one
  lead and as many named guests as there are people, each with their own dietary note.
- Asking holds nothing until the venue confirms, so an event keeps taking requests after it is full
  and the overflow is a waiting list rather than a closed door.
- Bookings can be changed after they are confirmed, by the venue or by the guest. A guest asks the
  venue to release a confirmed room rather than freeing it themselves, because the venue has
  catered and staffed against it.
- Confirming a booking now emails the guest, with a QR pass they show at the door and a calendar
  entry for each night they have booked. One code admits the whole party. Turning a booking down or
  releasing one writes too, and carries the venue's own reason.
- A venue can scan a pass at the door and be told who has arrived, how many, and which room —
  or be told in plain words why a code does not admit anybody, which is more use than "invalid".
- Changing a confirmed booking replaces its pass automatically, so a code in somebody's pocket can
  never quietly say the wrong thing. A pass can also be reissued for a guest who lost the letter,
  or withdrawn with a reason the door reads out.
- The bell now has its own rows for event bookings, separate from tour sign-ups, so a notification
  lands on the screen the decision is actually made from.
- An event can publish what it is serving. One card holds every sitting of the weekend — breakfast,
  lunch, dinner, a late supper, snacks — with the dishes under each and what is in them. Guests
  whose place has been confirmed can read it.
- A venue can see the dietary sheet its kitchen works from: how many people are expected, how many
  said something, and how many nobody has named yet. Notes typed the same way are grouped with a
  count, and two different ways of saying one thing are never merged into one.
- A venue can invite somebody who has no account here by email. The link proves their address and
  puts a day-pass request on the venue's board, which the venue then confirms as usual. Nothing is
  held until they accept, and the screen says so.
- The Field Kit's video is easier to find. Choosing what a session records — magnetic field, sound, video, location — now happens before the session opens, not part-way down a running session's screen.
- A venue's rooms can now say what they sleep and whether they may be booked, which is separate
  from whether they are shown publicly. A room left without a number is one to ask the venue
  about; a room that says zero is one nobody sleeps in.
- The event page now links to the venue's rooms, so describing them is one click from the event
  that is going to offer them.

## 2026-09-11

- An investigation can now run across more than one day.
- Ghost walking tours have their own kind of listing, with a reserved seat you can hold from your phone.
- Fixed price bands showing the wrong figure on the billing screens.
- Date fields accept the arrow keys again.
- Fixed a pop-up opening behind the window that had asked for it.
- Countdowns and timers now measure elapsed time rather than counting ticks, so they no longer run slow on a busy machine.

## 2026-09-10

- Maps across the site are now Apple Maps.
- You can sign in to the website with your Apple Account.
- Addresses are looked up as you type when adding a place.
- Deleting your account now also revokes the Apple sign-in that was attached to it.

## 2026-09-09

- The home map shows the cases that are yours.
- Maps load what is in view instead of everything at once.

## 2026-09-08

- The help pages and the product documentation were rebuilt against the current site.

## 2026-09-06

- Anyone can report a case from the public site.
- An export that is missing its media now says so instead of producing a broken file.
- A cancelled export stops immediately.
- The administrator's user list keeps its actions on one line.

## 2026-09-05

- A pass of fixes across the video editor.

## 2026-09-02

- Fixed a save that appeared to hang when nothing had been typed.
- Sessions that lost their owner are cleaned up, and the cleanup can be run selectively.

## 2026-08-31

- Groups can be deleted, properly, along with everything that belonged to them.
- Field sessions published to a public place now say whether what was recorded was unusual.
- A free account holds 2 GB of recordings and files.
- Confirmation emails can be resent from the message itself, and every email now looks like the site.
- Errors are written to a log an administrator can actually read.
- Menus and buttons that never applied to you are no longer shown at all.

## 2026-08-30

- Subscriptions are live end to end, from checkout to seats.
- The pricing page shows real, round numbers.
- Field sessions recorded at a public place accumulate into that place's own archive.

## 2026-08-27

- A guide can sign somebody up on the spot, with no account and no phone.
- Trial notices tell you where you are before anything is charged.

## 2026-08-26

- A request to investigate is reviewed and voted on, and the first group to accept takes it.
- What each role may do is now decided in one place, so a menu no longer leads to a refusal.
- Clients can see which groups are able to take their case before choosing one.
- Large files upload in pieces, so a dropped connection no longer costs the whole upload.

## 2026-08-25

- Media previews stream instead of loading the whole file first.

## 2026-08-24

- The feed opened: posts, replies, likes and media, with moderation behind it.
- Work at a private residence can be marked private, and addresses are held back accordingly.

## 2026-08-23

- Roles and permissions can be granted and taken away from a screen.
- Groups can give their members titles.
- Investigations have duties, and cases have named contacts.
- Profiles have avatars, including three you can choose without uploading anything.

## 2026-08-22

- Site-wide announcements.
- Attendees can submit what they recorded at a public event, and a member accepts it.
- Billing, subscriptions and what happens when one lapses.

## 2026-08-20

- Sign-up, permanent @names, and two-factor authentication.

## 2026-08-16

- The site opened to its first members.
