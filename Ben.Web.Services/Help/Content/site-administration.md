---
title: Site Administration
summary: Sitewide settings, the clipart library, and the audit log.
section: Site Administration
audience: AppAdministrator
order: 70
---

Visible to app administrators only.

## The dashboard

**Administration → Dashboard** is the overview the rest of this menu drills into: four headline
numbers over a grid of charts, with a 7 / 30 / 90-day range picker.

The first three headline cards are links — **People** opens the user list, **In a group** the
groups, **Cases** the case list — because the number is usually the start of a question the list
answers. *Signed in this week* deliberately is not: there is no page listing recent sign-ins, and a
card that sent you somewhere approximate would be worse than one that stays put.

The numbers worth knowing how to read:

- **People** and **In a group** — the second is the funnel. Someone who registers and never joins
  a group is the person the site has not finished convincing, and the percentage under the number
  says how large that group is.
- **Signed in this week** counts *people*, not sign-ins; the caption below it gives the raw
  attempt count. Someone signing in from a laptop and a phone is one person, two sign-ins.
- **Busiest groups** counts cases and investigations started inside the chosen range, so it moves
  with the range picker. **Largest groups** counts active membership and does not.
- **Where this is happening** has a People / Cases / Investigations toggle, and all three come from
  addresses already on record: people from their profile addresses, cases from the address the case
  is about, investigations from the place visited. Expect People to lag the other two — it counts
  only those who have filled in an address, which most people have not.

### What the dashboard cannot tell you

There is no count of visitors who never sign in, and no "new versus returning" split. Nothing on
this platform records anonymous visitors — a page view by someone with no account leaves no trace
anywhere in the database. Answering that would mean adding visitor tracking, which is a decision
about privacy and data retention rather than a chart, and it has not been made. Every number on
this page is about accounts and what they did.

## Site settings

![The site settings screen](help-media:site-administration/site-settings.png)
*Site settings apply to every group and every visitor.*

**Administration → Site Settings** holds values that apply to the whole site. Nothing personal
belongs here — settings about one person live on their profile, settings about one group live in
that group's settings.

Adding a new setting is a code change, not a database one: settings are declared in
`SiteSettingKeys.Seed`, and the page renders whatever is declared.

### Allowing groups to sign themselves up

**Allow groups to self-register** decides whether an ordinary signed-in visitor may found a group.
Left on — the default, and how the site normally runs — anyone can start one from **Organizations
→ Start a Group** and becomes its owner. Switched off, that button disappears, the address behind
it explains that new groups are not being accepted and points at the contact form, and the server
refuses the request even if somebody has the address saved. You are exempt either way: an
administrator can always create a group from this side of the site.

Reach for it when you want to pause growth — a billing change you are part-way through, or a
period where each new group needs a conversation first. It has no effect on groups that already
exist.

### The default profile picture

There are **three default pictures**: a generic one, one for members whose profile says they are
a man, and one for members whose profile says they are a woman. Each is an ordinary upload:
press **Upload image**, pick a JPEG, PNG, GIF or WebP, and it takes effect immediately — the
preview under the row shows what is live. Uploading a replacement removes the previous image;
there is nothing to clean up. The images are stored publicly on purpose, because they render for
visitors who are not signed in.

Which one a viewer sees follows the person's own profile — the optional **Sex** field under
their name (Male, Female, Unspecified; blank by default), self-declared and never guessed from
a name. Blank or Unspecified gets the generic image; Male or Female gets their specific image
**when you have uploaded one**, and the generic otherwise. A real photo always outranks every
default.

### The site-wide announcement

**Site-wide announcement** is for maintenance windows and known issues. While it has text, an
info banner shows it at the top of every page, to everyone — signed in or not — with your line
breaks preserved. Clear the box and save to take the banner down; both the posting and the
removal reach visitors within about half a minute, without a restart. It is shown exactly as
typed: plain text, no formatting or links.

### Turning sections of the site on and off

Near the bottom of Site Settings is a row of switches labelled **Feature — …**, one for each
major section: the video editor, equipment, events and calendars, local discovery and maps, group
public pages, the media library, group messaging, voting, and the two newer features below.

Where a switch is fully wired, it does exactly what it says. The links disappear from the
navigation **and the addresses stop working** — someone who bookmarked the page, or who is handed
a link by a colleague, gets the ordinary "page not found" screen rather than a way in. That
pairing is the point: hiding a link while the page still answers is not switching a feature off.

**Four of them are not wired yet, and this page would rather tell you than let you find out.**
**Local discovery and maps**, **Group public pages** and **Voting** currently change nothing at
all when switched off — the sections keep working exactly as before. **Events and calendars** is
half-wired and is the one to be careful with: switching it off stops the reminder emails while
leaving the calendars, event pages and RSVPs running, so people can still sign up for an event
and simply never be reminded of it. Leave these four on until they are finished.

What it does **not** do is delete anything. Equipment records, saved videos, messages and votes
all stay exactly where they are, and turning the switch back on returns the section with its
contents intact. Use it to take a section down while something is being fixed, or to run the site
without a feature your groups do not want — not as a way to remove data.

Two things worth knowing:

- **A change takes up to about half a minute** to reach every page, because the site keeps the
  answer in memory rather than asking the database on every click. Your own browser sees it
  immediately after you flip the switch.
- **If the site cannot reach its API**, the switches fall back to their normal settings — the
  established sections on, unreleased features off. A connection problem will never make the site
  appear to have lost half its features.

A switch with nothing saved against it shows the setting's own default and is marked
**(default)** beside On or Off — so the row always describes what the site is doing, not merely
what has been typed into it. Until recently it showed the stored value alone, which meant seven
running features reported themselves as switched off simply because nobody had ever touched them.

Two switches start **off**: **Public feed** and **Publications**. Both features are built, but
neither is something a site should acquire by accident — each one adds a public surface that
members and visitors will start using the moment it appears, so switching it on is a decision
somebody makes deliberately rather than a default they inherit.

- **Public feed** — short public posts from anyone signed in. Turning it on means signing up to
  moderate it; see [Moderating the Feed](/help/moderating-the-feed).
- **Publications** — long-form writing by groups, readable by visitors with no account. Turning it
  on adds the **Publications** entry to the menu for everyone, and a **Publications** tab to each
  group's page for its administrators. Nothing appears in the public directory until a group
  actually publishes something, so switching it on shows visitors an empty directory at worst, not
  an unfinished one.

## Impersonating a member

**Administration → Users → the impersonate button** signs you in as that person, and what you
see is exactly what they see: their navigation and their groups under Home, their notification
counts on the bell, their pages, their refusals. Your own Administration menu disappears while
you look — it is your tool, not part of their experience. A banner at the foot of the sidebar
says who you are viewing as, with **Return to SuperAdmin** beside it, and both now survive a
page reload — leaving impersonation always takes exactly one click, however you got there.

Use it to see a reported problem through the reporter's eyes before assuming the cause. Actions
you take while impersonating are real actions on their account.

## The clipart library

**Administration → Clipart Library** curates the shared artwork every group can use in the video
editor. Upload the file first, then publish it here by its file id. The format is read from the
file itself — SVG, PNG, WebP, AVIF, GIF or Lottie — and anything else is refused rather than
published as something the editor cannot draw.

Assets are **retired**, never deleted. Projects reference artwork by id, so removing one would
break renders that already use it. A retired asset leaves the catalog and stays downloadable.

## Keeping the shared vocabulary tidy

![The equipment taxonomy screen](help-media:site-administration/equipment-taxonomy.png)
*Makes and models members have proposed, waiting to be approved, merged or renamed.*

Two lists grow by proposal rather than by decree: the **experience taxonomy** and the **equipment
catalog**. Groups add what they need on the night, and you confirm or clear up afterwards.

**Confirming** an entry marks it as reviewed. That matters beyond the badge: reviewed entries are
the only ones offered as "did you mean" suggestions to the next person, and they stop being cleared
away automatically. Confirming is how a word becomes shared vocabulary rather than one group's
note.

**Renaming** does what it says — until the new name is already taken. Then you are shown the entry
it collided with and offered a merge, because renaming onto an existing name means two things
become one and somebody's records change meaning. That is too large to happen because a name was
typed, so it is always a second, deliberate step.

**Merging** moves everything across and removes the duplicate. It cannot be undone. Two guards:

- You cannot merge a **confirmed** entry into an unconfirmed one. That is almost always the
  direction reversed, and the result would be a list where the endorsed word vanished and the
  mistake survived. Merge the other way, or confirm the target first.
- You cannot merge an experience type into a **different category**. Moving a tagging from Visual
  to Auditory changes what somebody recorded about their own night, which is not a rename.

**Deleting** is only for an entry nothing uses. If it is tagged on something, you are told how many
and refused — **Reject** is the action that removes a type together with its taggings, and it tells
you how many it took with it.

Most tidying happens without you. An unconfirmed entry that a group proposed disappears on its own
once the last thing using it is gone.

## Support tickets

![The support ticket queue](help-media:site-administration/support-tickets.png)
*Every message sent through the contact form arrives here.*

**Administration → Support Tickets** is the queue for the public contact form. A ticket arrives as
**New**; replying to the sender marks it **Answered** and assigns it to you if nobody had it.

The sender reads the thread through a private tracking link, whether or not they have an account —
that is how a reply reaches someone who cannot sign in. **Internal notes are never shown there.**
They are for staff talking among themselves, and adding one does not mark the ticket answered.

Contact details shown beside the form — postal address, phone, email, when we reply — are site
settings, so they are corrected on the Site Settings page rather than in a deploy.

## Sidecar installs

![The sidecar installs screen](help-media:site-administration/sidecar-telemetry.png)
*Which builds of the native helper are actually in use.*

**Administration → Sidecar Installs** shows the optional native helper people install to make the
video editor faster. The sidecar runs on someone's own machine and talks only to their browser, so
these records are the only way to see which builds are in the wild — useful before changing
anything about it, and the answer to "can we stop supporting that version yet?".

Three numbers and a chart:

- **Installations seen** — distinct machines that have reported in.
- **Paired to an account** — how many of those got as far as pairing with a signed-in person. A
  gap between the two means people are installing it and not finishing setup.
- **People** — distinct accounts, which is lower than installs when someone uses two computers.
- **Installations by version** — the distribution. Watch this after releasing a new build; a
  version that never grows suggests people are not being told an update exists.

The table below lists the individual events. Nothing here identifies a machine beyond the
installation id the sidecar generates for itself.

## Audit log

![The audit log with its filters](help-media:site-administration/audit-log.png)
*The audit log records who changed what, and when. Filter it by entity, action, person or date.*

**Administration → Audit Log** records every mutation with who made it. It is filtered and paged
on the server, so date and user filters apply to the whole history rather than the page on screen.

## Billing

### Role areas on a price band

Each band carries a **Role areas** checklist — which parts of the site a group on that band may
build custom role permissions for: Cases, Equipment, Public pages, and so on. Every band starts
with everything checked, which changes nothing; unchecking an area is how bands become different
products. Changes save as you click and apply to every group on the band.

The checklist is fully enforced. For a group whose band excludes an area: the role editor
grays that area's section with a note naming the plan, the server refuses changes to it while
carrying the stored grants forward untouched, and at runtime those grants simply stop answering
— members lose the tabs the grants opened, owners and group administrators notice nothing.
Nothing is ever deleted; a paused grant resumes exactly as configured the moment the area
returns. The public Pricing page lists each band's included areas whenever any band excludes
something.

Unchecking saves per click, so the group-facing notices are **netted**: a removal is queued
(free-band groups hear after a short grace, paid groups ahead of their renewal), and re-checking
the area before delivery cancels the pending notice — an accidental toggle reaches the groups as
silence, not as two contradictory messages. Newly included areas are announced right away.

### Capabilities on a price band

Beside the role areas sits a **Capabilities** checklist — plain may-or-may-not switches, the
first being **Case transfers**. A band without it can neither send a case to another group nor
accept one transferred in, and both ends are enforced: a case can't be handed TO a group whose
band lacks it either. Existing cases are unaffected; declining an incoming transfer never
requires the capability. Changes save per click and notify the affected groups through the same
netted notices as the role areas, and the public Pricing page says plainly what a band leaves
out.


**Administration → Billing** holds three screens.

**Price Bands** is the price list the public Pricing page renders — member ranges, a price per
billing cadence, and the caps each band includes. Every save is validated as a whole list: the
bands must cover every possible member count with no gaps or overlaps, and a save that would leave
somebody unpriced is refused with the reason. Bands are retired, never deleted, because a band
that has priced a period is part of the billing record. When an edit would affect groups already
on the band, the save pauses and shows the blast radius first: improvements are announced to those
groups immediately, reductions are queued and delivered up to two weeks before each group's own
renewal — paid groups keep the terms they bought until then.

**Coupons** manages discount campaigns. A *shared* campaign is one code everybody types; a
*generated* campaign is a batch of single-use codes you can print or mail, each withdrawable on
its own, and addressable to a specific account. A campaign can be limited by date window, total
redemptions (the budget), billing cadence, and occasion — first subscriptions only, or renewals
only. A misconfigured campaign (one that takes nothing off, or whose window closes before it
opens) wears a red **Broken** badge in the list rather than failing silently for whoever types it.

Every code also doubles as a **referral link** — the codes panel's *Copy link* button gives a
`/pricing?code=…` URL to hand to whoever is promoting or selling access. Visitors land with the
code attached and see it priced against their group before anything is confirmed; the code's
redemption count is the referrer's scorecard.

**Subscriptions** is where every group's standing lives — including groups never set up, which is
the first thing to look for. Until a payment provider is wired in, this screen *is* the payment
record: when a group pays, set their band, cadence and period here. The member count and price are
frozen from the moment you save, and the group keeps those terms for the whole period whatever
happens to the price list. The Members column shows the current count beside the frozen one —
the gap is what the group will be re-banded on at renewal.
