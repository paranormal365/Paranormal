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

## 2026-09-19

- Files you own can be deleted from the Media Library, not only from the upload page. If a
  group is using one, it still asks the two questions first — remove it everywhere, or hand
  it over — rather than deleting something out from under them.
- The administration dashboard loads in a moment instead of half a second. Its figures are
  worked out every few minutes rather than on every visit, and the page says so.
- The privacy page now says what is kept when you sign in: that you did, when, and which way
  in — and that your IP address, your device and where you were are not recorded.
- Opening the Messages tab of a case nobody has written to yet no longer breaks the page. It
  ended with "An unhandled error has occurred" and needed a reload; it now shows the empty
  thread and the box to write in.
- Reopening a case clears its closed date. A case put back to Proposed, Accepted, Active or
  Summarized used to keep the date it was closed on, so a case being worked on still read
  "Closed" in its details.
- Line breaks in a feed post are kept. A post typed as three short lines arrived as one
  run-on paragraph. Names and tags in a post read as links now, and rows in the message list
  light up under the pointer and show where the keyboard is. None of that styling had ever
  reached a page.
- Every make is offered when you add a piece of equipment. Makes that already have something
  in the category you picked come first, and the rest say *(no models in this category yet)* —
  so the first person to own, say, a FLIR audio recorder can still choose FLIR and add the
  model. Picking one of those now says the model list is empty and points at the box that adds
  one, instead of leaving a blank list with no explanation.
- Arrows in the video editor draw their whole arrow. The line was missing, so pointing at
  something in a clip left a small arrowhead floating on its own — and the head is bigger now on a
  long arrow, instead of staying the same size however far it reaches.
- The button that adds a transition sits on the join between the two clips it applies to. It used
  to sit at the far left of the track wherever the join actually was, and with three clips you got
  three identical buttons in the same spot.
- All sixteen transition styles can be picked. The list opened downwards off the bottom of the
  window, so only the first few could be clicked. They read as words now — "Wipe left", "Fade
  through black" — rather than run together.
- Split at Playhead works from a clip's right-click menu. It was greyed out every time.
- Undo puts back a transition that an edit removed. Splitting a clip could delete the effect you
  had chosen with no way to get it back, and left the two clips overlapping with nothing to show
  why. Deleting, trimming or nudging a clip now tidies up its transition properly too.
- Clip names and lengths can be read in the light theme. On the timeline they were dark grey on a
  dark block.
- Sounds can be faded in and out from the timeline, not only from the panel on the right.
- Changing a clip's speed now shows the real length on the timeline. A clip set to double speed
  kept its full width while the finished video was half as long, and the difference came out as
  black nobody had been warned about. Speed can also be undone.

## 2026-09-18

- New board now asks what to start from. Five choices: a blank board, a moodboard of coloured
  sections with a cluster of themes, a research plan with a four-square and a grid of four weeks, a
  family tree, and a presentation deck of slide frames. Everything on one is yours to move, rename or
  delete — it is a head start, not a form.
- The family tree gives every person a photo frame above their name. It starts empty, and empty is
  finished: paste a photograph in, put any picture you like there instead, or leave them all blank and
  let the names do the work. The lines join the names, so going without a photograph moves nothing.
- A board can hold a grid. A table block is for anything that reads across — owners and the years
  they held a house, four weeks of who is doing what, a set of readings. Tab moves along and out of the
  last cell into a new row, and the header row can be turned off.
- A shape block puts a box, a circle or a diamond with a word or two in it on the board, for the
  things a card would overdress — a theme, a question, a step, a label on part of the board. A
  published board now draws them as the shapes they are; they used to come out as plain boxes.
- A table grows as you fill it in. Adding a row or a column makes the block big enough to show it,
  rather than leaving the new row below the bottom edge, and an empty grid is now a grid you can read
  and click into rather than a pair of hairlines. One undo takes back the rows and the room together.
- Choosing "Arrows at both ends" on a connector now puts arrows at both ends. It used to leave
  whatever you had already chosen for one end, so the words and the line could disagree.
- Pasting a grid of text onto a board now makes a table of it rather than a wall of words in a note.
- Colour goes further. Any block can be filled with its colour rather than striped with it, and a
  group can be drawn as a filled, titled panel rather than an outline — so part of a board reads as a
  section of it. Both are kept when the board is published.
- A line can say what it means. Choose its route (a curve, a straight line, or right angles), what
  each end looks like (an arrow, a diamond, a dot or nothing, per end), whether it is solid or dashed,
  a label along it, and a one- or two-character badge on it.
- A card can now open another board. One board fills up, so a card can point at another board on
  the same case — and at one card on it. The header then shows the boards you came through, oldest
  first, and any one of them takes you back to it. Only published
  boards can be linked to, and the picker says so; a board with a link on it will not publish until its
  target is published. If the target is deleted, the card says so when clicked rather than going
  anywhere, and if only the card you pointed at is gone, the board still opens.

## 2026-09-17

- Your two-week renewal notice now quotes the right number. A group billed quarterly or every six
  months was shown its price "per month", and a tour business was shown the price of one tour
  rather than of all of them. The notice exists so the charge is never a surprise, so both are
  worth saying plainly.
- Paying for a place of your own in a group stops when the group's plan grows to cover you. It used
  to carry on alongside what the group was already paying, so the group's own upgrade quietly left
  you paying twice.
- Adding a tour part way through a period is priced at the rate you signed up at, not at a price
  that has gone up since. If the price has come DOWN, you get the lower one.
- Buying a plan you are already paid up for is now refused, with the date you are covered to,
  instead of taking a second payment for a month you already own. Changing between monthly and
  yearly still works exactly as before.
- A group whose plan has ended no longer keeps the things the plan paid for. Everything already
  recorded stays readable, as it always has.
- Coupon codes handle some odd cases properly: a code with a fixed amount beside a zero percentage
  now takes the amount off instead of nothing, and a code that would have ADDED to the price is
  refused outright.

- Places are searchable now. "What's near you" has a fourth tab: public locations near you that
  groups have published work at. A location only appears once there is something to read at it, and
  somebody's home never appears.
- A published case now links to its place, so you can go from one group's write-up to everything
  anybody has shared about that location. Only for public locations.
- Your field sessions page shows how much storage you have used, and warns you before the cap rather
  than when an upload is refused. If a group's paid plan covers you, it says so instead of showing
  a cap that is not yours.
- A group that has gone read-only now says so at the top of the group, instead of letting you find
  out by pressing something.
- Accepting somebody into your group now tells you why when it cannot, with a link to your plan —
  it used to say "please try again" about the one thing trying again never fixes.
- Notification badges use the icon and colour set for that kind of message.
- An event that cannot be booked now says **why** — called off, or bookings closed — rather than
  showing a form or a greyed-out grid.
- Your event pass says who scanned you in. A case transfer says which person asked for it. Your
  desk shows how many borrowed items are overdue, not just which ones.
- A group's public page no longer names the city of an address you marked private. Two settings
  that never did anything — the address map and directions switches — have been withdrawn rather
  than left looking like they work.
- Withdrawing your own request for access to a file works properly, and a request somebody has
  already answered can no longer be withdrawn out from under them.
- Groups can rename a make, a model or an experience type, and are offered a merge when the name is
  already taken. Correcting a typo used to be impossible.
- Hosts can invite a guest by email, and book somebody in, from the bookings board — both of which
  the help has described for a while.
- A field session or a photograph that somebody flagged can now be looked at and put back. One
  report used to hide it permanently.
- You no longer need a group to investigate. A public location's page offers **Start investigating on
  your own** to anybody signed in with no group: it explains what it does, then sets up a private
  space of your own and opens the form to schedule your first visit there, in one step. It is free
  and nobody else joins it. What you record at a public location is public on a free account, which
  the page says before you agree to it.
- A public location also offers **Investigate here** for each of your groups, with the place already
  filled in.
- A public location's page now takes **posts**. Anybody signed in can add a note or a photograph
  about somewhere anyone can visit, and it appears on the place's page and on the feed alike, with a
  link back. Photos are checked, reported and hidden exactly as anywhere else on the feed. Somebody's
  home takes no posts.
- Signed in, a place also lists your own groups' cases there — published or not — so you can tell at
  a glance whether your group already has one.
- A case now names the place it is about. As you type the address, a place already on file that
  looks like it is offered, so your case joins the one everybody else has been working at instead of
  making a second copy of it. You say whether it is a public location or somebody's home, and the
  answer decides who may see the case.
- A place's page lists the cases groups have published there, beside the investigations and the field
  archive. A public place also offers **Open a case here**, and a case that names a place says at the
  top how many other groups have investigated the same location.
- A group with no plan now collects what it finds at public places in public. A case you open is
  public from the start, and a visit to a public location is shared with everyone; both say so where
  you would otherwise have been refused. A plan is what makes your work yours. Somebody's home is
  never affected, and nothing already private is changed.
- Investigating on your own no longer means doing without cases. What you may keep private is decided
  by your plan rather than by whether you work alone.
- A case you open yourself is accepted as you open it, instead of waiting as **Proposed** for a
  decision nobody was asked to make. If you would rather the group decided, tick the box on the new
  case form and it waits as it used to. Anyone who may open a case but not change one still proposes
  it, as before.
- Adding a file to a case shows it going. The file is named as soon as you choose it, with a bar
  that fills as it uploads, so a long recording is something you can watch rather than a still page
  you have to guess about. Several files at once each get their own line, and a refusal now says
  what the trouble was.
- On a research board, something you paste or drop no longer lands on top of a card that was already
  there. It goes to the nearest clear ground instead, the same way a card added from the toolbar does.
- A map box can be given an address instead of coordinates: write it and press Find. Pasting an address
  onto a board now makes a map of that place, and text nobody can place is still the note it was.
- A card, note or message with an address written in it offers **Make a map** on its own menu. The map
  lands beside it, and what you wrote stays where it is.
- A video or a recording on a case plays where it sits instead of showing a black rectangle with
  dead controls, and you can drag through it. The same applies to a case's public page, a place's
  archive, an event's evidence and a group's files.
- A busy research board keeps finding clear ground for new blocks. Past about thirty of them, anything
  added, pasted or dropped landed on the same square as everything else.
- The session player shades the stretch a phone was put away for, with the phone's own line under
  the trace saying what carried on, and names the two marks that bracket it.
- A link pasted on a research board builds a proper card — title, description, the site's name and a
  picture — and the picture is our own copy, so a published board still shows it to everyone reading.
- A research board can reach for what the client wrote. The case files button now opens two tabs, and
  the second lists the client's messages: pick one and their words land on the board with their name
  and the date, so what sent you looking sits beside what you found.
- A file added to a research board is marked **Research** in the case's Files, so you can tell which
  files the research is built from and rename the ones that came off a camera with a number for a name.
- A research card can be a historical note, an article, an experience, a quote or a person as well as evidence, each
  asking for what that kind of thing needs. Pick the kind while editing a card; changing it keeps what
  you typed. Until a card has a title, its heading says which kind it is rather than "Card".

## 2026-09-16

- A session's sound recordings no longer sit held out of a place's archive: the picture screener used to hold every audio file as an image that would not decode, so an archived night could show its readings but never its sound.
- A field session that was sent from the app as one session file can now be added to a public place's archive, and its recordings play there like any other.
- Research on a case is now a board you lay out yourself: cards, notes, pictures, maps, links and files, placed where they belong. It opens in its own editor, keeps what you are writing on your own machine, and stays yours until you publish it. Once published the group sees it, and anybody who can edit the case can add to it — changing what somebody else put there is for whoever put it there, a group administrator, or an administrator of the site.
- The older research pages have been replaced by boards. Research is also a kind of timeline entry again, for a note about what you read that belongs on the day it happened.
- Select a card on a board and four small + handles appear on its sides. Click one and the next card arrives there, already joined by an arrow and ready to type in; drag one onto empty board to put it where you let go, or onto another card to join those two. Ctrl+Shift and an arrow key does the same from the keyboard, and one Undo takes back the card and its arrow together.
- Present walks a board a card at a time, full screen, with everything else dimmed — for the meeting where a case gets talked through. Nothing to set up first: the cards are the slides, and the order is the one the board already shows — your groups if you drew any, otherwise the arrows, then down the page. Arrows, space or Page Down move through it; Escape leaves. Presenting changes nothing, so somebody who can only read a case can still drive.
- Zooming a map with a trackpad or wheel no longer scrolls the page out from under it part way through. The map keeps the gesture; the page stays where it was.
- A board can reach for a file the case already has, instead of sending a second copy of a photograph you uploaded last week.
- Recordings dropped on a board play where they sit — sound as a waveform, video in its own small screen at card size, resizable to whatever suits.

## 2026-09-15

- When a site role cannot be saved, the page now says exactly why instead of naming a SuperAdmin rule that may have nothing to do with it.
- Dates and times are typed the way you write them, like 09/15/2026 8:00 PM. A date that does not exist, such as September 31, is refused with a sentence saying why, instead of quietly becoming a different day; the calendar button is still there.
- When an administrator's new account is refused, the page now says exactly why instead of a general apology.

## 2026-09-14

- In every table, the line under a row now runs under its action buttons too.
- Buttons in tables are icons now, so a row stays on one line. Hover over one to see what it does.
- A group's Cases screen shows waiting requests in yellow with their count, and each status shows its count in
  that status's colour when it has cases.
- Research is no longer one of the kinds of timeline entry; research is written on the case's Research tab.
  Research entries already on a timeline stay, and the timeline can now be filtered to instrument readings.
- Editing a case opens a page of its own instead of a small window, with a tall description box and a short
  formatting toolbar. The status and publishing settings sit beside the description, and the page asks before
  you leave with unsaved changes. New cases get the same description editor.
- Case notes can be formatted with bold, italics, lists and links. Older notes keep their line breaks.
- Messages between a group and its client can be formatted too. Sending an empty message says so instead of
  doing nothing.
- Plans can be taken off sale. The prices stay on the pricing page with a line saying they aren't on sale,
  and the buttons to buy are removed.
- Every plan on the pricing page has a button. Without a group, it starts one with that plan chosen and then
  opens the new group's billing page; if you run a group, it opens that group's billing page with the same
  billing period selected.
- The pricing page's plans are redrawn: each has its own colour, an icon, the price large, its limits as a
  checklist, and the plan most groups start on is marked.
- Research on a case is written as pages. New page on the Research tab opens one: a stack of text, pictures,
  files, link cards and maps that can be pasted or dropped in anywhere and dragged into order. A page saves
  itself, stays a draft only its author can see until it is published, and later changes stay a draft until
  published again. On a phone, pictures can be taken with the camera.
- A research page keeps everything added to it under Files and links, where any of it can be put back on the page.
- A research page with a date appears on the case's timeline once it is published, and opens from there.
- A map on a research page shows up to ten places you choose, numbered, with straight lines, a walking route or a
  driving route between them. Each leg shows its distance and time, with a total, and the route opens in Maps.
- Links in group messages, case messages, feed posts and research pages show a card with the page's title,
  description and picture. While you write, the card appears once the address is finished.
- A research page's date is added with Add a date and can be taken away again; there is no empty date box.
- Each block on a research page has one handle beside it: drag it to move the block, click or tap it for the block's
  options. Clicking into a paragraph puts the cursor where you clicked, and formatted text pasted onto the page
  arrives with its formatting.
- Everyone in the group now sees the pictures and files on a published research page; before, only the person who
  added them could.
- On a case timeline, times are shown and entered in your own time zone; they were five hours out in the edit
  window. A new entry starts at the current time, and a client suggesting a different date starts on tomorrow evening.
- Bold words in notes, messages and research pages are properly bold.
- A case's messages open at the newest one and stay there as you send.
- A group's cases list names each case's manager instead of "Unassigned".
- A group's tab row fades at the edge that has more tabs, scrolls with an ordinary mouse wheel, and brings the chosen
  tab into view.
- Members tables say Yes or No for Active; the Clients tab's switch is labelled "Taking new cases".
- The bell shows "99+" in full, opening a notification scrolls to it, and the desk shows today's date in your time.
- On Public Investigations, a case you can open only because you are in its group is marked "Not public".
- The More actions menu on the organizations and page lists opens in full instead of clipped by the table.
- In dark mode, outlined buttons are readable; in light mode, the logo on phones and the sign-in card, the sidebar's
  Filter box and the work-waiting banners' links are too.
- On the investigation request form, example values start "e.g.", the missing address fields are named, and changing
  a checked address asks for it to be checked again.
- On a phone, the footer wraps instead of running off the screen, and the home map starts on the pins.
- Signing in from any page brings you back to that page — from the header, from a group's pages, and from the Sign in
  links beside voting, comments, polls, tour pages and event reviews.
- On a group's calendar, an event can be dragged to another day or time, or have its end dragged, and the move can be
  undone. Deleting an event asks first, and a refusal says why.
- A quick sweep of the mouse across a floor plan or seating plan chooses every square it passes over.
- When a SuperAdmin views the site as somebody else, the profile menu shows that person's name and picture.
- The "work waiting" banner for client requests opens the requests page, which every member who can read it can use.
  Accept and Decline appear only for the people who can answer for the group.
- Example text in the event pages' boxes starts "e.g.", so it isn't mistaken for something already filled in.
- A client's Home shows their case at the top — its state and the next visit — above the usual search.
- On a client's case, the calendar marks each day something was logged; the report's PDF button says it is preparing
  the file, and says so if it can't; downloaded reports have plain file names.
- Starting a request for an address you have already asked about says so, with a link to the earlier request.
- An accepted request's "View My Case" opens the case that request became.
- A case card marked "Not public" no longer offers the public vote.
- Signing in again from a SuperAdmin page brings you back to that page.
- Example text in boxes across the site starts "e.g.".
- A member sees the buttons they can use and a line saying who can do the rest: choosing a case's points of contact,
  editing or deleting somebody else's timeline entry, and uploading, sharing, publishing or deleting a group's files.
  The case publishing tour is offered to the people who can edit the case.
- In a case's messages, every message you did not write carries its author's name, including a colleague's reply to the
  client.
- Recordings no longer flash a pale grey box on a dark page while they load, and deleting a group file shows a success
  message instead of an error.
- A group's Viewers are read-only: they read the calendar and messages but can no longer add, move or delete events, send
  group messages, or change investigations, timelines or requests, whatever roles they hold.
- SuperAdmins have an Event health tab on the dashboard. It shows holds and how long parties wait for an answer,
  letters going out, errors on event pages, requests turned away by the booking limits, and when each
  background job last ran and whether it failed.
- On the SuperAdmin's list of hosted events, each row opens onto every screen of that event: bookings, plan,
  door, staff, menus, files and the rest.
- The door screen names the event it is for.
- A client's own case page and investigation list show when a visit starts and ends, instead of a line of
  program text after the start time. The same fix reaches a group's investigation panel and its scheduling
  proposals.
- A member who can read a group's events but not change them now sees them to read: the form to add an event
  is not offered, an event's page says who can change it, and nothing on it can be pressed by mistake.
- The list of people on the site no longer says there are no accounts while it is still loading.
- A photo whose thumbnail was interrupted while it was being made no longer shows as a blank square; the
  thumbnail is made again.

## 2026-09-13

- A guest who asks to stay with no room preference now sees "Waiting to be placed" for each night until the
  venue chooses a room, instead of "Just for the day". The booking board, door list, calendar file and
  spreadsheet say the same.
- Confirming a booking and moving the party into a different room no longer fails with an unexpected
  error, and guests can change their nights or the names in their party again.
- The events dashboard loads when it is opened directly from a link, not only from its tab.

- SuperAdmins have an Events tab on the dashboard, with charts of events by state, bookings, event credits,
  the busiest organizers and venues, and where events happen.
- SuperAdmins have a list of every hosted event, showing the organizer, the dates and the state, with
  buttons to view or remove each one.
- Removing an event takes it off the site, returns its event credit, tells everybody with a place that it
  is not going ahead, and emails the organizer a link to appeal. An upheld appeal brings the event back
  as a draft.
- Organizers whose event was removed see why on the event's page, and can appeal from there once.

- Organizers can write to everybody with a place at an event from the booking board: everyone confirmed,
  only the people there on one date, or people still waiting as well. The card shows how many parties
  the letter will reach before it goes, and keeps a list of every letter sent.
- A published event's page opens with its numbers at a glance: people coming, parties waiting for an
  answer, places left night by night, day passes, arrivals at the door and the reviews.
- Events can say how to get in and around, such as stairs, step-free doors, low light or parking. It
  shows on the public event page, in the iPhone app and in the confirmation letter.
- Sharing an event or a venue page on social media now shows its picture.

- My events lists each event once, however many times you asked for a place at it, showing the booking
  that is live or the most recent one.

- Organizers can seat confirmed parties at dining tables for each meal of an event, copy one meal's
  seating to another, and print a table list with dietary notes. Guests see their table on their pass.

- An empty seating or room plan can start from the plan used at the same venue before, with another
  group's prices and notes left out.
- Venues keep a photo library. Organizers can offer pictures from their event's gallery, and the venue
  decides which to show on its page.

- An event's files, gallery pictures and room photo links are kept for 90 days after it ends. The
  organizer is written to a month and a week before, and can pick what to keep and download it as one
  zip. Guests keep their own photos.

- The morning after an event, everybody who came can be sent a thank-you with the organizer's note,
  the pictures and what is coming up next. Guests who had a place can review the event for two months,
  and organizers can hide a review but never change it.
- What I'm going to now lists the sessions you signed up for under each event.

- An event can be copied to start the next one: the plan, menus, programme, bands, helpers, advert
  and files come across as a draft on the new dates, and bookings and photos stay behind.
- The booking board can hand over every booking as a spreadsheet, including the phone number each
  guest gave and which nights they arrived.

- You can choose seats or rooms at a hosted event without an account. Give your name, email address
  and phone number, and press the button in the letter within fifteen minutes to hold them; the same
  link shows whether the venue has answered and lets you let them go.
- Booking forms for hosted events ask for a name and phone number the organizer can use to reach
  you, and say plainly that they go to the organizer for that event only. Organizers see the number
  on the booking.

- Event pages open with the host's pictures and a countdown to the first night, and on a phone keep an
  ask-for-a-place button in reach. Hosts manage the pictures from a new gallery page.
- A group's own public pages can show one of its events — its dates, programme, pictures or venue — and
  the section keeps up with the event by itself.
- A group's ad can now lead to one of its events. The card shows the event's date and stops showing once
  the event is over.
- Each event has room for 2,000 MB of files and photos, and a guest agrees, the first time, before their
  photos can be shown in the room and on the photo wall.

- Events have a room: a private space on the event page for confirmed guests, the organizers and
  helpers to post what is happening. A photo stays the person's own, and they choose whether to send
  it to the organizers and the venue.
- A full-screen photo wall shows the room's photos one at a time for a screen at the venue, and a
  phone-friendly page lets guests add photos during the event. Organizers can keep photos to their
  own team.

- Events can have files — a guest pack, a stewards' briefing, a poster — each marked for the event's
  own people, confirmed guests, or anybody. The event page lists what each visitor may download, and
  downloads are checked every time.

- Events can have a programme: classes, talks and meals night by night, on the venue's clock. Guests
  with a confirmed place sign up for sessions that have a limit, join a queue when one is full, and
  are written to when a place comes free, a session moves, or one is cancelled.
- The event page shows what is on now and next, how many places each session has left, and an
  add-to-calendar link for every session.

- Places now have contact details — websites, phone numbers and email addresses — each public or
  private to the group that recorded it, for things like a coordinator's number for scheduling.
  Once a venue is confirmed, it keeps its own public details.
- A group can claim a place it runs. It proves the claim with a code sent to the venue's own public
  email address, one somebody outside the group recorded, or by asking a person to review what it
  sends. A proved claim waits a week, during which the groups that know the place can object.
  Nothing already booked at the place changes.

- A group can now be confirmed as the venue at a place, and other groups then ask that venue before
  publishing an event there. The organizer asks from the event page; the venue answers from a new
  hosting requests page, and a yes can also lend its rooms, the building's story and its own people
  on the door.
- A venue can take a yes back with a reason. Every published event resting on it stops, its guests
  are written to with the venue's reason, their passes stop working, and the organizer's credit
  comes back however close to the date it is.
- Confirmed venues can show a page of their own with the building's story, their rooms and
  everything on there, and the place's page names who runs it.

- Whoever decides bookings for an event is now written to when they arrive. The first request in a
  while arrives within minutes; a busy hour becomes one letter saying how many more came, never
  more than an hour late. A letter says what was asked for and when, and never the guest's name or
  address — those stay on the booking board.
- A daily letter says where each event stands: who has waited longest for an answer, holds about to
  run out, what is left on the next night and, while the event is on, how many came through the
  door. It is weekly once bookings close, and it is never sent when nothing is waiting.
- The notifications page has a new section, letters about bookings, for choosing group by group
  whether those letters come as bookings arrive, once a day, or not at all.
- The bell now counts places a guest has picked and is waiting to have confirmed, and gives holds
  about to run out a row of their own — for the venue and for the guest. A hold you placed yourself
  is no longer announced to you as an answer.

- Events can have **bands** — the wristbands, lanyards or stamps you hand out so a steward knows by
  glance what somebody is here for. Each is your own colour and what it means, and most parties'
  bands are worked out from what they booked rather than tagged one by one.
- A band shows beside the guest's name at the door, on the booking board, and on the guest's own
  pass, always with its name as well as its colour. Anything the site cannot work out, like who is
  having dinner, is given out by hand from the booking board.

- An event can now have its own **staff**: people who help at that event and nowhere else. Invite somebody
  by email — they need no account and no place in your group — and say what they may do: run the
  door, see who's coming, decide bookings, menus and dietary, files. An invitation grants nothing
  until it is accepted, and the list says so.
- The door is a screen of its own now, made for a phone. It scans a pass with the camera, takes the
  code typed in when the camera is no use, and finds somebody by name when they have lost the email
  entirely. Every reason a camera will not start comes with what to do instead.
- Arrivals are recorded **per night**, so a three-night weekend can say who was here on the
  Saturday. Marking somebody in, out, or not-them-after-all is one big button each.
- The door says **how many more people could come in tonight** — counting everybody expected and
  everybody who has already walked in — and somebody who simply turns up can be written down on the
  spot. Let too many in and it refuses, because a number nobody enforces is not a number.

- You can now get a place at a hosted event from its own page. Where the venue sells by the
  square, you tap the seats or rooms you want and hold them; where it takes requests, you say
  which nights you are coming and what you would like, and they place you.
- What you pick is the size of your party — three seats is three people — and the summary adds it
  all up, with the price and who you settle it with, before you commit to anything.
- Holding places puts them out of everybody else's reach while the venue answers, and the page
  says how long that lasts before you press the button and counts it down afterwards.
- If somebody takes a seat a second before you do, you are told which one went, by name, and
  everything else you chose stays yours.
- On a phone the summary and the button stay at the bottom of the screen while you choose, so you
  never scroll back through two hundred squares to find them.
- You do not need an account to look at a seating plan. Signed out you see exactly how full the
  house is, and the way in by email is still there underneath.
- Your booking says one thing plainly — asked for, held for you, you're coming, not this time, or
  the hold ran out — with what it means and what you can do next.
- An event that only runs if enough people come says so from the first time you look, along with
  the date the venue decides by.
- There is a page listing every event you have asked to come to, with what you have coming up
  first and anything you asked for and did not get underneath. The bell now lands there when a
  venue answers you, instead of on the list of everybody's evenings.
- Your pass shows the code large, and says in words everything the code says — who it admits, how
  many, which nights and which room — with a short code to read out when a camera will not focus.
  It prints, and it can be emailed to you again. A withdrawn pass is shown with the venue's reason
  rather than going blank.
- Asking for a place now sends you a letter that says your request arrived and that nothing is
  held yet, and holding places sends one saying until when — on the venue's clock, not ours.
- Calling an event off now actually tells the people who had places, including everybody still
  waiting on an answer. The organizer's screen has been saying so for a while; the letters are new,
  and it now counts them rather than claiming them.
- Reaching a minimum number tells everybody who was waiting to hear whether it was going ahead.
- Clicking an emailed link for a hosted event now says **you've asked for a place** rather than
  "you're coming", because the venue has not answered yet — and offers you a way to sign in
  properly on the account it just made you, so you can choose your own seats next time.
- A new help page, **Going to an Event**, covers all of it.

## 2026-09-12

- Events have a **Menus** page: every night, and under it every sitting you serve — breakfast,
  lunch, dinner, snacks, a late supper. Dishes are typed one to a line with the course before a
  colon if you use courses, arrows move a sitting to where it actually falls in the night, and one
  button saves the weekend.
- Sittings read in the order you arrange them and never by the clock, because a night runs from
  the evening people arrive through the morning they come down — so the eight o'clock breakfast
  belongs after the seven o'clock dinner it followed.
- Every guest's dietary note now becomes the sheet a cook works from — **what the kitchen needs**,
  from the event page or the booking board. It says how many people you are expecting, how many
  told you something, and how many nobody has named. The same words typed by several people are
  counted together; two different wordings of the same thing stay two lines, because guessing they
  are one would eventually merge two that are not.
- The kitchen's sheet can be read one night at a time, and built as one sheet for each night so
  every service prints on its own page.
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
