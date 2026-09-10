# Item 233: the tour tier — tours as the unit a business pays for

Branched from `develop` at `97efb013` (2026-09-10). Plan of record for `feature/tour-tier-233`.

## Ben's brief, in order and in his words

- "I think photos stay on site for a month unless the tour rep says to keep it. Video and audio
  are the same, but they stay on the site for a week and are limited to a max of 5 minutes each
  at 720p or 1080p. FieldKit submissions fall under the same limits. Unless they mark them to be
  saved, they stay on the site for a week so the end user can save them or download them."
- "The tour can schedule tours and people can sign up for the tour. Money collected is to be
  arranged by the tour company or person. We can email their information to people who sign up.
  We can let them create an e-mail template to generate for people who sign up and then as a
  reminder including the .ics file with information for their tour."
- "They can set the time and the number of people accepted for the tour."
- "The $29 per month is for a single tour no matter how many times scheduled. If they have a tour
  on one street and need another tour for another street, that is a different tour. The part to
  nail it down is 1) The start location which is required 2) Whatever you think we should use to
  differentiate them if they start at the same place and are for the same business."
- "Account for creating the email template with where the data about the tour is located. Like
  the start date and time and name and photo of person who will be leading the tour - for safety."
  "The photo of tour guide is optional… if it is led by more than one person, it would not be the
  same picture for each tour."
- "Tours are public so, they show up on the map and are searchable."
- "The address of the tour start would be in the e-mail." (So the default template carries the
  meeting point, and a template that drops it is still sent with the start address appended —
  a guest must never receive a tour mail that does not say where to stand.)
- "Evidence collected - unless marked to save - only lasts a week for everything but photos.
  Photos stay a month." "The tour can keep up to 50 1920x1080 72ppi images and they can swap them
  out or tag ones from tours to keep as well, but 50 images per tour max."
- "The tour owner is not necessarily the tour guide. So they would need to be able to add guides
  to the tour or guide." "The tour owner would need to be able to manage their tours from the web
  they would need to be able to select which one if they have more than one tour."

Decided with Ben the same day: build every phase; a recording over five minutes is refused (the
phone's trim tool is the answer); a date's guide is a member of the business, shown with their
public profile photo; a tour added mid-period is charged today for the days left, on the saved
card; reviews are optional per tour and on by default; the differentiator between two tours from
the same corner is the tour's **name**, unique within the business.

## The model

A **Tour** is the product: name, start address (one of the business's own, required),
description, typical length, default capacity, time zone, a guest-mail template, a gallery of up
to 50 images, a reviews switch, and the guides the owner has put on it. A **date** is an
`OrgCalendarEvent` naming its tour, with its own start, end, capacity and guides (defaulting to
the tour's). Billing counts active tours: units × unit price, re-counted at every renewal,
prorated when a tour is added mid-period. Guests sign up to dates exactly as today; the sign-up
mail and the reminder are the tour's template rendered for that date and guest, with an `.ics`.
Media a tour business holds expires on a clock unless it is in a gallery (images) or marked kept
(recordings). Guests who came may rate the tour.

## Phases

| Phase | Work | Status |
| --- | --- | --- |
| 0 | `Tour` entity, `OrgCalendarEvent.TourId`, `OrganizationSubscription.TourCountAtPeriodStart`, `TourBilling` arithmetic, `BillableUnits` used by checkout, quote, renewal, admin; migration `Tours` on the testing copy; tests | in progress |
| 1 | Tour API and add-on charge; tour and date guides; the calendar rule (a public date of a tour business belongs to a tour); tours page with a per-tour management page and switcher; scheduler and billing page; public tour page, org home, `/events`, home map pins, nearby search; iOS shows the tour name and guides | |
| 2 | Guest mail: `IEmailService` attachments, `IcsBuilder`, `TourMailRenderer` placeholders (tour, date, guide names and photos, guest, business contact), the four send sites, editor with preview | |
| 3 | Retention: four tier limits, `ExpiresAtUtc`/`KeptAtUtc` on files, `MediaRetentionPolicy` stamping at ingest, `MediaRetentionJob` (notices then deletion), the 50-image gallery as the keep for photos, keep for recordings, download while it lasts, five-minute and 1080p rules at the upload doors | |
| 4 | Reviews: one per guest per tour, only after a date they attended has passed, hideable by the business | |
| 5 | Help, screenshots, product PDF, the tour pitch PDF, deploy notes; production migrations and tier rows on Ben's say-so | |

## Verified, not assumed

- (filled in as phases land)

## Deliberately unchanged

- Non-tour events keep today's mails and today's rules.
- `EventReminderSent` stays one row per person per event; an invitee who never confirmed still
  gets no reminder.
- `README-tour-business-billing-231.md` and the pitch PDF as of 231 describe the superseded
  flat-per-business price; the pitch is rewritten in Phase 5, the README is history.

## Left for production

- Migrations `Tours` and (later) `MediaRetention` applied to `IsHauntedDb` only with an explicit
  `--connection` naming it, on Ben's word.
- The Tour & Event Business tier's four limit rows entered on production.
- `MediaTools:FfmpegPath` on the server for 1080p downscaling; SMTP still unconfigured.
- One assumption to confirm: kept recordings have no count cap and count against the tier's
  storage cap.
