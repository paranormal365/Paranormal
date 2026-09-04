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
