# Delete asks two questions, and the second can hand the file to a group (item 180 Phase B)

**Branch:** `feature/file-delete-and-reassign`

## Ben's spec (2026-08-24) and clarification (2026-09-04)

> when a user deletes a file that is shared and in use, ask whether they want it removed
> everywhere it is shared. If yes, honour it. If no, ask whether they still wish to delete it; if
> they do, the file and its EXIF record are reassigned to the organization using it rather than
> destroyed — ownership moves to the org, the person stops being the owner, it leaves their
> personal files, and it appears only to those with the right permission in that organization.

> Ownership remains with user who uploaded the file until they delete it and only if they choose
> not to delete usages beyond their account? — *Yes.*

## What was wrong before

`DELETE api/upload-files/{id}` had an owner check and nothing else: it removed the row and the
bytes while the groups' case copies still pointed at the source, and every share row went with
the cascade, silently.

## What this branch builds

- **Schema.** `UploadFile.AppUserId` nullable; `OwnerOrganizationId` (FK → Organizations,
  NoAction, indexed). Migration `AddUploadFileOwnerOrganization`; `scripts/create-database.sql`
  regenerated. Nullable on purpose: every "is this mine" check reads `AppUserId == userId`, so null
  fails them all at once. Uploader stays in `CreatedByAppUserId`.
- **Usage** — `GET …/usage`: one row per group with counts of shares, case copies, group copies
  and direct links (case, timeline, report, logo, ad, event evidence, equipment photo, client
  request, and a Field Kit session on the group's investigation). Counts, not case titles.
- **Plain delete** refuses with the usage (409) while a group is using it. SuperAdmin keeps the
  plain door.
- **Delete everywhere** — `POST …/delete-everywhere`: ends both share tables' rows, removes case
  copies with comments and votes, the group's own copies, and every direct link, then destroys
  the file. Refuses *before* touching anything if a session holds the file; refuses *after* (with
  everything shared already removed) if something this door does not know — a published video,
  a marker — still holds it, and says so.
- **Reassign** — `POST …/reassign {OrganizationId}`: only to a group that is using the file;
  clears `AppUserId`, sets `OwnerOrganizationId`, keeps the id so shares, copies and the metadata
  row keyed on it stay; gives the group a copy in its own Files (copy-from-user's shape).
- **Gates.** `CanManageFileAsync`: owner, or the owning group's Owner/Administrator, or SuperAdmin
  — never the former owner. `CanViewFileAsync`: any active member of the owning group.
- **Purge.** The group purge releases the claim in its transaction and removes each file
  afterwards through `UploadFileRows.TryDeleteAsync`; a file something else holds stays,
  ownerless, reachable by SuperAdmin.
- **UI.** `UploadFiles.razor`: usage first; plain confirm, or the two questions in a small dialog
  with a group picker when several groups are using it.

## Tests

`UploadFileControllerTests`: 13 new — usage shape and gate; plain delete refuses in use / works
otherwise; delete-everywhere clears share, copy, bytes; reassign hands over, keeps metadata,
leaves the listing, refuses a group not using it; former owner locked out; group admin can
delete, member cannot; member can view, stranger cannot; a session on a group's investigation
counts; a session-held recording is refused with the reason and can still be handed over. Suite
4,064/0. Three guards proven to discriminate. `OrganizationPurgeCoverageTests` updated: the
`UploadFiles` rule now states the Phase B ownership and the per-row removal.

Playwright `UploadFilesTests.Delete_AFileNobodyElseUses_AsksOnce_ThenRemovesIt` compiles; the
run needs `BEN_E2E_ADMIN_PASSWORD` and `BEN_SUPERADMIN_PASSWORD`.

## Docs

New help page `your-files.md` (Your Account); `getting-started.md` links it.

## Left open

Item 218: a person cannot delete a whole field session; the archive's paid-retraction rule has to
be decided first.

## Deployment

Apply the migration before deploying the API.
