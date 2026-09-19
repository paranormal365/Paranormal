# Equipment Phase 6a — Media Metadata Pipeline and Gap Fixes

Branch: `equipment-phase-6a-photo-pipeline-and-gap-fixes` · Backlog item **#55**, first of four
Phase-6 branches. Branched from `develop` after phases 1–5 merged.

## Why

Two things arrived at once. Ben asked that metadata for images, video and audio be **linked to the
record and removed from the media**, readable by Administrators and SuperAdmin only. And a full gap
audit of the shipped equipment feature found five blocking prerequisites plus a dozen defects —
several of which would have quietly defeated Phase 6's own requirements. This branch clears all of
it so 6b–6d are feature work rather than archaeology.

## The metadata pipeline

`IMediaSanitizationService` (SkiaSharp, **MIT** — chosen over ImageSharp so there is no
license-applicability question to revisit) re-encodes an image from decoded pixels, which drops
EXIF, GPS and every other tag **by construction** rather than by enumerating tags we happen to know.

`IMediaIngestService` is the single place the policy lives, so the three photo-upload paths cannot
drift apart and future surfaces adopt the whole rule by calling one method:

1. **Extract from the original** — into the existing `UploadFileMetadata` table, which already
   modelled GPS, camera, capture time and a raw dump.
2. **Keep the original, untouched** — this is an investigation platform; a re-encode is
   irreversible and a case photo must stay re-examinable. Ben chose this over stripping in place.
3. **Write a sanitized copy** — `…​.clean.jpg`, and it is what every serve path returns.
4. **Write a thumbnail** — `….thumb.jpg`; the endpoint regenerates lazily if missing, so nothing
   needs backfilling.

Metadata reads via `GET api/equipment/photos/{id}/metadata` — **org Administrators/Owners and
SuperAdmin only, deliberately not the item's owner**, per Ben. Never carried on the bytes or
thumbnail routes.

**Images only for now.** Video and audio stripping needs an ffmpeg remux and ffmpeg is reachable
only from the sidecar, not this process — a hosting decision, so it becomes its own phase rather
than blocking equipment work. A/V metadata is still extracted, so the Admin view is complete from
day one and only the stripping half waits.

**Capture time** prefers `DateTimeOriginal` (shutter) over IFD0 `DateTime` (which can be a later edit), and
now honours `OffsetTimeOriginal` when the camera recorded it. Without that, EXIF's timezone-less
timestamps were assumed to be the *server's* zone — a photo taken abroad landed hours from when it
was really taken, which matters when the time is the evidence.

## Defects fixed (all confirmed, none speculative)

| What | Consequence |
|---|---|
| **XSS in phase 4's own notifications** | Message bodies render as markup by design; user-typed text was interpolated raw. A decline reason of `<script>…</script>` ran in the borrower's inbox. Now encoded at composition. |
| **The inbox named every sender** | Falling back to their **email**. Anonymous Q&A would have leaked identity through the notification. `UserMessage.HideSenderIdentity` now nulls name *and* id. |
| **Org gear photos reachable by nobody but SuperAdmin** | Org items have no `OwnerAppUserId`, so nothing matched `IsOwner` — the members whose group owns the kit could not see its photos. |
| **Org gear had no photo capability at all** | While the editor and help docs both said it did, and projections read a collection that could never fill. |
| **Make and model transposed** | Every group surface showed them swapped. |
| **Retire did not exist** | Four places told users to retire instead of deleting; anything with history was permanently stuck. |
| **Personal delete had no history guard** | Hard-deleted the item, its photos and its files — destroying loans other people were party to. The org guard existed but checked only the service log. |
| **Loans tab rendered on row count** | An approver at a group whose gear had never been borrowed saw no tab, and no way to learn the surface existed. |
| **`DateNeededFrom` shown nowhere** | Collected since phase 4 and promised by the help doc; the approver could not see when the borrower needed the gear. |
| **`CanRequestCheckout` permanently false** | Superseded by `BorrowEligibilityRecord`; a permanently false flag is a trap where the convention is to render flags as given. Removed. |

## Two things worth knowing about how this went

**A test that proved nothing.** The GPS fixture was malformed — wrong tag numbers and types. It
still produced a GPS *directory*, so the strip assertion passed happily, while no latitude could be
read from it. The end-to-end "GPS reached the table" test would have passed on a file that never
carried any. The guard test now asserts a **readable position**, not the presence of a directory.

**A fix that would have broken a feature.** The XSS was first fixed at the *render* site by encoding
message bodies. That would have broken the rich-text org-message composer outright and shown raw
`<strong>` tags across membership, taxonomy and support notices — those bodies are intentional HTML.
Checking what actually authors them is what caught it. Encoding at composition keeps both
properties.

## Verification

- Builds clean, **0 warnings** (grepped for warnings, not just errors).
- Suite **2,479 → 2,512**, all green.
- **Discrimination checks run against deliberately broken code**, each failing as required before
  restore: EXIF stripping, image resize, the capture-time offset, the org-membership photo branch,
  both notification-injection guards, and the delete-history guard.
- Migration `AddUserMessageHideSenderIdentity` applied; `scripts/create-database.sql` regenerated.
- Needs Ben's signed-in click-through: uploading a GPS-carrying photo to personal and group gear,
  retire/put-back, and the Loans tab appearing for an approver before any loan exists.

## Not in this branch

Model pages, website links and interest counters (6b); FAQ and anonymous Q&A (6c) — 6a only lays
its `HideSenderIdentity` groundwork; mutual loan feedback (6d). Video/audio stripping is recorded as
its own future phase.
