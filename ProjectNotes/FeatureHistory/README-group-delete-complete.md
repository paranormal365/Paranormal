# Delete a Group — the answer is yes

Branch: `feature/group-delete-complete`, cut from `develop` at `5065e9e`.

## The problem

On 2026-09-03 Ben deleted Music City Spirit Seekers on production and got:

> Nothing was deleted. The database refused: The DELETE statement conflicted with the REFERENCE
> constraint "FK_InvestigationDutyAssignments_InvestigationAttendees_InvestigationAttendeeId".

The purge is a hand-kept list of tables. Duty assignments were added after it was written, they
hang off an attendee rather than off the group, and the guard that existed only looked for tables
with an `OrganizationId` column — so nothing noticed. Ben's rule: *if I am deleting an entire
group, there is a reason. Nothing should prevent me as SuperAdmin from deleting a group and its
data. Also delete any files and folders.*

## What changed

**Fifteen tables the database would have refused on**, found by a new guard that derives the list
from the EF model rather than from anybody's memory (below). Eleven are now deleted with the group:
duty assignments, address member accesses, member-group memberships, CMS page permissions,
membership answers, member-level roles, file shares *into* the group or its investigations, loan
feedback about the group, and the group's own equipment with its photos, shares, service logs,
FAQs and questions. Four are **updated, not deleted**: a proposal to the shared equipment or
experience catalogue (`EquipmentBrands`, `EquipmentModels`, `ExperienceCategories`,
`ExperienceTypes`) outlives the group that made it — other groups may be using the brand or type
by now — so the purge clears `ProposedByOrganizationId` and leaves the row.

**Folders.** `IFileStorageService.DeleteDirectoryAsync` (local: recursive delete, refused for the
root or anything resolving outside it; Azure: every blob under the prefix). After the transaction
commits and the files named by rows are gone, the purge removes `orgs/{id}` and `cases/{id}` for
each of the group's cases — file-by-file from the rows leaves behind whatever the rows never knew
about, and an empty folder named for a group that no longer exists.

## How it is proved

`OrganizationPurgeCoverageTests.Every_table_that_would_block_the_purge_is_purged` walks every
foreign key in the model: if the principal is a table the purge deletes from and the delete
behaviour is anything but Cascade or SetNull, the dependent must be a table the purge deletes from
too (or, for a nullable reference, one it updates). Against the code on `develop` it names all
fifteen, duty assignments first among them. The earlier guards are kept alongside it, tightened so
that the catalogue *update* does not read as a delete.

**Verified on real data**: the side database is yesterday's copy of production. Purging Music City
Spirit Seekers there through the API — the same group, the same duty assignment that refused on
production — returned 200, the preview afterwards 404, and a file planted in `orgs/{id}` was gone
with its folder. Full unit suite: 3,862 pass.

## On production

Deploy, then Administration → Delete a Group → Music City Spirit Seekers, exactly as before.
