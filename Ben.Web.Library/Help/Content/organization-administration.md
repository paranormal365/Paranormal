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

**Let clients see members' private photos** is one half of a two-key rule. Turning it on shares
nothing by itself; it only permits members who have *also* opted in on their own profile. This is
deliberate: a group cannot volunteer its members' faces, and a member cannot overrule a group that
has decided its people stay unidentified to clients.

## Members and roles

| Role | Typically |
|---|---|
| Owner | Created the group. Full control. |
| Administrator | Configures the group and manages members. |
| Manager | Runs cases day to day. |
| Member | Works cases and attends investigations. |
| Viewer | Reads without changing anything. |

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

## Incoming requests

Client requests arrive under the group's **Requests**. Mark one as under review while you decide.
Declining tells the client, and lets them send it elsewhere without rewriting it.

## The group calendar

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

## Editing your public pages

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

### Seeing a page before it goes live

**View as visitor** on the editor opens the saved page exactly as the public renderer draws it,
published or not. That is the honest check: the side-by-side panel next to the editor follows what
you are typing, but it draws its own approximation in half a column, so a page can look right there
and wrong in reality.

The preview also tells you if the page is not in the menu yet, which is the usual reason a
freshly-published page seems to have vanished.

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
- **Sign-ups close** sets a cut-off. Leave it empty and people can sign up right up to the start.
- Somebody who cancels frees their place for somebody else.

### The link you can share

A public event gets a readable web address — `/o/your-group/events/2026-08-24-ghost-walk` — made
from its date and title the first time you publish it. That is the link to put on social media.

It does not change afterwards, even if you rename the event. A link somebody has already shared
should keep working.
