# Area 6 — In-App Help Documentation

Branch: `feature/help-documentation`

Help lives in its own section of the app at `/help`, reachable signed out from the footer, with
small `?` badges beside the parts of the UI that need explaining.

## What a reader sees

One catalog, filtered — documents are never duplicated per role. Each document declares the
lowest audience that should see it, and a reader's *ceiling* decides how far down the list they
can read:

| Ceiling | Who has it | Adds |
|---|---|---|
| `Everyone` | anyone, signed out | What the site is; how to request an investigation |
| `SignedIn` | any account | Your case; your profile and photos |
| `OrganizationMember` | any active group membership | Working a case |
| `OrganizationAdministrator` | group Owner or Administrator | Administering a group |
| `AppAdministrator` | app-wide SuperAdmin or Admin | Site administration |

A Manager runs cases day to day but does not configure the group, so the group-administration
documents are not theirs — "create/own/administer" is Owner and Administrator.

The ceiling is computed server-side by `GET /api/me/help-audience`. It has to be: the app-wide
roles and the caller's memberships are only both visible there, and `OrganizationSummaryResponse`
— the shape the browser can already fetch — carries no role at all.

## Pieces

| Piece | Where |
|---|---|
| Audience ladder | `Ben.Data.Common/Enums/HelpAudience.cs` |
| Ceiling endpoint | `Ben.Data.WebApi/Controllers/HelpAudienceController.cs` |
| Documents (markdown) | `Ben.Web.Library/Help/Content/*.md` |
| Loader + renderer | `Ben.Web.Library/Help/HelpContentService.cs` |
| Per-circuit resolver | `Ben.Web.Library/Help/HelpViewerResolver.cs` |
| Index + document pages | `Ben.Web.Library/Help/HelpIndex.razor`, `HelpDocumentPage.razor` |
| In-app badge | `Ben.Web.Library/Help/HelpLink.razor` |
| Styling | `Ben.Web.WebApp/wwwroot/app.css` (`.help-*`) |

## Decisions worth keeping

- **Embedded resources, not `wwwroot`.** A file under `wwwroot` is served raw to anyone who
  guesses its name, which would hand out the administration documents past the audience gate.
- **Front matter that does not parse falls back to `AppAdministrator`**, the most restrictive
  rung. A typo must never publish an internal document.
- **Missing and forbidden are indistinguishable.** Returning "forbidden" would let anyone
  enumerate the administration topics by guessing slugs.
- **Help opens in a new tab.** Several screens hold unsaved work, and an in-page overlay would
  cover the interface the document is explaining.
- **`RoleNames.Admin` grants nothing else.** It was added for this feature only; all 88 existing
  SuperAdmin checks were deliberately left alone. Widening any of them is a separate decision.

## Adding a document

1. Drop a `.md` file in `Ben.Web.Library/Help/Content/` with front matter:
   ```
   ---
   title: Working a Case
   summary: One line, shown under the title in the index.
   section: For Group Members
   audience: OrganizationMember
   order: 40
   ---
   ```
2. Use `##` for the sections that should appear in "On this page".
3. Link to it from the app with `<HelpLink Slug="working-a-case" Anchor="the-case-tabs" />`.

The csproj glob picks the file up automatically.

## What the tests actually catch

Three failure modes here are silent, so each has a test that fails loudly instead:

- A document dropped for malformed front matter — the embedded-resource count is compared against
  the loaded-document count.
- A contents link pointing at an anchor Markdig never emitted — every heading of every shipped
  document is checked against real rendered HTML.
- A `<HelpLink>` pointing at a renamed document or heading — every usage across
  `Ben.Web.Library` and `Ben.Web.WebApp` is resolved by source scan.

Each was confirmed to fail against deliberately broken input before being trusted.
