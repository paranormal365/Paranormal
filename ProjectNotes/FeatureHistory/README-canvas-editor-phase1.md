# Case canvas phase 1 — the server half

Branch: `feature/canvas-editor`, cut from `master` at `2ff3def9` (then identical to
`feature/production-deploy-ishaunted`), in the worktree `Paranormal-canvas`.
Plan: `make-a-plan-down-floating-treasure.md` (2026-09-14), milestones M6-01 to M6-13 and
M7-01 to M7-04, with the binding review corrections R1, R2(b), R3, R20, R21, R22, R25, R26 and R28.

The editor itself is a separate tree (`Ben.Web.Website.Library.Manage\Messenger`: Ben.Canvas.Core,
Ben.Canvas.Editor, Ben.Wasm.Canvas). This branch is only what the editor needs from the server,
plus the deploy scripts that put the static host at `/editors/canvas/`.

## Why this phase exists

Ben asked for a board an investigator opens on the case they are working: cards, message boxes,
map boxes, pictures, notes and files, connected with lines, zoomed and panned freely. It runs on
the person's own machine. The server is touched for four things only: load a board, save it,
publish a picture of it to the case, and turn a pasted link into a preview card.

## Decisions, and the reason for each

### Link previews fetch a stranger's page — a deliberate reversal

`PublicLinkPreviewController` says in its remarks that nothing there fetches anything, because a
preview that reads the target page is a request this server makes to an address a stranger chose.
That rule was right for message links, and it still stands there.

**Ben reversed it for the canvas on 2026-09-14**: a pasted https link should become a Twitter/X-style
card with a title, a description and a picture, and only the server can read another site's page
(browsers refuse cross-origin reads). The reversal is made safe, not merely made:

- `SafeUrlPolicy` refuses anything but `https` on port 443 with a DNS name: no user info, no IP
  literals, no `localhost`, no `.local`/`.internal`/`.localhost`/`.home.arpa`, no single-label hosts.
- `SafeUrlFetcher` resolves the name itself, refuses when **any** resolved address is private,
  loopback, link-local, metadata, documentation, multicast or reserved (a mix of public and private
  answers is rebinding bait), refuses IPv6 forms that carry an IPv4 inside (`::ffff:0:0/96`, `::/96`,
  NAT64 `64:ff9b::/96` and `64:ff9b:1::/48`, 6to4 `2002::/16`, Teredo `2001::/32`, discard
  `100::/64`) by testing the embedded IPv4, and then connects **to the address it vetted**, never to
  a second lookup. No proxy (`UseProxy=false`), no cookies, no automatic redirects (each hop is
  vetted again, at most three), a five second budget, a short pooled-connection lifetime, and a byte
  ceiling counted as the body arrives rather than trusted from `Content-Length`.
- The image proxy never passes a third party's bytes through. It reads the image header with
  SkiaSharp, refuses anything over 40 million pixels before decoding (a decompression bomb), and
  answers only with our own JPEG re-encoded through `IMediaSanitizationService` at 800 px.
- Only people who can create a case in at least one group may unfurl. Both endpoints are rate
  limited per person, and the image proxy also has a ceiling shared by everybody, so a thousand
  accounts cannot turn it into a fetch cannon.
- The cache stores what a link said, keyed by a SHA-256 of the full normalised URL, but the stored
  `Url` column has its query string removed: a pasted link can carry a token (`?sig=`, `?code=`)
  and the cache is not a place to keep one.
- The whole of it is behind **Feature — Canvas editor**, which is **off by default**.

### Writes are case-scoped, unlike video projects

A video project is its author's; anybody on the case may read it, only the author may change it.
A board on a case is the case's document: anybody holding Cases Update in the case's group may
save it, which is what "a board the team works on" means. That is why a board carries an integer
`Revision`: two investigators saving the same board must not silently overwrite each other.

- `PUT` requires `If-Match: "<revision>"`. Missing or unreadable → **428**. A stale revision →
  **409** carrying the server's copy, so the editor can offer "Keep mine / Take theirs".
- The check is atomic: `Revision` is an EF concurrency token and the `UPDATE` carries
  `WHERE Revision = @loaded`, so two saves racing between the read and the write cannot both win.
  A read-compare-write in C# alone would let them.
- `Revision` is an `int`, not a SQL `rowversion`, because the browser has to send it back as text and
  show it in a conflict sentence.
- A board with no case is personal: only its author sees or changes it.

### Message HTML is cleaned on the way in

A message box holds rich text. The editor cleans it on paste, but a board can also arrive from an
import, another tab, or a hand-written request. So POST and PUT walk every message node's `html`
through `CmsMarkupSanitizer` before storing: a stored `onerror` never comes back out.

### Publish is a picture that stays inside the case

`POST /api/canvas-documents/{id}/publish` stores a PNG snapshot as an `UploadFile` plus a
`CaseFile`, so it appears on the case's Files tab. Publishing again replaces the previous snapshot.
The picture bakes in real names and addresses, so it is filed under its own upload type,
**Board Snapshot** (`80000000-0000-0000-0000-000000000001`, `IsPublic = false`), and the two
routes by which a case file could reach a visitor both refuse it: a timeline entry holding one
cannot be made Public, and the public-page media rule never offers one.

### The switch

`features.canvas-editor`, label **Feature — Canvas editor**, defaults **off**. While off the API
answers 404 on every canvas and unfurl address (anonymous callers still get 401 first, because
authorization runs before the feature filter).

Found on the way: `FeatureGatedAttribute` read every flag with `whenUnset: true`, so a brand-new
flag with no settings row would have read as **on** and put the unfurl proxy live on deploy. It now
reads the flag's declared default (`SiteSettingKeys.DefaultFor`). Every flag gated before this
branch defaults on, so none of them changed.

### Smaller decisions

- `LinkUnfurlRecord` carries `ImageSourceUrl`, the page's own https `og:image`. The server never
  builds a proxy address; the client builds `{ApiBaseUrl}/api/link-unfurl/image?url=` at render.
- Rate policies: `link-unfurl` 30 a minute per person, `link-unfurl-image` 120 a minute per person
  (a board with forty link cards must open without a 429), plus a shared ceiling on the image proxy.
- The cache keeps a successful answer 7 days and a failure 1 day, so one link costs at most one
  outbound fetch a week.
- MapKit: the token endpoint accepts `?origin=` only for origins listed in
  `Maps:AllowedTokenOrigins`, which is empty in production. It exists so the dev canvas on
  `http://localhost:5125` can show tiles against the website on 5078.
- `StandaloneCanvasAddress` mirrors `StandaloneEditorAddress`: `/editors/canvas/` in production,
  `http://localhost:5125/` in development, `CanvasEditor:StandaloneUrl` wins when set.
- In-body hosting (a Canvas tab on the case page) is deferred to M8.
- Rollout touches production only. `IsHauntedDb_player` belongs to the Mac session and is not
  migrated from this machine.

## Known and separate

The deployed video editor posts sign-in to `"/login"` and handoff to
`"/api/auth/editor-handoff/exchange"` with a leading slash on a base address of
`https://ishaunted.com/webapi`, so those requests land on the website rather than the API
(`Ben.Wasm.Video/Services/AuthService.cs`, `EditorHandoffService.cs`). The canvas host does not
copy the bug. Fixing the video editor is separate work.

## Status

Recorded below as each step lands.
