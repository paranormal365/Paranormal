# Venue owners: no limit on pictures, no limit on words, sections of their own (10/05/2026)

Branch `feature/venue-owner-no-limits`.

Ben: "For venue or property owners, there is no limit on their photos and items uploaded to display.
If they want to add text to the site for their location it should be okay."

## What stood in the way

- **Pictures.** A venue's library stopped at 60 (`VenuePhoto.MaxPerVenue`), on upload, on keeping an
  organizer's offer, and on the venue offering its own event pictures. Nothing on screen or in the help
  said so; the owner met it as a refusal.
- **Words.** The building's story stopped at 8,000 characters and the house rules at 4,000 (API,
  textarea `maxlength`, and an `nvarchar(4000)` column for the rules).
- **Anything else.** The venue page had exactly three things an owner could write: the story, the
  rules, and photo captions. A hotel that runs ghost hunt weekends, a dinner theatre and a chapel had
  nowhere to say so.

Venue pictures were already outside every storage quota and retention clock (they live under the
organization's own storage and are never stamped for expiry), so nothing there changes.

## The plan

1. Remove the picture cap everywhere it was enforced; keep the per-picture fitting (1920×1080) that
   keeps the page quick.
2. Remove the length caps on the story and the house rules (API, editor, column — migration
   `VenueSectionsAndUnlimitedText`).
3. **Sections of the owner's own**: any number of titled sections (`VenueSection`: title, text,
   order), written on *Your venue* and shown on the public venue page after the building's story —
   "Ghost Hunt Weekends", "Dining", "The chapel". Saved with the rest of the profile, in the order the
   owner puts them.
4. Tests, help text and pictures, product documentation, change logs.

**Deploy note:** the migration must reach the live database (MonsterASP) with the site.

## What shipped

- `VenuePhoto.MaxPerVenue` is gone, and so is every check on it (upload, keeping an offer, the venue
  offering its own event pictures). `A_venue_library_keeps_pictures_past_the_old_sixty` fails against
  the old cap and passes now.
- The story and the house rules have no length limit (API, editor, column).
- `VenueSection`, edited under *Your venue* (Add a section, move up and down, remove) and shown on the
  public venue page in the venue's order. Saving replaces the list; leaving `Sections` out of a save
  leaves them alone; a blank row is dropped; words without a heading are refused with a sentence.
- Help: *Your venue page* and *Photos of the venue* say there is no limit and how sections work; the
  editor and page pictures retaken with two sections (in a taller window, because they now run past
  900px). Product documentation reprinted.
- Found on the way: on a phone, "Say yes to Thu 11/19/2026 – Fri 11/20/2026" was wider than its card on
  the hosting-requests page and was cut off; the button wraps now
  (`The_requests_page_fits_however_narrow_it_is(375,812)` was failing on every run).
- Tests: 7,847 unit tests pass; the hosted-events browser category (133, including the new
  `A_venue_adds_a_section_of_its_own_and_visitors_read_it`) passes on a fresh database.
- **Deploy:** migration `VenueSectionsAndUnlimitedText` (a new table; house rules widened to
  `nvarchar(max)`) must be run against the live database with the release.
