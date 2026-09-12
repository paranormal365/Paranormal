# Universal links + a web manifest (item 209)

A link to the site opens the app on a phone that has it, and the site installs cleanly on a device
that does not.

## The part that decided the design

The app already had a `DeepLinkParser` that understands website URLs, so the app-side routing was
mostly done. The real work was deciding **which paths the association file may claim** — and the
answer is narrower than the parser's grammar, for a reason that is easy to miss.

**Claiming a path the app cannot render is worse than claiming nothing.** The link leaves Safari,
where the real page is, opens the app, and shows whatever the app can make of it. Two paths parse
perfectly well and then land on the router's `default:` arm, which renders a **"Coming soon"
placeholder**:

| Path | Parses to | Renders |
|---|---|---|
| `/events/{id}` | `.eventDetail` | placeholder — no case in `RootShell.destination` |
| `/organizations/{org}/cases/{case}` | `.orgCase` | placeholder — same |

A third, `/attending/{token}`, parses the RSVP token and the router then **throws it away** and
opens the events list; its own comment says the flow stays on the website until an association file
exists. Claiming it would silently lose somebody's RSVP.

So the claimed list is nine patterns, each of which reaches a real view:

```
/feed  /feed/*  /events  /my-cases  /my-cases/*
/my-investigations  /notifications  /profile  /validate-email/*
```

## Two smaller decisions

**No exclusions, and therefore no ordering question.** Apple's component matching evaluates the
array in an order that is easy to get subtly wrong, and wrong here fails silently on a stranger's
phone. Rather than claim `/events/*` and exclude the detail route, `/events` is claimed exactly and
`/events/*` simply is not claimed. The document contains no `exclude` key at all — a test asserts
that.

**Endpoints, not files in wwwroot.** The association file has no extension, so static middleware
has no content type for it, and iOS requires `application/json` over HTTPS with no redirect in
front. Building both documents in C# also means the app identifier and the site name come from
configuration rather than from a JSON blob nothing validates. There is precedent: `/build-info.json`
is an endpoint for a closely related reason.

## Shape

- `AppleAppSiteAssociation.cs` — the claimed list, and `UnclaimedPaths` carrying **the reason for
  every omission as data**, so a test can assert each is absent and a future author finds the
  reasoning rather than "tidying up" the gap.
- `WebAppManifest.cs` — built from `SiteIdentity`, because a name baked into a manifest is read by
  an *installer* rather than by a page and goes stale unseen. `ShortNameFor` drops a domain suffix.
- Two endpoints in `Program.cs`; `<link rel="manifest">`, `theme-color` and the two capability
  metas in `App.razor`. The colour comes from `WebAppManifest.ThemeColor` rather than a literal —
  two copies drift, and this drift shows as the browser chrome flashing a different shade on every
  launch.
- `IsHaunted.entitlements` gains `com.apple.developer.associated-domains` for `ishaunted.com` and
  `www.ishaunted.com`.

## Verification

Unit: **4,032** in `Ben.Web.Tests`, zero failures — 24 new. Swift: 5 new in `BenKitTests`, run and
passing. Playwright: 4 new, **run against a real host and passing** (they need no credentials).

Guards proven against broken code:

| Break | Test that failed |
|---|---|
| `/events/*` added to the claimed list | 4 tests, including the one naming that exact path |
| `/*` added | `No_claimed_path_swallows_the_whole_site` |
| Association served as `text/plain` | the Playwright content-type test |

The short-name rule was itself caught by its own test: the first version capped a domain suffix at
four characters and would have kept `.paranormal` while stripping `.com`. It now asks whether the
whole name looks like a domain — whitespace is the reliable signal.

Live, against a running host: the association file returns 200 as `application/json` with nine
paths and no `exclude`; the manifest returns 200 as `application/manifest+json`; all three icons
resolve; and a real browser parses the manifest from the page's `<link>` with the right name, short
name, display mode and theme colour.

## What is left, and it is not code

**The entitlement needs a signed build, and none of it can be checked in the simulator** — iOS only
performs the association check on a real device with a real provisioning profile. The sequence is:

1. Deploy the site so `https://ishaunted.com/.well-known/apple-app-site-association` is live. It
   must be reachable **before** the app is installed; iOS caches the result.
2. Enable **Associated Domains** for the App ID in the developer portal, so the profile carries it.
3. Ship a build after 1.0 clears review — the entitlement changes the provisioning profile.

Until step 3 the entitlement is inert and nothing regresses: an app without it simply never claims
any link, and every URL opens the website as it does today.

**Deliberately not done:** the `webcredentials` service for password autofill. It needs its own
entitlement, and adding the key without it would claim a capability the app does not have.
