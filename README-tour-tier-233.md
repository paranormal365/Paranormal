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
| 0 | `Tour` entity, `OrgCalendarEvent.TourId`, `OrganizationSubscription.TourCountAtPeriodStart`, `TourBilling` arithmetic, `BillableUnits` used by checkout, quote, renewal, admin; migration `Tours` | **done** |
| 1 | Tour API and add-on charge; tour and date guides; the calendar rule; the tours page and a page per tour with a switcher; scheduler and billing page; public tour page, org home, `/events`, home-map pins, nearby search; the gallery, the slideshow, the meeting-point map, a participant's own uploads | **done**, except iOS |
| 2 | Guest mail: attachments on `IEmailService`, `IcsBuilder`, `TourMailRenderer` placeholders, three send sites, the editor and its preview | **done** |
| 3 | Retention: four tier limits, the clock on a file, stamping at ingest, the sweep with its warnings, both keeps, the five-minute refusal, the date shown wherever a file is listed | **done** |
| 4 | Reviews: one per guest per tour, only after a date they came to has finished, hideable and never editable by the business | **done** |
| 5 | Help, screenshots, the product and persona PDFs, the tour mailing; production migrations and tier rows on Ben's say-so | **done**, except the production data |

## Not built

- **The iPhone and iPad app knows nothing about tours.** Phase 1 promised the tour's name and its
  guides on an event in the app; `Ben.iOS` is untouched on this branch. The public endpoints it
  would read are all in place, and the shared records already carry the fields, so this is a
  client-side piece of work rather than a design question. Item 234 is the larger version of it.
- **1080p downscaling.** The five-minute refusal is enforced; resolution is not. `MediaTools`
  carries an ffmpeg path used for stripping and frame sampling, and nothing yet re-encodes an
  over-large recording. Refusing on length alone is the honest half: it is the one that costs
  storage, and the phone can already trim.

## Verified, not assumed

Everything below was watched happening on the running site, not inferred from a passing test.

- A business registered through the wizard's own endpoint, given a meeting point, and quoted
  1 × $29 on its billing page; a second tour moved it to 2 ×; a third with the same name was
  refused in the words the controller writes.
- A public date scheduled without a tour was refused; with one, it took the tour's meeting point,
  length and capacity, and inherited its guides.
- Apple geocoded the meeting point, so the tour drew a real pin on the home map, in nearby search,
  and on its own page beside the directions button.
- A 3000×2000 photograph came back 1620×1080 with its camera data gone, and served anonymously
  through the site's proxy to a signed-out reader.
- A real sign-up mail, read off the wire from a local SMTP sink: multipart, one `text/calendar`
  attachment carrying the date's own uid, reply-to the business, the guide named, the meeting
  point, the contact line, and the time in the tour's zone rather than UTC.
- The calendar file was opened and read back; its escaping keeps an address with commas in one
  piece.

## Found by reading it afresh, after it worked

A full audit of the branch turned up twelve defects that a green suite and a walk through the site
had both missed — a cross-tenant delete in the gallery, two FK breaks, four money defects, a paused
tour that still took sign-ups, a rescheduled walk that never corrected a guest's diary, a tour that
vanished off the map when its address had no coordinates, guides that could not be removed, a photo
route that served anybody's published photograph, and a meeting point printed twice. A second audit
found the keystone of the retention design unreachable: nothing on any screen could keep a file, so
a recording could not be kept at all. All are fixed and pinned; the commits name them one by one.

## Deliberately unchanged

- Non-tour events keep today's mails and today's rules.
- `EventReminderSent` stays one row per person per event; an invitee who never confirmed still
  gets no reminder.
- `README-tour-business-billing-231.md` and the pitch PDF as of 231 describe the superseded
  flat-per-business price; the pitch is rewritten in Phase 5, the README is history.

## The look, and the clock (2026-09-10, after the audits)

Ben asked for the site to "pop" and to read "more 2026 layout than 2000", then pointed at one
surface at a time. The chain a guest walks — the home strip, "What's near you", the tour's page,
a date's page, the business's page, and "What's on" — was taken in order, and the idioms that
turned up more than once moved into the Kit rather than being copied: `TourPlate` (a colour and
initials drawn from a name, for anything with no photograph yet), `BenTourCard`, `BenFactRail`,
`BenSectionHeading`, `BenGuideStrip`, and `OrgPublicBanner` / `OrgPublicNav`.

Three defects came out of that pass rather than out of a test:

- **A page could render in three different clocks.** Most surfaces used `DateTime.ToLocalTime`,
  which is the machine the site runs on. Ben's rule, in his words: "The dates and times may be
  recorded in UTC, but should render at either UTC or at the time of the location where the
  evidence was collected or photo taken." There is one `EventClock` now and every public surface
  asks it. Only a tour records a zone, so a walk reads in the walk's time and any other event
  reads in UTC, named either way. **Giving an ordinary `OrgCalendarEvent` a zone is the follow-on**
  — nothing changes when it arrives.
- **Two Playwright tests had never run.** `PublicEventDescriptionTests` searched the list response
  for a field only the detail response carries, so it skipped itself on every run since it was
  written; its sibling asked a URL that 404s. Both work now, and the first immediately caught a
  real race in its own reading of the page.
- **A published CMS page with no sections** rendered a heading over an empty screen.

## Where else a tour is (2026-09-10)

Ben: "let the tour add their Instagram, Facebook, X, URL, TikTok, BlueSky, Rumble, YouTube, etc.
These could be displayed at the bottom of their pages." Nine services, one box each, a row of
pills at the foot of the tour's public page. `SocialPlatform` is append-only and each entry names
the hosts it accepts, matched on a dot boundary; a link is checked when it saves and again when it
is served, because an icon that says Instagram and opens somewhere else is link laundering on a
page a reader is trusting the business for. Migration `TourSocialLinks`.

## Left for production

- Migrations `Tours`, `TourDetailsAndGuides`, `TourReviews`, `TourGallery`, `MediaRetention`,
  `TourAuditFixes` and `TourSocialLinks` applied to `IsHauntedDb` only with an explicit
  `--connection` naming it, on Ben's word. All are on the testing copy `IsHauntedDb_player`.
- The Tour & Event Business tier's four limit rows entered on production.
- `MediaTools:FfmpegPath` on the server for 1080p downscaling; SMTP still unconfigured.
- One assumption to confirm: kept recordings have no count cap and count against the tier's
  storage cap.
