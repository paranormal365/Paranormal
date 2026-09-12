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

## 2026-09-12

- One upload now carries up to 5 minutes of video and 500 MB in total. Recording on the phone is
  not limited in any way — this is only about how much travels at once, and a session can be sent
  in as many goes as it takes.

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
