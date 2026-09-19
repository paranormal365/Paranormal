# The date field walks off the day and starts changing the month

Ben, 2026-09-09:

> "When you manually try to enter it, it kept moving the focus back to the month after I changed
> the day a few times."
>
> "I had entered 09 and was moving the numbers up for the date from 00 to 18 and before I could
> even get to 18, it moved and my 09 had changed to 10. This is what is happening."

So: type the month, land on the day, hold the up arrow to walk the day up — and partway there the
caret leaves the day segment for the month, where the remaining presses turn September into
October. The date you end up with is not the one you were building.

## What I already ruled out

Six variants against the real dialog on an isolated stack, at localhost speed and with 200ms of
emulated latency: typing a full value; making the day the keystroke that completes it; changing
the day repeatedly; typing past the end of the value; a single digit then Tab; four slow arrow
presses. None moved the caret backwards. **Four slow presses is the difference** — Ben was
holding the key.

Two other things did turn up, and are recorded whatever the cause of this one:

- Typing `31` into a September date silently gives you the **1st**. Telerik rejects the impossible
  day and keeps the second digit.
- Clicking a day in the calendar switches the popup to the **Time** panel, so changing your mind
  about the date means clicking **Date** again each time.

## What it is

An **empty** Telerik date field rebuilds the whole date the first time a segment is stepped.
Measured on the seeded case, in the Propose Dates dialog:

| | |
|---|---|
| select the month, type `09` | `09/dd/yyyy hh:mm aa`, caret on the day |
| **one** press of Up | `10/01/yyyy hh:mm aa` |

One slow press does it, so it is not a race with the circuit. The caret never moves — it stays on
the day — but the month you typed is gone, which is what it looks like from the other side of the
screen. Everything after that press is being typed into a date you did not start.

A field that **already holds a date** is fine: twelve fast presses on the day of
`09/14/2026 03:00 PM` walk it 14 → 26 and never touch the month. The Schedule Investigation dialog
has always seeded its start, which is why the fault only ever showed up in Propose Dates.

The empty **time** field is fine too — stepping an unset hour just counts up.

## The fix

1. **Proposed dates start from a real date** — a week out at 7pm, and each further option is the
   next evening, which is how three dates get offered. The picker is never empty, so nothing ever
   rebuilds itself.
2. **The start is a date box and a time box**, Ben's choice, in both dialogs. It also removes the
   second complaint: the combined popup switches to its Time panel the moment you click a day, so
   changing your mind about the date cost a trip back through the tab every time.

## Also found, not fixed here

Typing `31` into a September date silently gives you the **1st** — Telerik rejects the impossible
day and keeps the second digit. Recorded in the backlog rather than fixed: it needs Telerik's
own auto-correct behaviour changed, and no call site can reach it.
