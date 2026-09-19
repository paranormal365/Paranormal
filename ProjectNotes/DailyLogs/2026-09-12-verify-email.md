# 2026-09-12 — Dev Log addendum: an email nobody proved they could read

Companion to `2026-09-12.md`. Branch `fix/verify-a-new-email`, from `master`.

## What Ben reported

> *"I just updated my account in the profile by adding a new e-mail. I set it as primary and
> public. Shouldn't we verify that?"* … *"when I returned to my profile page, it didn't say that it
> was updated."*

## What was actually wrong — less than item 237 first claimed

**The correction matters, so it is first.** Item 237 was written saying the verification flow did
not exist. Reading the code says otherwise: `CreateEmail` already refused public, `UpdateEmail`
already refused publishing an unvalidated address, re-typing the address already cleared validation
and unpublished it, and the whole chain — `send-validation`, the emailed link, the anonymous
`/validate-email/{token}` page, the redeem endpoint, the seven-day expiry, the one-minute resend
cooldown — was built, tested and working. `UserEmail` already carried all four columns.

Two things were genuinely broken.

### 1. `IsPrimary` was governed by nothing

Unguarded on create and on update. Primary is the address a person is **presented by**, so an
unproven one taking that label is the same mistake as publishing it, only quieter — and it is the
rule public already had.

*Stated honestly:* nothing today routes mail by the primary `UserEmail`; the site writes to the
Identity account address, which has its own confirmation. So this was a wrong label on a
public-facing record, not mail silently redirected. Worth fixing at the rule level regardless,
because the day something reads it, it becomes the other kind of defect.

The trap in fixing it: re-typing an address clears validation, and a row that stayed primary through
that lands in exactly the state the rule prevents. Changing the address of a primary row is
therefore refused outright, with a test.

### 2. No confirmation was ever sent on add

The row was created with `ValidationToken = null` and nothing was emailed. The link only went if
somebody found a **Send confirmation link** button — a button most people never press. So the
address sat unconfirmed for ever and could never become primary or public, which is exactly what
Ben saw.

Adding an address **is** the request to confirm it. Create now issues the link immediately through
the same `IssueValidationAsync` helper the resend button uses — one copy, so the two doors cannot
drift apart on the token's lifetime or on what happens with no mail server. `MyEmailRecord` gained
two optional fields carrying the link and whether it was emailed; additive, null everywhere except
the create response, because a live token has no business in a list.

**One behaviour change, pinned by its own test:** asking for another link inside the minute is now
throttled, since the first has just gone.

### 3. The save said nothing

`SaveAsync` closed the form and reloaded. A save that reports nothing is indistinguishable from one
that was refused. The card now says what it did, names the address, and shows the confirmation link
there when mail could not be sent — so on a dev machine adding an address is one step, not two.

## The screen

- **Primary** is disabled until the address is confirmed, with the note Public already had.
  `CanBePrimary` is defined as `CanPublishForm`, in one place, because it is the same rule.
- A first address no longer defaults to primary: a tick the save would refuse is a tick that lied.
- A green line after a save.

## Verification

- **32 controller tests**, proved to discriminate: restoring the unguarded primary failed 3,
  removing the send-on-add failed 4.
- **Four existing tests failed on the first run and were read rather than suppressed.** Two because
  create now sends and a following resend hits the cooldown; two because they created addresses as
  primary. All four now walk the real sequence — add, confirm, promote — which is a better test than
  what was there before.
- Full suite: **4,987 pass, 1 fail**, and that one failure is **pre-existing on master** —
  `TitleSuggestedRolesTests.Both_halves_are_reachable_from_a_screen` reports
  `SetSuggestedRolesAsync`, `GetSuggestedRolesAsync` and `applySuggestedRoles` as server endpoints
  no screen calls. Confirmed by stashing this branch's work and running it against clean master.
  **Not fixed here** — it is a different feature and it deserves its own look.
- **`ProfileEmailConfirmationTests` is written and compiles but is UNRUN.** `scripts/run-e2e.sh`
  refuses while anything is serving 5252, and Ben's own dev hosts are. Stopping them is his call,
  not something to do underneath him. It covers the two facts only a screen can show: that the
  Primary tick is unavailable rather than merely refused, and that the card says what it did.

## Documentation

`your-profile.md` rewritten around the new behaviour, the website changelog given two entries, the
product PDF rebuilt. Item 237's slice A is marked fixed on the branch that carries it.

---

## Correction, same day: that failure was not pre-existing on master

I reported `TitleSuggestedRolesTests.Both_halves_are_reachable_from_a_screen` as a pre-existing
failure on master, having "confirmed" it by stashing this branch's work and re-running. That check
proved nothing: it ran in the same **worktree** either way.

**The guard fails whenever the suite is run from a git worktree, on any branch.** It walked up to
`Ben.slnx`, then excluded every path containing `/worktrees/`. From the main checkout that skips
the other branches parked inside it. From *inside* a worktree it excludes the entire tree — the
scan sees zero files and the guard reports the feature inert because it never opened a single
screen. The exclusion was right; the substring was not. It is the same trap the comment two
methods below already describes for path separators on Windows.

**Both directions were live in that one file.** The positive scan (must be called) failed falsely.
The negative scan (must not be read outside these files) would have passed *vacuously*, seeing
nothing to object to — the quieter and worse half.

And the wider version: the other forty-odd source-scanning guards exclude nothing at all, so from
the main checkout they read every worktree under `.claude/worktrees/` and **a guard can be
satisfied by a file that exists only on another branch**.

**Fixed** on `fix/source-scan-guards-in-a-worktree`: `Ben.Web.Tests/Support/RepoFiles.cs` resolves
the root and excludes worktrees *nested in that root* by prefix, never by substring, plus build
output. Both scans in `TitleSuggestedRolesTests` now go through it, and both assert the scan found
something before judging what it found. Five tests on the helper itself, proved to discriminate:
restoring the substring exclusion fails three of them and the guard with them.

Converting the remaining guards is not done here — several deliberately scan one project rather
than the root, and sweeping 40 files I have not read would be a worse change than a documented
helper the next one uses.
