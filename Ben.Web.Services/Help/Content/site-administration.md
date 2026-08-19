---
title: Site Administration
summary: Sitewide settings, the clipart library, and the audit log.
section: Site Administration
audience: AppAdministrator
order: 70
---

Visible to app administrators only.

## Site settings

![The site settings screen](help-media:site-administration/site-settings.png)
*Site settings apply to every group and every visitor.*

**Administration → Site Settings** holds values that apply to the whole site. Nothing personal
belongs here — settings about one person live on their profile, settings about one group live in
that group's settings.

Adding a new setting is a code change, not a database one: settings are declared in
`SiteSettingKeys.Seed`, and the page renders whatever is declared.

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

## Audit log

![The audit log with its filters](help-media:site-administration/audit-log.png)
*The audit log records who changed what, and when. Filter it by entity, action, person or date.*

**Administration → Audit Log** records every mutation with who made it. It is filtered and paged
on the server, so date and user filters apply to the whole history rather than the page on screen.
