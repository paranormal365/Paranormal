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

The group's **Edit** screen holds its public identity — name, URL, contact details — and two
switches worth understanding.

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

Groups that existed before role-based case access arrived also carry an **Investigator Role**
(read cases and investigations), assigned automatically — once — to everyone who was already an
ordinary member, so the change took nothing from anyone. Members who join later start with no
roles; hand them the Investigator Role, or any other, as they earn it.

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
