# Ben.Web.WebApp — Pages & Layout

---

## Pages

### `Login.razor`

**Route:** `/login`  
**File:** [`Ben.Web.WebApp/Components/Pages/Login.razor`](../../../Ben.Web.WebApp/Components/Pages/Login.razor)

Login form that calls `IWebApiAuthService.LoginAsync`.  
- Redirects to `/` only when `IsAuthenticated && UserId.HasValue` (a partial Entra session — where `AccessToken` is set but `UserId` is not yet resolved — no longer triggers a false redirect).
- Shows "Sign in with Microsoft" button when Entra is enabled (navigates to `/auth/entra-signin`).
- On success: `StateChanged` fires → `MainLayout` re-renders the app bar.

---

### `OrganizationSecurity.razor`

**Route:** `/organization-security`  
**File:** [`Ben.Web.WebApp/Components/Pages/OrganizationSecurity.razor`](../../../Ben.Web.WebApp/Components/Pages/OrganizationSecurity.razor)

Developer/test page for exercising organisation security features.

**Sections:**
- Register organisation (calls `RegisterOrganizationAsync`)
- Check my access (calls `CheckMyOrganizationAccessAsync` with table + action selectors)
- Load organisation users (calls `GetOrganizationUsersAsync`)
- User search (calls `SearchUsersAsync`, populates target-user dropdown)
- Update membership (role dropdown → calls `UpsertOrganizationMembershipAsync`)
- Set access grant (table/action/allowed → calls `SetOrganizationGrantAsync`)
- Check user access (checks another user's access)

---

### `UploadFiles.razor`

**Route:** `/upload-files`  
**File:** [`Ben.Web.WebApp/Components/Pages/UploadFiles.razor`](../../../Ben.Web.WebApp/Components/Pages/UploadFiles.razor)

File management page.

**Features:**
- Upload file (file picker + file type selector → multipart POST)
- List all uploaded files
- Download file (`GetAsync` stream → browser download)
- Update file metadata (description, public flag)
- Delete file
- Share file with organisation
- Submit permission request

---

### `Entra/CompleteProfile.razor`

**Route:** `/entra/complete-profile`  
**File:** [`Ben.Web.WebApp/Components/Pages/Entra/CompleteProfile.razor`](../../../Ben.Web.WebApp/Components/Pages/Entra/CompleteProfile.razor)

Shown when an Entra-authenticated user has no linked local account (`UserId == Guid.Empty` from `/api/me`).

**Three-state flow:**

| State | UI | Action |
|---|---|---|
| Choose | "Create new account" or "Link existing account" | Picks a path |
| Create | DisplayName input, email pre-filled | Calls `EntraRegisterAsync` → force-reloads to `/` so OIDC middleware repopulates `EntraTokenHolder` |
| Link | Email + password form | `LoginAsync` → then `EntraLinkAsync` to attach OID to the local account |

---

## Infrastructure Components

### `EntraTokenPersister.razor`

**File:** [`Ben.Web.WebApp/Components/EntraTokenPersister.razor`](../../../Ben.Web.WebApp/Components/EntraTokenPersister.razor)  
**Render mode:** None (static SSR — lives outside `<Routes @rendermode="InteractiveServer" />` in `App.razor`)

Bridges the HTTP-scope `EntraTokenHolder` (populated by middleware) to the Interactive Server circuit scope.

**How it works:**
1. Runs in the static SSR phase (HTTP request scope) where `EntraTokenHolder` is populated.
2. Calls `PersistentComponentState.RegisterOnPersisting` — the framework calls this callback just before rendering the HTML, embedding `SerializedEntraToken` JSON into the page.
3. `MainLayout.OnInitialized` (in the circuit scope) calls `TryTakeFromJson("EntraToken")` to restore the data into the circuit-scope `EntraTokenHolder` before `TryBridgeEntraAuthAsync` runs.

Without this bridge, the circuit always sees an empty `EntraTokenHolder` and the Entra user is never logged in.

---

## Layout Components

### `MainLayout.razor`

**File:** [`Ben.Web.WebApp/Components/Layout/MainLayout.razor`](../../../Ben.Web.WebApp/Components/Layout/MainLayout.razor)

The application shell. All pages render inside this layout.

**Top app bar features:**
- Displays `UserEmail` when authenticated.
- "Sign Out" button → `IWebApiAuthService.Logout()`.
- "Sign In" link when not authenticated.
- **"Administration" button** — visible when `IsSuperAdmin && !IsImpersonating`. Toggles the `AdminSidePanel`.
- Amber impersonation banner with "Return to SuperAdmin" when `IsImpersonating`.
- Theme toggle (dark/light).

**Auth persistence:**
- Subscribes to `IWebApiTokenStore.StateChanged`.
- On state change: serialises to `ProtectedLocalStorage["ben-auth-state"]` AND calls `StateHasChanged()`.
- On `OnAfterRenderAsync(firstRender = true)`: restores from `ProtectedLocalStorage`, then calls `TryBridgeEntraAuthAsync` only if `!TokenStore.UserId.HasValue` (skips if already fully authenticated — prevents Entra from overriding a local session).
- Unsubscribes in `Dispose()` to prevent memory leaks.

**`TryBridgeEntraAuthAsync`:**
- Skips early if `EntraTokenHolder` has no token, or if `TokenStore.UserId.HasValue` (already authenticated).
- Sets `AccessToken`, `UserEmail`, `IsEntraSession = true` then awaits `/api/me`.
- Re-checks `UserId.HasValue` after the await (guards against race with local `LoginAsync`).
- On `Guid.Empty` (no linked account): clears partial state and navigates to `/entra/complete-profile`.
- On `null` response (API failure): clears partial state and returns (user sees unauthenticated home page; no partial auth state persists).

**Navigation drawer** (left side):
- Home, Counter, Weather (placeholder pages)
- Organisation Security page

---

### `ThemeChanger.razor`

**File:** [`Ben.Web.WebApp/Components/Layout/ThemeChanger.razor`](../../../Ben.Web.WebApp/Components/Layout/ThemeChanger.razor)

Toggle button that switches between Telerik's dark and light themes.

- Persists selection to `localStorage["ben-theme"]`.
- Reads OS `prefers-color-scheme` as the initial default.
- A small inline `<script>` in `App.razor`'s `<head>` applies the stored theme before first render (prevents FOUC — Flash of Unstyled Content).
