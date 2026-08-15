# Self-Service Contact Info & Calendar Event Types

Branch: `feature/self-service-contact-info`

## Why

Two features that had been designed but never finished, shipped together because they are both
small and both about the same complaint: a screen that exists in the database but not in the app.

**Contact info.** `UserEmail`, `UserPhone`, `UserAddress` and `UserLink` have existed since early in
the project, with full CRUD — behind SuperAdmin-only `/admin/user-*` routes. A signed-in person
could not add so much as a phone number to their own profile. Everything below the display name was
someone else's to edit.

**Email validation was dead.** The `ValidationToken` and `IsValidated` columns existed, but nothing
ever wrote a token; the admin controllers hardcode the token to an empty string. That mattered more
than it looks: `OrgCalendarEventController.AddAttendeeByEmail` — invite somebody by typing their
address — matches only rows that are `IsPublic && !IsHidden && IsValidated`. No row in the database
could satisfy that. The feature shipped, was tested, and had never once matched a real person.

**Calendar event types.** Full CRUD API, gated on `IsOrgAdminAsync`, wired all the way through the
client — and zero UI callers. The only way an organization had ever acquired a "Meeting" type was
someone calling the API by hand with curl.

## What was built

### Contact info (`api/me/...`)

New `MyContactInfoController` — emails, phones, addresses, links, all scoped to the caller.

- Every row is matched on **id *and* owner together**, so another person's row reads as **404, not
  403**. A 403 confirms the row exists to someone who has no business knowing.
- Requests are plain records, never bound entities. The admin controllers bind `[FromBody] UserEmail`
  directly, which is fine when only a SuperAdmin can reach them and an `AppUserId`-spoofing hole
  when they are not.
- `IsPrimary` is a slot: setting it on one row clears the caller's others.
- Addresses geocode server-side, and **save regardless** if the lookup fails. An address that
  refused to save because a map lookup failed would be an outage caused by a nicety.
- Links must be absolute `http`/`https`, and changing the URL withdraws any prior approval.

### Email validation (`api/public/email-validation/{token}`)

- Token is 32 random bytes, Base64Url. Reissuing retires the previous one. 60-second resend
  throttle, seven-day lifetime.
- Sending is **best-effort**: `IEmailService.SendAsync` throws when SMTP is unconfigured — which is
  every environment today — so the send is gated on `IsConfigured` and the response **always**
  carries the link itself. The profile page shows it in a copyable field when nothing was sent.
  A button that appears to do nothing is worse than an honest "we can't mail this yet".
- **Confirm is POST, never GET.** Corporate mail scanners and link-preview services fetch every URL
  in an email automatically; a GET that validated as a side effect would validate addresses nobody
  ever confirmed. The landing page renders a button and the click is the confirmation.
- Redeeming clears the token rather than flagging it used, so a replay is a 404 and looks
  indistinguishable from a bad link.
- The landing page `/validate-email/{token}` is **anonymous on purpose** — no AuthReady gate. The
  link arrives in a mailbox, which is very often read on a different device.

### Publishing rule

An email can only be made public once it is validated. Creating one public is refused outright
rather than silently coerced — a client that asked for public and got private without being told
would ship that bug. Editing the address text resets validation and unpublishes: whatever was
proven about the old string says nothing about the new one.

### UI

- `/profile` grows four cards — `MyEmailsCard`, `MyPhonesCard`, `MyAddressesCard`, `MyLinksCard` —
  each loading and saving independently, so one failure does not take the page with it.
- The addresses card uses an **inline form**, not a modal, matching `OrgAddressManager`: the map
  preview inside `AddressFieldsWithMap` needs real width, and sizing a window around a map is
  fussier than expanding a card. (The plan said `TelerikWindow`; this is a deliberate deviation.)
- New `OrgCalendarEventTypesManager`, mounted as a second card on the org **Settings** tab. Name,
  colour, icon (existing `IconClassPicker`), sort order, active flag. Zero new API or client work.

## Tests

`MyContactInfoControllerTests` (24), `PublicEmailValidationControllerTests` (8), and one new
end-to-end test on `CalendarInviteByEmailTests` that drives the whole chain — add address, issue
link, redeem anonymously, publish, invite — and proves the calendar feature is no longer inert.

Every load-bearing assertion was **checked against deliberately broken code** in two rounds: the
validation reset, the token clear, the owner filter on the primary slot, both publish guards, and
the expiry check. Each round failed exactly the four tests it should have, and no others.

## Known gap, not fixed here

`OrgCalendarEventTypeController` writes no audit rows for create/update/delete. That is pre-existing
and out of scope for a UI phase, but it is the only mutation surface touched by this branch that
does not audit.
