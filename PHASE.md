# Phase A — Critical Authorization Holes

Branch: `feature/security-phase-a-critical-auth`

## Why

A four-pass audit of `Ben.Data.WebApi` and `Ben.Web.WebApp` (two authorization passes, one
correctness pass, one Blazor-client pass) found ~25 issues. This branch fixes the four that are
**critical and currently exploitable by any authenticated user, or by an unauthenticated one**:

1. **`EntraAuthController.Register`/`Link` — account-takeover primitive.** Both actions trusted a
   client-supplied `EntraOid`/`EntraEmail` in the request body instead of the caller's own validated
   token claims. `Register` was `[AllowAnonymous]`, so anyone could pre-register an
   `EmailConfirmed = true` account for any real email address. `Link` let any authenticated user link
   an arbitrary OID to their own account — so a victim's later, genuine Microsoft sign-in would
   resolve to the *attacker's* account (`EntraClaimsTransformation` matches by OID from the validated
   JWT).
2. **`EntityReadControllerBase` — 14 endpoint families with zero filtering.** `GetAll`/`GetById`
   returned every row to any logged-in user: home addresses, emails, phone numbers, private message
   bodies, internal notes. 13 of the 14 have no frontend consumer at all.
3. **`UploadFileShareController` — no authorization on any action.** Any authenticated user could
   share someone else's private file into any org, change any share's visibility, or delete any org's
   shares.
4. **Org privilege escalation.** A non-Owner `Administrator` could self-promote to `Owner`, or
   demote/deactivate the real Owner, via `OrganizationSecurityService.UpsertMembershipAsync`.

None of these were introduced by recent work — all four are pre-existing gaps in files this session
hadn't touched before the audit found them.

## Approach

- **Entra**: read `oid` from `User.FindFirst(...)` (the same pattern `MeController` already uses),
  requiring the `Entra` JWT scheme instead of trusting the body. `Link` additionally requires proving
  ownership of the target local account via password check. `CompleteProfile.razor` and
  `WebApiClient.EntraLinkAsync` updated to match.
- **`EntityReadControllerBase`**: default to `[Authorize(Policy = RoleNames.SuperAdmin)]` at the base
  class. The one real consumer (`AppUserController`, used by two org-CMS editors purely to resolve
  display names) gets a new lean, org-scoped `GET /api/organizations/{orgId}/user-directory` endpoint
  instead of full user records.
- **`UploadFileShareController`**: gate every action using the existing `FileAudienceAccess` helper
  (visibility) plus file ownership (mutations) and org-membership/visibility tiers (`GetOrgFiles`),
  matching the pattern that helper already documents.
- **Org privilege escalation**: `Administrator` can now only manage roles strictly below itself,
  cannot grant `Owner`, and cannot touch an existing `Owner` membership; a last-Owner removal guard
  was added too.

## Verification

Regression tests per hole (attacker path rejected, legitimate path still works) plus a live check
that the org-CMS "assign to user" pickers still resolve names correctly after the `AppUserController`
lockdown. The Entra fix additionally needs a live Microsoft sign-in to fully verify — flagged
separately for manual confirmation since it changes a real OIDC-adjacent flow.

See the full remediation plan (phases A–D) in the session that opened this branch.
