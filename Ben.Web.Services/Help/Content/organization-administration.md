---
title: Administering a Group
summary: Settings, members, requests, and the decisions only owners and administrators make.
section: Group Administration
audience: OrganizationAdministrator
order: 60
---

Visible to owners and administrators of a group. Managers and members work cases but do not
configure the group itself.

## Group settings

Everything a group can configure lives on the **Settings** tab, and there is a lot of it: billing,
promotion, feed attribution, address display, media privacy, the kind of group you are, walking
tours, the role new members start on, calendar event types, member titles, investigation duties
and the duty eligibility matrix.

The row of buttons at the top of the tab jumps to each of those in turn. Nothing is hidden behind
them — the whole page is still one page you can read top to bottom — they are there so you can
find out in one glance whether the thing you came to change is on this screen, and go straight to
it if it is.

The group's **Edit** screen — the button beside the group's name, not the Settings tab — holds its
public identity: name, URL, contact details, and two switches worth understanding.

**Accepting membership applications** controls whether people can apply to join.

### Your web address

Your group lives at `/o/your-address` — the link you put on a card, in a post, or in an email to a
client. It can use lowercase letters, numbers and hyphens, like `ghost-squad`. Spaces, slashes and
punctuation are refused, and a handful of words the site uses itself are reserved.

No two groups can share an address, and **you can change yours whenever you like**. When you do:

- **The old address keeps working.** Every link anyone has already shared still opens your pages —
  visitors are simply moved along to the new address, so what they copy next is the current one.
- **The old address stays yours.** No other group can take it later, even years on. Pointing
  somebody's saved link at a different group would be worse than the link not working at all.

So changing your address is safe. Changing it often is still worth avoiding, because the address is
what people remember.

**Let clients see members' private photos** is one half of a two-key rule. Turning it on shares
nothing by itself; it only permits members who have *also* opted in on their own profile. This is
deliberate: a group cannot volunteer its members' faces, and a member cannot overrule a group that
has decided its people stay unidentified to clients.

## Members and roles

![The group's members list](help-media:organization-administration/members.png)
*Members and their roles. Roles decide what each person may do inside the group.*

| Role | Typically |
|---|---|
| Owner | Created the group. Full control. |
| Administrator | Configures the group and manages members. |
| Manager | Runs cases day to day. |
| Member | Works cases and attends investigations. |
| Viewer | Reads without changing anything. |

## The roles every group starts with

Every group begins with seven ready-made roles — **Case Manager Role, Equipment Manager Role,
CMS Manager Role, Client Manager Role, Content Manager Role, Historian Role, Secretary Role** —
each a starting point you can edit, rename, or delete like any role you build yourself. The
"Role" suffix is deliberate: member *titles* (the ladder below) say how senior somebody is,
roles say what they may do, and the naming keeps the two impossible to confuse.

Every group also has an **Investigator Role** — read the group's cases and investigations — which
is the one an ordinary member needs to do anything at all.

## What new members start with

**Settings → New members start as** decides which role somebody is given the moment they join,
whether you added them yourself or accepted their application. A new group starts on
**Investigator Role**, which is what lets a member open the cases they are put on.

This matters more than it sounds. A membership rank alone opens nothing below Administrator: a
Manager, Member or Viewer with no role can be named on a visit roster and see the group's cases
listed on their desk, and every one of those links will refuse them. Owners and administrators
are skipped, because their rank already opens everything.

You can set it to **Nothing** and hand out roles yourself. Groups that existed before this
setting arrived have it set to nothing, so nothing changed for them.

Changing it applies to people who join **from then on**. It never reaches back into your existing
roster — a setting that silently re-permissioned everybody would be a worse surprise than the one
it prevents. To change what somebody already has, use their entry on the Members screen.

## What role grants open today

**Since the enforcement flip:** reading the group's cases and investigations follows role
grants, not bare membership. Everyone who was already an ordinary member when the flip arrived
holds the Investigator Role and noticed nothing; a member without any case-reading role no
longer sees the Cases and Investigations tabs at all — the tabs and the server give the same
answer. Owners and administrators always see everything.

Three of the role editor's sections started doing real work with the roles arc's Phase B:
**Cases** (creating a case, and editing one you don't manage), **Client Requests** (accepting or
declining what clients send in), and **Calendar** (event types, and managing other people's
attendance). Each is strictly additive: owners and administrators keep everything they had, and
a role grant now opens the same doors for the members who hold it. Nothing was taken from
anyone — the grants only add. The remaining sections' grants become decisive as the later
phases land; each section's own description says where it stands.

## Banners for work that blocks other people

When a client's investigation request or a membership application is waiting on your group, a
banner appears under the site-wide announcement on every page — for you and anyone else whose
permissions let them open that queue, and for nobody else. Each banner names the group and the
waiting count and links straight to the tab where you act. Dismissing one keeps it away for the
rest of your session; a new arrival brings it back.

## Having a plan, and not having one

A group is free by **having no plan at all**, not by being on a free one. There is no such thing
as a free group plan: an individual can be free, a group cannot.

With no plan a group can do public work — public cases, public investigations, results anyone can
read. A plan is what lets it work with other people, take private-residence and client cases, and
keep its own sessions to itself.

The price is set by how many members the group has, and the **Billing** screen under Settings shows
the band you are in and what it costs. If the price list has no charge for a group your size, that
screen says there is nothing to buy rather than offering you a plan for nothing.

## Custom-role permissions and your plan

Each subscription plan includes custom-role permissions for a set of areas — cases, calendar,
files, and so on — and the **Pricing** page lists exactly which areas each plan includes, so
what an upgrade buys is never a surprise. Most plans include everything; when yours excludes an
area, the role editor says so rather than hiding it:

- Sections the plan does not include are **grayed out with their toggles disabled**, under a
  note naming the areas and the plan.
- **Nothing is deleted.** Grants you configured in an excluded area are kept exactly as you set
  them — the note counts them — and apply again the moment a plan change includes the area. A
  downgrade pauses; it never erases.
- The rule holds server-side too: a save can change only the sections your plan includes, and
  everything else is carried forward untouched.

When an administrator changes what a plan includes, groups on that plan are told: newly included
areas are announced right away, and removals arrive as a notice — before the renewal that
applies them for paid groups. Owners and group administrators are unaffected by area
exclusions, exactly as they are unaffected by role grants: they always hold every permission.

## Member titles — the ladder

Separate from roles, on purpose: a **title** says how senior somebody is in the group — a
**role** says what they may do. The two never mix. Giving somebody a grander title changes
nothing about what they can access, and a brand-new member with the right role can do things a
ten-year veteran without it cannot.

Every group starts with the same five-rung ladder — **Probationary, Junior Investigator,
Investigator, Senior Investigator, Lead Investigator** — and it is entirely yours to change:
rename rungs, reorder them, add your own, or delete the ones you will never use, all under
**Settings → Member titles**. Deleting a title simply clears it from anyone holding it; nobody
loses any access, because titles never granted any.

Assign titles from the **Members** tab — each row has a title dropdown if you administer the
group. A member's title shows as a badge beside their name on the roster. Someone who belongs to
two groups holds a separate title in each, which is exactly how it should be: seniority is
earned per group.

## Investigation duties

Under **Settings → Investigation duties** lives the list of jobs your group hands out per visit
— every group starts with Lead Investigator, Equipment, Equipment Assist, Evidence Collection and
Documentation, and the list is yours: rename, add, retire. A duty marked **one holder** (like the
lead) can only be held by a single person per visit; assigning it to someone takes it from the
previous holder.

Equipment comes as two jobs on purpose. Assisting with the gear is something a newer member can
do long before they are the one running it, and one combined duty cannot say that.

## Who may hold which duty

Under the duty list sits the grid: your titles across the top, your duties down the side. Tick the
titles each duty is open to. A new group starts with this filled in — an Associate can document
and assist with equipment, a Junior Investigator adds evidence collection, an Investigator runs
the equipment, and the Lead Investigator duty is open to your two most senior rungs.

The ask stays deliberately soft. Whoever manages a visit can assign past it with an explicit
*Assign anyway*, and the exception is marked on the assignment — the senior calls in sick and the
capable junior steps up, and a hard wall would just send the group back to organising by text
message.

Two exceptions to that.

**A duty that carries something under *On the night* takes an owner or administrator to assign
past.** Handing somebody point of contact, or the right to hand out the other duties, is a
different act from handing them a label, and the person given it could then make exceptions of
their own. Whoever is running the visit can still assign it to anybody the duty is already open
to; it is only the exception that needs the extra pair of hands.

**Tick *no exceptions* on the few duties where the title is a real qualification** rather than a
preference — certified equipment, or being the client's point of contact inside their home. Then
nobody can assign past it on the night, not even an owner. The way to change it is to change this
grid, which is a deliberate and visible act rather than a decision taken at nine o'clock at a site.

A duty whose row is untouched says so, and falls back to the older **minimum title** setting on
the duty itself — "asks for Investigator or above". That is the single-threshold version of the
same idea, and nothing changed for groups that were already using it.

### On the night

Two of the columns are not about who may hold the duty but about what holding it lets somebody do,
**for that one visit only**:

- **Point of contact** — the person to call about this visit. It shows on the roster so the group,
  and the client, can see who is answerable that night.
- **Hands out duties** — may give and take back the other duties on this visit, without being able
  to change anything else about it.

Both end when the visit does. Duties still grant no standing permissions: what somebody may do in
the group as a whole is decided by roles, under **Roles & permissions**, and a duty can only ever
open a door that the roles left shut for this one night. It can never close one.

## The Investigations tab

Every visit the group has run, mapped and listed, including ones that belong to no client case.
It is visible to every member, not only administrators — knowing where the group has worked is
ordinary team knowledge, not an administrative secret.

**Schedule an investigation** books a visit with no case behind it. What the group finds there is
shared under the same rules as any other investigation.

## Who can change an investigation

Being a member is enough to *read* an investigation and to answer for yourself — your own RSVP and
your own arrival. Changing the record itself is narrower. Any one of these is enough:

- whoever scheduled it
- the case manager, for a visit attached to a case
- whoever is leading that particular visit
- group owners and administrators
- anyone the group has granted the **Investigation** permission under **Roles**

That last one is the lever you have as an administrator. Grant a role the *Update* action on
**Investigation**, give somebody that role, and they can edit any of the group's investigations
without being made an administrator of everything else.

**Leading a visit is not a rank.** It is set per investigation, in the **Lead** column of the
investigation's team list, and it ends with that visit. One person leads at a time — naming a lead
takes it from whoever held it — and the current lead can hand it on, which is what happens when
somebody leaves halfway through the night. Somebody senior enough to lead every visit is better
served by the permission above; somebody leading one Saturday is better served by the lead flag,
which stops mattering on Sunday.

## Being found by clients

A client looking for help searches by where they are. Your group appears in that search only when
all three of these are true, and the **Clients** tab beside Requests is where the first two live.

- **You are taking client cases.** New groups set this when they are created. A group that is not
  taking cases is left out of client search entirely, which is the right answer when you only work
  your own investigations.
- **You have an operating area.** A town or city and a radius. Without it the site has no way to
  decide whether you are near anybody, so it cannot offer you to anyone.
- **The group is listed.** Unlisted is on the **Edit** screen and keeps you out of search, browse
  and nearby results — including client search.

You can also choose whether to **accept clients outside your area**. Those clients still see you,
below the groups whose area covers them.

If you are taking cases but one of the other two is missing, the group page says so at the top.
That notice is the usual reason a group waits a long time for a first request.

## Incoming requests

Client requests arrive under the group's **Requests**. Mark one as under review while you decide.
Declining tells the client, and lets them send it elsewhere without rewriting it.

## The group calendar

![The group calendar](help-media:organization-administration/calendar.png)
*The calendar carries investigations and the group's own events.*

The group's **Calendar** holds meetings, training, anything with a time attached. Investigations
are scheduled from the case they belong to, not here.

**Where it happens.** Type a free-text location, or pick one of the group's saved addresses. Prefer
the saved address: it is stored as a reference rather than copied text, so correcting the address
once corrects every event held there. A **meeting link** can sit alongside it — paste a Zoom, Teams
or Meet join link, and a bare `zoom.us/j/123` is accepted as readily as the full URL.

**Repeating events.** The **Repeats** list covers the usual patterns — daily, weekly, monthly, and
so on. **Custom** takes an iCal rule directly (`FREQ=WEEKLY;BYDAY=TU`) for a pattern the list does
not cover.

**Invites.** A public event is open to the whole group by definition, so invites appear only on a
non-public one. Tick members from the list, or type an email address to invite somebody outside the
group. That lookup only finds addresses their owner has published *and confirmed* on their own
profile — never the private address someone signs in with. If an address does not resolve, the
person either has no account or has not published that address; ask them rather than guessing at
another spelling.

Invites are invitations, nothing more. Assigning work belongs to investigations, where it already
exists.

## Calendar event types

Under **Settings**, *Calendar Event Types* is the list the calendar's **Type** dropdown offers —
Investigation, Meeting, Training, whatever the group actually does. A group with no types can
still create events; the events simply have no type.

Each type carries a name, an optional colour and icon, and a sort order that decides where it sits
in the dropdown. Only owners and administrators can change the list.

Clear **Active** to retire a type. It disappears from the dropdown for new events but stays on the
events that already use it — which is almost always what you want. Deleting one removes it from
those events too, leaving their date and title untouched.

## The Equipment tab

Two lists, because a group deals with two kinds of gear.

**The group's gear** is equipment the group itself owns. Every member can see the list — what the
group has is not a secret from the people who use it — but adding, editing and deleting it needs
the **Equipment** permission, and so does seeing serial numbers.

Each piece tracks who is holding it, when it was last serviced, whether it is currently faulty, and
its photos.
**Held by** can be set by hand: kit gets passed over in a car park without anyone opening the app,
and the record should still be able to tell the truth. A holder has to be an active member.

**History** on a piece opens two things: the full account of everything that has happened to it —
loans, requests for more time, service and faults, newest first — and the service log itself, where
new entries are added.

The service log part is what you write to. Anyone can read it; adding an entry needs the
Equipment permission. The entry type does real work rather than just labelling the note:

| Entry | What it does to the item |
|---|---|
| Serviced | Moves its last-serviced date |
| Fault reported | Marks the piece faulty, and the note becomes the reason |
| Fault fixed | Clears the fault |

The entry and its consequence are saved together, so the log never disagrees with the piece it
describes. Entries are kept — fixing a fault does not erase the report of it.

Once a piece has any loan or service history, deleting it is refused and you are asked to
**retire** it instead — the button sits in the item editor. Retired gear drops out of borrowing,
out of the public catalog and out of the group list, while everything that happened to it stays
readable. You can put it back in service the same way. Destroying a serial-numbered asset would
take the account of what happened to it along too.

**Members' shared gear** is the second list: equipment members own personally and have chosen to
show this group. It is read-only here — it belongs to its owner — and it needs no permission to
read, because the sharing itself is the owner's decision, not the group's to gate. Serial numbers
stay with owners even here.

The **Borrowing** column says whether a piece can be borrowed at all, which is a separate choice
from sharing it — a member can let the group know they own something without offering to lend it.
Whether a particular piece is offered to the group or only lent personally is shown when you
actually ask to borrow it, since that is the point at which the difference matters.

A shared piece disappears from this tab if its owner leaves the group. Their sharing choice is
kept, so it comes back if they rejoin.

## The two equipment permissions

Under **Roles**, equipment has two entries, and the second sits underneath the first because it is
narrower:

- **Equipment** — manage what the group owns: add and edit gear, upload its photos, keep its
  service log, set who is holding a piece, and see serial numbers. Read access to the list needs
  nothing at all beyond membership.
- **Equipment Checkouts** — run the loans desk: approve and deny borrowing requests, hand gear over
  and receive it back. This is the "Equipment Management" job in item #55's own words.

They are separate so a group can hand someone the loans desk without also handing them the catalog.
Deciding what the group buys and owns, and lending out what it already has, are different jobs and
often different people. Granting **Equipment** does not grant **Equipment Checkouts**.

Neither permission has any say over a member's personal gear. A loan of somebody's own equipment is
always approved by its owner.

## The Files tab

The group's file library: upload directly, or pull in a member's own file with **Share from
User**. That button opens a picker listing everything the group could take a copy of — files
their owners have shared with your group first, then public files — with search, a type
filter, and thumbnail or list views. Choosing one copies it into the group's library; shared
files arrive **private by default**, and someone with publish permission approves them before
they ever appear publicly. The original stays the owner's: only a file's owner can delete it,
and a group removing its copy never touches the member's own file.

## Editing your public pages

*The CMS editor has a built-in walkthrough — the **?** button beside New Page runs it any
time, and it runs itself once for first-time editors.*

![The CMS page list](help-media:organization-administration/cms.png)
*Your public pages. A page is not visible to anyone until you publish it.*

Pages you build under **CMS** appear on your group's public site. Two things control what visitors
see, and they are different.

**Published** decides whether the page exists for visitors at all. **Drafts** decide whether the
edits you are making right now are visible while you make them.

### Drafts

Editing a page that is **not** published changes it directly. Nobody can see it, so there is nothing
to protect.

Editing a page that **is** published works differently. Open it and you will be offered **Work on a
draft instead**. Take it, and your changes go into a copy — the live page carries on exactly as it
was until you press **Publish changes**. Until then, visitors see the old version, and you can
**Discard draft** to throw the work away and leave the live page untouched.

A page has one draft at a time. If a colleague has already started one, you will see a note saying
so with a link to open it, rather than being handed a second copy nobody knows about.

Publishing keeps the page's address and its permissions — the live page is updated in place, not
replaced, so nothing linking to it breaks.

### Page addresses

A page's address is the last part of its web address — `/o/your-group/about`. Keep it short and
lowercase; it is what people paste to each other.

A few words are used by the site itself — **cases** and **events** today, plus some held back for
things coming later. A page can't use one, because it could never be opened: the site would show its
own page at that address instead. If you try, you'll be told which word and offered a way round it —
"our-cases" rather than "cases".

If you have an older page marked **Unreachable** in the list, that is one made before this check
existed. Rename it and it will start working.

### Pictures on a page

An **Image or banner** section takes a picture from your group's files — **Choose an image** opens
the picker with everything shared with the group; search it or filter by visibility. If the image
you pick is **members only**, the editor says so on the spot: the page will render the picture for
you and show visitors a broken frame, because a public page cannot hand out a file its owner kept
private. Pick a public image, or make that one public on its file page first.

### Seeing a page before it goes live

**View as visitor** on the editor opens the saved page exactly as the public renderer draws it,
published or not. That is the honest check: the side-by-side panel next to the editor follows what
you are typing, but it draws its own approximation in half a column, so a page can look right there
and wrong in reality.

The preview also tells you if the page is not in the menu yet, which is the usual reason a
freshly-published page seems to have vanished.

## Reusing a page layout

Built a write-up you want to do again? On the page's editing screen, **Save as a layout** keeps its
sections — their types, titles and current wording — as a named layout, for example *Investigation
Results*.

Next time you add a page, pick that layout under **Start from one of our layouts** and the new page
arrives with those sections ready to fill in.

Two things worth knowing:

- **The layout belongs to the group**, not to whoever saved it, so it stays when people come and go.
- **The copy is yours.** Editing the layout later does not touch pages already made from it, and
  editing one of those pages does not change the layout.

Sections that point at a case or an investigation keep the *shape* but you will want to re-point the
pickers — a layout reused for a different night should show that night's work.

## Putting your cases and investigations on a page

Two section types — **Our investigations** and **Our cases** — let you show your own work on a
public page. Pick which records to show, and answer two questions about each section.

**Where it happened.** You can show the rough area, or nothing at all. There is no option to publish
the exact address, whichever way you set it: the point shown on the map is the centre of an area
several miles across and cannot be traced back to a building. That is deliberate — a case is
somebody's home, and a link outlives the page it was on.

**Who it was for.** Cases can show the client's chosen name. Only the alias the client picked, or
the one your group set for them — a real name is never published here. If neither is set, the case
appears with no name.

**Work that is not public yet** is flagged in the picker, and adding it asks you to confirm. Until
you do, those records are left off the page entirely. Publishing a private investigation makes it
readable by anyone who visits, including the person it is about.

You can only show your own group's work. Everything is looked up fresh each time somebody views the
page, so if a client later asks to be unnamed, they disappear from pages you published months ago —
you do not have to remember which ones.

*On any case page, the **?** button beside Edit Case walks through exactly how a case goes
public — the pseudonym, generalized addresses, and which media may appear.*

## Putting a case's photos on a page

**Photos from a case** shows pictures and files taken from one of your own cases. Choose the case,
then tick the files you want.

**Only some of a case's files can be published, and this is not a setting you can change here.** A
file appears in the picker when it is attached to a timeline entry marked **Public**, on a case that
is itself public. Files on the case's **Files** tab never appear, whatever they are.

That is on purpose. There is exactly one way to publish a case file — put it on a public timeline
entry — so there is one place to look when you want to know what is visible, and one place to change
your mind. If a photo you want is missing from the picker, add it to a public timeline entry on the
case first.

**Captions** are off unless you turn them on. A caption is the timeline entry's title, which is your
group's own working description of what happened — useful to you, and sometimes more than you would
choose to say to a visitor.

**The photos stay linked to the case rather than copied onto the page.** If somebody later makes that
timeline entry private, or the case is unpublished, the photos come off every page that used them, by
themselves. You do not have to remember which pages they were on. The same is true of the direct
image links — a link copied out of the page stops working at the same moment.

## Tours

If you run ghost walks, a **tour** is the thing you sell and the thing your plan is priced on. The
walk down Printers Alley is one tour whether you run it twice a year or four nights a week; a
second walk down a different street is a second tour. **Administration → Tours** is where they
live, and each one has its own page.

Two things tell your tours apart. The **meeting point** is required and must be one of your own
addresses, so it can be corrected in one place and every date follows. The **name** is what
separates two tours that leave from the same corner, so it has to be different from your other
tours — and it is what a guest will look for, so use what is on your leaflet.

A tour also carries how long it runs, how many people a date takes, its time zone, a line about
how to reach you and how to pay, and a description. Nothing about money passes through this site:
that line is where you tell your guests what to bring and what it costs.

![Your tours](help-media:organization-administration/tours.png)

Opening one gives you everything about it on a page of its own — and a picker at the top for
switching between them when you run more than one.

![One tour](help-media:organization-administration/tour-details.png)

### Adding a tour, and what it costs

Your plan covers one tour. Adding another is charged for the part of your current period that is
left, on the card you already have on file, and after that every renewal counts the tours you are
running. If there is no card on file the tour is still yours to run and simply gets counted at
your next renewal. The Tours page tells you which of those is about to happen before you press
the button.

**Retiring** a tour stops it taking new dates and drops it from your next renewal. It refunds
nothing for the period you have already paid for, and it leaves everything the tour has already
run exactly where it is. **Pausing** — turning off *Taking sign-ups* — is the lighter version for
a closed season.

### Guides

You do not have to be a guide yourself. Add the people who actually walk it from your members, and
each date starts with those guides on it — then change them per date, because a tour led by two
people is not the same person every night.

Every guest who signs up is told who is leading their walk, by name, with their photograph when
they have made one public on their profile. That is a safety thing: somebody meeting a stranger
after dark should know who they are looking for. The photograph is optional and always theirs to
publish or not; a guide who has not published one is simply named.

### Dates

A date is the tour happening, and it goes on the **calendar** like anything else — pick the tour,
set the time, and the meeting point, length and group size come from the tour unless you say
otherwise. Scheduling a tour again costs nothing; the plan counts tours, not nights.

A public date of a tour business has to name a tour. Without one there is nothing to tell a guest
where to stand or who they are meeting, so the calendar refuses it and says so. A private date —
a guides' meeting, a rehearsal — needs no tour.

### Sign-ups: nobody has a place until you say so

![The sign-ups waiting on a date](help-media:organization-administration/tour-seats.png)
*Waiting on you first, because it is the only part of this page that is work.*

A sign-up for a walk is a **request**. It holds no place until a guide or manager approves it —
which is where you say the money side is settled between you and your guest. **IsHaunted never
takes payment.** Approving records that the two of you agree; how they pay you is yours to arrange.

Open a date's **Sign-ups** from the tour's Dates list. The list shows a "waiting" count on each
night, so you can see where the work is without opening every one.

- **Approve** holds the places and sends your guest the tour's welcome email, with the meeting
  point and the calendar file. Nothing goes to them before this — telling somebody where to stand
  for a walk you have not agreed to would send them out to nothing.
- **Turn down** says so plainly. The row is kept rather than deleted, because a guest who is not
  coming has to be able to see that they are not coming.

A sign-up can ask for **more than one place**, and a date's capacity counts places rather than
people. If a party does not fit, the refusal names how many places are left — so you can go back
to them about a smaller group rather than guessing.

**A full walk keeps taking requests.** The overflow is a waiting list you work through, not a
closed door, and it stays your decision. If somebody turns a place down, the room is there for the
next party in the queue.

Once you have approved a seat, your guest can tap **Got it** to say they have seen it. That is
entirely optional and nothing waits on it — it just tells you they are expecting to be there.
Their sign-ups also show on your bell as **Sign-ups waiting on you**, for group owners and
administrators.

### The email your guests get

When somebody's seat is approved, and again the night before, they are sent an email about the
walk with a **calendar file** attached, so it lands in their diary with the meeting point and their
phone reminds them.

You can write that email yourself under **Tours → the tour → Guest email**, or leave it empty and
we send a complete one. Drop in the things that change per date by clicking a placeholder and
pasting it: the tour's name and meeting point, the date and time in your tour's own time zone, who
is guiding that night and their photograph, how many places are left, and your own contact line.
Anything we do not recognise is simply left out, and **the meeting point is always included**
whether or not you ask for it — a guest who cannot tell where to stand has not been told about the
tour.

**See what it looks like** renders it against your next scheduled date, with your times, your
guides and your wording, and shows the calendar file that travels with it. Nothing is sent.

![The guest email](help-media:organization-administration/tour-guest-email.png)

Replies go to your group's public email address when you have set one, so a guest who hits reply
reaches you rather than us.

### Pictures and the slideshow

Each tour keeps up to 50 pictures. Add your own from **Tours → the tour → Pictures**; the first one
is the picture on the tour's card and at the top of its page. Anything you add is fitted inside
1920 by 1080 and re-encoded, which strips the camera data — including where the photograph was
taken — before it is ever published.

The tour's page shows a slideshow of those pictures **and** of the photographs guests sent in that
you accepted, each credited to whoever took it. Accepting a guest's photograph is also how you keep
it: it is the answer to the retention clock, so a shot worth keeping goes on the page.

Somebody who came on the tour sees their own uploads on the tour's page, whatever you decided about
them — accepted, waiting, or not published. They took the photograph; your decision is about what
the tour publishes, not about who it belongs to.

### What people say about it

Guests who came on a date that has finished can leave a rating out of five and a few words. It is
on by default and switchable per tour under **Details**; turning it off stops new ones and leaves
anything already there.

You can **hide** a review you think is unfair. You cannot change what somebody wrote — their name
is on it — and hiding is undoable, so nothing is lost either way. Whoever wrote it still sees it on
the page, marked as hidden, rather than being left to wonder where it went. If they rewrite it, it
comes back: new words have not been judged.

### How long things stay

On the tour plan, a photograph a guest sends in stays for **a month** and a recording for **a
week**, unless somebody keeps it. Recordings are limited to **five minutes** each; a longer one is
refused when it is sent, with the length said plainly, because the phone can trim before uploading
and only the person who was there knows which five minutes matter.

**Keep it** on the submission itself stops the clock on anything — a recording you want to hold on
to, a photograph you are not going to publish. **Put it on a tour** does the same for a picture and
publishes it: the business gets its own copy, fitted inside 1920 by 1080 with the camera data
stripped, while the guest's original keeps its own clock and its own owner. Both are on the
evidence queue, beside Accept and Decline, along with the date each file comes off.

Letting something go again puts it back on the clock from that day, not from when it was uploaded,
and whoever sent it in is warned afresh.

Everyone who sends something in is emailed before it goes, a week ahead and again the day before,
so they can download it. Nothing is ever deleted that has not been warned about first.

Groups that are not on a tour plan have no clock at all: their files stay, as they always have.

### Where your tours show up

Tours are public. They appear on your own public page, on the map on the front page, and in the
nearby search, with the meeting point given in full — shown on its own map on the tour's page, with
directions and a link that opens it in Maps — an address held back from somebody deciding
whether to come is held back from the wrong person. Each tour also has a page of its own at
`/o/your-group/tours/the-tour-name`, which is the link worth sharing.

### Where else you are

Under **Where else you are** on a tour's page you can add the accounts that walk goes with:
your own website, Instagram, Facebook, X, TikTok, YouTube, Bluesky, Rumble and Threads. They show
as a quiet row at the **foot** of the tour's public page, under the dates — somebody reading it is
deciding whether to come, and sending them to Instagram before they have seen when it runs would
be an odd thing for the page to do.

![The nine link boxes on a tour's settings page](help-media:organization-administration/tour-links.png)

One account per service. Leave a box empty to take that one down; there is nothing to confirm.
Each link has to point at that service's own site — an Instagram link that opens somewhere else is
refused, because a reader clicks the word without reading the address.

### Which clock an event runs on

Every event carries the zone it actually happens in, chosen on the scheduler under **Which clock**.
It starts on your own zone, because whoever is scheduling is usually standing where it happens.

![The Which clock picker on the scheduler](help-media:organization-administration/event-clock.png)

That clock is what the public sees, wherever in the world they are reading — an event at eight in
Nashville reads as eight in Tokyo, with the zone named beside it, so the listing, the event's page
and the reminder email all agree. A date under a tour takes the tour's clock unless you say
otherwise. An event with no clock set is shown in UTC and says so.

## Hosted events

An **event** is something you put on over one or more dates: a weekend at a hotel, a run of
performances, an evening with a talk and supper. It is not a ghost-hunting thing — a retreat, a
dinner theatre and a lock-in are the same record with different words in the description.

An event is different from a date on your calendar in the way a tour is different from a walk. The
calendar holds dates; an event holds everything that makes the dates worth coming to, and it lives
at **Events** in your group's menu.

### Building one costs nothing

Give it a name, a venue and the dates it runs. That is a **draft**: only your own people can see it,
it has no public page, it takes no bookings and it sends nothing. Build all of it — the dates, the
programme, the rooms, the files, the page — and change your mind as often as you like. Nothing is
charged and nothing is announced until you press **Publish**.

### A venue we haven't listed

Search for the venue by name, and if we have it already, use the one we have. We share venues across
the whole site, so a building's map pin, its history and everything anybody has ever recorded there
hang together on one record.

If it isn't there, choose **The venue isn't listed** and enter its name and address. We look up where
it is so it shows on the map. If we can't find it, the event still works perfectly — it just has no
pin yet. Entering a venue that turns out to already exist quietly uses the existing one rather than
adding a second copy.

A venue has to be somewhere public. A private residence is refused, because an event is published by
definition and publishing somebody's home is not yours or ours to decide.

### One stay, or a run of dates

Two shapes, chosen with a switch when you add the event.

**One stay** is nights running together: a weekend, where every day between the first and last date
becomes a night of the same event and a guest books across several of them.

**Separate dates** is a production put on more than once — a play every month, a supper club, a
seasonal tour. The event is the production, each date is a performance of it, and each date is booked
on its own. The dates need not be evenly spaced or anywhere near each other.

**It costs the same either way.** A monthly show is one production on twelve dates, not twelve
events, and you are never charged twelve times for putting the same thing on repeatedly.

### Each date can say its own thing

Give any date its own title, its own start and end time, and a note. Opening night starting earlier
than the rest is exactly what this is for. Dates with nothing special said about them take the
event's own times.

Moving an event's dates keeps what you wrote about the days that survive the move. Extend a weekend
by a day and your Saturday is still your Saturday, with its title and its notes.

### What publishing does

Publishing puts the event on your public page, on the site-wide **What's on** list and in the phone
apps, and opens it for bookings. It is the moment it starts costing anything, and the page tells you
what that is before you press it.

If your plan includes running events, publishing uses one of the slots it includes. If it doesn't,
publishing spends an **event credit**: $99, one credit for one event, and it covers that event from
the day you publish it. You are shown how many you have and what the button will do before you press
it.

**An event is only ever charged once.** Take it down and put it back up as often as you need — the
first time paid for it, and there is no second charge for the life of that event.

### Buying event credits

Credits are bought on your group's **Billing** page, under **Event credits**, and the sentence on
the Events page that says you need one links straight to it. Buy one or several at a time; payment
happens on Stripe's secure page, and the receipt lands in your billing history like every other one.

A credit lasts **a year from the day you buy it**. Two things have to be true when you spend it: the
credit itself has to be in date, and the event's first date has to fall inside that year. So a credit
bought today cannot be parked on an event dated three years out — if that is what you are planning,
buy the credit nearer the time. When it will not stretch, the refusal names both dates so you can see
exactly why.

Credits are spent **oldest first**, so the one closest to running out is always the one used.

The card lists everything you hold: what is ready, what each one cost, when it lapses, and — for the
ones already spent — which event each went on. That last column is the answer to "did we pay for the
October weekend?", a year later.

**Thirty days before a credit lapses we write to you**, so it never quietly disappears. An unspent
credit past its date is gone; we do not extend it and it is not refunded automatically. If something
went wrong — you bought two by mistake, or never got to use one — ask us, and a refund is something a
person here does by hand.

A credit that says **Given to you** rather than a price is one we handed over: a payment that did
not land, or something we owed you. It works exactly like one you bought.

### Taking it down, archiving it, calling it off

**Un-publish** takes it off the public site. Nothing is destroyed and nothing is refunded.

**Archive** stops it counting against your plan and takes it off your list. Everything that happened
stays exactly as it is. Events are archived for you automatically a fortnight after their last date,
so you never pay for last Halloween.

**Cancel** is different, and it is for an event that was going to happen and now is not. The listing
says CANCELLED in its title — the one line everybody actually reads, on your page, on a shared link
and on somebody's phone — and the reason you give is shown to the people who had places. The event is
kept rather than deleted, because somebody who was coming needs to be able to see that it is off.

### If the event is called off, your credit

An event that took one of your plan's own slots rather than a credit frees the slot when you call
it off, and the slot is yours again at once.

**For an event published with a credit, the rule we are building to is this:** call it off 48 hours
or more before its first night and the credit comes back to you — as a credit, ready for the next
event, and never as money. Call it off later than that and the credit is spent, because by then the
venue, the kitchen and the people who were coming have all made their arrangements.

**That return is not switched on yet.** Until it is, calling off an event published with a credit
does not give the credit back on its own — ask us and we will put one back on your account.

### The date on your calendar

Every event puts one entry on your group's calendar, spanning the whole thing from the first date's
start to the last date's end. That entry is what the public list, the reminder email, the calendar
file and the phone apps all read, which is why it is kept in step with the event automatically.

You can't edit it from the calendar — it tells you so, and points you at the event, where its dates,
venue and description all come from. That way two screens can never disagree about the same evening.

### If IsHaunted removes your event

Hosted events have to meet the site's guidelines. If one doesn't, IsHaunted can remove it:

- it comes off the site and stops taking bookings;
- everybody with a place is told it is not going ahead;
- the event credit spent on it comes back to you, whatever the timing.

The people who run the group's billing and whoever created the event are emailed, with a link to appeal.

![An event IsHaunted removed, with the appeal](help-media:organization-administration/event-removed.png)

The event's page then opens with **Removed by IsHaunted**. If you think it was a mistake, write why it
should come back and anything you've changed, and send the appeal. A person reads every appeal, and you
hear back by email and in your messages:
- **Upheld** — the event comes back as a draft. Publishing it again spends a credit or a slot, as the
  first time did.
- **Declined** — the event stays removed, and the answer says why.

Each removal can be appealed once. A removed event can't be un-cancelled, restored or published from its
page, but you can still edit it before you appeal.

### Rooms and bookings

Under **Rooms offered** you tick which of your venue's rooms this event is using. The rooms
themselves are the ones you have described for the place (see *Rooms* on the venue), and each one
already says what it sleeps — but an event can say otherwise. A double you have put two camp beds
in for a séance weekend sleeps four for that weekend and goes back to two afterwards.

A room with confirmed bookings in it can't be untick­ed. Move those parties somewhere else first;
dropping the room would leave them sleeping in one your event says it is not using.

The **plan** is where the rooms — or the seats, for an event sold by the seat — are drawn out so
everybody can see what is where. It has a page of its own, listed as *Plan* down the side of the
event page. The **board**, where requests wait for your decision, is *Bookings* in the same list.

### Getting in and getting around

**Access and what to know** on the event page is where you say what a guest needs to know before they
come: *no lift — the bedrooms are up one flight*, *the ballroom is step-free from the side door*,
*some of the hunt is in low light*, *a hearing loop in the bar*, where to park. It shows on the
public event page under *Getting in and getting around*, in the iPhone app's page for the event, and
in every confirmation letter, just before the pass.

Say what is true of the building rather than what you think somebody can manage. A guest who uses a
wheelchair decides for themselves whether a flight of stairs to the bedrooms is a problem.

### Before you publish

A draft costs nothing and shows nobody anything. Publishing is the moment it goes on your public
page, starts taking bookings, and spends a slot or an event credit — so the page carries a
checklist of everything standing in the way, and each line links to the place you fix it.

There are four things. **At least one date**, because nobody can come to something with no date on
it. **The venue's say-so**, below. **Somewhere to book** — a room or a seat on the plan, or a
number of day passes; an event where everybody just turns up is fine, one with nothing at all is
not. And **a contact line**, because this site never takes a guest's money and that line is the
only thing on the page that says how anybody pays you.

The publish button refuses in exactly the same words the checklist uses. If it says something is
missing, it is the same thing the list is pointing at.

### Who confirms the event is happening

Under **The venue's say-so** you record how this event came to be allowed to happen where it is
happening. There are three answers and they are genuinely different.

**It's our own venue** is a hotel running its own weekend. Publishing is the whole declaration and
nothing more is asked, because asking a hotel to prove it may use itself would be silly.

**Somebody else's, arranged directly with them** is a hall you hired or a house whose owner said
yes on the phone. Record who agreed and when, and their reference if there is one. We cannot check
any of it — recording it is so that everybody knows what was arranged, and so that being asked
reminds you to go and ask.

**Somebody else's, and they're on this site** is a place another group has been confirmed as
running — a hotel on IsHaunted, say. Then the choice is not yours to make: the card says who runs
the place, and **their yes is what lets you publish**, whichever answer you picked. See
[Venues on this site](#venues-on-this-site).

### Pick or ask, and how long a hold lasts

Under **Your guests** you choose how somebody gets a place.

**They ask, you place them.** A request holds nothing until you confirm it, which means the event
keeps taking requests after it is full and the overflow is a waiting list rather than a closed
door. This suits a hotel putting families into the right rooms.

**They pick on the plan.** A guest clicks or drags across the seats they want, and those seats are
held for them — shown to everybody else as waiting on an answer, and unavailable — until you
confirm or the hold lapses. This suits an evening sold by the seat, where two people spending the
evening choosing the same row is the thing to prevent.

A hold lasts as long as you say, from an hour to a fortnight, two days by default. When it lapses
the seats go back and the guest is told; they stay on your list as a request, so nobody is silently
dropped.

**Changing between the two is refused once anybody is booked or waiting.** The switch would change
what their existing booking means — a hold would become nothing, an ask would become a place they
never chose — and neither is something you would find out about until the night. Decide the ones
waiting first, and the switch is free again.

### The booking board

The **Bookings** page is where a weekend is actually run, and it opens on the one thing that
matters: **Waiting on you**. Everything nobody has answered is at the top, soonest deadline first,
and everything else is below it. A board that opened on a list of everybody would be a board you
have to search before you can work.

Each row says who, how many, what they asked for, and anything they wrote. **Confirm…** opens the
party. **Turn down** tells them no. On an event where guests pick their own places there is a third
button, **Give them longer**, and a line saying how long is left on their hold.

**Why the hold matters.** A hold that runs out is a decision the clock took instead of you. So the
row says "their hold runs out in about four hours" while there is still time to answer, and the
button gives them another full stretch from the moment you press it — not from a deadline that has
already gone.

If holds have already run out, the header offers to **give them back now**. The site does it by
itself within five minutes anyway; the button is there because five minutes is a long time with
somebody standing at a desk asking whether the Blue Room is free.

**The house** below shows one night at a time, with the night's own buttons at the top. A square
that is settled is solid, one somebody is still holding is hatched, and one you have held back is
marked as not on offer. Under it is how many are coming for the day on that night.

### At a glance

Once an event is published, its page opens with **At a glance**: how many people are coming and in
how many parties, how many are waiting on you, places left, day passes, who has come through the
door, and the reviews. Each number opens the screen that explains it.

![The event's numbers at a glance](help-media:organization-administration/event-at-a-glance.png)

**Places are counted night by night.** A room on a two-night plan is two places to sell, and a party
in it for both nights has taken both. A place somebody is holding counts as taken, as it does on the
plan, and a place you have held back is not counted as for sale.

When the event has a minimum number, the card says how far past it you are or how many short.

### Deciding about one party

Opening a party gives you the whole of it in one place, without losing your spot in the queue.

**Where they are actually staying** is the question confirming asks. The pickers start on what the
guest asked for, and you change them to wherever suits you: moving a party of three out of a double
and into the suite is the ordinary thing, and making them turn it down and ask again would be
absurd. Each choice says what the room sleeps and whether anybody is in it.

If they do not fit, the refusal names the room and the number — "The Blue Room sleeps 2 more on
10/30, and this is a party of 4" — because "full" on its own leaves you guessing whether a smaller
party would go in.

**The line for the guest** is optional and they read it. It goes out with the confirmation or the
refusal.

Confirming sends their pass at the same time. A guest who was told yes and given nothing to show at
the door has to be looked up by name on the night, which is the queue this whole thing exists to
remove.

**A guest asking to get out** of a confirmed booking shows on the row and in the sheet. Releasing
frees their places and tells them; their pass stops working. Keeping it changes nothing and leaves
the ask on record.

### Holding a room or a seat back

Not everything on a plan is for sale. Open a room or a seat and you can hold it back, either **for
one night** or **for the whole run**, and say which of two things it is: **out of use** — behind a
pillar, out of order, kept for the crew — or **the venue is using it**.

Guests see the difference between those two and never see your note about it. Write "Mrs Cole's
family are in it on the Friday" if that is what it is; what a guest is told is that the venue is
using that room on that night.

A held-back place cannot be picked or confirmed into, and it goes back to being available the
moment you remove the block.

### When two guests want the same seat

On an event where guests pick their own places, two people can reach for the same seat within the
same second. One of them gets it. The other is told which seat went, by name, and keeps everything
else they had chosen — so a party of four who lost one seat picks one seat again rather than
starting over.

That is settled by the database rather than by the page, which is why it is reliable: there is no
window in which both people can be told yes.

**A hold lasts as long as you said** under *Your guests*. When it runs out the places go back and
the guest is told what happened — that the hold ran out, not that you turned them down — and they
stay on your list. You can still offer them a place.

### Who can see what

Four different things, and they are deliberately separate permissions:

- **Seeing the event and its plan** is open to any member. What an event offers is mostly on the
  public page anyway.
- **Changing the event** — its dates, its plan, its blocks, its menus — is arranging it.
- **Seeing the bookings** means guests' names, their email addresses and their dietary notes. A
  dietary note is a health disclosure somebody made to you so they would not be poisoned, and it is
  not something a whole group's membership needs. This used to be open to every member.
- **Deciding the bookings** is confirming, turning down, releasing, editing and issuing passes.
- **Running the door** is scanning passes and marking people arrived. Somebody on the door sees
  tonight's dietary flags and nothing else — not addresses, not phone numbers.

**Publishing is separate again** and needs whoever can change the group's settings, because it is
the one act that spends money.

### Minimum numbers

Some events are only worth running if enough people come. Say the fewest, and the date by which
you will decide, and we will remind you a week before and again the day before. Leave both empty
and the event runs whatever the numbers come to.

Once the date is set, **Is it going ahead?** appears on the same card with two buttons. Saying yes
records the decision and nothing else changes. Saying no calls the event off, which is the same as
calling it off for any other reason: the people who had places are told, and the listing says so.

### Calling it off, and your credit

Ben's rule, and the site follows it exactly: **more than 48 hours before the event starts, the
event credit you spent comes back.** Inside those 48 hours it does not. The event was advertised,
it took bookings, and people arranged a weekend around it, so by then you have had what you paid
for.

The card says which of the two is about to happen *before* you press the button, in the same words
the site will use afterwards.

When the credit does come back, the event goes back to needing one — so publishing it again spends
a credit, exactly as the first time. That is deliberate: you cannot end up holding both the credit
and the event. When the credit does not come back, the event stays paid for and putting it up again
costs nothing.

A credit that comes back keeps its own expiry. One bought last October still lapses next October
whatever happened to the event it was spent on, and if it has already lapsed the card says so
rather than implying there is something waiting.

**An event that has already happened cannot be taken back to a draft.** It ran; it stays on the
record. Archive it if you want it off your list, or make a new event for the next one.

### What happens without you

An event moves itself along, on your venue's clock rather than ours:

- **On the site** once you publish it.
- **On now** when the first night starts.
- **Over** when the last one ends. It stays readable and stops taking bookings, because a request
  arriving after the night is somebody who has misread the date.
- **Filed away** a fortnight later. It comes off your list and out of the counts; nothing is
  destroyed.

When it is filed away, anybody still waiting on an answer is told the event has passed rather than
left waiting for ever, and you are told what was closed on your behalf. That is the one thing here
that acts for you, and it only ever acts once the event is two weeks gone.

### The plan: rooms or seats

An event's plan holds one kind of thing, and you choose which while it is still empty: **Rooms** for
a venue where people stay the night, **Seats** for an evening where they sit down. Once anything is
on the plan the choice is settled and the buttons stop being offered — changing it would throw away
everything arranged so far, and every booking made against it.

![A floor plan of a hotel's rooms](help-media:organization-administration/event-plan-rooms.png)

On a **Rooms** plan the list on the left holds every room the venue has marked bookable that is not
yet on the plan. Pick one, then press an empty square to say where it goes. *Arrange them for me*
lays out everything still waiting, one floor per row, which is usually close enough to right that
you only nudge a couple. A room does not have to go on the plan at all — a cellar you describe and
never let is simply left in the list.

Rooms keep the venue's own names, so renaming a room on the venue's Rooms page renames it here and
on every guest's booking at once. A row whose rooms all share a floor gets that floor's name in the
margin, which is how a hotel ends up with its floors labelled without a column spent on it.

### Laying out a theatre quickly

![A 260-seat house with a centre aisle](help-media:organization-administration/event-plan-seats.png)

**Add a block** is how a house gets built: how many rows, how many seats in each, where the letters
start, which way the numbers run, and what the section is called and costs. A line under the form
shows the first and the last seat before you commit — *13 rows × 20 = 260 seats, A1 … N20* — which
is the cheapest way to catch a backwards row before you have named two hundred and sixty chairs.
Press it twice for a Stalls and a Balcony.

**Rows skip I and O.** On a printed ticket and a brass rail, I is a 1 and O is a 0, and dropping
both is cheaper than every usher in the building explaining it for the life of the venue. So the
rows run A to H, then J, K, L, M, N.

**Insert an aisle** opens a gangway to the left of whatever you have chosen, pushing everything to
its right one place over. The gap is genuinely empty — it is a walkway, not a row of seats nobody
may sit in — and the seats keep their numbers, so you can relabel afterwards if the venue numbers
across the aisle rather than through it.

Choose seats by clicking them, or by dragging across a run with a mouse. Everything you press is
added to the choice and pressing it again takes it back out, so there is no modifier key to
remember; **Clear** on the bar is how you start over. With a selection you can set a section, a
price or a note on all of them at once, relabel a whole block by row, nudge it about with the
arrows, or take it off the plan.

**Prices are shown to guests and never charged here.** Blank means "ask us", and zero means the
seat is included. You and your guests settle up between yourselves.

### Starting from a plan used here before

When an event's plan is empty and the venue has been laid out for an earlier event, the plan page offers
**Start from a plan used at this venue before**: the venue's own plan first, then your group's, then other
groups' published events there. Pressing *Start from this plan* fills the page with its rooms or seats,
sections and positions — not saved until you press Save, so you can change anything first. Your own
group's prices and notes come across; another group's never do, and a room this event can't offer is
left out and the page says so.

### The plan on a phone

![The same 260-seat house at iPhone width](help-media:organization-administration/event-plan-phone.png)

Laying a whole house out is a computer or an iPad job, and the page says so rather than pretending.
On a phone the plan is there to be read and adjusted a square at a time: tap a room or a seat to
open it, fix its price or its note, and save. The squares stay big enough to hit and the plan
scrolls sideways inside its own box, so the page underneath never slides about.

If you do want more of a big house on screen at once, *Fit the whole plan* under the tools button
shrinks the squares. It is a deliberate press, not something the page decides for you.

Nothing is saved until you press **Save the plan**, and the page says *Not saved yet* while there is
anything to lose. Leaving with unsaved work asks first.

#### Sleeps, bookable and beds

The rooms themselves live on the venue's own **Rooms** page, which the event's Details card links
to as *Describe the venue's rooms*. Each room can say what it sleeps, whether it may be booked, and
how many beds it has.

**Sleeps** left blank means nobody has said: ask the venue, and an event that offers the room will
have to give a number of its own. **Zero** is a decision — a room nobody sleeps in, a parlour or a
séance room that guests visit and leave. The two look alike on a form and mean opposite things on a
booking, so a blank is never quietly read as zero.

**Bookable** is separate from **Public**. Public says whether a visitor can read about the room on
the venue's page; bookable says whether a party can take it for the night. A famous room can be
public and never booked — the owner sleeps in it — and a plain twin can be bookable without earning
a paragraph on the page. Ticking one does nothing to the other.

**Beds** is a description, in your own words — "one king", "two twins and a cot", "four bunks". It
answers the question a number cannot: *will the two of us have to share a bed?* It is not a count
and nothing is booked by it; **Sleeps** is the number a booking is checked against. A room let out
bed by bed is described as several rooms — "Bunk 1" through "Bunk 6", each sleeping one — because a
party takes a room, and six beds sold separately are six things to take.

#### How a booking works

A **booking is a party**, not a person: one lead who talks to you, a party size, and as many named
guests as there are people. Guests don't need accounts — somebody brings a partner who has never
heard of us — and each guest can carry their own dietary note.

There are two kinds. An **overnight** party takes a room on named nights. A **day pass** is for
somebody coming without staying, counted against one number for the whole event.

A guest asks; you decide. Until you confirm it, a request **holds nothing** — no room, no day pass —
which means your event keeps taking requests after it is full and the overflow is a waiting list
you can work through rather than a closed door. The board shows both numbers: how many are in a
room on a night, and how many have *asked* for it.

#### Confirming, and moving people about

Confirming is you saying the place is theirs and that you two have settled how money changes hands.
**This site never takes a guest's money.** When you confirm you say which room they are actually in
on each night, and that need not be the room they asked for — putting a party of three out of a
double and into the suite is the ordinary thing, and there is no need to make them start again.

If they don't fit, the refusal tells you what to do about it: which room, which night, and how many
more it sleeps. "Full" on its own would leave you guessing whether a smaller party would go in.

Confirming also puts them on the event's calendar entry, which is what the reminder email, the
calendar file, the public count and the phone apps all read.

#### Changing and cancelling

Bookings are editable after they are confirmed, because a real weekend has somebody dropping out on
the Thursday. Change the party size, the rooms, the nights or the guests; the room is re-checked so
an edit can never do what a booking couldn't, and a party moving rooms is never blocked by the beds
it is leaving.

A guest can change their own booking too. If they change what you agreed to — the party size or the
nights — it comes back to you as a request, because the thing you said yes to is not the thing
being asked for any more.

**A guest cannot free a confirmed room on their own.** They ask, you release it. You have catered,
staffed and possibly turned somebody else away against that room, and a room freed without your
knowing is a room that stays empty. Their reason comes with the ask.

Turning a booking down and cancelling one both tell the guest, and both give the room back.

#### Bookings close

You can set a date when the event stops taking new requests, so you can cater and staff to a known
number. It stops *new* ones only — a request already waiting when it closes is still yours to
decide, because somebody who asked in time shouldn't be refused by a clock while you were asleep.

#### Inviting somebody who has no account

Most guests ask you through the event's page, which needs an account. For the ones who ring up, or
who you meet at a conference, there is **Invite by email**: you type their address and we send them
a link. Clicking it proves the address, makes them a passwordless account and puts a **day-pass
request** on your board, which you then confirm like any other.

Two things about it are worth knowing before you use it.

**Nothing is held until they click.** The invitation reserves no room and no day pass, and we say
so on the screen rather than showing you something booking-shaped. If you need a room held *now*
for somebody on the phone, make the booking against an account instead.

**It only ever asks for a day pass.** Sleeping somewhere means choosing rooms night by night and a
hyperlink is not a booking form — so if they are staying over, move them into a room when you
confirm, which is where you decide every other room anyway.

The link is good for two weeks and works once. Sending it again to the same address replaces the
old one rather than stacking a second guest, and if they had already asked you for a place
themselves, they become one party rather than two. If your site has no mail configured, we tell you
the letter did not go rather than leaving you waiting at a door.

An event that isn't published yet, has been called off, or sells no day passes refuses the
invitation and says which — better than thirty people finding out a fortnight later.

### Menus

**Menus** on the event page is one page for the whole weekend: every night, and under each one
every sitting you serve.

![Menus for a weekend, with three sittings under the Friday](help-media:organization-administration/event-menus.png)

Each night's heading carries four buttons — **+ Breakfast**, **+ Lunch**, **+ Dinner**,
**+ Snacks** — and pressing one adds a sitting under that night. They are a starting point and not
a list to choose from: the name is an ordinary box afterwards, so a midnight vigil supper can be
called whatever you actually call it. Give it a time if it has one, a note if it needs one, and
then type the dishes into the box below, **one to a line**. Put the course before a colon if you
use courses — `Starter: Tomato soup` — and a line without one is simply a dish.

The arrows beside a sitting move it **within its night**, and where it sits in the list is the
order it is served in. The bin removes it. Nothing is saved until you press **Save the menus**,
which writes the whole weekend at once.

**A night runs from the evening people arrive through the morning they come down.** So Friday
night's sittings include Saturday's breakfast, and the last night of a weekend still serves the
breakfast on the day everybody leaves. Sittings read in the order you arrange them, not by the
clock, because an eight o'clock breakfast comes *after* the seven o'clock dinner it followed.

The tags on a dish describe **the food** — "vegan", "contains nuts" — and everybody who can see the
menu sees them. A guest's own allergy is a different thing entirely and lives on their booking.

Menus are shown to guests whose place you have **confirmed**, not to the public. A menu is part of
what somebody has booked, and you may not want the hotel down the road costing your catering before
you have sold the weekend. A guest still waiting is told you publish it once their place is agreed.

Typing a whole weekend's food is a job for a keyboard, but the page works on a phone if you need to
add a sitting between two other things.

![The menus page on a phone](help-media:organization-administration/event-menus-phone.png)

### Dining tables

**Dining tables**, from the event page, decides who sits where at each meal.

![Seating parties at tables for Friday dinner](help-media:organization-administration/event-dining.png)

1. **Set up the tables** once for the event — a name and how many it seats, or *Add 6 tables of 8* to start.
   The same tables are used at every sitting.
2. **Choose a sitting** from the buttons at the top. Each sitting is a meal from the Menus page.
3. **Tap a party, then tap a table.** The party is seated there. To split a big party, change how many to
   seat before you tap the table, then seat the rest at another.

Only confirmed bookings that are here that night are listed, with how many of each party still need a
seat and a badge when somebody in the party has dietary notes. A table never seats more people than it
has chairs; if a party doesn't fit, the page says how many chairs are left. A booking that is cancelled
drops off its table by itself.

**Seat the same way** copies another sitting's seating — everybody who is at both meals goes back to the
same table. **Print table list** prints each table with who sits there and their dietary notes, for the
kitchen and the dining room. Guests see their table on their pass.

Changing a meal's name or dishes keeps its seating. Removing a meal, or a table, removes the seating for it.

### The confirmation email and the pass

**Confirming a booking sends the guest a letter, and the letter carries their pass.** You do not
have to remember to send anything — a guest who was told yes and given nothing to show has to be
looked up by name at the door, which is the queue this whole thing exists to remove.

The letter says which nights they have and which room they are in, repeats anything you typed in
the decision note, and carries a **QR code**. Turning a booking down or releasing one sends a
letter too, using the same machinery, so the guest who is *not* coming is told exactly as reliably
as the one who is. A refusal carries your reason, because a refusal with none reads as arbitrary
and the commonest reason is one the guest can act on.

Attached to a confirmation is a **calendar file with one entry per night**, each naming the room.
A single entry across the whole weekend would sit there as one block and tell them nothing about
where they are sleeping on Saturday.

If your site has no mail configured, nothing is sent and the decision still stands. A guest who is
confirmed but whose letter bounced is a confirmed guest.

#### The pass, and the door

**One pass admits the whole party.** Not one each: three of the four people arriving together may
never have had an email address here, and four people at a door each hunting for their own code is
worse than one person holding one.

Scan it at the door and you are told who they are, how many, which nights and which room. If the
code will not scan — a flat battery, a camera that will not focus in the dark — everything the scan
would have told you is also written on the guest's own pass screen in words, so you can let them in
anyway.

**Every refusal at the door is a sentence you can read out.** Not "invalid", which does not tell
you whether to send somebody to the desk, wait, or turn them away:

- a code from another event names that event, because somebody showing last month's is the
  commonest case and naming it ends the conversation;
- a withdrawn pass reads out the reason you gave when you withdrew it;
- a booking not yet confirmed says so, rather than pretending the code is fake.

**Scanning the same party twice is not refused.** You are told when they first arrived and left to
decide — a door that turned people away for walking back in from the car park would be a worse
door.

#### Replacing a pass

**A pass is never edited, only replaced.** Change a booking's party size or its nights and the old
pass is withdrawn automatically and a new one issued, so the code in somebody's pocket can never
quietly say the wrong thing. **Reissue** does the same on purpose, for the guest who lost the
letter. **Revoke** withdraws one without replacing it, and asks you for a reason, because that
reason is what the door reads out.

A withdrawn pass is kept rather than deleted. Somebody showing an old code is told it was replaced,
which is a different sentence from being told it was never real.

### Who is helping, and the door

**The person on the door is not a person with billing rights.** A hotel gives the door to a weekend
helper who is not in your group and never will be, and until now the only way to let them scan a
pass was to make them somebody who could change your billing.

![Who is helping at an event](help-media:organization-administration/event-staff.png)

**Staff** on the event page invites somebody by email and says what they may do. Five switches, and
they are not a ladder:

- **Run the door** — scan passes and mark people in. Most helpers need only this one.
- **See who's coming** — the board, with guests' names and anything they cannot eat.
- **Say yes or no to bookings** — deciding, which carries seeing.
- **Menus and the kitchen's sheet.**
- **The event's files.**

The role — *Door*, *Kitchen*, *Guide* — is a label for your own rota. It grants nothing.

**An invitation grants nothing until it is accepted**, and the list says so plainly against
anybody who has not. The letter tells them which event, which venue and exactly what they are
being trusted with, before the button. Accepting makes them an account if they have none.

**It is this event only.** A steward for Saturday is nobody on Sunday, and nobody in your group.
**Remove** takes effect on their very next tap.

### On the door

**Phone-first, because that is where it is used** — one thumb, in the dark, with somebody standing
in front of you.

![The door on a phone](help-media:organization-administration/event-door-phone.png)

- **The count is the first thing**: how many are in, out of how many are expected, and **how many
  more could come in tonight**. That last one is what somebody at your elbow is asking.
- **Scan a pass** with the camera. Where the browser cannot read QR codes — Safari, mostly — we
  fetch a reader the first time and nothing else changes. Every reason a camera will not start has
  a sentence saying what to do instead, including the one a venue meets testing on its own network
  over `http`.
- **Type the code** from the guest's pass when the camera is no use. It is the six characters
  printed under their QR code.
- **Find somebody by name** when they have lost the email entirely.
- **They're here** admits them, **Left** marks them out again, and **Not them** takes it back —
  because a doorway is exactly where the wrong row gets pressed.

**Arrivals are per night.** Somebody marked in on the Friday is not marked in on the Saturday, so
the Saturday door tells you the truth and you can answer "who was actually here on the Saturday"
afterwards.

**Somebody who just turns up** is written down as a walk-up: how many, and a name if they gave one.
No account is invented for them and they get no pass — the point is that the count is right, for
the kitchen and for the fire officer. Let too many in and the door refuses, because the number at
the top is only worth anything if it is enforced.

Print the page before the doors open if you expect no signal: a browser cannot scan a code with
nothing to talk to — or run the door from the iPhone or iPad app, which can.

**On the iPhone or iPad app.** Anybody who may run an event's door — somebody in your group with the
door, a helper who accepted, or the venue's own people where it lent them — finds **Doors I'm running**
under **Profile**. The door there works the same way, with two differences that matter in a cellar:

- **Scanning finds the reservation; it doesn't check anybody in.** The camera reads the code and the app
  shows the reservation it belongs to. The person on the door taps it, sees who it is, and checks them in
  as arrived — all of the party, or as many as are actually there. The camera reads the code on the phone
  itself, so this works with no signal.
- **Tonight's list is kept on the phone** once it has been opened. With no signal a scanned pass is
  matched to its reservation on that list by its code, and whoever is checked in is kept on the phone
  **with the time they came in** and sent as soon as there is signal. Your list and count on the website then show
  them arriving when they did, not when the phone found a bar.

A pass you withdrew while the phone was offline is still refused when the phone catches up, and the
app lists that person by name so somebody can find them. A walk-up, and taking back an arrival that
has already been recorded, need a signal, because they have to be checked against the count first.

### Bands

Wristbands, lanyards, a stamp on the hand — whatever your people give out so a steward knows **by
glance** what somebody is here for. *Blue is the whole weekend with dinner, purple is the whole
weekend, red is Saturday only.*

![Setting up the bands](help-media:organization-administration/event-bands.png)

**Bands** on the event page lists them. Each has a colour — **your** word for it, because the box of
wristbands in your drawer says orange and the screen should too — an optional swatch, what it means,
and who gets it:

- **Here every night** — the whole run.
- **Here some nights** — the Saturday-only guest, who is exactly the one a steward gets wrong.
- **Day pass** — here for the day, not staying.
- **Given out by hand** — you choose, party by party.

**Most parties never need tagging.** Their band is worked out from what they booked, so two hundred
guests do not mean two hundred decisions. **The first rule that matches wins**, in the order on the
page, so put *every night* above *some nights* if that is what you mean.

**Anything about food is given out by hand.** Nothing on a booking says a party is eating, so we
cannot work that one out. Open the party on the booking board and choose it under **What they
wear**; *Worked out from the rules* puts them back.

The band then shows **beside their name at the door**, on the booking board, and on the guest's own
pass — so somebody who already knows they are blue does not have to be told.

![A band beside a name at the door](help-media:organization-administration/event-door-bands.png)

### Venues on this site

**A place is nobody's**, and several groups can hold events or investigate at the same building. But
a group can be **confirmed as the venue** at a place — the hotel itself, or the people who run it —
and from then on, other groups ask that venue before they publish an event there.

**Asking.** On your event's page, **The venue's say-so** names who runs the place. Write anything
they should know and press **Ask**. They are told straight away, and are asked about your event's
exact nights: add a night afterwards and their yes does not cover it, and the checklist says so.
While they have not answered, you can take the question back.

![What the organizer sees once the venue has said yes](help-media:organization-administration/event-venue-yes.png)

**Answering, as the venue.** **Hosting requests** — from **Your venue** in your group's settings,
or the bell — lists every group waiting on you, oldest first, with their event, their nights and
their message. **Say yes**, or say no with a reason they will read.

![A group asking to hold an event at your venue](help-media:organization-administration/venue-requests.png)

A yes can lend three things, each your choice:

- **Your rooms**, so their plan can place the rooms you have described instead of them typing
  your building out again.
- **The building's story**, shown on their public event page.
- **Your own people**, who may then see who is coming and help on the door. They never decide who
  comes — that stays with the organizer.

**Taking a yes back.** **Withdraw…** asks for a reason and says exactly what it will do first. Every
published event resting on that yes stops: its page says the venue withdrew, everybody with a place
is written to with your reason, their passes stop working, and the organizer gets back any credit
they spent, however close to the date it is — they did nothing wrong. Events that have already
happened are left alone.

**Your venue page.** Under **Your venue**, describe each place you run — its story, house rules,
how many may stay — and, once you are confirmed as the venue, show the page to everybody. It lists
your public rooms and everything on at the place, whoever runs it, and the place's own page links
to it.

![A venue's page](help-media:organization-administration/venue-page.png)

**Describing a place does not make you its venue.** Any group may write about a hall it rents every
October, and it changes nothing for anybody else. Being confirmed as the venue is a separate step —
below — and until it is done the page stays private, because a public page headed "the venue" is
itself a claim to be the building.

![Describing a place you run](help-media:organization-administration/venue-profile.png)

**Photos of the venue.** Under each place you run, *Photos of the venue* keeps pictures of the building.
Pictures you add are kept straight away. Organizers holding events at your venue can offer pictures from
their event's gallery with **Offer to venue**; an offer waits for you to press *Keep it* or *No thanks*.
Kept pictures show on your published venue page, fitted and with their location removed. Declining an
offer never takes the picture away from the organizer's gallery, and a picture you keep stays in your
library after the organizer's event files are removed.

![A venue's photo library](help-media:organization-administration/venue-photos.png)

### A place's contact details

Every place's page has a **Contact** card: its website, phone numbers and email addresses.

- **Public** details are for anybody — the website, the box office, the front desk.
- **Private** details are your group's own notes — the events coordinator's mobile you were given for
  scheduling. Nobody outside your group sees them.

Before anybody has been confirmed as running a place, any group may record its public details, and
each one says who added it. **Once a venue is confirmed, its public details are the venue's.** It
presses **That's right** on what others added, or removes it, and other groups can add private notes
only — the card says why, rather than offering a switch that does nothing.

![A place's contact details, public and private](help-media:organization-administration/place-contacts.png)

### Claiming a venue that is already on the site

Most venues arrive on the site as a place somebody else typed in — the organizer of an event held
there, months before the venue heard of us. When your group runs a place, open its page and choose
**Is this your venue? Claim it**, or **Claim it** beside a place on **Your venue**. Say whether you
are its owner, its manager, or a representative acting for them.

**A claim is on the place, never on an event.** It decides who future organizers must ask. Nothing
already booked there moves, is cancelled or changes hands, and you do not see who came to events you
had no part in.

There are two ways to prove it:

- **A code to the venue's own email.** If a public email address for the place was recorded by
  somebody **outside** your group at least a week ago, we can send a code there — shown masked, so you
  can recognise your front desk. Whoever reads that inbox gives you the code. An address your own
  group added proves only that you read your own mail, so it is never offered.
- **A review by a person**, when there is no such address. Tell them what shows you run the place —
  a licence, a listing, your role — and they may contact you.

![Claiming a venue](help-media:organization-administration/venue-claim.png)

**A proved claim waits a week.** Every group that knows the place — that has held events, named rooms,
recorded details, or investigated there — is told, and any of them can say **This claim is wrong**
with a reason. An objection sends the claim to a person instead. With no objection, you are confirmed
as the venue when the week is up.

**Reviewing claims** (site staff) happens at **Venue Claims** in the admin menu: claims with nothing to
prove them by and claims somebody objected to come first, and a confirmed venue that turns out to be
wrong can be undone — which takes its page down and stops it gating new events, but leaves any yes it
already gave standing.

![Reviewing a claim](help-media:organization-administration/venue-claim-review.png)

### The programme

**Programme** on the event page is where the classes, talks and meals go — the part of the weekend
guests opt into one piece at a time. Add each session with its night, its times **on the venue's
clock** (an end earlier than the start is the next morning), where it is — one of the venue's rooms,
or words like "meet at reception" — and who leads it.

- **Guests sign up for a place** with an optional limit. Leave the limit empty for no cap; switch
  signing up off for something anybody just comes to.
- Only guests the venue has **confirmed** can sign up, for up to their party's size, and anybody
  helping at the event.
- A full session **queues** people. When somebody leaves or you raise the limit, the queue moves up
  in order, a party is never jumped by one person behind it, and whoever gets in is written to.
- **Who's coming** lists the people with places and the queue, and lets you take somebody off.

![The programme, as the host builds it](help-media:organization-administration/event-programme.png)

**Nothing is shown to guests until you press Publish the programme.** After that, every change says
what it will do: **moving** a session writes to everybody with a place and rings their bell;
**cancelling** writes to everybody signed up or queued, with your reason; a session somebody signed
up for cannot simply be removed — cancel it, so they hear.

### Files for the event

**Files** on the event page holds the documents that go with a weekend: the guest pack, the stewards'
briefing, the poster. Each file has a **folder** — just a word, like *Guest pack* — and says **who it
is for**:

- **The event's people only** — your group, and anybody helping at the event. New files start here.
- **Confirmed guests** — people the venue has said yes to. Somebody still waiting on an answer does
  not see these: the guest pack is where the key safe code lives.
- **Anybody** — shown under *Downloads* on the public event page.

The site checks every time a file is downloaded, so a link copied into a group chat downloads nothing
for somebody the file is not for. Changing who a file is for takes effect straight away. A helper can
add and change files when you tick **The event's files** on the staff list. Photos have their location
removed when they are added, like every upload on the site.

![An event's files, each with who it is for](help-media:organization-administration/event-files.png)

### The room and the photo wall

Every published event has a **room** on its public page, for the people at it: confirmed guests,
your group, anybody helping, and the venue's own people when the venue lent them. It never appears
on the public feed.

- **Photos stay the guest's.** A guest's photo is their own file. They choose whether to send it to
  your group and, when there is one on the site, the venue — which shares it with you rather than
  giving it away.
- **A guest agrees before their first photo.** The first time a guest adds a photo at your event they
  are told it may be shown in the room and on a photo wall or slideshow at the venue, and they tick that
  they agree; the site keeps that agreement and the words they were shown. Your group and your helpers
  are not asked.
- **Each event has 2,000 MB** for its files, gallery and room photos together. An upload past that is
  refused with how much is left; remove files you no longer need to make room.
- **Who can add photos** is yours to set, from the room: *the team and confirmed guests*, or *the
  event's team only* for an event with a no-cameras rule. Guests can still write either way.
- **Hide** takes a post out of the room for everybody but you; members can **Report** a post, and
  you see how many have. **Close the room** stops new posts early; otherwise it closes a week after
  the last night.

**The photo wall** (*Photo wall* on the event page) shows the room's photos full screen, one at a
time, newest first, picking up new ones every half-minute. **It opens only for your group, the
event's helpers and the venue's people** — never for guests, and never for anybody outside the event —
because a slideshow goes on a screen people walk past, and some of the people in the photos do not want
their faces in public. Put it on a TV at the venue from a computer signed in with one of those accounts. When there are none yet it shows a code guests can
scan to **add photos from their phone**.

![The photo wall](help-media:organization-administration/event-photo-wall.png)

### The event's page, gallery and advert

**Gallery** on the event page puts your own pictures on the event's public page — last year's weekend,
the building, the ballroom set for dinner. The first picture leads the page; the arrows change the
order. Each picture is resized and has its location removed. Guests' photos from the room are never
added by themselves: they are the guests', and some of the people in them did not agree to a public page.

![The event's gallery](help-media:organization-administration/event-gallery.png)

The public event page opens with the pictures, says how long until the first night, and on a phone
keeps an **Ask for a place** button at the bottom of the screen while people scroll.

**Putting an event on your group's own pages.** When you edit one of your public pages, four sections
show one of your published events and keep up with it by themselves: *An event: dates and ask for a
place*, *An event's programme*, *An event's pictures* and *An event's venue*. A section pointing at an
event that is no longer on the public site says so rather than showing it.

**Advertise this event** on the event page starts your group's ad pointing at the event. It is reviewed
like any ad, shows the event's name and date on the card, leads straight to the event's page, and stops
being shown by itself once the event is over.

### Letters about bookings

**A request is answered because somebody was told.** Everybody who may say yes or no to bookings —
you, anybody your roles let decide, and a helper you handed **Say yes or no to bookings** — is
written to when they arrive.

- **The first request in a while** is written about within about five minutes.
- **A rush becomes one letter.** When forty people ask in an hour, you get the first straight away
  and one more saying *40 more bookings* once it quietens down — never later than an hour.
- **A daily letter** says where each event stands: who has waited longest for an answer, holds that
  run out in the next day, what is left on the next night, and — while the event is on — how many
  came through the door last night. It is weekly once bookings have closed, and **it is never sent
  when there is nothing waiting**, because an empty letter every morning trains people to stop
  opening the one that matters.

**A letter never names a guest** or carries their email address or what they cannot eat. It says *a
party of 4 has asked for the Blue Room on Fri 10/30* and links to the booking board, where the rest
is, behind the permission the board checks. Letters get forwarded and left open on shared desks in
a way a screen does not.

Your own booking is never news to you.

![Choosing how often a group writes to you](help-media:organization-administration/event-booking-letters.png)

**How often** is yours to choose, group by group, on the **Notifications** page under *Letters about
bookings*: **As they arrive** (the default, with the daily letter too), **A daily letter** only, or
**Nothing**. The bell still counts what is waiting whichever you pick, and a hold about to run out
gets a row of its own there.

It always says the colour's name as well as showing it, because about one man in twelve cannot tell
red from green.

### Writing to your guests

**Write to your guests**, at the bottom of the booking board, sends one letter to everybody with a
place: *the car park is closed, use the church lot*, *doors open at eight, not seven*, *bring a coat*.
It is for what everybody needs to know. Anything about one party belongs on that party.

![Writing to everybody with a place](help-media:organization-administration/event-write-to-guests.png)

- **Who it goes to.** Everybody with a confirmed place, or only the people there on one date. A pass
  for the whole event counts as being there every date. Turn on **Include people you haven't answered
  yet** to reach people who have asked or are holding places as well.
- **The count comes first.** As you choose, the card says how many parties and people the letter will
  reach. Sending takes a second click that repeats the number, because a letter can't be taken back.
- **How it arrives.** One letter per party, to the person who booked, by email and in their messages
  on this site. The subject starts with the event's name, and replies go to your group's email address.
- **What is kept.** Every letter sent stays listed under the card: when, who sent it, to how many, and
  how many of those went by email.

Only people who may say yes or no to bookings can write to guests. An unpublished or called-off event
can't be written to from here, and an event can have **ten letters in a day**. The limit stops a stuck
button or a bad morning turning into forty emails; put the rest in one letter.

### After the event: the next one, and the list of who came

**After the event**, from the event page, gathers what happens once it is over.

![After the event](help-media:organization-administration/event-after.png)

- **Thank-you email** is sent automatically the morning after the event ends, once to each confirmed
  booking. It includes your note, a link to leave a review, a link to the gallery if you added
  pictures, and up to three of your upcoming events. It is on unless you turn it off, and it is never
  sent for an event that ended more than a week before.
- **Reviews** come only from people who had a confirmed place, for two months after the event. You
  can hide one from the page; you can't change its words, and the person who wrote it still sees it,
  marked as hidden. Turning reviews off stops new ones.

**Files are kept for 90 days after the last date.** Then the event's files, its gallery pictures and the
links to photos in its room are removed. The event itself stays, with its bookings, programme and reviews,
and guests keep their own photos — a photo a guest sent you stays shared with you. You are written to a
month and a week before, and nothing is removed within a week of the last letter.

**Keep the files**, from *After the event*, lists everything you can take away: the event's files, its
gallery, photos your own people posted in the room and photos guests sent to you. Tick what you want and
**Download as a zip** — up to 100 at a time, in a folder each. A guest's photo that was never sent to you
isn't on the list, because it is theirs.

![Picking files to keep](help-media:organization-administration/event-keep.png)

**Copy this event**, from the event page, starts the next one from this one. Choose a name and the
first date; every other date moves with it, keeping its gaps, and a session at 7 PM stays at 7 PM
even when the clocks have changed in between.

![Copying an event](help-media:organization-administration/event-copy.png)

Each part you made is offered with its size, ticked:

- **the plan** — rooms or seats, sections, prices, and what the venue held back on which night,
- **menus**, on the same night of the run,
- **the programme**, at the same times, not yet published,
- **bands**, and **helpers** with what they may do (an invitation nobody answered stays behind),
- **the advert**, as a draft that is reviewed again before it shows,
- **files**, copied into the new event's own space (unticked to start with, because they count
  against its allowance).

**What happened stays behind:** bookings, passes, arrivals, the room, its photos and the gallery. The
copy is a **draft**, so nothing is spent and nothing is published until you say. **The venue's
agreement does not come across** either — a yes for October is not a yes for March — so a venue
you recorded by hand keeps its contact and asks you when they agreed, and one on this site has to
be asked again. The *Ready to publish* list says so.

**Download as a spreadsheet**, on the booking board, hands you every booking: status, name, email,
the phone number the guest gave, how many, which nights and places, the guests and their dietary
notes, their note, when they asked, when you decided and which nights they came through the door.
It opens in Excel, Numbers or Google Sheets. Anything a guest typed that starts like a formula is
written as plain text, so a note cannot run in your spreadsheet.

### The dietary sheet

**What the kitchen needs**, from the event page or from the booking board, turns every guest's note
into the sheet the kitchen actually works from.

![The kitchen's sheet: three numbers, a tally and the people](help-media:organization-administration/event-dietary.png)

It counts **confirmed** parties by default, because that is who you are buying food for.
**Count parties you haven't decided on** folds in everybody still waiting — the ones who asked and
the ones holding places they picked — for ordering ahead of a weekend that isn't settled. The sheet
says which of the two you are looking at, so nobody reads a provisional number as a final one.

Three numbers sit at the top, and the third is the one that matters most:

- how many people you are expecting in total,
- how many of them told you something,
- and **how many nobody has named**. A party of four who listed two names leaves two people you
  know nothing about. A sheet that showed only the notes would look complete when it was half your
  guests.

Below that, the same note typed by several people is grouped with a count, so "vegan × 6" is one
line. **We group on the words as typed and never guess.** "No nuts" and "nut allergy" mean the same
thing to a cook and are two different lines here — a tally clever enough to merge them would
eventually merge two that aren't the same, and acting on a wrong merge is how somebody gets fed the
wrong thing. Each line names the guest, their party and which nights they are here, so Saturday's
cook can see whose meal is theirs.

**One night at a time.** The pills at the top narrow the sheet to a single night, because a cook
working Saturday is cooking for the people who are there on Saturday and the weekend's total is the
wrong number to hand them. A party with no night of its own — a pass for the whole event — counts
on every night of it. **A sheet for every night** builds the lot in one go, and each one prints on
its own page, which is what a kitchen pins up.

The page is readable standing in the kitchen with it, not only at a desk.

![The kitchen's sheet on a phone](help-media:organization-administration/event-dietary-phone.png)

**Dietary notes are health information about named people.** They are shown to staff who can decide
bookings, and withheld from members who can't — an ordinary member of your group has no reason to
read a stranger's allergy list. They never reach a public page, and the printed sheet is for the
kitchen rather than the notice board.

## Public events

An event on your calendar can be opened to anybody. Tick **Public** and it appears on your group's
public site, on the site-wide **What's on** list, and anyone with an account can say they are
coming.

This is the one part of the site that brings you people who have never heard of you. Somebody
looking for a ghost walk near them finds your event, turns up, and meets the group — so the listing
names you prominently and reads as an invitation rather than a record.

### What can be made public

Events at **landmarks, businesses, and your own addresses**. Not events at a private residence, and
not events attached to a case.

That is deliberate and cannot be overridden. A public listing is a date and an address advertised to
strangers, and a client did not agree to have people turn up at their home. If you want to publicise
work you are doing on a case, create a separate event for a venue that is yours to publish.

### Hiding the address until somebody is coming

**Hide exact location** shows the town but not the street address. Whoever is coming sees the full
address once they have said so; nobody else ever receives it, and the listing says as much up front
so nobody feels tricked into signing up to find out where they are going.

If somebody cancels, the address stops being shown to them.

### Places and sign-ups

- **Places** caps how many people can come. Leave it empty for no limit.
- **Sign-ups close** sets a cut-off. Leave it empty and people can sign up until half an hour
  after the start — the late-arrival grace, for the guest who reaches the meeting point a few
  minutes after you set off. Set it earlier to require booking ahead, or later to hold a
  particular night open longer.
- **Anyone who can edit the calendar can sign somebody up by hand**, at any time, from the event's
  attendee list. That is the walk-up who has no account, or the latecomer who turns up with three
  friends — you are standing there, so you decide. Type the address in the invite box: if they
  already have an account they go straight onto the list, and if they don't they get a sign-up
  link instead.
- **A link you sent still works after sign-ups close, and past a full house.** That is the
  difference between you signing somebody up and them signing themselves up — you vouched for
  them in person, so your cut-off and your capacity are yours to set aside. A guest working
  through the public page on their own gets no such latitude.
- The guest still has to open the link. It only takes a moment on their phone while you are
  taking the money, and it is what makes the address real — otherwise anybody could put any
  address on any event. Until they open it, they are not on the list, and **they cannot share
  what they recorded on the night**, because that is tied to having been there.
- Somebody who cancels frees their place for somebody else.

### The link you can share

A public event gets a readable web address — `/o/your-group/events/2026-08-24-ghost-walk` — made
from its date and title the first time you publish it. That is the link to put on social media.

It does not change afterwards, even if you rename the event. A link somebody has already shared
should keep working.

### People coming who don't have an account

Somebody who finds your event and has never been here before doesn't have to sign up for anything
first. They give an email address, we send them a link, and clicking it is what actually confirms
they're coming.

The link matters: an address typed into a box proves nothing, and an event that hides its exact
location until people are coming would be protecting nothing if anyone could type any address and be
shown where you're meeting.

Confirming makes them an account with no password. They can set one later if they want to sign in
properly, but they don't have to — it exists so you have somebody you can reach, and so they can see
what they've said they're going to.

You'll see them in the attendee list like anybody else.

### The reminder the day before

Anyone who has said they are coming is emailed a reminder about a day beforehand, automatically.
You do not send it and there is nothing to switch on.

Somebody who signed up three weeks ago has had three weeks to forget, and a stranger who does not
turn up is worse for you than one who never signed up at all — so the reminder repeats the time and
place, carries the link to your event page, and gives them a way to say they can no longer come
while you can still offer the place to somebody else.

It goes only to people whose answer was **yes**. Somebody invited who never replied, or who said
maybe, is not written to: they did not agree to come, and a reminder about a thing you never agreed
to is just mail you did not ask for.

Everyone is reminded once. If the site cannot reach somebody's address, it tries again on the next
run rather than giving up quietly.

## Applications from would-be members

When **Accepting Membership Applications** is on (the group's Edit screen), your public page shows
an apply box to signed-in visitors. Applications land on the **Members** tab, where any member
with the requests permission can Accept or Deny — a denial asks for a reason, and the applicant
sees your answer.

## What kind of group you are

Every group is either an **investigation group** or a **ghost walking tour**, chosen when it was
created and changeable in **Settings**. The choice decides what a NEW group starts with — a tour
starts with a public, searchable meeting point and public events; an investigation group starts
private — and after that it is a label, never a restriction. Nothing is withheld from either kind.

Alongside it sits **runs public tours**, which is a separate switch on purpose. Plenty of
investigation groups run paid public tours as a sideline, and none of them should have to
register a second group to be found for it. Turn it on and your group appears under **Walking
tours** in the group finder while its badge still says investigation group — which is the truth
about both facts at once.

## Promoting your group

**Settings → Promote your group** builds a promotional card that rotates through the group
finder's Featured groups, the home page, and the public feed, always marked "Promoted". In the
feed, cards are fed to the people nearest your group's public address first — a group with no
public address still rotates, just unranked. Once approved, the page shows two honest numbers:
how many times the card was shown, and how many people clicked through. The builder walks you
through it — headline (say who you help and where), a short message (what you do, what it
costs, what to do next), an optional picture from your media library, and where the card
leads: your public page or the group finder. Send your people to the public page only once
it's worth arriving at — a striking headline over an empty page loses the person you just won.

Every card goes through review before anyone sees it: submit, and a platform administrator
approves it or declines it with a reason (you're messaged either way). Any later edit comes
back through review — the approved text is the only text that ever shows. One card per group;
withdraw it any time and it leaves the placements immediately.

## Feed posts made from your cases

When a member (or a client) renders a video from one of your cases in the video editor and
posts it to the public feed, the post is credited to *them* — **your group's name appears on
nothing until you say so**. **Settings → Feed attribution** lists that footage for your
decision:

- **Claim** puts your group's name and a link to your public page on the post, plus a
  **Group verified** badge — you vouching that the footage is what it says. That badge also
  lifts the post's standing in the feed.
- **Decline** leaves the post up, credited to the person, with no link to you — ever.

Either decision can be changed later. Footage from a **private engagement** additionally
required the poster's explicit, recorded confirmation before it could be posted at all —
claiming or declining is only about your name, never about whether the post exists.

## Location data in your photos, audio and video

Cameras and phones write the coordinates of wherever they were into the file itself. On a case at
somebody's home, that file knows the address — and the site's careful, deliberately vague map pin
counts for nothing if the photograph beside it carries the exact spot.

**Photographs are cleaned for every group, always.** The copy anyone downloads is rebuilt from the
picture alone, so nothing that was not the image survives. There is no setting, because there is no
reason anyone would want the alternative.

**Audio and video are a setting**, under Settings on your group's page. Cleaning a recording means
rebuilding the file, so it is offered where a plan includes it — the switch says plainly when it is
not available, and to whom to talk. Groups working only at landmarks and businesses may reasonably
leave it off; a group going into people's homes should not.

**Whatever is removed is still yours.** Every file, of every kind, has its details recorded against
its record when you upload it: where and when it was taken, the device, the technical figures.
Removing that from the copy people download never removes it from your own records — you keep the
facts, the public copy does not carry them. And a clip you cut from a recording keeps the
recording's place, since a clip has no coordinates of its own.

## Your group's plan

Plans are priced by **active members, per group** — the price list is on the **Pricing** page,
visible to everyone. Yearly billing usually costs less than paying month by month; the card says
how much less.

Two things worth knowing about how plans behave:

**What you signed up for is a contract for the period you paid for.** If the plan changes after
you subscribe, improvements reach you immediately — a raised limit, a lower price, nothing to do
on your side. Changes that would reduce what you have wait until your renewal, and you'll get a
message about them **before** your period ends, so nothing ever changes under you mid-period. When
your current terms are better than what the pricing page shows, your Pricing page says so:
"you keep the terms you signed up for until" your renewal date.

**Plans can include limits** — on open cases, equipment, loans out at a time, open investigations,
public pages. If your group reaches one, the screen where you tried says exactly which limit and
what your plan includes; closed cases and retired equipment never count against you. A larger plan
raises the limit.

Have a coupon code? There's a line for it at checkout — type it and the price updates before you
confirm anything. Codes can be limited to first subscriptions, to renewals, to yearly billing, or
to a specific account, so if a code is refused the message says why.

**If your group grows past what your plan covers**, your plan does not change and nothing stops
anyone joining. Each person who joins beyond your member count is billed for their own seat at a
per-person price, told to them when they are accepted, and shown on their own Pricing page. Your
group keeps one plan and one renewal date.

**Tax** appears as its own line on the quote when your state taxes the service, computed from
your group's address — never folded into the price. And every payment your group makes has a
**receipt**: open **Billing history** on your group's card on the Pricing page to see every
charge, payment and adjustment, and download any receipt by its number. Receipts are generated
from the payment record itself, so the one you download in five years says exactly what it said
on the day.
