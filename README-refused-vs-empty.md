# Item 120 — the client cannot tell "refused" from "empty"

## The problem, stated once

`WebApiClient.GetAsync` returns `default` on **any** non-2xx, and 136 call sites in
`BenAdminClientAdapter.*` follow it with `?? []`. A 403, a 500 and a genuinely empty list all
arrive at the component as the same value, and every list surface renders them identically:

> No records available. 0 – 0 of 0 items.

The server refuses correctly. The page reports an empty world. Nothing appears in any log a
person watches.

## Why this is the next thing

It is not a theoretical smell — it is the mechanism behind **every** bug found on 2026-08-20:

| Bug | What the user saw | What was true |
|---|---|---|
| Files tab (item 119) | "This group has no files" | 403; the group had a handbook |
| Members tab (item 119) | "No members" | 403; the group had three |
| Members page (item 122) | "No members", GUID heading | Unauthorised prerender; caller was a SuperAdmin |

Three bugs, one cause. Two were invisible from every seat the suite signed in from, and the third
was caught by a *screenshot*. Fixing the surfaces one at a time has now been done three times.

## What this branch will not do

**It will not rewrite 136 call sites.** That is a mechanical change across every feature in the
product, and a large diff whose testing story is "we clicked around" is how this kind of fix
introduces worse bugs than it removes.

## Approach — to be settled before code

Sketch, in the order the decisions matter:

1. **A result type that can say "I could not load this."** Probably a small readonly struct
   (`items` + `failed` + optional reason) rather than exceptions — exceptions across a Blazor
   Server circuit have their own failure modes, and most call sites want to render something.
2. **A component-level convention** for the three states a list actually has: *loading*,
   *nothing here*, *could not load*. Today only the first two exist, which is the whole problem.
   The equipment tab's empty state is the model to copy — it already distinguishes "no gear yet"
   from "you may not add gear".
3. **Adopt it where it pays.** Start with the surfaces a person is refused from most often:
   org-scoped lists gated on `HasAccessAsync`. Leave the rest on the old path until touched.
4. **A guard** so a new list surface cannot silently join the old pattern.

Sizing and sequencing land here once the shape is agreed.
