---
title: Site Administration
summary: Sitewide settings, the clipart library, and the audit log.
section: Site Administration
audience: AppAdministrator
order: 70
---

Visible to app administrators only.

## The dashboard

**Administration → Dashboard** is the overview for everything else in this menu: four headline
numbers above a grid of charts, with a 7 / 30 / 90-day range picker.

The first three headline cards are links: **People** opens the user list, **In a group** opens the
groups, and **Cases** opens the case list, so you can go straight from a number to the details
behind it. *Signed in this week* isn't a link, because there's no page that lists recent sign-ins.

Some of the numbers need a little explaining:

- **People** and **In a group**: the second number is your funnel. Someone who registers but never
  joins a group hasn't been won over yet, and the percentage under the number shows how many people
  are in that position.
- **Signed in this week** counts *people*, not sign-ins; the caption below it gives the raw number
  of attempts. Someone who signs in on a laptop and a phone is one person with two sign-ins.
- **Busiest groups** counts cases and investigations started within the chosen range, so it changes
  with the range picker. **Largest groups** counts active members and doesn't change.
- **Where this is happening** has a People / Cases / Investigations toggle, and all three use
  addresses already on record: people by their profile addresses, cases by the address the case is
  about, and investigations by the place visited. Expect People to trail the other two, because it
  only counts people who have filled in an address, and most haven't.

### What the dashboard cannot tell you

There's no count of visitors who never sign in, and no "new versus returning" split. The site
doesn't record anonymous visitors: a page view by someone without an account leaves no trace in
the database. Every number on this page is about accounts and what they did.


## Site settings

![The site settings screen](help-media:site-administration/site-settings.png)
*Site settings apply to every group and every visitor.*

**Administration → Site Settings** holds values that apply to the whole site. Nothing personal
belongs here: settings for one person are on their profile, and settings for one group are in that
group's settings.

You can't add a new setting from this page. Settings are declared in code, in
`SiteSettingKeys.Seed`, and the page shows whatever is declared there.

### Allowing groups to sign themselves up

**Allow groups to self-register** controls whether an ordinary signed-in visitor can start a group.
When it's on (the default, and how the site normally runs), anyone can start one from
**Organizations → Start a Group** and becomes its owner. When it's off, that button disappears, the
page behind it explains that new groups aren't being accepted and points to the contact form, and
the server refuses the request even if someone has the address saved. Either way, you're exempt: an
administrator can always create a group from the administration side.

Use it when you want to pause growth, for example partway through a billing change, or during a
period when every new group needs a conversation first. It has no effect on existing groups.

### The default profile picture

There are **three default pictures**: a generic one, one for members whose profile says they're a
man, and one for members whose profile says they're a woman. Each is an ordinary upload: press
**Upload image**, pick a JPEG, PNG, GIF or WebP, and it takes effect immediately. The preview under
the row shows what's live. Uploading a replacement removes the previous image, so there's nothing
to clean up. The images are stored publicly so that they can appear for visitors who aren't signed
in.

Which picture a viewer sees depends on the person's own profile: the optional **Sex** field under
their name (Male, Female, Unspecified; blank by default). It's self-declared and never guessed from
a name. Blank or Unspecified gets the generic image. Male or Female gets the matching image **if
you've uploaded one**, and the generic one otherwise. A real photo always takes priority over any
default.

### The site-wide announcement

**Site-wide announcement** is for maintenance windows and known issues. While the box has text, an
info banner shows it at the top of every page to everyone, signed in or not, with your line breaks
kept. To take the banner down, clear the box and save. Posting and removing both reach visitors
within about half a minute, with no restart needed. The text appears exactly as typed: plain text,
with no formatting or links.

### Turning sections of the site on and off

Near the bottom of Site Settings is a set of switches labeled **Feature — …**, one for each major
section: the video editor, equipment, events and calendars, local discovery and maps, group public
pages, the media library, group messaging, voting, and the two newer features described below.

Each switch does exactly what it says. The links disappear from the navigation **and the addresses
stop working**, so someone who bookmarked a page, or who gets a link from a colleague, sees the
ordinary "page not found" screen instead of a way in.

Here's what some of the switches take down. **Local discovery and maps** removes the "what's near
you" panel, the home-page maps and the nearby search. **Group public pages** removes every
/o/{group} page (for visitors who aren't signed in, too), along with the CMS editor and its tab.
**Voting** removes the vote controls from cases, evidence and files, and the server refuses votes
sent any other way. **Events and calendars** removes the Calendar tab, the public What's-on list,
event pages, RSVPs and the reminder emails all at once, so nobody can sign up for an event and then
never get a reminder.

What a switch does **not** do is delete anything. Equipment records, saved videos, messages and
votes all stay exactly where they are, and turning the switch back on brings the section back with
its contents intact. Use it to take a section down while something is being fixed, or to run the
site without a feature your groups don't want. Don't use it to remove data.

Two things worth knowing:

- **A change takes up to about half a minute** to reach every page, because the site keeps the
  setting in memory instead of checking the database on every click. Your own browser sees the
  change as soon as you flip the switch.
- **If the site can't reach its API**, the switches fall back to their normal settings: the
  established sections on, unreleased features off. A connection problem will never make the site
  look as if it has lost half its features.

A switch that has never been saved shows the setting's default, marked **(default)** next to On or
Off, so the row always shows what the site is actually doing.

Two switches start **off**: **Public feed** and **Publications**. Both features are built, but
each one adds a public area that members and visitors will start using as soon as it appears. Turning
either one on should be a deliberate decision, not something a site gets by default.

- **Public feed** — the complete feature: anyone can read it without an account; members and
  clients post text, photos and video; posts carry experience-type categories the site learns
  from; groups decide which videos from their cases link back to them; and promoted cards rotate
  through it, shown nearest-first to viewers who share their location. Turning it on means taking
  on the job of moderating it (see [Moderating the Feed](/help/moderating-the-feed)). The
  **Feed Media** page tells you whether screening is **automatic** (the on-server model is
  installed) or **manual-only** (every photo and video waits for a person). Don't launch on
  manual-only unless you mean to. While the feed is off but has content, every SuperAdmin sees a
  reminder banner naming this switch, so the feature can't be forgotten by accident.
- **Publications** — long-form writing by groups, readable by visitors without an account. Turning
  it on adds a **Publications** entry to the menu for everyone, and a **Publications** tab to each
  group's page for its administrators. Nothing appears in the public directory until a group
  actually publishes something, so at worst visitors see an empty directory, not an unfinished one.

## Closing the door to new ghost walking tours

**Administration → Site Settings → Allow new ghost walking tours** switches off new sign-ups as a
ghost walking tour.

It only affects new tours. Every tour business already signed up carries on as before: its tours,
dates, sign-ups and billing are untouched, and nobody loses anything they're paying for. What stops
is starting a new one. The option disappears from the Start a Group wizard with a sentence
explaining why, the server refuses a new tour registration, and an existing group can't start
running tours while the switch is off. A SuperAdmin can still create one at any time.

**Paranormal events businesses aren't affected.** They're on the same flat plan, but they're a
different kind of business, and this switch is only about walking tours.

If the setting has never been saved, it counts as **on**, so a site that never touches it behaves
exactly as it always has.

## A person's record

**Administration → Users** lists every account. The view button on a row opens that person's
record in tabs: their profile fields, addresses, emails, phones, links, notes, memberships, files
and site roles. You can edit each tab directly, and every change is written to the audit log under
your name. A photo in the files tab opens in the photo editor; see *Editing a photo* in
Organization Administration.

### Former members

Closing an account doesn't remove it. The person's name, contact details and sign-in credentials
are removed, but the account itself stays, so the cases, evidence and messages they wrote for a
group stay where the group left them, signed "A former member". Deleting the account outright
would also delete the group's record of its own work.

So every account that has ever been closed is still in this list. **Include former members**, above
the grid, controls whether you see them. It's off when you arrive, and a count next to it shows how
many are hidden. If a search finds nobody, that count reminds you to check the box before assuming
the person was never here.

A group's own member list never shows former members. Nobody there can give a closed account a
role, invite it or ask it to do anything, so it would only clutter a screen people work from.

### Last sign-in and how many

Two columns on the list show who is actually using the site. **Last sign-in** is the most recent
sign-in; **Sign-ins** is the total. Both columns sort, so clicking either heading puts the quietest
or busiest accounts at the top. An account that has never been signed in to shows **Never** instead
of a blank.

Every way of signing in counts the same: a password, Sign in with Apple, a Microsoft account, and
the code that passes a session to the standalone editor.

Two things to keep in mind before you read too much into a number:

- **Nothing goes back further than August 20, 2026**, when the site started recording sign-ins. An
  account older than that has a count starting from that day, not from the day it was created.
- **A Microsoft account is counted once every twelve hours of use**, not once per sign-in. A
  Microsoft session doesn't have a single moment when someone signs in; it's checked on every page.
  So there's no exact count: a working day counts as one, and coming back in the evening counts as
  another. Microsoft sign-ins weren't recorded at all before September 19, 2026.

Once an address has been placed on the map, the **Addresses** tab shows two buttons beside it:
**Map** shows the address with its region circle, and **Directions** opens the directions
window.

![Driving directions to a person's address](help-media:site-administration/directions.png)
*Directions from a typed starting point, drawn on the map and read out beneath it.*

Type a starting address (a town is enough), or press **My Location** to use where your browser
says you are, then press **Get Route**. The route is drawn on the map with the start marked **A**
and the address marked **B**, and the distance, driving time and turn-by-turn steps appear below.
**Print** prints the window as it is; **Open in Maps** sends the same route to Apple Maps, which
opens the Maps app on a phone. The route isn't saved.

An address that couldn't be placed on the map has neither button. Correct it and save, and the
lookup runs again.

## Impersonating a member

**Administration → Users → the impersonate button** signs you in as that person, and you see
exactly what they see: their navigation and groups under Home, their notification counts on the
bell, their pages, and anything they'd be refused. Your own Administration menu disappears while
you're viewing, since it's your tool and not part of their experience. A banner at the bottom of
the sidebar shows who you're viewing as, with **Return to SuperAdmin** next to it. Both stay in
place if you reload the page, so getting out of impersonation always takes just one click.

Use it to see a reported problem through the reporter's eyes before you decide what's causing it.
Anything you do while impersonating really happens on their account.

## Site roles

There are four roles that apply across the whole site, separate from anyone's role inside a group.
They combine: one person can hold any mix of them.

- **SuperAdmin** — everything: every page under Administration, impersonation, billing, and the
  power to give out these roles.
- **Admin** — can see the administration help documents and, for now, nothing else. The role
  exists so that its powers can be widened one at a time rather than all at once.
- **Moderator** — reviews what people post: the feed's report queue and the media waiting for
  review, with the power to approve, hold or hide. No billing, no user administration, no
  impersonation. A SuperAdmin can moderate without holding this role.
- **Seller** — a member who makes and sells their own items in the store. Only people with this
  role are listed in a product's **Seller** field. It doesn't open any part of Administration, and
  a seller never sets a price or puts an item on sale; a SuperAdmin does both.

Everyone else is an ordinary member. Creating an account and confirming an email address makes
someone a verified member; it never puts them in a site role.

**To give somebody a role**, open **Administration → Users**, view the person, and open the
**Site Roles** tab. Check the roles they should hold and press **Save Roles**. Whatever is checked
when you save is exactly what they end up with, so unchecking a role removes it. The roles they hold
also show as badges beside their name at the top of the page.

The change takes effect the next time they sign in. Any session they already have open ends within
the hour, so a removed role can't linger longer than that.

The page won't let you do two things. You can't remove your **own** SuperAdmin role; another
SuperAdmin has to do that, and the box is locked to show it. And nobody can remove the **last**
SuperAdmin on the site, so make someone else a SuperAdmin first. Either change would leave nobody
able to reach this screen.

**Administration → Site Roles** lists the roles themselves. Here you can add a new role name or
delete one that nobody holds, and see how many people hold each role. A role you add here appears
on the Site Roles tab right away, but it doesn't grant anything until the site's code checks for it
by name. It's a label, not a permission.

## The clipart library

**Administration → Clipart Library** manages the shared artwork every group can use in the video
editor. Upload the file first, then choose it from your media library with the picker and publish
it. The format is read from the file itself (SVG, PNG, WebP, AVIF, GIF or Lottie), and anything else
is refused, so nothing is published that the editor can't draw.

Artwork is **retired**, never deleted. Projects refer to artwork by id, so deleting a piece would
break videos that already use it. Retired artwork leaves the catalog but can still be downloaded.

## Keeping the shared vocabulary tidy

![The equipment taxonomy screen](help-media:site-administration/equipment-taxonomy.png)
*Makes and models members have proposed, waiting to be approved, merged or renamed.*

Two lists grow from members' suggestions rather than being set from the top: the **experience
taxonomy** and the **equipment catalog**. Groups add what they need while they're out
investigating, and you confirm entries or tidy up afterward.

**Confirming** an entry marks it as reviewed. That does more than add a badge: only reviewed
entries are offered to the next person as "did you mean" suggestions, and they're no longer cleaned
up automatically. Confirming is how a word becomes shared vocabulary instead of one group's note.

**Renaming** does what it says, unless the new name is already taken. In that case you're shown
the existing entry with that name and offered a merge instead. Renaming onto an existing name turns
two entries into one and changes what some people's records mean, so it's always a separate,
deliberate step.

**Merging** moves everything over to the other entry and removes the duplicate. It can't be undone.
There are two safeguards:

- You can't merge a **confirmed** entry into an unconfirmed one. That's almost always the wrong way
  around: the approved word would disappear and the mistake would remain. Merge the other way, or
  confirm the target first.
- You can't merge an experience type into a **different category**. Moving a tag from Visual to
  Auditory changes what someone recorded about their own experience, which is more than a rename.

**Deleting** is only for an entry nothing uses. If it's tagged on anything, you're told how many
times and the delete is refused. **Reject** is the action that removes a type along with its tags,
and it tells you how many tags it removed.

Most tidying happens without you. An unconfirmed entry that a group suggested disappears on its own
once nothing uses it anymore.

## Support tickets

![The support ticket queue](help-media:site-administration/support-tickets.png)
*Every message sent through the contact form arrives here.*

**Administration → Support Tickets** is the queue for the public contact form. A ticket arrives as
**New**. Replying to the sender marks it **Answered** and assigns it to you if nobody had it yet.

The sender follows the conversation through a private tracking link, whether or not they have an
account, so your reply reaches them even if they can't sign in. **Internal notes are never shown
there.** They're for staff to talk among themselves, and adding one doesn't mark the ticket
answered.

The contact details shown beside the form (postal address, phone, email and reply times) are site
settings, so you can correct them on the Site Settings page without a site update.

## Sidecar installs

![The sidecar installs screen](help-media:site-administration/sidecar-telemetry.png)
*Which builds of the native helper are actually in use.*

**Administration → Sidecar Installs** shows the optional native helper people install to make the
video editor faster. The sidecar runs on a person's own computer and talks only to their browser, so
these records are the only way to see which versions people are actually running. That's useful
before you change anything about it, and it answers "can we stop supporting that version yet?"

Three numbers and a chart:

- **Installations seen** — distinct machines that have reported in.
- **Paired to an account** — how many of those were paired with a signed-in person. A gap between
  the two means people are installing it but not finishing setup.
- **People** — distinct accounts. This is lower than installations when someone uses two computers.
- **Installations by version** — how installations are spread across versions. Watch this after
  releasing a new build: a version that never grows suggests people aren't being told an update
  exists.

The table below lists the individual events. Nothing here identifies a machine beyond the
installation id the sidecar creates for itself.

## Outgoing mail

**Administration → System → Outgoing Mail** answers one question: *can this machine actually send
email right now?*

This matters because a mail failure doesn't show up anywhere else. Someone signs up, no email
arrives, and nothing on the site looks wrong: the account exists, the site is up, and the person
just can't get in.

**The answer can differ from one machine to another, so check it on the one you care about.** The
mail password is given to the running website as an environment variable on its application pool,
not read from a file in the repository. So a developer's laptop, a shell on the server and the
website itself can each get a different answer, and only the website's answer reflects what your
members experience.

The page shows the settings the machine will send with (host, port, security, user and
from-address) and whether a **password is present**. It never shows the password itself. There are
three states to know about:

| What it says | What it means |
|---|---|
| Mail is switched off on this machine | No host is set, so nothing is even attempted. Every confirmation, invitation and password reset is silently going nowhere. |
| A host is set but no password is present | The most likely real fault, and the one that looks as if nothing is wrong. Every visible setting can be correct and sending will still fail. |
| Settings listed, password present | Configured. Send a test to be sure. |

**Send a test message** does exactly that: it really sends a message instead of just checking the
connection. The failures that matter happen after the connection opens (authentication rejected,
sender not permitted, relay denied), and none of them show up until a message is actually sent.

If the test fails, you see the server's exact error, not a tidied-up "could not send".
*"535 authentication failed"* and *"connection timed out"* need completely different fixes, and a
polite summary wouldn't tell you which one you need.

If the test succeeds but the message still doesn't arrive, the mail left this server and the
problem is delivery: spam filtering, or the recipient's provider rejecting it after accepting it.

### Every letter the site meant to send

Below the settings, **Recent letters** lists what the site has actually tried to send recently. It
opens on the list you most likely came for: the ones it has **given up on**.

Each row shows who the letter was for, what it was, when it was written and how far it got:
accepted by the mail server, still waiting (with the time of its next attempt), or given up, with
the server's last error.

**Accepted isn't the same as received.** It means the mail server took the message. Only bounce
reports could tell you whether anyone got it, and this site doesn't collect them. If a letter was
accepted and the person still has nothing, the problem is delivery, not sending, and the mail
settings above won't help.

**Send again** puts one given-up letter back in the queue, and the sender picks it up on its next
pass, within five minutes. There's also a bulk version for after you've fixed a relay problem; it
tells you how many letters it will requeue.

Two kinds of letter can't be sent again, and they say so instead of offering a button that would
fail:

- One the mail server already accepted. Sending it again would send a duplicate.
- One whose **words were cleared**. A letter's body is deleted a month after it's accepted,
  because it can contain someone's name, what they booked and, for a hosted event, a working door
  code. The record of the letter is kept for as long as you need it, but its contents aren't. To
  send it again, the part of the site that wrote it has to write it again.

### Reading a letter

**Read it** on a row opens the letter the site actually sent, shown as the person received it. The
rest of the row tells you *whether it went*; this tells you *what it said*. That's what you need
when someone reports that a confirmation was wrong, blank or meant for someone else.

![A letter that was sent, opened for reading](help-media:site-administration/reading-a-letter.png)

Three things to know before you use it.

**Opening a letter is recorded against your account.** A letter contains someone else's private
details: a booking names a guest, a password reset contains a working link, and an event pass
contains a code that opens a door. You're allowed to read letters, because someone has to be able
to check what was sent, but the site keeps a record of who read whose letter. It appears in the
audit log as a **Read**.

**A letter whose words were cleared can't be read.** Instead of showing an empty frame, it tells you
when the words were removed, so you won't mistake it for a letter that was sent blank.

**The letter can't do anything.** It's shown in a sealed frame that runs nothing: no scripts, and no
requests out to anywhere else. A letter can contain whatever someone typed into a form, so this
keeps it from doing any harm while you're looking at it.


## Email templates

Every letter the site sends is written in code. **Email templates** lets you write your own version
of any of them, and your version is used as soon as you publish it.

**Deleting your version brings back the original.** Your template only takes the place of the
built-in letter, which never changes, so "Use the site's letter" undoes everything and there's
nothing to restore.

### Filling in the details

A letter can include the reader's name or the date by using a **token**: a word in braces that is
replaced when the letter is written.

Pick a **table**, then a **column**, then press **Add token**, and `{AppUsers.DisplayName}` is added
to the body. Move it wherever you want it.

![Writing a letter: the table and column dropdowns, the pieces, and the ready-made tokens](help-media:site-administration/email-template-editor.png)

The tables offered are the ones *that letter* actually has information from. A password-reset
letter only knows about the person, so it offers only `AppUsers`, and it won't save a token it
could never fill in, so no letter goes out with a gap in it. Columns meant for the site's own use
rather than for readers (anything holding a password, a security code or internal record-keeping)
are never offered.

There are also ready-made tokens that don't need a table:

| Token | Looks like |
|---|---|
| `{Date}` | 09/20/2026 |
| `{Time}` | 9:05 AM |
| `{FullDate}` | September 20, 2026 |
| `{FullDateTime}` | September 20, 2026 9:05 AM |
| `{Year}` | 2026 |
| `{SiteName}` | IsHaunted.com |
| `{SiteUrl}` | https://ishaunted.com |

**Times are shown in the reader's time zone, not yours.** A letter written at 9:05 in the morning
in Tennessee says 10:05 to someone in New York and 3:05 in the afternoon to someone in London. Date
columns from a table work the same way, so everyone sees times in their own time zone.

### Looking at it before anybody gets it

**Preview** fills in the tokens with made-up details (Marguerite Ashdown, at 1201 Del Rio Pike) and
shows the letter as it would arrive. No real data is used, so a preview never shows you anyone's
actual name or address.

![The body of a letter being written, with the preview underneath](help-media:site-administration/email-template-preview.png)

**Save draft** keeps what you're working on without changing anything anyone receives. **Publish**
is what makes it take effect, starting with the next letter of that kind. Letters already in the
queue aren't rewritten.

### Writing the body

The body is HTML, and email HTML isn't the same as web HTML: use tables for layout, write every
style directly on the element, and don't use a stylesheet. Outlook displays mail using Word's engine
and Gmail strips out style blocks, so anything fancier breaks somewhere. That's why the box is a
plain one rather than a word processor: a rich editor would quietly rewrite the very markup that
keeps a letter looking right in those email programs.

If a template ever fails to render, the site sends its own letter instead and records why. Nothing
you write here can stop a letter from going out.

### The store's letters

The store sends six letters, and you can edit each one here like any other: the buyer's **receipt**
("Thank you for your order"), **Your order is on its way** (with the carrier and tracking, or a note
that it was sent without tracking), **A refund on your order**, **A link to your order** (for "Find
my order"), and, to SuperAdmins, **A new store order** and **Store stock is running low**. Each has
a starter version to begin from and sample values in the preview. Once you publish a template, it's
used from then on; deleting it brings back the site's own letter.

## Knowing whether a member was ever emailed

**Administration → Users** has a **Verified** column that shows which of three situations an
account is in:

- A check mark with a date — confirmed, and when.
- **Sent \<date\>** — the link went out and they haven't used it yet. Nothing is wrong; they just
  haven't gotten around to it, or it's in their spam folder.
- **No email sent**, in red — no confirmation email has ever successfully left this machine for
  that account. **Nobody can complete it**, however long they wait. The fault is with the site, not
  the member; check Outgoing Mail above.

That last one is the row worth scanning for. It's how you tell someone who hasn't bothered from
someone who never got the chance.

## Audit log

![The audit log with its filters](help-media:site-administration/audit-log.png)
*The audit log records who changed what, and when. Filter it by entity, action, person or date.*

**Administration → Audit Log** records every change and who made it. Filtering and paging happen on
the server, so date and user filters search the whole history, not just the page on screen.

## Error log

![The error log with its summary cards, filters and grid](help-media:site-administration/error-log.png)
*The error log: how many rows, the oldest kept, the most repeated message — then the rows themselves, newest first.*

**Administration → Error Log** is the audit log's counterpart, and the two are easy to confuse. The
audit log records what people did on purpose and is kept for years. The error log records what
broke, and old entries are removed after a retention period (thirty days unless configured
otherwise).

Open a row to see the full message, the exception and the request path. The path is usually the
quickest way to pin down a fault, because it names the endpoint that was being called when it
happened.

**Read the cards before the rows.** If one message makes up half the log or more, the page tells
you. That's a finding in itself: a flood of one message buries everything else until you deal with
it, even though each entry looks harmless on its own. The *Most repeated message* card is there to
catch exactly that.

**What isn't here.** Only errors are recorded. The database log is set to Error level, so warnings
never reach it. That matters more than it sounds: a failure logged as a warning leaves no trace on
this page at all. If something is clearly failing and nothing shows up here, suspect the logging
level before deciding there's no fault.

**Nothing on this page deletes anything.** Old entries are removed only by the retention job, which
has a minimum window and works in batches. There's no button to empty the log, so evidence someone
is about to need can't be wiped out with one click.

Administrators and SuperAdministrators can both open it, so whoever is on call can always see why
the site is failing.

## Billing

### A flat price for tour and event businesses

The price bands below set the price of an **investigation group** by the size of its team. A
**ghost walking tour** or a **public event provider** isn't priced that way. These businesses pay
one flat price regardless of team size, because guides aren't investigators and counting them
would price the wrong thing.

To offer that price, add a tier under **Administration → Subscription Tiers** and uncheck
**banded by members**; the pricing page labels it for tour and event businesses. Every
business-type group is then quoted, billed and renewed on that tier, and its member count is
ignored. If no such tier is offered, a business is priced by the bands like any other group, so the
offer in the tour-business mailing only holds while that tier exists and is active.

### Role areas on a price band

Each band has a **Role areas** checklist: the parts of the site (Cases, Equipment, Public pages and
so on) that a group on that band can build custom role permissions for. Every band starts with
everything checked, which changes nothing; unchecking areas is how you turn bands into different
products. Changes save as you click and apply to every group on the band.

The checklist is fully enforced. When a group's band leaves out an area, the role editor grays out
that area's section with a note naming the plan, and the server refuses changes to it while keeping
the saved permissions exactly as they are. Those permissions also stop taking effect: members lose
the tabs they gave access to, while owners and group administrators notice no difference. Nothing
is ever deleted. A paused permission comes back exactly as it was set up as soon as the area is
included again. Whenever any band leaves something out, the public Pricing page lists each band's
included areas.

Because unchecking saves with every click, the notices sent to groups are **netted**. A removal is
queued (groups with no plan hear about it after a short grace period, and paid groups hear before
their renewal), and checking the area again before the notice goes out cancels it. So an accidental
click means groups hear nothing, rather than getting two contradictory messages. Newly included
areas are announced right away.

### Capabilities on a price band

Next to the role areas is a **Capabilities** checklist of simple yes-or-no permissions.
**Case transfers**: a band without it can't send a case to another group or accept a case
transferred in. Both ends are enforced, so a case can't be handed TO a group whose band lacks it
either. Declining a transfer never requires the capability. **Audio/video location stripping**:
whether the group's media privacy setting can remove location data from audio and video files.
(Photos are always cleaned, and every file's metadata is always extracted and kept regardless.)
**Private-residence cases**: whether the group can take on client and residence work at all.
Accepting a client request, placing an investigation at a residence, receiving a private case and
publishing one are each checked. Work a group already has keeps working, and a client moving their
own case is only ever limited by the receiving group's band. Unchecking doesn't affect existing
cases. Changes save as you click and notify the affected groups through the same netted notices as
the role areas, and the public Pricing page says plainly what a band leaves out.


**Administration → Billing** has three screens.

**Price Bands** is the price list shown on the public Pricing page: member ranges, a price for each
billing cadence, and the caps each band includes. Every save checks the whole list: the bands must
cover every possible member count with no gaps or overlaps, and a save that would leave anyone
without a price is refused, with the reason. Bands are retired, never deleted, because a band that
has priced a billing period is part of the billing record. When an edit would affect groups already
on the band, the save pauses and first shows you who will be affected. Improvements are announced
to those groups immediately. Reductions are queued and delivered up to two weeks before each
group's own renewal, and paid groups keep the terms they bought until then.

### Reshaping the ladder

Editing one band at a time works for ordinary changes, but it can't make every valid change,
because each save is checked against the list as it will look after that single edit. Splitting an
open-ended top band shows the problem: giving the top band an upper limit leaves the members above
it without a price, and adding the new band above it first overlaps the open-ended band below. Both
orders are refused, so you can't get there one band at a time.

**Reshape the ladder** lets you edit every band on one form, and only the end result is checked.
Add a band, remove one, move the boundaries, and save once. The rules are the same (start at one
member, leave no gaps between bands, and give the last band no upper limit), but they're checked
against the finished ladder rather than each step. If the save is refused, nothing changes.

The list on that form **is** the ladder. A band you remove there is retired, not deleted, for the
same reason as everywhere else: a band that has priced a period is part of the billing record.
Groups already on a retired band keep the terms they bought until they renew.

There's no free band, and there shouldn't be. A group is free by having no plan at all, not by
having a plan that costs nothing. A plan that costs nothing is still a plan, and every paid feature
treats it as one. The editor warns you if a band is ever priced at zero.

**Coupons** manages discount campaigns. A *shared* campaign is one code that everyone types in. A
*generated* campaign is a batch of single-use codes you can print or mail; each one can be withdrawn
on its own and assigned to a specific account. A campaign can be limited by date range, total
redemptions (the budget), billing cadence and occasion (first subscriptions only, or renewals
only). A misconfigured campaign (one that takes nothing off, or whose window closes before it
opens) shows a red **Broken** badge in the list instead of silently failing for whoever types it.

Every code also works as a **referral link**. The codes panel's *Copy link* button gives you a
`/pricing?code=…` URL to pass to whoever is promoting or selling access. Visitors arrive with the
code already applied and see it priced for their group before they confirm anything. The code's
redemption count is the referrer's scorecard.

**Subscriptions** shows every group's billing status, including groups that were never set up,
which is the first thing to look for. Card payments made on the site are recorded here by themselves;
when a group pays some other way, set their band, cadence and period here. The member count and
price are locked in when you save, and the group keeps those terms for the whole period, whatever
happens to the price list. The Members column shows the current count next to the locked-in one. At
renewal, the group is re-banded based on the current count.

## Taking plans off sale

**Sell plans and seats**, under *Selling plans* in Site Settings, controls whether anyone can buy.
It's on unless someone has turned it off.

When it's off:

- the pricing page still shows every band and price, with one line saying plans aren't on sale;
- a group's billing page shows the same sentence instead of **Subscribe**, and a member with an
  unpaid seat is told seats can't be paid for on the site at the moment;
- the site refuses to start a payment for a plan or a seat, even from a page that was already open.

Nothing already bought changes: plans and paid seats continue and renew as before. Event credits
have their own switch, **Sell event credits**.

## The storage ceiling

**Administration → Site Settings → Limits → Free account storage (MB)** sets how much a free account
can store for itself. Leaving the box empty is safe: there's a built-in default of 2048 MB.

Raising it takes effect immediately, with no site update needed. When more disk space becomes
available, this is the one number to change.

![The storage ceiling](help-media:site-administration/storage-ceiling.png)
*Free account storage, in the Limits group. Empty means the built-in default.*

**What counts against it** is everything stored under the person: recordings, photographs, video
projects, equipment pictures, place evidence and feed media. What doesn't count: a group's own
work, the old version of a replaced file, and anything published to a public location's archive,
which earns its space. Members of a group on a paid plan have no cap at all.

**People are warned before they reach it.** A message goes out at 90% used and a stronger one at
95%, each sent once. Someone who frees up space and drops back under 90% is warned again the next
time they fill up, so nobody is warned once and then hits the limit later without notice. This
means lowering the ceiling can put accounts straight into the warning range on the next check.
That's intended, but worth expecting.

## Places

**Administration → Places** lists every location on the site: the homes groups work in and the
public locations anyone signed in can contribute to. Search by name, street, town or state, and
filter to one kind or the other.

Each row shows the three counts that decide what you can do with it: cases, visits and pieces of
evidence. A row with nothing linked to it can be removed. A row with anything linked to it should
be merged or made private instead.

**Add a place** opens the same form everyone else uses, so a location you add is checked against
existing places instead of becoming a second record of the same building.

### Correcting a place

The pencil on a row opens the place for editing: its name, street, town, state and ZIP, and its
position on the map.

This is the only way to correct a place. When someone adds a public location at an address that's
already recorded as a home, the page tells them "there is already a place at that address, and it
is recorded as somebody's home — if that is wrong, ask a site administrator to correct it". This is
where you make that correction.

**An address that already belongs to another record is refused.** Two records for one building
would split its evidence between them, which the archive is designed to prevent. So an edit that
would move a place onto an address already in use stops and names the record that's already there.
Use **Duplicate Places** to combine the two instead.

**Coordinates.** If you type them in, they're kept exactly as entered, since someone correcting a
record by hand usually knows better than an automatic lookup. If you leave them empty and change
the address, the old position is discarded and the new address is looked up. A pin left over from
the previous address would be worse than no pin, because nothing on the map would show it's wrong.

**Look up the address again** reruns that lookup without changing anything else. Use it on a row
that says "not on the map". If the lookup still can't place the address, the page tells you why
instead of showing an empty map, because otherwise a misspelled street and an address the mapping
service simply doesn't have would look the same.

### Taking a place off the public map

This fixes the mistake that matters most: someone's home entered as a public location. A public
location has a page strangers can read and add photos to, so a home listed that way becomes a
public evidence page for the address where a family lives.

**Make it a private residence** removes the page and stops anyone from adding to it. Nothing
recorded there is deleted: the group working the case keeps every file, session and note. It's the
safe choice, and almost always the right one.

Going the other way, **Make it a public location** is refused for any place with a street number.
If a genuine landmark has a street address, clear the address first. This is deliberate: one wrong
click on a list of fifty places is exactly how someone's home could end up public.

### Deleting a place

You can only delete a place when nothing at all is linked to it: no case, visit, evidence, event,
room, venue profile, contact or claim. If anything is, the refusal says what ("3 cases and 6 pieces
of evidence still point at it") so you know what to move first.

For a place that duplicates another, use **Duplicate Places** instead: merging moves everything onto
the record you keep and then removes the empty one. For a place with real history that shouldn't be
public, make it private rather than trying to empty it.

## Merging two groups

**Administration → Groups → Merge Groups** combines two organizations into one. Choose the
**base** (the group that remains, keeping its URL and settings) and the group to merge into it,
then read the preview before anything happens. It lists exactly what will move and every conflict,
and viewing it changes nothing.

The merge happens in a single step that either completes fully or leaves both groups untouched.
Here's what it does:

- **Everything the merged group owns moves to the base**: members, cases, investigations, files,
  equipment, pages, calendar and messages. The preview's list is built directly from the database
  structure, so nothing is left out.
- **A person in both groups** keeps one membership, at the **higher** of their two roles.
- **Case numbers collide by design**, since both groups started at #1, so merged cases with
  clashing numbers are renumbered to follow the base's sequence. Page addresses that clash get a
  suffix.
- **The merged group's URL becomes a permanent alias** of the base, so old links keep working and
  no new group can ever take that name.
- **The merged group's subscription is dropped**, and the base's plan covers the combined group.
  If money is owed back, record it as a ledger adjustment.
- **Everyone is told**: former members get a message that their group is now part of the base,
  and clients with open cases are told the group handling their case has a new name.

You choose the name after the merge: either group's name or a new one. To confirm, you type the
merged group's name, because there's no undo.

## Deleting a case

**Administration → Groups → Delete a Case**, or the trash button on any row of
**Administration → Cases & Investigations → All Cases**, which opens the same screen with that
case already chosen.

This is the only place on the site where a case can be deleted. Groups can't delete cases; they
close a case and keep it. This page is for mistakes that closing can't fix: a duplicate, a test
case, or a case opened under the wrong group.

Read the preview first. It has two parts, describing two different things that will happen.

**Destroyed.** Everything that exists only because the case does: its timeline, files, notes,
messages, research boards, reports, investigations, contacts, votes, transfer records and any client
access records. Files the case made its own copy of are destroyed with it.

**Kept, with the case reference removed.** Anything that belongs to someone else and only mentions
the case. In particular, **field sessions survive**: a recording belongs to the person who made it,
so each one goes back to them as a personal session, with its files, readings and share links
untouched. Feed posts, calendar events, video projects, evidence votes and public pages all stay,
and simply stop pointing at the deleted case. The client's original request is kept too.

You're warned about two things, but neither stops you: the case has a **client**, whose record of
the work they asked for is deleted with it; or the case is **public**, so its page and any links to
it stop working. Neither one blocks the delete. The point is that you know.

Then type the case title exactly. The button does nothing until it matches, both on screen and on
the server.

## Deleting a person

**Administration → Groups → Delete a Person**, or the trash button on any row of
**Administration → Users**, which opens the same screen with that account already chosen.

Read the preview before you agree to anything. It has two parts, describing two different things
that will happen.

**Destroyed.** Everything that's only theirs and that nobody else has a claim on: field sessions
they recorded on their own rather than for an investigation, the files in those sessions, their
memberships, sign-in history, messages they received, follows, blocks, contact details and any
external sign-in methods. An Apple sign-in is also revoked with Apple, so IsHaunted disappears from
the list of apps using their Apple ID. If Apple can't be reached at that moment, the deletion still
goes through and the revocation is retried later.

**Kept, with their name removed.** Anything they wrote for a group: case notes, timeline entries,
group messages, evidence, and sessions recorded for an investigation. Those belong to the group,
and often to a paying client. One person leaving shouldn't erase a group's record of its own work,
so these records stay and are credited to a former member.

### Whether the account itself disappears

Before you press anything, the screen tells you which of these will happen.

- **Removed completely** when nothing else in the database refers to the account. A sign-up that
  never did anything disappears entirely.
- **Kept, emptied** when anything still refers to it: records written for a group, a session
  recorded for an investigation, or a file something else still uses. Everything about the person
  is removed (name, email, password, sign-in methods) and the account can never sign in again, but
  the account record stays so those other records still link up correctly.

The difference matters. If you're clearing out a test sign-up, you want the first, and the screen
will say so. If you're removing a real member of a real group, you'll get the second, and you'll
know that before you go ahead.

### Two warnings, and one refusal

The screen warns you, without stopping you, when:

- **They own a group.** Each group has exactly one owner, so the group will be left with nobody
  able to manage it. Appoint a new owner first, or be ready to do it right afterward.
- **They're paying for a seat.** Nothing here cancels a subscription, so the card on file keeps
  being charged for an account that no longer exists. Cancel it first.

It refuses outright in only one case: **the last SuperAdmin account**. A site nobody can administer
can't be recovered.

To confirm, type the person's display name exactly, just as you would when deleting a group, and
for the same reason: there's no undo.

You can't delete your own account here; do that from your own profile.

## The money trail

Three more screens under **Administration → Billing** handle the actual money.

**Ledger** is the record: every charge, payment, adjustment and referral payout, newest first. You
can only add to it. There's no edit and no delete, here or in the API behind it. To fix a wrong
entry, record an **adjustment** that names the mistake, just as you would in a paper ledger. That
way the answer to "who changed this number?" is always the same: nobody. Recording a **charge**
calculates tax from the group's state and locks both the rate and the dollar amount on the row.
Recording a **payment** assigns the next receipt number, and the group can download that receipt
(and download it again at any time) from their own billing history on the Pricing page. A payment
carries no tax of its own: the tax was on the charge it pays, and counting it twice would make the
figures wrong.

**Tax Rates** holds the current rate for each state, matched against the group's address. A state
with no rate is taxed at **zero**. That's the honest default, since many states don't tax this
service, and a visible zero on a bill gets questioned, while a quietly guessed rate might not.
Editing a rate never changes history: every document keeps the rate it used.

## Overflow seats

A band covers a range of member counts. When a group grows past its band, the group's own plan
doesn't change. Instead, each person who joins beyond the band is billed **individually**, at the
per-extra-member price you set on that band's price row. The group keeps one contract with one
renewal date, and each extra person holds a seat.

To set it up, put a **per-extra-member price** on a band's price row in **Price Bands**. That also
changes what the price list accepts. Normally the top band must have no upper limit, so that a
group can never outgrow the list. A band that prices extra members can have an upper limit,
because growth beyond it is priced per seat instead of by a bigger band.

**Member Seats** is the worklist. A new member who takes a group past its band gets a seat marked
**Awaiting payment**, and their acceptance message tells them the price. A seat never stops anyone
from joining: they're a member from the moment they're accepted, and the seat is only the billing
record. When they pay, record the payment on the Ledger and set the seat to **Active** with its
period. Recording the payment and activating the seat are deliberately two separate steps.

## Event credits

**Event Credits** lists every credit ever bought. One credit publishes one hosted event, whatever
it costs to run and however many nights it lasts, and is good for a year from the day it was
bought. It's spent as soon as the event goes live. The price it was bought at is locked in on the
row, so changing the price never affects a credit someone already holds.

The screen shows who holds each credit, when it expires, whether it has been spent and on what, and
the receipt from the purchase. An envelope next to the expiry date means the holder has already had
the thirty-day warning. That warning goes out by email, or to the notification bell for anyone who
can't be emailed.

**Granting** gives a group credits nobody paid for: a purchase that never went through, an apology,
or a credit someone was promised. Choose the group and how many, and give a reason. The reason is
required, because it and your name on the row are the only record that the grant happened. In every
other way, a granted credit works exactly like a bought one: a year to use it, spent when an event
is published, oldest first, with a warning at thirty days. It shows as **Granted** instead of $0.00
in the Paid column, both on this screen and on the group's own.

**A grant adds nothing to the ledger.** A $0 charge and payment would put a sale that never happened
into the money trail, and the receipt would say someone paid nothing. If money really did change
hands outside Stripe (a check or a bank transfer, say), record that on the Ledger as well, as its
own payment.

**Refunding** is the other reason this screen exists, since nobody can refund themselves. A refund
needs a reason. It marks the credit so it can never be spent, whatever its date says, and adds a
credit adjustment to the group's ledger. Uncheck that box if you've already returned the money
through Stripe, because Stripe's own record reaches the ledger separately.

A **spent** credit can't be refunded, and the refusal names the event it was used on. Each event
uses one credit for its whole life. Giving the credit back would make that event's payment record
wrong, and republishing the event would then never charge for it. If money really does have to be
returned for a live event, record a **credit adjustment** on the Ledger instead. An **expired**
credit can still be refunded. That's the goodwill case, for someone who paid and never got to use
it. Refunding a **granted** credit just revokes it and adds nothing to the ledger, since there's no
money to return.

## Hosted events

### The events dashboard

**Administration → Dashboard** has three tabs. **The site** is the dashboard described above;
**Events** is a similar page for hosted events across every group; and **Event health** shows
whether hosted events are working (see below). The range picker controls all three.

![The events dashboard's cards](help-media:site-administration/events-dashboard.png)

The cards show where things stand right now:
- events on the site, with drafts, past events and canceled events listed underneath;
- organizers (groups with at least one hosted event);
- venues, and how many are confirmed for their building;
- event credits held;
- people confirmed at events that are still taking bookings;
- appeals waiting for an answer.

The charts cover the chosen period: events created and published, bookings made and their status,
credits bought and spent, the biggest events, and the busiest organizers and venues. **Where events
happen** counts published events by the state their venue is in.

The dashboard only shows counts; it never names a guest. To see individual events, use the events
list.

### Every event, and removing one

**Administration → Events** lists every hosted event, whatever its state: the person who created it
and their group, the event and its venue, the dates, its state, and how many people are coming.
Search matches the event, the group, the organizer or the place, and the state filter narrows the
list.

![Every hosted event](help-media:site-administration/every-event.png)

The eye icon opens the event's own page as its organizer sees it. The arrow at the start of each
row opens every other screen for that event: plan, bookings, menus, the kitchen's sheet, program,
staff, the door, bands, files, gallery, what happened afterward, keeping the files, copying it, the
photo wall and, while it's published, in progress or just ended, its public page. A SuperAdmin can
open all of them for any group's event, including groups you don't belong to.

The trash icon opens **Remove an event**, which tells you what removing it will do before you
confirm:

- **It comes off the site** and can't take bookings. Nothing is deleted: the event, its bookings and
  its history all stay.
- **Everyone who has a place or is on the waiting list is told** it isn't going ahead, with no
  reason given, and their passes stop working.
- **The event credit spent on it is returned** to whoever paid for it, regardless of timing.
  Publishing the event again later spends another credit.
- **The organizer is emailed** that the event doesn't meet the guidelines for hosted events, with a
  link to appeal. The letter is deliberately generic. Your **note for the record** is kept with the
  removal for whoever reviews an appeal, and is never sent to anyone.

![Removing an event says what it will do first](help-media:site-administration/remove-an-event.png)

The organizer can't un-cancel, restore or publish a removed event.

### Appeals

The removal letter takes the organizer to their event's page, where a card explains the removal and
lets them appeal once. When they send the appeal, SuperAdmins get a message, and the appeal waits at
the top of **Administration → Events**. It shows the organizer's message, what the event looked like
when it was removed, whether its credit was returned, and your note from the removal.

- **Uphold** brings the event back as a **draft**, never straight onto the site. Its guests were
  told it wasn't going ahead, so putting it back up has to be the organizer's deliberate decision.
- **Decline** requires an answer. The organizer reads it, and the event stays removed.

Either way, the organizer is told by email and in their messages. Each removal allows one appeal. If
an event is removed again after an upheld appeal, it can be appealed again.

## Event health

**Event health** is for whoever builds or looks after hosted events. It shows whether the feature is
working, while **Events** shows how it's being used. It counts and names addresses and jobs, but it
never names a guest.

The cards show where things stand right now:
- **holds live**, and how many will expire in the next 24 hours unless someone answers them;
- **waiting for an answer**: requests and holds nobody has answered at events still taking bookings,
  and how long the oldest one has been waiting;
- **letters in the outbox** not yet sent, and how many were given up on during the period. This
  covers all of the site's mail, because the outbox doesn't record which letters are about events;
- **errors on event addresses** the server logged during the period.

The charts cover the chosen period:
- **Bookings made, answered and lapsed**, by day. If lapses climb while answers stay flat, nobody is
  watching the board.
- **How long parties waited** for an answer, from within an hour to over three days.
- **Letters** queued, sent and failed, by day.
- **Turned away by a limit** — requests refused by the booking and attendance limits, for all time,
  with the last day each limit turned someone away.
- **Errors on event addresses**, by day and by address, from the server's error log. Ids in the
  addresses are shown as `{id}`. Days follow the server's own clock. Where the log can't be read
  (it's only kept on SQL Server), the panels say so instead of drawing an empty chart.

**Scheduled jobs** lists every background job (expiring holds, moving events through their states,
alerts and digests, reminders, thank-you letters, retention, sending mail and the rest) with when it
last ran, how long it took, whether it failed and the first line of its most recent error. It counts
from when the server last started, so a restart clears it. Every job runs every five minutes, so a
job that stops appearing or keeps failing is the first place to look.

## Referrals and what they earn

**Referrals** shows where every referrer stands. A referrer is anyone a coupon campaign is credited
to (set on the campaign, using their account email), and every redemption of their codes counts as
their referral, with the amounts locked in at the time of redemption. The screen shows what their
codes brought in, the discount given, what they're **owed**, what has been paid, and the
outstanding **balance**. Owed is a **percent of what redeemers actually paid**, set on each
campaign, because deals differ from one referrer to another. A campaign with no percent isn't
counted, and when that happens the owed figure shows a **partial** badge, so a low number is never
mistaken for a final one. Recording a payout fills in the outstanding balance for you, and it goes
on the ledger like everything else.

## The store

The store sells scientific gear for investigations (EMF meters, spirit boxes, recorders) to anyone,
with or without an account. Everything about it is managed under **Store** in the administration
menu: the dashboard, categories, products, stock, discount codes, reviews and the store's own
settings. All of those screens work while the store is switched off, so you can enter, price and
photograph the catalog before any visitor can see it.

![The store dashboard: orders to pack and ship, reviews waiting, sales, and what is running low](help-media:site-administration/store-dashboard.png)

## Turning the store on

There are two switches, for two different jobs:

- **Feature — Store**, under Site Settings → Features, shows or hides the whole store. When it's
  off, the store's pages, cart and checkout show "page not found". Orders already placed aren't
  affected: a buyer's order page, the thank-you page and the links in their emails keep working,
  and so do these admin screens.
- **Take orders**, on the store settings page, pauses buying without hiding anything. Visitors can
  still browse the catalog, and every cart says the store isn't taking orders at the moment. This
  is the switch to use if something goes wrong with payments.

Before turning the store on, open **Store Settings** and read the **Ready to sell** checklist. It
lists every reason the store couldn't take an order right now (no ship-from address, nothing live
with stock, missing Stripe keys, Stripe Tax not active in the Stripe dashboard, the store switched
off) and shows **Ready to sell** only when the list is empty.

![Store Settings, with the Ready to sell checklist beside the form](help-media:site-administration/store-settings.png)

## Categories and products

A **category** is a shelf: EMF meters, spirit boxes, field accessories. Each one has an address
(/store/c/emf-meters), an optional picture, a position in the order and its own on/off switch.
Hiding a category takes its products off the store without changing them. Before you confirm, the
page tells you how many live products it will hide, and showing the category again restores exactly
what was live. A category with products in it can't be removed; move them first. The last remaining
category can't be removed either.

**Subcategories.** A category can be placed under another one: choose the parent in **Sits under**
when editing it. Subcategories only go one level deep. A subcategory sits under a top-level
category, and a category that has subcategories stays at the top level. On the Categories page,
subcategories are indented under their category, and the arrows move a row up or down within its
own level. In a product's **Category** list they appear as "Category › Subcategory". Hiding a
category hides its subcategories' products too, and a category with subcategories can't be removed
until they're moved or removed.

![The products list: each product's price, stock, seller and whether it is live](help-media:site-administration/store-products.png)

**What shoppers see.** A category or subcategory appears on the store only when something under it
is on sale. A category includes its subcategories' products, so it appears as soon as any of them
has something for sale. An empty one stays hidden (its address says "Page not found"), but you can
always file products in it. The store's front page shows top-level categories, and the side list on
product pages shows subcategories under them. Only SuperAdmins can change categories.

A **product** starts with just a name. It's created hidden, with one variant at $0.00 waiting for a
price. **Activate** puts it on sale, but it refuses (and tells you what to do) until the product
has a price, at least one picture, a live variant and a visible category. A product's address
(/store/p/k-ii-emf-meter) is created from its name once and then kept, so renaming a product doesn't
break links people have shared. Only type a new address if you really mean to change it.

**Preview** (the button at the top of the product, the tab next to Pictures, or the eye icon on the
products list) shows the product page as shoppers will see it: the pictures, the price, the options,
the description and the specifications. It uses what's on the form right now, so changes show up
before you save them (the note above it tells you when something is unsaved), and it works while
the store is switched off. With the store on, **Open the saved page** shows the real page as it
currently is.

**Seller** names the member who makes and sells the product. Leave it on "The site's own stock" for
anything the site sells itself. Only people with the Seller role are listed, so give them the role
on their **Site Roles** tab first. Shoppers never see who the seller is; the products list shows it
in its **Seller** column. If a seller leaves the site, their products stay and become the site's
own.

A product that has been sold can be switched off but never deleted, because its orders, invoices
and refunds refer to it. For a product that differs only slightly, **Duplicate** makes a hidden
copy: options, variants with new SKUs and no stock, specifications and copies of every picture.

![Editing a product: its details, with tabs for options and variants, pictures and a live preview](help-media:site-administration/store-product-edit.png)

## Options, variants and stock

**Options** are what a buyer chooses, such as Color or Size. A product can have up to three, each
with its own values. (A Color can show round swatches from a hex color; everything else shows as
buttons.) **Generate variants** makes one variant for every combination the product doesn't have
yet, so two colors and three sizes make six. Running it again adds nothing. You can't remove a
value from an option while a variant still uses it.

Each **variant** has its own SKU, price, optional old price (shown crossed out; it must be higher
than the price) and stock. A variant that has been ordered can be switched off but not deleted, and
a live product always keeps at least one live variant.

Stock only ever changes with a reason (**Received**, **Correction** or **Damaged**), and every change
adds a line to the variant's stock log showing who made it, when, by how much and what was left.
The store writes **Sold**, **Refunded** and **Canceled** lines itself. A change is refused if it
would leave fewer on the shelf than open checkouts are holding.

The **Stock** page shows every live variant at once: on hand, held by checkouts in progress, and
free to sell, with a low-stock filter. Enter a delivery in the **Receive** column for each variant
it covers and save once. Either all of it is applied or none of it is, and a refusal names the SKU
that caused it. You can also upload a supplier's sheet as a CSV of `Sku,Delta` rows, with the same
all-or-nothing rule. **Export CSV** downloads the whole stock list for an inventory count.

![The stock page: on hand, held by checkouts, free to sell, and a box to receive more](help-media:site-administration/store-stock.png)

## Product pictures

Each product can have up to twelve pictures, shown in the order you drag them into. A picture can
be tied to a variant, so choosing that color shows that photo. Every picture is stored as a clean
copy, with the camera details and location removed, sized for a sharp product page, plus a small
copy for cards and the cart. A live product keeps at least one picture.

## Sellers and sale requests

A member with the **Seller** role (given on their Site Roles tab) has their own **Selling** section.
It only appears in their menu while the store is switched on, like every other way into the store,
but its pages still work at their addresses while the store is off. So a seller you send to
`/store/selling` can prepare their items before the store opens. There they add items as hidden
drafts and edit the wording, pictures, options, variants and stock. They can never change the
price, web address, placement, tax code or seller, and never another seller's item. A variant they
add starts switched off and unpriced.

A seller's item only goes on sale when they ask. **Store → Sale Requests** lists the waiting
requests, oldest first, each showing what the seller wants per unit (on top of cost) and what the
item still needs before it can go on sale. Set the selling price on the item's **Options &
variants** tab, then press **Approve and put on sale**. This locks in the seller's asking price on
the item as what they're paid. Or press **Decline…** and give a reason, which the seller will read.
The dashboard's **Sale requests** tile and a **Waiting** badge on the products list show when
requests are waiting.

**Put on sale** isn't offered on a seller's item. Approving their request is how it goes on sale,
because that records what they're paid. A seller can take their own item off sale at any time, and
SuperAdmins get a message when they do.

![Sale requests: the item, what the seller asks, and what it still needs](help-media:site-administration/store-sale-requests.png)

## Packages and partial shipping

An order ships as one **package per seller**: the store's own stock is one package, and each seller
sends their own. Every package has its own status (being prepared, packed, shipped, delivered),
carrier and tracking, or "No tracking provided". The order page shows a card for each package with
its own **Mark packed**, **Ship…**, **Correct tracking…** and **Mark delivered** buttons. A seller
does the same for their own packages from **Selling → My Packages**.

The order's status follows its packages: **Partially shipped** while some have gone and some
haven't, **Shipped** once all have gone, and **Delivered** once all have arrived. The buyer gets one
"on its way" email per package, listing only that package's items. Once any package has shipped, the
order can't be canceled as a whole and its address can't be changed; refund the items instead.
**Refunds and packages.** The **Refund…** dialog can return a package's shipping (and the tax on it)
along with items, but each package's shipping can only be refunded once. **Cancel package…** on a
package that hasn't shipped refunds its items and its shipping, puts the items back on the shelf if
you check that box, and marks the package canceled. The rest of the order carries on, and it shows
**Shipped** once everything left has gone. If it's the only package left, it can't be canceled on
its own; cancel the whole order instead, which refunds every package's shipping. A package that has
shipped can't be canceled; refund its items instead. The **To ship** tile counts partly shipped
orders too. The CSV export has a row for each item with its package, sender, carrier and tracking.

![An order in two packages, each with its own status, tracking and buttons](help-media:site-administration/store-order-packages.png)

## Parts and cost

Every product's **Parts & cost** tab holds its parts list (each part's price per pack or per piece,
how many one unit uses, links and a picture) and an **other costs** line. Together they make up the
product's **cost basis**, rounded to the cent once at the end. Sellers keep the list for their own
items; the store keeps it for its own stock. For each unit sold, a seller is paid the cost basis
plus their approved asking price. None of this is ever shown to shoppers.

## Product files

A product's **Files** tab holds its manuals, firmware, software and documents: uploads up to 95 MB,
or a manual written on the site (in Markdown, or imported from a `.md` or `.txt` file). Each file is
either **for buyers** or **private**. A file for buyers appears under **Downloads** on the order page
of everyone who bought the product, once they've paid, as long as the order isn't canceled and that
line wasn't refunded in full. A private file is only for the store and the product's seller.
Sellers manage their own items' files; the store can change any of them. Changes are recorded in the
product's history.

## FAQ and questions

Each product has an **FAQ** tab: questions and answers shown on its page after the reviews, with a
switch to hide them. Signed-in shoppers can also **ask a question** from the page. A question about
a seller's item goes to that seller. A question about the store's own stock goes to every
SuperAdmin and waits under **Store → Questions**. (**The store's own** shows those; **Every item**
includes sellers' questions too, and the store can answer any of them.) Answering or declining
notifies the shopper, who reads the reply under **My Questions**. Neither side is told who the other
is. **Add to the FAQ…** copies an answered question into the product's FAQ, reworded if needed, and
leaves the answer the shopper received unchanged. A person's questions are removed along with their
account when it's closed or deleted.

![Questions: the store's own stock first, with Answer, Decline and Add to the FAQ](help-media:site-administration/store-questions.png)

## Versions

A product that has been on sale can have a **new version** (**Versions** tab → **Start a new
version…**). This creates a hidden draft that copies everything except the stock (wording, pictures,
options and variants with new SKUs, parts, FAQ and files) and links back to the old product.
Whoever starts it chooses what happens to the old version when the new one **first goes on sale**:
sell what's left and then come off sale (a daily job takes it off once none are on hand), stay on
sale alongside it, or come off sale immediately. This happens only once; after that, the choice is
fixed. An old version taken off sale this way keeps its page, marked "No longer made" with a link to
the new one, but can't be bought. Putting it back on sale by hand clears that. Each product can have
at most one newer version, and a product that a newer version links back to can't be deleted.

## The item's page

A product's **Page** tab holds its own **returns** wording (shown under the store's returns window),
its **warranty**, and up to three **videos** (mp4, webm or mov, 95 MB each). Where the server has
ffmpeg, videos are served from a copy with the location and camera details removed, and a video file
is only served while a product uses it. **Take and show reviews** is controlled by the store only; a
seller can't change it. Turning it off hides the item's reviews and stars everywhere (cards, sorting,
the rating filter), and nobody can write a new one. Turning it back on brings them back.

## Paying sellers

Sellers are paid by hand. The store takes the whole payment for every order, pays its sellers
outside the site (by bank transfer or check, for example), and then records the payment.
**Store → Sellers** lists everyone with the Seller role or with earnings on record: what each one is
**owed**, what's **ready to pay**, what they've been paid so far and when they were last paid.

A seller's earnings are recorded when their package **ships**: cost plus asking price for each unit
(as fixed when the order was placed), plus the label credit. Refunding units that had already
shipped takes back those earnings; units refunded before shipping were never earned. A refund by
amount comes out of the store's share. To charge a seller for one, add an **Adjustment…** with a note
the seller will read.

**Ready to pay** is the part that's older than the returns window, so a return can't take it back
after it's paid. On a seller's page, **Record payment…** pays either what's ready or everything owed,
and marks exactly those earnings as paid. If their earnings changed while the page was open, it
refuses and shows the new figure; record the payment again. A seller who owes the store (because of
refunds after they were paid) carries that balance forward, and no payment can be recorded until
their balance is positive again. **Void…** a payment recorded by mistake, and its earnings become
owed again. **Export CSV** lists every line with when it was paid.

A seller can close their account even with earnings unpaid. SuperAdmins are notified, and the
seller's page stays here so you can settle up.

![Sellers: what each is owed, ready to pay, and has been paid](help-media:site-administration/store-sellers.png)

![One seller's books: Record payment, adjustments, and every line](help-media:site-administration/store-seller.png)

## Economics and fees

A product's **Parts & cost** tab ends with its **Economics**: what one unit costs to make, what its
seller asks and earns (cost plus asking price), the price, the card fee on a single unit bought on
its own, and what the store keeps. **Suggested price** covers the seller's earnings (or the cost of
the store's own stock), adds the store's markup and leaves room for the card fee. A price that
wouldn't cover the seller's earnings plus the fee is flagged in red.

The markup and the fee estimate are set in **Store Settings → Prices and fees** (30%, 2.9% and $0.30
unless changed). The estimate is for one unit bought on its own; an order of several units pays the
fixed part of the fee only once.

**What an order came to** is locked in at checkout on every line (its cost, the seller's asking
price and earnings, and the store's markup), so later changes to a parts list or an asking price
affect the next order, never one already placed. For whatever wasn't refunded, an order's
**Economics** card shows the sellers' earnings and label credits, the items' markup minus the
discount, the shipping the store kept, the card fee Stripe actually charged (read from Stripe
shortly after payment, and shown as "not known yet" until then) and what the store keeps. Stripe
keeps its fee when a payment is refunded, and the store absorbs discount codes and fees.

## A product's history

The **History** tab on a product lists every change to it, newest first: who made it, when, and
what it was, in a sentence such as "Renamed it from “REM Pod” to “REM Pod II”", "Changed Default’s
price from $59.99 to $64.99" or "Received 12 of Large". It covers details, going on and off sale,
the seller, options, variants, prices, stock received or written off, and pictures. Sales and
refunds appear in each variant's stock log instead. A save that changed nothing doesn't add a line.

If an item has a seller, the seller sees its history too, with two differences: price lines are
left out, and changes made by store staff show **The store** instead of a name.

![A product's history: every change, and who made it](help-media:site-administration/store-product-history.png)

## Discount codes

Store discount codes (such as GHOST10) are separate from the plan coupons under Billing. A code
takes a percentage or a dollar amount off the products, never shipping or tax, and can have a
minimum order, a start and end date, a total number of uses and a number of uses per buyer. The list
shows anything that stops a code from working today: **Takes nothing off**, **Expires before it
starts**, **Used up** or **Expired**. Once an order has used a code, the code can be retired
(switched off) but not renamed or deleted, because the order keeps the code it was bought with.

## Orders and fulfillment

**Orders** (under Store) lists every paid order and every checkout still waiting for payment,
newest first. Search by order number, buyer, email, SKU, product or tracking number; filter by
status, discount code, dates, **Waiting to go out or needing attention**, **Needing attention**, and
**Refunds that failed or are stuck**. Finished checkouts that were never paid are left out unless
you ask for them. The dashboard's **To pack**, **To ship** and **Needs attention** tiles, and a
discount code's "N orders" link, open this list with the matching filter already applied. **Export
CSV** downloads whatever the filters show, one row per item: number, date, buyer's state, SKU,
quantity, prices, discount, tax, shipping and refunds.

![The order desk: every order with its buyer, total and status](help-media:site-administration/store-orders.png)

Open an order to see everything about it: the buyer, where it's going, the items, the money, its
refunds and its history. Packing, shipping and delivery are done **on each package's card**. An
order with one package has one card; for orders with more than one, see **Packages and partial
shipping**. Only the buttons that make sense right now are shown:

- **Mark packed** — for a paid package.
- **Ship…** — choose the **carrier** (USPS, UPS, FedEx, DHL or Other) and type the **tracking
  number**. The tracking link is built from the two, and the dialog shows it before you confirm. For
  "Other", you can paste the carrier's own https link. If the parcel isn't tracked, check **No
  tracking provided**: it ships with the carrier and no number, and the buyer is told it was sent
  without tracking. Shipping emails the buyer right away.
- **Correct tracking…** (or **Add tracking…** for a parcel sent untracked) — fixes the carrier or
  number on a shipped package. It doesn't email the buyer; use **Resend email…** if they need it.
- **Mark delivered** — for a shipped package.
- **Edit address…** — available until the first package ships. Changing the buyer's email replaces
  the order's private link (the old one stops working) and sends the receipt to the new address.
  Sales tax isn't recalculated for a new state; the history notes this.
- **Resend email…** — the receipt, or "Your order is on its way" once it has shipped.
- **Release checkout** — releases a checkout that's still waiting for payment, returning its stock
  and discount code. It's refused while the payment is still being processed at Stripe.
- **Invoice** prints the order's invoice; **Open in Stripe** opens the payment in Stripe's dashboard.

![One order on the desk: its actions, the buyer, where it's going, the items and the money](help-media:site-administration/store-order.png)

**Needs attention.** An order is flagged when something about its payment needs a person to look at
it, such as a payment that arrived after its checkout was canceled, or an amount that didn't match.
It can't be packed or shipped until you read the reason and press **I've looked — clear it**.

**Notes** you add go into the order's history with your name. Everything done to an order is
recorded there, newest first.

## Refunds and cancellations

**Refund…** on an order returns money to the buyer's card through Stripe. You can refund **by item**
(choosing how many of each, and whether they go back on the shelf) or **by amount** (for shipping or
a goodwill amount), which never restocks. Every refund needs a reason. You can refund up to whatever
is still refundable, and a refund still being processed at Stripe counts against that.

A refund changes nothing until Stripe confirms the money has gone back. Then the stock is returned
(if you chose that), the order's refunded total goes up, the order becomes **Refunded** when nothing
is left to refund, the buyer is emailed, and the sales tax filing is reversed. **A pending refund
finishes when Stripe confirms it.** Until then the refund shows **Awaiting Stripe**, and the order
updates on its own. If Stripe refuses, the refund shows **Failed** with Stripe's reason, and nothing
else changes. **Try again** checks Stripe for the refund first, so it never refunds twice. A refund
made in Stripe's own dashboard is recorded on the order too ("Refunded from the Stripe dashboard"),
without restocking.

**Cancel…** is for a paid order that hasn't shipped. It refunds everything still refundable,
including shipping, puts the items back on the shelf if you check that box, and marks the order
Canceled once the refund goes through. A shipped order should be refunded instead.

**Refunds that failed or are stuck**, on the Orders list, finds the refunds that need a look.
**Export refunds** on the same page downloads every refund in the chosen date range with its status
and Stripe's ids.

## Store reviews

Buyers can review what they bought, and every review waits in **Reviews** until someone approves or
refuses it. Only approved reviews count toward a product's stars. Refusing requires a reason,
because the reviewer is told why. The store can reply under an approved review; refusing a review
later removes the reply too. The queue shows the oldest waiting review first.

The reviewer can see their own review on the product page the whole time: "Waiting for approval",
then published, or the reason it wasn't. If a reviewer edits a published review, it goes back to the
queue and is hidden until it's approved again.

![The review queue: a waiting review with Approve, Refuse and Delete](help-media:site-administration/store-reviews.png)

## Store settings

The store settings page holds the flat shipping rate and the free-shipping threshold, the low-stock
number, the ship-from address Stripe Tax uses to work out sales tax, the support email shown as
"Need help?", the returns window, how long a checkout holds its stock, and whether to offer Stripe
Link. The whole form is checked before anything is saved, so a single mistyped value means nothing
is saved, and the page tells you which value it was. Site Settings also shows these values,
read-only, with a link here.

The page also lists the states Stripe Tax is registered in. Registrations are made in the Stripe
dashboard, and buyers in any other state aren't charged sales tax.

## Stock alerts

Every morning while the store is on, each SuperAdmin gets a notification on the bell and an email
("Store stock: N running low") listing the variants on sale that are at or below the store's
low-stock number, with a button to the Stock page. It comes once a day, and not at all when nothing
is low. The dashboard's low-stock table and the Stock page's **Running low only** filter show the
same information at any time.
