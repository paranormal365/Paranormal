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
- [ ] **Phase 4** — Library migration waves *(A, B, C done; Manage/ pulled forward)*
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

## Phase 4 — library migration waves

### Wave A — `Shared/`, `Help/`, `Support/`

26 components ported. The real home page is live: `HomeHero`, `NearbyDiscovery` and
`PublicCaseDiscovery` replace the Phase 2 placeholder. Routes now served: `/`, `/help`,
`/help/{Slug}`, `/contact`, `/places/{id}`, `/support/{token}`.

Telerik converted: 13 `TelerikButton`, 7 `TelerikTextBox`/`TextArea`, 7 `TelerikLoaderContainer`,
1 each of `Window`, `Pager`, `ButtonGroup`, `DropDownList`. **Kept:** the two `TelerikMap`
instances — the only Telerik left in this wave, and every remaining `k-*` element on the home page
is one of the map's own controls.

New kit component: **`BenPager`** (Bootstrap pagination, windowed page numbers). `BenConfirmDialog`
was widened to mirror the old `ConfirmDialog`'s full parameter surface — same names and defaults,
including ones a fresh design would not pick — so its remaining ~15 call sites move by renaming
the tag rather than being rewritten.

**Wave-order correction:** `AddressFieldsWithMap` is deferred, not ported. It needs
`Manage/Maps/AddressMapPlayer` (planned for Wave G) but its consumers are in Waves B and C, so
**`Manage/Maps/` has to move ahead of Wave B**. `OrgCard` was pulled forward from Wave C for the
same reason — `NearbyDiscovery` cannot render without it — at a cost of 49 Telerik-free lines.

Behaviour changes worth knowing:
- The case popup on the public map was a *non-modal* resizable window floating over the map; as a
  `BenModal` it is modal, so choosing another pin means dismissing the current one first. Taken
  deliberately, to keep one dialog primitive that is known to hold its bindings.
- Two conversions replaced `@bind` with `@oninput` (the hero search, the message composer). Both
  gate a button on their value, and binding on blur leaves that button dead for anyone who types
  and clicks straight through — the trap the original code documented against `TelerikTextBox`.

**A real accessibility bug, found by measuring rather than looking.** Night's semantic colours are
deep (`primary #37508a`, `info #66366c`) because the template uses them as solid fills behind white
text. Bootstrap's `.btn-outline-*` variants invert that, painting the same colour as text on the
page background. Measured on the home page: **info 1.57:1, primary 1.82:1, danger 2.97:1,
success 4.25:1** — all under the 4.5:1 WCAG AA threshold, and the "Request an Investigation" call
to action was the visible symptom. Resting foregrounds are now lightened in dark mode (and success
darkened in light mode); **all four variants pass AA in both themes**, with hover still filling
with the true brand colour. Fixed centrally, before the remaining waves add hundreds more.

Also this wave: Administration now sits directly under Home for an admin, and the one forced
`font-weight` in the Telerik sheet was removed — the template sets its own type weights.

### Wave B — `Manage/Maps/`, `User/`, `Messaging/`

24 components ported. Routes added: `/profile`, `/notifications`, `/admin/users`,
`/admin/users/create`, `/validate-email/{token}`, `/organizations/{id}/messages`.
`AddressFieldsWithMap`, deferred in wave A, lands here with its map dependency.

Not ported: `UserMenu` and `NotificationBell` already exist as host chrome (`BenUserMenu`,
`BenNotificationBell`) — porting them would give the app two of each.

**Converted:** 77 buttons, 26 text boxes, 18 check boxes, 12 windows → `BenModal`, 2 tab strips →
`BenTabs`, plus sliders, numeric inputs and icons. **Kept:** 8 `TelerikGrid`, 8 templated
`DropDownList`, 3 `DateTimePicker`, 3 `ColorPicker`, 2 `TelerikMap`, 1 `TelerikEditor`.
`BenTabs` gained `ActiveId`, because `OrgMessages` addresses its tabs by name rather than index —
which survives tabs being reordered or hidden.

**Deferred: `AdminUserDetail`** (1,038 lines). It needs `Manage/Audio`, `Manage/Media` and
`Manage/Icon` — essentially all of `Manage/`. Combined with wave A's finding, **`Manage/` is the
real blocker and should be the next wave**, not wave G: it is a dependency of User, Organization
and Client alike. `/admin/users/{id}` 404s until then.

**Automating 77 button conversions took three attempts, and the first two silently corrupted
markup.** Both failures were the same mistake — using a regex where Razor needs a parser:

1. `/<TelerikButton.*?>/` ended the tag at the `>` inside a lambda (`@(() => Foo())`), truncating
   attributes into the element's content.
2. Matching attribute values as `"[^"]*"` truncated at the first nested C# string, so
   `OnClick="@(() => Nav.NavigateTo("/admin/users"))"` broke apart.

The third attempt reads tags with a small scanner that balances `@( … )` and skips string literals.
Both original failure cases are now verified correct, and every `<button>` in the wave balances.
The lesson is worth keeping: **a converted file that compiles is not evidence the conversion was
right** — the first attempt's output compiled in most files and was still wrong.

Sizing and colour, all from Ben's review:
- **Night's palette restored.** The wave-A contrast fix lightened the outline buttons far too much
  for a ghost-hunting theme. They now sit close to the originals (`#37508a → #6377a4`,
  `#66366c → #916e95`) — a small lift for legibility rather than a repaint. These read 3.2–5.0:1,
  below the 4.5:1 AA threshold for three of them; that is a deliberate trade in favour of the skin.
- **Icons no longer break onto their own line.** The template's `.sa-icon` is `display:block`,
  which suits standalone icons but made every converted icon button ~60px tall with the glyph
  stranded above its label. Inline contexts are now corrected and icons scale with the label.
- **Controls tightened** from the template's ~43px to ~32px, with `.form-control` and the kept
  Kendo inputs moved in step so a date picker still lines up beside a text box.
- `.btn-system`, the header control, is left alone: it sets `padding` and `line-height` directly
  rather than through Bootstrap's variables, so an override via `--bs-btn-padding-y` is dead code.

**Verified:** solution builds clean, 2430/2430 tests pass, `/profile` renders all four cards with
live data and contains no Kendo markup beyond `k-body` on `<body>`, `/admin/users` loads six real
users into a kept grid that now inherits its panel's surface, and header controls sit correctly.

### Wave C — `Manage/`

The whole `Manage/` area: `Audio/` (WaveSurfer player, `AudioFilePreview` at 2,091 lines,
`WsRegionExplorer`), `Calendar/OrgScheduler`, `Icon/`, `Media/ImageEditorPlayer`, `Video/`, and
`UploadFileVoteBar`. **`AdminUserDetail` is un-deferred** — it needed Audio, Media and Icon, and
now renders all eight of its tabs with live data. Routes added: `/my-videos`, `/video-editor`,
`/admin/users/{id}`, `/organizations/{id}/calendar`.

**Converted:** 99 buttons, 16 sliders, 12 check boxes, 11 windows → `BenModal`, 10 text boxes,
5 numeric inputs, 5 `ExpansionPanel` → `BenPanel`, and 3 `TelerikNotification` instances →
`BenToastService` (the `@ref`/`Show()` calls became service calls). **Kept:** the scheduler, the
rich-text editors, the maps, the date and colour pickers, one grid, and the templated dropdowns.

**Two parser bugs and one code-generation bug, all found by building rather than by reading.**

The tag scanner needed a third fix: an attribute value of the form `@Helper("arg")` — as in
`FillMode="@Fm("select")"` — has to balance parentheses just like `@( … )`, or it truncates at the
nested quote. That is the same lesson as wave B for the third time, and the scanner now handles
`@(…)`, `@Helper(…)` and bare values uniformly.

The generation bug was worse, because it compiled in some files and not others. For a conditional
`ThemeColor` the converter emitted `class="btn @($"btn-{cond ? "a" : "b"}")"` — a ternary inside an
interpolated string, where the `:` terminates the interpolation. It now emits
`@(cond ? "btn-a" : "btn-b")` instead. Two related cases hit the same rule: an interpolated string
in a component *attribute* (`Title="@($"Edit: {FileName}")"`) fails where the identical expression
as element content is fine, so those titles moved to `HeaderContent`.

Diagnosis was by bisection — reverting to the original and re-applying one conversion pass at a
time — after brace and quote counts came back identical to the original and proved nothing. Worth
remembering: **matching brace counts do not mean the markup parses the same way.**

Also fixed: `ImageEditorPlayer`'s dialog was `Width="94vw"`, which the width-to-size mapping read
as 94 pixels and made a *small* modal out of a near-fullscreen editor. It is `Size="fullscreen"`
now; the mapping only ever made sense for pixel widths.

**Verified:** solution builds clean, 2430/2430 tests pass, `/admin/users/{id}` renders its eight
tabs and switches between them with a kept grid showing live memberships, `/my-videos` renders with
the toast host in place and no Telerik notification element left, and the console is clean.

### Wave D — `Organization/` core and `Roles/`

17 components. Routes added: `/find`, `/organizations`, `/organizations/create`,
`/organizations/{id}/edit`, `/members`, `/files`, `/client-settings`,
`/membership-questions`.

**Converted:** 77 buttons, 20 text boxes, 13 windows → `BenModal`, 11 loaders, 10 check boxes,
6 numeric inputs, 3 switches, 3 text areas, 2 notification hosts, a tab strip, a toggle-button
permission grid and a split button. **Kept:** 9 grids, 8 templated dropdowns, a date picker, and
one `TelerikSvgIcon` — that last one on purpose: a stored icon value prefixed `t:` *names* a
Telerik icon, so the icon set is what the data means rather than a styling choice.

**Deferred: `OrganizationView`** (the org hub). It embeds `CaseList` and `OrgCmsEditor`, so it
lands with the CMS wave, the later of its two dependencies. `/organizations/{id}` 404s until then.

**A real bug in `BenDropdown`, found only by using it.** `OnParametersSet` copied the `Open`
parameter into internal state unconditionally, so an *unbound* `Open` — always false — slammed the
menu shut on every re-render of the parent. Invisible in the header, where nothing re-renders
often, but a Telerik grid re-renders constantly, so the split-button menu inside one could never
stay open long enough to click. The component now owns its state unless a caller actually binds it.

**And the same parser mistake for the fourth time.** The split-button conversion was hand-written
rather than run through the scanner, and pulled its menu items out with `OnClick="([^"]*)"` — which
truncates at the first nested quote, so every handler became
`@(() => NavManager.NavigateTo($`. Rebuilt using the scanner. The rule has earned a name by now:
**in Razor, never read an attribute with a regex.**

**Verified:** solution builds clean, 2430/2430 tests pass, `/organizations` lists three groups with
live counts and mm/dd/yyyy dates, and the rebuilt split button opens with all eight actions and
closes on click-away.

### Wave E — `Organization/Cases/` (+ `Media/` pulled forward)

17 case components plus the 4-file `Media/` area, pulled forward because `CaseFiles` needs
`MediaLibraryGrid`. Routes added: `/organizations/{id}/cases`, `/cases/new`, `/cases/{id}`,
`/cases/{id}/audio-mix`, `/cases/{id}/video-editor`, `/pending-requests`.

**Converted:** 93 buttons (plus 12 more once the converter learned literal attribute values like
`Size="sm"` and `FillMode="outline"`), 20 text boxes, 16 windows → `BenModal`, 11 text areas,
2 id-addressed tab strips, and the notification hosts. **Kept:** 7 templated dropdowns, 6 date
pickers, 2 grids, 2 rich-text editors, 2 date pickers.

`OrganizationView` stays deferred: `CaseList` has landed, but it still embeds `OrgCmsEditor`.

### Header chrome — measured against the template, not guessed

A round of header fixes that took several attempts, and the lesson is the one Ben named: **compare
against the template's own rendering rather than eyeballing it.** Running the template at :5266 and
measuring the same elements settled in one step what three rounds of guessing had not.

What was actually wrong, in order:

1. **Controls at four different heights** (29, 35, 98, 42px), two of them hanging above an 88px
   header. Root cause: the template's `.btn-system` contains *nothing but an `<svg>`*, so its
   `line-height: var(--app-header-height)` never applies to the box. Razor emits comment markers
   and whitespace inside the button, which creates an inline formatting context — and there that
   88px line-height *does* apply. `line-height: 0` removes the stray inline box.
2. **No gap at all** between controls: every one measured `margin: 0` with no gap on the row.
3. **Both toggles beside the wordmark at once.** Self-inflicted: `display: inline-flex !important`
   beat the template's `.d-none`/`.d-lg-none` utilities as well as the one it was aimed at. The
   override is now scoped to the breakpoint each utility already intends.
4. **A sun and a moon together in light mode.** Also self-inflicted: the kit's inline-icon rule had
   the same specificity as `.app-header .sa-mode-light { display: none }` and loaded later, quietly
   un-hiding it. The kit rule now excludes the template's own state pairs.
5. **Sign In crushed into a 40px square** with its highlight off the words, because it carries a
   label and had been swept into the icon-only sizing.

**Two chrome fixes after wave E:**

- **Sidebar type** dropped from the template's 600 to 400, with the active item at 500 — semibold
  down a long list reads heavy.
- **The mobile menu was a one-way door.** Opening it worked; closing it was impossible without
  picking a link, because the open sidebar is 288px wide from x:0 and covers the hamburger that
  opened it. The template solves this with a backdrop in `_Sidebar.cshtml` that both dims the page
  and toggles the menu closed — omitted when the sidebar was rebuilt as `BenNav`, and now restored.
  Verified: tapping the backdrop closes the menu.

### Wave F — `Client/` and `Equipment/`

25 components, including `MyCaseDetail` at 1,346 lines — the largest single file in the app.
Routes added: `/my-cases`, `/my-cases/{id}`, `/my-requests` (+ new/edit/detail),
`/my-investigations`, `/equipment-catalog`, `/equipment/{id}`, `/equipment-models/{id}`,
`/my-equipment`, `/my-equipment/questions`, `/my-checkouts`, `/my-checkouts/{id}`.

**Converted:** 101 buttons, 14 loaders, 13 icons, 10 windows → `BenModal`, 10 text boxes,
2 tab strips. **Kept:** 7 templated dropdowns, 6 date/time pickers, a calendar, a grid and an
editor. The site now serves **49 routes**.

Three familiar shapes recurred and were fixed the same way: dialog titles built from literal text
plus a C# expression (`Title="Ask about @(…)"`) became properties, since that is mixed content in
a component attribute; a four-way icon ternary needed all its branches converted, not just the
first two; and the last `ConfirmDialog` call sites were renamed to `BenConfirmDialog`.

**Date formats swept again.** Wave A's pass only covered the files that existed then, so the newer
waves arrived with `"MMMM d, yyyy"`, `"MMM d, yyyy"` and friends — "Opened August 11, 2026" on
`/my-cases` is what surfaced it. 32 files normalised onto the shared helpers; the only literal date
format left in the library is `ToString("D")` on a **Guid**, which is not a date at all.

### Wave G — `Organization/Cms/`

The CMS authoring area: `OrgCmsEditor` (1,161 lines), `OrgCmsPageEdit` (1,084), `CmsSectionEditor`
(835), plus snippets and the file thumbnail. **`OrganizationView` is un-deferred** — the org hub
now renders all 13 of its tabs, including the embedded case list and CMS editor that kept it
waiting two waves. Routes added: `/organizations/{id}` and the two CMS routes.

**Converted:** 49 buttons, 10 check boxes, 11 windows → `BenModal`, 9 text boxes, 3 numeric inputs,
2 dropdown buttons → `BenDropdown`, tab strips. **Kept:** 7 templated dropdowns, 5 grids, and both
rich-text editors — the CMS is exactly the place where a real editor earns its keep.

**Deferred: `OrgCmsPagePreview`** — it renders public sections, so it belongs with the microsite.

**Two conversion bugs, both caught by the compiler rather than by reading:**

1. **A `@foreach` was flattened.** The dropdown-button converter extracted a single item as a
   template and dropped the loop generating them, along with the `var chosen = snippet;` that made
   the closure correct. Rewritten to keep the items markup *verbatim* and only swap the wrapper
   tags — loops, locals and all.
2. **An inline lambda was called as if it were a method.** `ValueChanged="@((string v) => { … })"`
   became `lambda(arg)`, which is not valid C#. The converter now binds the lambda's parameter to
   the DOM value and inlines its body. A related case needed `.InvokeAsync(…)`, because
   `ContentJsonChanged` is an `EventCallback` parameter, not a method.

**Verified:** solution builds clean, 2430/2430 tests pass, `/organizations/{id}` renders 13 tabs
with its Cases and CMS tabs both live, and the CMS editor lists real pages in a kept grid.

**Administration is now one expandable entry.** It was a headed section taking a permanent third of
the sidebar; it is a single top-level item holding the same four groups (Users, Cases &
Investigations, Content, System), each still expandable — three levels in total. The template's own
nav nests three `<ul>` deep and styles `.primary-nav ul ul ul`, so the depth costs nothing.

Two changes made it work: the item markup is rendered **recursively** rather than as two fixed
levels, and the auto-open check recurses too — landing on `/admin/users` opens Administration *and*
the Users group beneath it, where a one-level check would have opened neither.

**Minified sidebar: labels bled past the edge.** Reported as "Home text appears when you minimize",
and the cause was not styling at all — **Blazor's enhanced navigation rewrites `<html>` from the
server's response, and the layout classes are a client-side preference the server never renders.**
So `set-nav-minified` survived exactly until the first link click, after which the sidebar stayed
narrow (its width already applied) while the labels un-faded and spilled out of it.

`ben-boot.js` now exposes `benApplyLayoutSettings()`, and `App.razor` re-asserts it on Blazor's
`enhancedload` — registered there rather than in the script because `Blazor` only exists after
`blazor.web.js` runs. This affects every layout toggle, not just minify: nav-dark and fixed-header
were being lost on navigation too.

Also fixed alongside it: the menu filter kept the wrong markup. The template hides it with
`.app-menu-filter-container #searchInput`, so a differently-named wrapper left the box on screen at
70px wide. It uses the template's own class and id now.

### Wave H — `SuperAdmin/`

12 admin screens: audit log (618 lines), file types (512), experience and equipment taxonomies,
lookup types, roles, all-cases, all-investigations, site settings, support tickets, video assets and
sidecar telemetry. **`AdminSidePanel` is deliberately not ported** — Administration lives in the
sidebar here, so a second right-hand drawer would be the same navigation twice.

**Converted:** 62 buttons, 15 text boxes, 9 windows → `BenModal`, 8 loaders, 7 check boxes,
5 numeric inputs, 4 text areas and a tab strip. **Kept:** 10 grids, 4 templated dropdowns, 2 date
pickers, and one `TelerikSvgIcon` for the same reason as before — a stored `t:` value names a
Telerik icon. The site now serves **64 routes**.

### I broke sign-in, and it took a report to find it

The layout-persistence fix registered its listener as `Blazor.addEventListener("enhancedload", …)`
— **which does not exist**. The exception was thrown on the line *before* `Blazor.start()`, so
Blazor never started: nothing on the site was interactive and sign-in silently failed. The
symptom Ben saw second — "you never organized the administration into groups" — was the same bug,
since Administration only renders for a signed-in SuperAdmin.

Two things changed as a result:

- `Blazor.start()` now runs **first**, and the optional listener follows it inside a `try`. Nothing
  that can throw belongs ahead of the call that makes the site work.
- The listener uses `document.addEventListener("enhancedload", …)`, which is the actual API.

Worth recording: my own verification missed this because I re-tested the *feature* (does the class
survive navigation?) rather than the *page* (does anything still work?). A JS exception in
`App.razor` breaks everything downstream of it, so that file deserves a login check after every
edit, not a feature check.

**The nested admin menu did not actually open**, though the DOM said it had. Two template hooks
were missing, and both are the same lesson as the mobile backdrop and the menu filter:

- The template's nav script adds `has-ul` and a `<span class="collapse-sign">` chevron to any item
  with a submenu. Without them the group carried no affordance at all — it looked like a plain
  link, which is what Ben reported first.
- More seriously, the template hides **every** nav list with `.primary-nav ul { display: none }`
  and reveals a submenu with `.primary-nav ul.nav-menu ul.active` — the class belongs on the
  `<ul>` itself. Toggling an inline `display` style instead left the stylesheet's default in force,
  so the group opened in the DOM, reported `open`, and stayed invisible.

That is three times now that rebuilding template markup "equivalently" cost a bug. The rule for the
remaining work: **copy the template's class names and hooks verbatim, then change behaviour** —
the class names are not decoration, they are the API.

**Button labels wrapped under their own icons.** Bootstrap 5 allows button text to wrap, so in a
flex row where the input takes the width, "Add" broke onto a second line beneath its `+` and the
control grew to 55px. `.btn` keeps its label on one line now, and a button in a flex row no longer
shrinks to make room for the field beside it. Worth noting this looked identical to the earlier
`display:block` icon bug and was not: the icon was already inline, the button was simply too narrow.
