# Photos from a case, on a public page (item #80, part 2b — second half)

This branch closes item #80. It adds the **case-bound slot**: a section type that shows photos
chosen from one of the group's own cases, with the choice stored as references and re-checked on
every request.

## What was already there

Most of the machinery. The prerequisite (`CaseMediaPublication`) already decided *which* of a case's
files may be published — a file qualifies when it hangs off a timeline entry marked `Public`, on a
case that is itself public. Part 4 established the pattern this follows: store ids, resolve at read,
never snapshot.

So the work was the section type, its two-step picker, the renderer — and one thing nobody had
noticed.

## The thing nobody had noticed

**Nothing could serve the bytes.**

`CaseMediaPublication` said a photo on a public timeline entry was publishable. But
`/api/upload-files/{id}/download` gates anonymous callers through
`FileAudienceAccess.CanViewFileAsync`, which for `userId == Guid.Empty` grants exactly two things:
files flagged `IsPublic`, and files with an active share targeting `Public`. A case photo is
neither.

The rule said publishable. The pipe said 401. A gallery pointing at that endpoint would have shown
every visitor a row of broken images.

The reason this would have shipped: **the author would never have seen it.** They are logged in, and
an org member passes `CanViewFileAsync` via the case-linked branch. The editor would have looked
perfect.

### Why not just set `IsPublic`

Because it is two lines and it is wrong. `IsPublic` is global and permanent:

- it outlives the page the photo was put on;
- it survives the timeline entry being pulled back to private;
- it grants the file to *every other endpoint* at the same time.

Publishing one photo on one page would have quietly handed that file out everywhere, for good. That
is the precise opposite of the binding-not-copying rule the rest of item #80 is built on.

### What was done instead

`PublicCaseMediaController` — `GET /api/public/cases/{caseId}/media/{fileId}` — asks
`CaseMediaPublication.MayPublishAsync` per request and streams the bytes only if the answer is still
yes. 404 for both "no such file" and "not published", so a refusal does not confirm which ids are
real. The route carries the case id because "may this file be published" is only answerable in the
context of a case; a bare file id could not have posed the question.

Narrow the entry, unpublish the case, or unlink the file, and the endpoint stops answering — including
for a direct image link somebody copied out of the page.

## The section

`CmsSectionType.CaseMedia`. Stored: a case id, the chosen file ids **in the author's order**, and a
caption switch. Resolved by `CmsEmbed.ResolveCaseMediaAsync` behind two independent gates:

1. **Ownership** — the case must belong to this organization. Publishability alone would have let a
   group decorate its page with another group's public case, which reads as a claim about who did
   the work.
2. **Publishability** — each file re-checked through `CaseMediaPublication`. Files that no longer
   qualify are dropped silently; "there was a photo here you may not see" is itself a disclosure.

**No `IncludeNonPublic` acknowledgement**, unlike part 4's embeds — and that asymmetry is the
design. For a case or investigation the group owns, "publish it anyway" is a real decision somebody
is entitled to make. For an investigator's working file, nobody has ever said it could be shown, so
there is no acknowledgement that would make it acceptable. The single route to publishing a case
file stays the one the prerequisite describes.

**Captions default off and are absent from the payload when off.** A caption is the timeline entry's
title — the group's own working description of what happened — so it is withheld at the server, not
hidden in the renderer.

## Authoring

Two steps, not one: choose the case, then choose from its files. Collapsing them into a single list
of every photo the group owns is how somebody publishes the wrong case's file. Switching cases
clears the selection, because a file id means nothing without the case it came from.

The picker renders thumbnails **through the public media endpoint**, deliberately. The author sees
the photos through exactly the pipe a visitor will, so anything that would arrive broken publicly
arrives broken while somebody is still looking at it.

An empty picker is a normal answer and says so in words — a case with no public timeline entries has
nothing to offer, and an author who picked the right case and saw a blank box would reasonably
conclude the feature was broken.

## Tests

`CmsCaseMediaTests` — 12, all mutation-verified: dropping the ownership gate, trusting the stored
ids, dropping `Distinct`, ignoring `ShowCaptions`, resolving in publishable rather than author order,
and removing `CaseMedia` from `IsEmbed` each fail at least one.

The two that matter most narrow an entry's visibility and unpublish a case *after* the section is
saved, and assert the photos leave the page with nothing being edited. That was this item's stated
requirement.

`ReachableComponentTests.Case_media_can_be_authored_rendered_and_fetched` guards all three halves —
including that the renderer uses the public media URL by name, since pointing it at the ordinary
download URL compiles, passes every resolution test, and looks correct.

## Not done

**Nothing here has been click-tested by a human.** The picker, the thumbnails and the published
gallery have never been exercised in a browser — the standing caveat on all Telerik/Blazor work in
this repo.
