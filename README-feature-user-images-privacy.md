# Area 4 — User Images & Client Privacy

Branch: `feature/user-images-privacy` (from `main` @ `027e10d`)

Full plan: [`ProjectNotes/Feature-Roadmap.md`](ProjectNotes/Feature-Roadmap.md) → *Area 4*.

## Why this area exists

A signed-in user can currently change **nothing** about themselves after signup. Every `AppUser`
field lives behind SuperAdmin-only `/admin/users/{id}` screens, and the top bar shows an email
address and a Sign Out link. There is no profile page of any kind. This area builds one, and then
uses it as the foundation for the privacy work the client side needs.

## Phases

| Phase | What | State |
|---|---|---|
| U1 | `AppUserPhoto` entity + `MyProfileController` + 4-layer client wiring | ✅ shipped (merged in `ed45350`) |
| U2 | Self-service `/profile` page, top-bar user menu, private-photo opt-in + org policy | ← current |
| U3 | `GET /api/users/{id}/avatar` viewer-aware resolution + `UserAvatar.razor` | pending |
| U4 | Client private-photo sharing to the orgs handling their case | pending |
| U5 | Witness photos on `CaseRelatedPerson` + the missing PUT endpoint | pending |
| U6 | Client anonymity aliases (public pages and reports only) | pending |
| U-Occ | Occurrence enrichment — files at creation, experience tags, witness links | pending |
| U2-cam | Camera capture for the private photo — **native camera app**, deferred | deferred |

## Decisions already made (don't relitigate)

- **Private-photo sharing to clients needs BOTH** the org's policy *and* the individual's opt-in.
  Either one alone is not consent.
- **Aliases apply to public pages, reports, and shared documents only.** Org investigators always
  see the real name — you cannot investigate what you can't identify.
- **Camera capture uses the device's native camera** (`capture` attribute), not an in-page
  `getUserMedia` preview. See U2-cam in the roadmap for why, and for the secure-context trap.

## What U1 already gives U2

- `GET/PUT /api/me/profile`, `GET/POST /api/me/photos`, `DELETE /api/me/photos/{id}`,
  `GET /api/me/photos/file-type` — all scoped to the caller; none take a user id.
- `IBenAdminClient.GetMyProfileAsync` / `UpdateMyProfileAsync` / `GetMyPhotosAsync` /
  `SetMyPhotoAsync` / `DeleteMyPhotoAsync` / `GetProfilePhotoFileTypeIdAsync`.
- Two photo slots per user (public/private), one active each, enforced by a filtered unique index.
  Setting the public slot also flips the underlying `UploadFile.IsPublic` so the file is actually
  servable.

So U2 is UI work, not API work.

## House practice for this branch

- `dotnet build Ben.slnx` clean — **0 warnings**.
- Full `Ben.Web.Tests` suite green before every commit.
- Live verification against seeded dev data (WebApi `:5252`, WebApp `:5078`,
  `ASPNETCORE_ENVIRONMENT=Development` required; SQL Server at `192.168.1.71`, never Docker).
- New auth-gated pages await `IBenUserState.AuthReady` in **both** `OnInitializedAsync` and any
  `OnAfterRenderAsync` fetch.
- Prefer plain Blazor `<select>`/`<input>` over Telerik pickers for bound values — Telerik
  dropdowns have repeatedly failed to push their selection to the bound field in this codebase.
