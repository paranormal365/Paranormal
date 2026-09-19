# An email address you never proved you could read

Branch: `fix/verify-a-new-email`, from `master`. Item 237 slice A and A2.

Ben, 2026-09-12: *"I just updated my account in the profile by adding a new e-mail. I set it as
primary and public. Shouldn't we verify that?"* And: *"when I returned to my profile page, it didn't
say that it was updated."*

## What was actually wrong

**Less than item 237 first claimed, and worth correcting.** The verification machinery already
existed and worked: `CreateEmail` refused public, `UpdateEmail` refused publishing an unvalidated
address, re-typing the address cleared validation and unpublished it, and the whole chain of
`send-validation` → emailed link → anonymous `/validate-email/{token}` page → redeem endpoint was
built, tested and live. `UserEmail` already carried `IsValidated`, `ValidationToken`,
`DateValidated` and `DateValidationSent`.

Two things were genuinely broken.

### 1. `IsPrimary` was governed by nothing

An unconfirmed address could be made primary, on create and on update. Nothing checked it, ever.

Primary is the address a person is **presented by** — the one a screen reaches for when it wants
"their email" — so letting an unproven one take that label is the same mistake as publishing it,
only quieter. It is the rule public already had, and primary had simply never been given it.

The trap in fixing it: re-typing the address clears validation, and a row that stayed primary
through that would land in exactly the state the rule exists to prevent. So changing the address of
a primary row is refused rather than silently demoted, and a test pins it.

*Severity, honestly stated:* nothing today routes mail by the primary `UserEmail` — the site writes
to the Identity account address, which has its own confirmation. So this was a wrong label on a
public-facing record rather than mail silently redirected. Worth fixing at the rule level anyway,
because the moment something does read it the defect becomes the other kind.

### 2. No confirmation was ever sent when an address was added

The row was created with `ValidationToken = null` and nothing was emailed. The link only went if
somebody noticed a **Send confirmation link** button beside the row — which is a button most people
never press. So an address sat unconfirmed indefinitely and could never become primary or public,
which is precisely what Ben observed.

**Adding an address is the request to confirm it.** There is no other reason to type one in. So
create now issues the link immediately, through the same `IssueValidationAsync` helper the resend
button uses — one copy, so the two doors cannot drift apart on the token's lifetime or on what
happens when there is no mail server.

The response carries `ValidationLink` and `ValidationEmailSent` as new optional fields on
`MyEmailRecord`. Additive, defaulted, null everywhere except the create response: a live token has
no business in a list response, and the client only needs it on a machine with no mail server,
where it is shown instead.

**One consequence, pinned by its own test:** asking for another link within the minute is now
throttled, because the first one has just gone. That is correct — the cooldown exists for
double-clicks — but it is a behaviour change and should not be a surprise.

### 3. And the save said nothing (A2)

`SaveAsync` closed the form and reloaded. A save that reports nothing is indistinguishable from one
that was refused, which is the same principle this codebase already keeps for refusals. The card
now says what it did, naming the address, and shows the confirmation link there when mail could not
be sent — so adding an address on a dev machine is one step rather than two.

## The UI half

- The **Primary** tick is disabled until the address is confirmed, with the same explanatory note
  the Public tick already had. `CanBePrimary` is defined as `CanPublishForm`, in one place, because
  they are the same rule.
- A first address **no longer defaults to primary**, since it cannot be one yet. A tick the save
  would refuse is a tick that lied.
- A green line on the card after a save, naming the address and saying whether the link was emailed.

## Verification

- **32 controller tests**, proved to discriminate: putting the primary guard back to unguarded
  failed 3, and removing the send-on-add failed 4.
- Four existing tests failed on the first run and were **read rather than suppressed** — two
  because create now sends and a following resend hits the cooldown, two because they created
  addresses as primary. All four now walk the real sequence: add, confirm, promote.
- Help updated in `your-profile.md`, changelog entry added, dev log updated.

## Left open from item 237

The shared verified-tick component for two-factor, phone and linked providers. Email already had a
*Confirmed* badge; the rest of that thread is slice 3 and not in here.
