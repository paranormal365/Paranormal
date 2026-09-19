# Site role assignment (item 216)

**Branch:** `feature/site-role-assignment`

## The gap

Ben, 2026-09-04: *"I don't see a way I can assign Site Roles to people. ... How do I, as
SuperAdmin, add roles to users like Admin, Moderator or SuperAdmin?"*

He could not. The site has three roles — SuperAdmin, Admin, Moderator — and the seeder creates
all three "so a SuperAdmin can assign them", but nothing let one:

- **Site Roles** (`/admin/roles`) creates and deletes role names and counts members. It never
  assigns anyone.
- **New User** offers a SuperAdmin checkbox at creation only, and only that role.
- The **user detail** page had no roles section.

The only writers were the startup seeder and the development roster seeder. Admin and Moderator
were reachable by a hand-typed row in `AspNetUserRoles` and by nothing else — the eighth
write-only feature found on this site.

## What this branch adds

- `PUT api/admin/app-users/{id}/roles` (SuperAdmin) taking the **whole set** of roles. Names are
  checked against the roles that exist and canonicalised to the stored spelling. Two refusals:
  a caller cannot remove their own SuperAdmin role, and nobody can remove the last SuperAdmin on
  the site. On a change the security stamp moves, so existing bearer tokens fall back to sign-in
  at their next refresh; the change is audited as `AppUserRoles`.
- `GET .../detail` now carries `Roles`.
- **User detail page**: role badges beside the name; a **Site Roles** tab with a checkbox per
  defined role (the seeded three carry a one-line description of what they grant; a custom role
  says it grants nothing until code checks for it), locked on your own SuperAdmin box, a Save
  button, and the honest note about when it takes effect.
- Client method `SetUserRolesAsync`; shared records `AdminSetUserRolesRequest` and
  `AppUserRolesAdminRecord` in `Ben.Service.Models.Admin`.

## Tests

- `AdminAppUserControllerTests` — eight new: not-found, unknown role, add+remove+stamp+audit,
  case canonicalisation, no-op when unchanged, own-SuperAdmin refusal, last-SuperAdmin refusal,
  another SuperAdmin may remove when not last, and `GetDetail` carrying roles.
- Playwright `AdminTests.AdminUserDetail_HasSiteRolesTab_WithACheckboxPerRole` — written; the
  isolated run (`scripts/run-e2e.sh --filter AdminUserDetail_HasSiteRolesTab`) needs
  `BEN_E2E_ADMIN_PASSWORD` and `BEN_SUPERADMIN_PASSWORD` exported, which live outside the repo
  since the secrets sweep, so it has not yet been run here.
- The own-SuperAdmin guard was proven to discriminate: with the check removed, its test fails.

## Docs

`site-administration.md` gains a **Site roles** section: what each role grants, how to assign
one, when it takes effect, and the two refusals.
