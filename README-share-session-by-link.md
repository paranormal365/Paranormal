# Share a session or a piece of evidence by link (item 207)

A client wants to see what was recorded in their house. A producer wants to decide whether a night
is worth sending a crew to. Neither will make an account to look at one thing once — so what
actually happens today is that somebody emails the files, and at that moment the group has lost
every control it had over them. There is no expiry on an attachment, no way to take it back, and no
way to know whether it was ever opened.

This branch adds a link that does all three.

## What it does

- An owner (or an active member of the group running the investigation) makes a link from the
  session's own player page — the page the case's Report Builder already opens with "Play back".
- The link lasts between 1 and 30 days. **Expiry is required**; there is no way to make one that
  never ends, because a share with no end is a public URL with extra steps.
- It can cover the whole session or **one recording** out of it.
- It can be **withdrawn** at any time. That takes effect on the next click, in every inbox it was
  pasted into.
- Every opening is **logged**, so "did they look at it?" has an answer.
- Withdrawn and expired links stay in the owner's list. *Was this ever shared, and when did that
  stop* is a question somebody eventually has to answer.

## The four decisions worth knowing about

**Coordinates do not travel by default.** A session document carries a GPS fix per reading, and a
fix taken indoors is the building's street address. `SharedSessionDocument` nulls every coordinate
before the document leaves, unless the person making the link deliberately turned that off. The
sweep is **by property name over the whole tree**, not down the one path the format documents:
`position` declares `additionalProperties: true`, so a device may write a fix somewhere this code
has never seen, and a redaction that only clears what it was told about fails silently on the first
such document. A document that cannot be parsed cannot be redacted, so it is **refused** rather than
forwarded — the page says the readings cannot be shown, which is a worse page and a correct one.

**The token names a row; the row holds every rule.** Per item 201, nothing token-sized goes in a
URL. The token is 22 characters of base64url over 128 random bits and means nothing anywhere except
in `FieldSessionShareLinks`. It is **random rather than derived** — the opposite of the website's
browser tickets, which are hashes so a URL stays stable across renders. A derived token would come
back the moment its inputs recurred, which is exactly what a revoked link must never do.

**Sharing is narrower than reading.** `MayContributeAsync` lets anybody at all read a *public*
investigation's sessions — that bargain is what makes open investigations worth running. Minting a
link that outlives the visit and reaches people the group has never heard of is a different act, so
`MayShareAsync` is the session's uploader or an active member of the group, and nothing else. An
attendee who was there but is not in the group cannot share somebody else's recordings.

**Unknown, expired and revoked all answer 404.** A distinct "this link has expired" would confirm to
somebody guessing tokens that they had found a real one. The person holding a genuine expired link
is told what happened by whoever sent it.

## Shape

- `FieldSessionShareLink` / `FieldSessionShareLinkView` (migration `AddFieldSessionShareLinks`).
  The view row stores a **salted hash** of the caller's address, never the address — these are
  people with no account who agreed to nothing. The salt is the link's own token, so the same
  visitor on two links produces two unrelated digests.
- `FieldSessionShareController` — four authenticated routes under
  `api/field-sessions/{id}/shares`, two anonymous ones under `api/shared-sessions/{token}`.
- `SharedFieldSessionDetail` is **a separate record**, not `FieldSessionRecord`. It has no field for
  the investigation, the place, the case, the uploader, the publication state or any storage path.
  A shape that cannot carry a thing cannot start leaking it because a query was widened later;
  two reflection tests assert exactly that.
- `FieldKitPlayer` gained a `ShareToken` parameter. In that mode it fetches anonymously, streams
  recordings through `/media/shared/{token}/files/{id}` (no ticket — the share token is the
  authority), suppresses the "nobody signed in when recorded" line, and says plainly that locations
  were withheld rather than letting an empty map read as a fact about the night.
- `FieldSessionSharePanel` is a **section on the player, not a modal** — making a link is a decision
  with four parts and a list to review afterwards. It decides nothing about permissions itself: it
  asks the server for the list, and a refusal means no panel.
- The route is `/s/{token}`, short because this string gets pasted into email and read over the
  phone.

## Two purges had to learn about it

`FieldSessionShareLink.FieldSessionUploadFileId` is `NoAction`, and both alternatives are worse:
`Cascade` gives SQL Server two cascade paths into `FieldSessionUploadFiles` and is refused outright,
and `SetNull` would silently turn a link that reached **one** recording into a link that reaches the
whole night — a privacy escalation performed by a delete. So `OrganizationPurge` and the orphaned
session purge both delete share links before the file rows.

`OrganizationPurgeCoverageTests` and `OrphanedSessionPurgeCoverageTests` caught this the moment the
table existed. Both sweeps name **both** columns, for the reason that guard was written after: an
invariant no constraint enforces is not one to delete on.

## Deliberately not done

- **No notification when a link is opened.** The count and the log are there when the owner looks;
  a notification per view would turn a producer skimming a session into eight alerts.
- **No download of the whole night as a bundle.** The anonymous side is two GETs and nothing else.
- **No navigation off the shared page.** A recipient gets the one thing they were sent; the site is
  not a doorway they have been let through.

## Verification

Unit: 3,988 in `Ben.Web.Tests`, zero failures. 34 of them are new — 10 on the redaction, 24 on the
controller, plus a three-case reachability guard.

Every guard was run against deliberately broken code before being cited:

| Break | Test that failed |
|---|---|
| Coordinate sweep narrowed to `latitude`/`longitude` | a fix in a vendor block survives |
| Unparseable document forwarded instead of refused | the refusal test |
| Revocation check removed from `ResolveAsync` | a withdrawn link still opens |
| Expiry check removed | an expired link still opens |
| Single-file scope removed from the file route | a one-recording link serves the whole night |
| Share authority widened to the public-read door | four authority tests |
| The revoke call site deleted from the panel | the reachability guard |

Live, against a running stack: a 22-character token; anonymous read with no bearer token; the
document arriving with every coordinate nulled and every reading intact; the view logged with a
hash and not the address; withdrawal answering 404 on the next request while the owner's list keeps
the dead row. The shared page rendered in a real browser with its banner and the corrected map
sentence.
