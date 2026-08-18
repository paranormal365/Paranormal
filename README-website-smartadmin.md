# Branch: `feature/website-smartadmin-night`

Building **Ben.Web.Website** — a re-skinned parallel version of the whole app on the
SmartAdmin Bootstrap template (Night skin), alongside the existing Telerik-styled
`Ben.Web.WebApp`. Both run side-by-side until the new one is judged better; if it wins,
the old stack is removed.

**Hard invariant for every phase: authentication must keep working exactly as it does today —
local password login *and* Microsoft Entra. Nothing should break.**

Full plan: `~/.claude/plans/i-would-like-for-snug-kite.md`

## Why

The current UI is Telerik-styled throughout (~1,340 Telerik tags across
`Ben.Web.Library` + `Ben.Web.WebApp`). The SmartAdmin template
(`/Users/ben/Source/ASP.NET-Core10`) is Bootstrap 5.3.8-based and already matches many
features the app hand-rolls. Since the app's pages already lean on Bootstrap utilities
heavily (611 `d-flex`, 403 `form-label`, 192 `card`, …), page layout ports nearly
unchanged — what changes is the widget layer and the page chrome.

## Decisions

| Question | Decision |
|---|---|
| Naming | `Ben.Web.Website` (host) + `Ben.Web.Website.Library` (RCL). No "SmartAdmin" naming. |
| Service sharing | Extracted to a new shared `Ben.Web.Services` project, referenced by old *and* new stacks. |
| Theme | Night skin only, baked in server-side; light/dark toggle kept (`data-bs-theme`). No skin picker. |
| Grids | Keep `TelerikGrid`, restyled. SmartAdmin's `smartTables` rejected — it is a 5,700-line JS DOM-mutating table that fights Blazor Server's ownership of the render tree. |
| Kit prefix | `Ben*` (`BenModal`, `BenPanel`, …) |
| Bootstrap | Stays — SmartAdmin *is* Bootstrap 5.3.8 (compiled into `smartapp.min.css`). The current CDN Bootstrap link is dropped in the new host to avoid loading it twice. |

## Phases

- [x] **Phase 0** — Template asset prep
- [x] **Phase 1** — Extract `Ben.Web.Services`
- [x] **Phase 2** — New projects + host shell
- [x] **Phase 3** — Component kit + Telerik Night restyle
- [ ] **Phase 4** — Library migration waves A–J
- [ ] **Phase 5** — Parity, guard tests, docs

### Phase 0 notes — the Night theme was broken upstream

`wwwroot/scss/themes/night.scss` imported `lunar/variables` instead of `night/variables`,
so the compiled `night.css` was **byte-for-byte identical to `lunar.css`** — the Night skin
never actually rendered its own palette (`$color-primary: #37508a`; it showed Lunar's
`#557596`). Fixed in the template repo and rebuilt.

The template repo has no `node_modules` and `bun` isn't installed here, so the gulp `scss`
task (sass → autoprefixer `last 2 versions` → clean-css) was replicated with those three
packages directly. Validated by rebuilding **lunar** first and diffing against the checked-in
file: identical except one legacy `-webkit-gradient` line the original's older browserslist
data emitted. Rebuild recipe lives in the eventual `Ben.Web.Website/README`.

### Phase 1 notes — `Ben.Web.Services`

Moved (via `git mv`, so history follows):

- from `Ben.Web.Library/Services/` → `Ben.Web.Services/`: the 11 `IBen*Client` interfaces,
  `BenAdminClientRecords` (~1,000 lines of DTOs), `IBenUserState`, `AuthGuardExtensions`,
  `NotificationState`, `NotificationBadge`, `AvatarCache`, `VideoExportPublisher`,
  `CaseStatusExtensions`, `DateTimeViewerExtensions`
- from `Ben.Web.WebApp/Services/` → `Ben.Web.Services/WebApi/` (+ root): the 11
  `BenAdminClientAdapter` partials, `WebApiClient`, `WebApiIdentityClient`,
  `WebApiTokenStore`, `WebApiAuthService`, `WebApiBearerTokenHandler`, `JwtClaimsParser`,
  contracts, `WebApiOptions`, `EntraTokenHolder`, `BenMediaLibraryProvider`,
  `SidecarPairingReporter`

Namespaces `Ben.Web.Library.Services` and `Ben.Web.WebApp.Services[.WebApi]` both collapse to
`Ben.Web.Services[.WebApi]` — 132 files rewritten. `HelpContentService`/`HelpViewerResolver`
stayed in `Ben.Web.Library` (coupled to its embedded `Help/Content/*.md` resources).

The two same-named `AuthGuardExtensions` classes do **not** collide: one extends
`IBenUserState`, the other `IWebApiTokenStore`, and they land in different namespaces
(`Ben.Web.Services` vs `.WebApi`). They exist separately only because the library previously
couldn't see `IWebApiTokenStore`; merging them is now possible but deliberately deferred —
it is not behaviour-preserving work.

**One test broke, and it was the right one to break.** `RecurrencePresetTests` reaches
`OrgScheduler`'s private helpers by reflecting over
`typeof(IBenAdminClient).Assembly` — which stopped being the library's assembly the moment the
service layer moved out. Its own `The_component_and_its_helpers_are_reachable` guard exists
precisely so a rename can't turn the suite "green by absence", and it fired. Re-anchored on
`LibraryAssemblyMarker`, the one type that cannot leave `Ben.Web.Library`.

**Verified:** solution build clean (0 warnings), `Ben.Web.Tests` 2429/2429 pass, local login
works, auth survives a hard refresh, org grid loads live WebApi data, `/auth/entra-signin`
still issues a correct OIDC challenge (PKCE + downstream API scope), no console errors.

### Phase 2 notes — the shell

`Ben.Web.Website` (host, ports **5078**/7050 — see the port-swap note below) + `Ben.Web.Website.Library` (RCL). `Program.cs` is
a clone of the original host's differing by exactly two lines — the components namespace and the
router's additional assembly — so every DI registration, the whole Entra OIDC block, the token
capture middleware and both `/auth/entra-*` endpoints carry over byte-identically. MainLayout's
auth bootstrap `@code` block is likewise verbatim; only the markup around it changed.

Chrome rebuilt as components: `BenHeader`, `BenNav`, `BenFooter`, `BenNotificationBell`,
`BenUserMenu`, plus `BenIcon` in the library's `Kit/`.

Decisions taken during the build:
- **Administration is an expandable sidebar group**, not the old right-hand drawer — one place
  to look for navigation. `AdminSidePanel` has no counterpart here.
- **No settings drawer.** The template's layout options are decisions this site makes once, not
  settings to hand a visitor. Light/dark stays, using the template's own two-icon switcher.
- **Nav labels**: "Request Investigation" (→ the client request wizard) sits above "Join a Group"
  and "Equipment"; no Login entry, since the header carries Sign In on every page.
- `ben-boot.js` replaces `saveloadscript.js` + `smartApp.js`, keeping the template's own
  `layoutSettings` storage and `data-action` contract but implementing only the layout actions.
  Panel state and nav state are C#; the script never touches Blazor-rendered DOM.
- Assets vendored: **3.5 MB**, not the template's ~30 MB — no demo images (the app has its own
  branding), sprite only rather than 288 loose icons, and woff2 only from FontAwesome's 16 MB of
  legacy font formats.

**Three bugs found and fixed while verifying, all of the same family — state that only looks
right on the happy path:**

1. A chromeless `BlankLayout` for the login page would have **hung sign-in forever**: Login
   awaits `AuthReady`, and only `MainLayout` ever signals it. Login stays on the full layout;
   giving it its own shell means first extracting the bootstrap into something both layouts render.
2. **Header and sidebar disagreed after a hard refresh** — sidebar signed in, header showing
   "Sign In". `RestoreAuthStateAsync` deliberately calls `StateHasChanged()` without raising
   `StateChanged` (raising it would immediately re-persist what was just read back), so a child
   component subscribing to that event never learns it is signed in. The old layout got away with
   it because the bell and user menu were inline in MainLayout's own markup. Auth flags are now
   passed down as parameters.
3. The sidebar **could not scroll**, leaving the Administration items unreachable on short
   viewports. The template gets its scrollbar from `smartSlimscroll.js`, which this app does not
   ship; native overflow needs `.app-wrap { height: 100vh }` and a `min-height: 0` chain, because
   flex/grid children default to `min-height: auto` and refuse to shrink below their content.

**Verified:** solution builds clean, 2429/2429 tests pass, local login works, auth survives a hard
refresh with header and sidebar agreeing, sidebar scrolls to the last admin item on a 500px-tall
viewport, theme toggle persists via `layoutSettings` (mirrored to `ben-theme`), every asset
returns 200, and **no `delegateTarget` error** — the bootstrap-bundle-vs-enhanced-navigation
regression the original host removed Bootstrap's JS to avoid.

Not yet ported (so their routes 404 on this host until their wave lands): everything in
`Ben.Web.Library`. `/find` is the one you will hit first.

### Port swap — the new site holds :5078

The two front ends traded ports:

| App | Ports |
|---|---|
| `Ben.Web.Website` (SmartAdmin/Night) | **5078** / 7050 |
| `Ben.Web.WebApp` (original, Telerik) | **5079** / 7051 |

:5078 is not arbitrary — it is the redirect URI already registered with Entra
(`http://localhost:5078/signin-oidc`) and an allow-listed CORS origin on the API. Giving it to
the new site means Microsoft sign-in works there with no change to the app registration; the
original app keeps working on :5079, which was added to the API's dev CORS list alongside it.

Start either with the API attached:

```bash
bash scripts/start-website-with-api.sh   # new site,  :5078
bash scripts/start-webapp-with-api.sh    # original,  :5079
```

Both scripts share one implementation — `start-webapp-with-api.sh` now takes `BEN_APP_PROJECT`
and `BEN_WEBAPP_URL`, and the website script just sets them.

The Playwright suite's `BEN_BASE_URL` default moved to **:5079**, because its pages still live in
the original app; flip it to :5078 as the migration waves land. Fixed along the way:
`ErrorHandlingTests` derived the API URL by string-replacing `"5078"` out of the base URL, which
silently pointed at the front end for any other port. It now reads a `BEN_API_URL`/`ApiUrl` of
its own.

### Phase 3 notes — the component kit

`Ben.Web.Website.Library/Kit/`: `BenModal` (+`BenModal.razor.js`), `BenConfirmDialog`, `BenPanel`,
`BenPageHeader`, `BenTabs`/`BenTab`, `BenToastService`/`BenToastHost`, `BenDropdown`,
`BenLoaderOverlay`, `BenIcon`, plus `wwwroot/kit/ben-kit.css`. `/styleguide` (Development only)
renders all of it beside the kept Telerik widgets for side-by-side checking in both themes.

**The invariant these share:** every one renders Bootstrap's *markup* while keeping open/active
state in C#, and none opts into Bootstrap's JS plugins. The plugins move, re-parent and remove
nodes; Blazor then patches a tree that no longer matches and input inside the subtree is silently
dropped — the same failure that got `TelerikDialog` banned here, reached by a different route.
`BenKitOwnsItsDomTests` enforces it by scanning for `data-bs-toggle`, and
`NoTelerikDialogTests` now scans the new projects too.

`BenPanel` deliberately does not remove itself on close — it raises `OnClose` and lets the parent
decide, because a component that deletes its own markup cannot be brought back without the parent
knowing. That is also why the template's `panel-close` handler could not simply be kept.

**Telerik restyle** (`wwwroot/theme/telerik-night.css`): Kendo 14.x is CSS-variable-driven, so the
sheet maps `--kendo-*` onto the template's own `--bs-*` tokens — one mapping serves light and dark,
no Kendo SASS build. Two corrections were measured on `/styleguide` rather than guessed:

- Telerik inputs came out **39.6px tall at 14.8px type** against Bootstrap's **43.6px at 16px** —
  visible misalignment wherever a date picker sits beside a text box. The fix needed `.5rem`
  padding, not Bootstrap's stock `.375rem`, because the template overrides `.form-control`.
  Both now measure 43.6px in both themes.
- `.k-grid` painted itself `--bs-body-bg` while the panel containing it uses `--bs-tertiary-bg`,
  so every grid read as a pasted-on box. Grids now inherit the panel's ground.

**Three fixes while verifying:**

1. **Nothing should grab focus on load.** `FocusOnNavigate Selector="h1"` was focusing the page
   heading on arrival, painting a ring as though a control had been activated. Removed — noted in
   `Routes.razor` that the cost is screen-reader focus no longer moving to the heading on
   client-side navigation.
2. **`BenModal` never focused its first field.** The cause was `requestAnimationFrame`: a browser
   does not paint a hidden tab, so the callback never ran at all. It is a real product bug, not a
   test artifact — a modal opened in a background tab would stay unfocused forever. Now
   `setTimeout`, which fires regardless of visibility.
3. **The new guard test tripped on its own documentation** — `BenTabs`'s comment quotes
   `data-bs-toggle="tab"` to explain why it is not used. It now strips Razor and HTML comments
   before scanning, and was checked against a planted offender to confirm it still fails when it
   should.

**Verified:** solution builds clean, 2430/2430 tests pass, and on `/styleguide` — modal holds an
input binding through close/reopen (the TelerikDialog failure does not reproduce), Escape closes a
normal modal while a static confirm ignores it, body scroll locks and unlocks, toasts auto-dismiss
while errors persist, tab state survives switching, panels collapse, Telerik and Bootstrap inputs
measure identically in light and dark, and no `delegateTarget` error after repeated navigation.
