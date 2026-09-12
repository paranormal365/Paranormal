# One mark for "we proved this"

Branch: `feature/verified-tick`, from `master`. Item 237 slice 3.

Ben: *"one shared component, because four screens inventing four ticks is how they end up meaning
four different things."* That was the whole brief, and it was already true — four screens were each
drawing their own.

## What was there before

| Screen | What it drew |
|---|---|
| `MyEmailsCard` | a green `badge bg-success` reading **Confirmed**, and an amber one reading **Not confirmed** |
| `AdminUsers` grid | a bare green tick, with a date beside it, and prose for the other states |
| `TwoFactorPanel` | a green pill reading **On**, a grey one reading **Off** |
| `MyProfile` hero | nothing at all when confirmed, and a warning box when not |

Four marks for the same idea, in three shapes and four vocabularies. The profile hero was the worst
of them: a confirmed account said *less* about itself than an ordinary contact row did.

## `BenProven`

In `Ben.Web.Website.Library/Kit/`, beside the other shared controls, so the next screen that needs
it looks where everything else lives.

- `Label`, `Proved`, `On` (a date, in the tooltip), `Detail`, and `Chip` for a pill rather than a
  bare tick.
- **`Unproved` is a sentence saying what to do about it** — and its presence is what allows the
  unproved state to be drawn at all.

### The rule the component enforces

**An unproved fact renders nothing unless there is something a person can do about it.** Leaving
`Unproved` null is how a caller says "there is no path", and then no mark appears.

That is not a nicety. A mark reading *Not verified* against a fact nobody can change is a dead end,
and it reads as a fault the person caused. Putting the rule in the component makes shipping one
impossible by accident rather than something to remember — the same reasoning as every other
refusal-needs-a-path rule in this codebase.

**It is why phone numbers get no mark.** `UserPhone.IsValidated` exists and is written `false` on
create; no endpoint and no screen can ever set it. A tick there could never turn. A test asserts
`MyPhonesCard` stays unmarked, so when somebody builds phone verification, that test is the reminder
to come back.

### Colour

Green means proved, and nothing else on the component is ever green. Amber means "not yet, and here
is what to do". That is the same discipline item 237 asks for on the user-manager badges, where
green is to mean "currently paying" and nothing else: a column stops answering its question the
moment a second thing is coloured.

## Where it is used

`MyEmailsCard` (each address), `MyProfile` (the account address, or *Signed in with Apple* where
Apple proved it), `TwoFactorPanel` (on or off), `AdminUsers` (the same fact an administrator reads,
in the same vocabulary the account holder sees).

## Verification

- **5 source guards** (`BenProvenTests`): the component exists and is in Kit; every screen that
  states a proved fact uses it; no hand-rolled *Confirmed* pill survives; the unproved branch is
  guarded by `Unproved`; and phones stay unmarked. Proved to discriminate — putting one hand-rolled
  pill back fails two of them. They read the tree via `RepoFiles`, so they work from a worktree.
- **3 Playwright tests** (`ProvenMarkTests`), run green through `scripts/run-e2e.sh` against the
  isolated database: the profile hero, a contact address and two-factor each draw the mark with a
  tooltip on a real page.
- The contact-address test **adds its own address** rather than relying on the seed. Its first run
  failed on an empty list, which is a test about the seed rather than about the mark. Its second
  failed on the Blazor Server interactivity race, and now uses the harness's own `ClickUntilAsync`.
- Suite: 4,999 pass.

## Not in here

The rest of item 237: the user-manager badge column, the positions popover, and the sign-in charts.
The tick is the shared piece those will reuse.
