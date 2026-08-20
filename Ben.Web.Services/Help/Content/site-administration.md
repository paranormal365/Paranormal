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

The numbers worth knowing how to read:

- **People** and **In a group** — the second is the funnel. Someone who registers and never joins
  a group is the person the site has not finished convincing, and the percentage under the number
  says how large that group is.
- **Signed in this week** counts *people*, not sign-ins; the caption below it gives the raw
  attempt count. Someone signing in from a laptop and a phone is one person, two sign-ins.
- **Busiest groups** counts cases and investigations started inside the chosen range, so it moves
  with the range picker. **Largest groups** counts active membership and does not.
- The three **by state** charts come from addresses already on record: people from their profile
  addresses, cases from the address the case is about, investigations from the place visited.

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

### Turning sections of the site on and off

Near the bottom of Site Settings is a row of switches labelled **Feature — …**, one for each
major section: the video editor, equipment, events and calendars, local discovery and maps, group
public pages, the media library, group messaging, voting, and the two newer features below.

Turning one off does exactly what it says. The links disappear from the navigation **and the
addresses stop working** — someone who bookmarked the page, or who is handed a link by a
colleague, gets the ordinary "page not found" screen rather than a way in. That pairing is the
point: hiding a link while the page still answers is not switching a feature off.

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
