---
title: Moderating the Feed
summary: The reported-posts queue, and what hiding a post does.
section: Site Administration
audience: AppAdministrator
order: 75
---

Visible to app administrators only.

**Administration → Content → Reported Posts** is the queue of posts someone has objected to.

## What a report is, and is not

A report is one person saying a post shouldn't be here. **It hides nothing.** Neither do five
reports, or fifty. There's no threshold, on purpose: an automatic one would remove whatever is least
popular rather than whatever breaks the rules. That hurts people with unusual things to say, and
unusual things are most of what this site is about.

Hiding is your decision, and your name is recorded with it.

## Working the queue

![The reported-posts queue](help-media:moderating-the-feed/queue.png)
*Each row shows the post as readers saw it, who reported it, and why.*

Reports are listed **oldest first**, so the person who has been waiting longest gets an answer first.

Each row shows the post as its readers saw it, who reported it and why, and, if more than one
person reported the same post, how many. Treat that number as context, not a verdict.

| Button | What happens |
|---|---|
| **Hide the post** | It disappears from every feed, thread, tag page and profile. |
| **Leave it up** | Nothing changes for readers. The report is marked as reviewed. |

Both buttons resolve **every** waiting report on that post at once. Five reports about one post are
one decision, so the others don't stay in the queue for a colleague to handle all over again.

### Reports about a published case

The same queue also holds reports about **published cases** and about **comments on them**, so you
work from one screen instead of three. A case report names the case and links to it. The decision
works the same way: a person makes it, and a report on its own hides nothing.

## Photos and videos waiting for a look

**Administration → Content → Feed Media** is the other queue: photos and videos that haven't been
cleared for the feed yet. Nothing anyone uploads is shown to readers until it has been screened,
either automatically (if the site's screening model is installed) or by a person on this page. The
page tells you which applies.

A file the automatic screener held shows its reason ("blocked", or "borderline, needs a person")
and a confidence score. **Approve** publishes it; **Hold** keeps it unpublished with your note.
Neither one deletes anything. The author is only ever told that their upload is being checked,
never which check it failed, so nobody learns how to get the next one past it.

The page opens on the **Waiting** pile, which holds only what nobody has looked at yet. Under
automatic screening it's usually empty. Anything the screener refused is in the **Held** pile, and
that refusal isn't final: Approve there publishes it just as it does anywhere else. Scary content is
fine. The screener only looks for nudity, so a ghost, a dark hallway or a frightening frame looks
"normal" to it.

### When an account keeps sending it

Uploads are normally queued, not refused. The one exception is an account that has used up the
benefit of the doubt: **three confident refusals in a day** (scores the screener is sure about, not
borderline ones) pause that account's photo and video uploads until the oldest of the three is a
day old. Text posts still work. The poster is told only that their uploads are paused.

Rows from a paused account carry an **Uploads paused** badge, and rows from an account one refusal
short of a pause are marked too. Approving any one of the three lifts the pause right away, because
the rule counts what a person *decided*, not what the screener first said. So if the model misread
a run of genuine evidence, a single Approve clears it. Borderline scores never count toward a pause.

### The category question

Next to the safety question is a different one: **is this what it says it is?** A post's category
label (Apparition, Voices / Whispering…) is the author's claim, shown alongside the site's own
match score. **Is what it says** and **Category is wrong** each record your judgment, and that's all
they do. A wrong category never hides a post. It prompts the author to fix the label and slightly
lowers the post's ranking.

These judgments are worth the click even when nothing is wrong. Each one becomes a labeled example
the site's classifier learns from, and the classifier can only be as good as the decisions people
record.

## Posts about a place

A post written on a public location's page is an ordinary feed post that also names that place, so
everything on this page applies to it: it appears in the same queues, its photo goes through the
same check, reports reach you the same way, and hiding it removes it from the place's page as well
as from the feed. There's no separate screen for moderating places, and nothing new to learn.

One thing worth knowing: **anybody signed in may post about a public location**, which is a wider
group than can post on the feed's front page. That's intentional, since anyone can visit a public
place and its page is meant to be filled in by visitors. But it does make a place's page the most
likely first stop for someone with no group and no history. Private residences don't accept posts
at all.

## The place archive

**Moderation → Place Archive** is a second queue, and it isn't about posts. Two other things can
appear on a public location's page: a **field session** someone published from the app, and a piece
of **event evidence** a guest published from an event held there.

Both work the opposite way from the feed. They go public as soon as their owner publishes them, and
they only show up in this queue when something has been questioned: either a screener couldn't
clear the media, or **any signed-in reader flagged it**.

**A flag hides the item immediately, and then you decide.** If it stayed up until a moderator got to
it, the thing someone objected to would be visible that whole time, which is exactly what a flag is
meant to prevent. Hiding first costs a contributor some visibility for a while. Not hiding could
expose people to whatever the picture showed.

That means **one flag from one person is enough to hide someone's work**, and it stays hidden until
you look at it. That's why this screen exists, and why it opens on what's already held rather than
only what's new. A held session is hidden from the public and from its owner's audience, and only an
administrator on this screen can release it.

- **Approve** puts it back on the place's page.
- **Hold** takes it down again. That's how you undo an approval.
- The row shows the reason given with the flag, which usually tells you all you need.

**The readings stay up either way.** A flag is about what a photo shows. Magnetic-field numbers
can't be objectionable, and pulling the whole session would let one flag erase someone's
contribution to the archive instead of just hiding a picture.

If a place is later changed to a **private residence**, its media is taken down automatically. You
don't need to deal with it here.

## Hidden, not deleted

A hidden post is still there. Nothing is destroyed: its replies, its reports and the record of who
decided what all remain, so the next person asking "what happened here?" can find out. Deleting it
would lose all of that.

**Put it back** on a hidden post undoes the hiding.

## What the author is told

Nothing, for now. The author gets no notification; the post simply stops appearing.

## When the feed is switched off

This page still works. Turning the feed off doesn't clear any reports, and a site that turns it off
may still have complaints nobody got to. Every other feed page disappears with the feature, but this
one stays, so those reports are never stranded.
