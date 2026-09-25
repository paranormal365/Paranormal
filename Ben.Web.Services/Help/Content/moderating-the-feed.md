---
title: Moderating the Feed
summary: The reported-posts queue, and what hiding a post does.
section: Site Administration
audience: AppAdministrator
order: 75
---

Visible to app administrators only.

**Administration → Content → Reported Posts** is the queue of posts somebody has objected to.

## What a report is, and is not

A report is one person saying a post should not be here. **It hides nothing.** Neither does five
reports, or fifty — there is no threshold, deliberately. An automatic one would remove whatever is
least popular rather than whatever breaks the rules, and the people worst served by that are the
ones with unusual things to say, which is most of this site's subject matter.

Hiding is your decision, and your name is recorded against it.

## Working the queue

![The reported-posts queue](help-media:moderating-the-feed/queue.png)
*Each row shows the post as its readers saw it, who objected, and why.*

Reports are listed **oldest first**. A queue worked newest-first leaves the oldest complaint
unanswered for ever, and that is the one somebody is waiting on.

Each row shows the post as its readers saw it, who reported it and why, and — where more than one
person reported the same post — how many. Treat that number as context, not as a verdict.

| Button | What happens |
|---|---|
| **Hide the post** | It disappears from every feed, thread, tag page and profile. |
| **Leave it up** | Nothing changes for readers. The report is marked as looked at. |

Both resolve **every** waiting report against that post together. Five people reporting one post is
one decision, and leaving the rest waiting would put it back in front of a colleague with no sign
it had already been dealt with.

### Reports about a published case

The same queue also carries reports about **published cases** and about **comments on them**, so a
moderator has one screen rather than three. A case report names the case and links to it; the
decision is the same one, made by a person, and hiding still hides nothing by itself.

## Photos and videos waiting for a look

**Administration → Content → Feed Media** is the other queue: photos and videos that have not been
cleared for the feed. Nothing anybody uploads is shown to readers until it has been screened —
automatically where the site's screening model is installed, and by a person here otherwise. The
page says plainly which of those is the case.

A file the automatic screener held carries its reason ("blocked", or "borderline, needs a person")
with a confidence score. **Approve** publishes it; **Hold** keeps it unpublished with your note.
Neither deletes anything, and the author is only ever told their upload is being checked — never
which check it tripped, because that would be a manual for dressing up the next one.

The page opens on the **Waiting** pile, which is only what nobody has looked at yet — under
automatic screening that is usually empty. What the screener refused sits in the **Held** pile,
and it is refused, not denied: nothing the screener says is final, and Approve there publishes
it like anywhere else. Scary is fine. The screener knows one thing, which is nudity; a ghost, a
dark hallway or a frightening frame is "normal" to it.

### When an account keeps sending it

The one time an upload is refused outright rather than queued is an account that has used up the
benefit of the doubt: **three confident refusals in a day** — a score the screener is sure
about, not a borderline one — pause that account's photo and video uploads until the oldest of
the three is a day old. Text posts still work. The poster is told their uploads are paused and
nothing more.

Rows from such an account carry an **Uploads paused** badge, and one a step short of it says
so too. Approving any one of the three lifts the pause at once, because the rule counts what
was *decided*, not what the screener first said — so a run of real evidence the model misread
is one Approve away from clearing. Borderline scores never count toward a pause at all.

### The category question

Beside the safety question sits a different one: **is this what it says it is?** A post's category
label (Apparition, Voices / Whispering…) is the author's claim, shown with the site's own
match score. **Is what it says** and **Category is wrong** each record your judgment — that is all
they do. A wrong category never hides a post; it nudges the author to fix the label and gently
lowers the post's ranking.

Those judgments are worth the click even when nothing is wrong: every one becomes a labelled
example the site's classifier learns from, and the classifier is only ever as good as the record
of what people who looked actually decided.

## Posts about a place

A post written on a public location's page is an ordinary feed post that also names that place, so
everything on this page applies to it unchanged: it appears in the same queues, its photo goes
through the same check, reports reach you the same way, and hiding it removes it from the place's
page as well as from the feed. There is no separate place-moderation screen, and there is
deliberately nothing to learn.

One thing worth knowing: **anybody signed in may post about a public location**, which is wider than
the feed's front page. That is the free lane working as intended — a shared record of somewhere
anyone can visit is filled by visitors — but it does mean a place's page is the likeliest first stop
for somebody with no group and no history. Private residences take no posts at all.

## The place archive

**Moderation → Place Archive** is a second queue, and it is not about posts. Two other things reach
a public location's page: a **field session** somebody published from the app, and a piece of
**event evidence** a guest published from an event held there.

Both work the other way round from the feed. They go public the moment their owner publishes them,
and a queue entry appears only when something has been questioned — either a screener could not
clear the media, or **any signed-in reader flagged it**.

**A flag hides it straight away, and then you decide.** That order is deliberate: waiting for a
moderator before hiding leaves the thing somebody objected to up for however long that takes, which
is the failure a report exists to prevent. Hiding first costs a contributor some visibility for a
while; not hiding costs somebody whatever the picture was.

Which means **one flag from one person is enough to hide somebody's work**, and it stays hidden
until you look. That is the whole reason this screen exists, and it is why it opens showing what is
already held rather than only what is new. A held session is invisible to the public and invisible
to its owner's audience, and nothing releases it except somebody here.

- **Approve** puts it back on the place's page.
- **Hold** takes it down again — that is how an approval is undone.
- The reason the flag was given is shown on the row, which is usually the whole story.

**The readings stay either way.** A flag is about what a photograph shows; magnetic-field numbers
cannot be objectionable, and pulling a whole session would let one flag erase a contribution to the
archive rather than hide a picture.

If a place is later corrected to a **private residence**, its media comes down with it
automatically. You do not need to work through it here.

## Hidden, not deleted

A hidden post is still there. Nothing is destroyed: its replies, its reports and the record of who
decided what all survive, so the next person asking "what happened here" can find out. Deleting it
would take all of that with it.

**Put it back** on a hidden post undoes the hiding. It is the same act read the other way round.

## What the author is told

Nothing, at present. There is no notification, and the post simply stops appearing. If telling
people is something this site should do, it is a decision to make deliberately rather than a
feature to add quietly — the wording matters more than the mechanism.

## When the feed is switched off

This page still works. Switching the feed off does not un-report anything, and a site that turns it
off may still have complaints nobody got to. Every other feed page disappears with the feature;
this one does not, so those reports are never stranded.
