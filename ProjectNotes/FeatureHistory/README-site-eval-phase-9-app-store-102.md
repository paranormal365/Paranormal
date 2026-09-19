# Site evaluation 2026-09-06 — Phase 9: App Store 1.0.2

Branch: `feature/site-eval-phase-9-app-store-102`. Plan of record:
`ProjectNotes/Site-Evaluation-2026-09-06.md`, *Phase 9 — App Store 1.0.2*.

## What this phase is

`Ben.iOS/APP-STORE-1.0.2.md` was written on 2026-09-04 and says the submission is *ready, not
sent*. Two evaluation phases landed in the app after that, so every claim in it was a claim about a
tree that no longer existed — and it is the document a reviewer's answers are copied from. This
phase re-verifies it line by line against the tree that would actually be archived.

**The submission itself is Ben's.** The archive, the upload, the App ID capability, the association
file, the demo account on production and the fourteen steps in §6 need his Apple account and his
production site. Everything below is what could be done here.

## Two decisions, and what they cost

**Build 3, not 2.** Build 2 was prepared on 2026-09-04 and, as far as this repository knows, was
never sent — but only App Store Connect knows for certain, and an uploaded number can never be
reused. Ben's call: burn 2 and go up as 3. `CURRENT_PROJECT_VERSION` is 3 in all four
configurations, proven in a bundle built from this tree.

**Both paid-plan sentences softened.** The document had flagged them as the one review risk in the
build: two places said a paid plan exists with no way to buy one in the app. Neither linked
anywhere, named a price or said "buy", but Guideline 3.1.1 has been applied to wording that merely
points at an outside purchase, and a question you never have to answer is better than one you can.

| Where | Was | Now |
|---|---|---|
| `SessionReviewView` | "A paid plan compares this session with theirs — …" | "This account doesn't compare your session with everyone else's here: …" |
| `PaidPlan.WhyCannotKeepPrivateAsync`, shown verbatim by the app | "Keeping your sessions private is part of a paid plan. …" | "Publishing to a place's archive cannot be undone on this account. What you publish stays there — it is what makes the archive worth reading." |

Each still names exactly what it withholds. These sentences were written to a rule — a refusal that
hides the shape of what you are missing teaches people to assume it is nothing — and softening them
must not cost that.

**The website keeps its own words.** The second sentence is the server's and the website shows it
too, so the plan is explained beside it in `MyFieldSessions.razor`: Razor markup no phone ever
renders. That is the whole trick, and it is why the server string could be neutered without making
the website vaguer.

`PaidPlanTests` now asserts the sentence carries its reason **and does not contain the word
"plan"**, so nobody puts it back by accident. The assertion was run against the old wording first
and seen to fail.

## What was re-verified rather than carried

Everything in §3, §4 and §8 of the document was checked against this tree on 2026-09-08, not copied
forward:

- Every `import` in `IsHaunted/` and `BenKit/` is Apple's or BenKit itself; zero remote package
  references; no dependencies in `Package.swift`. The enumeration in §3 had **missed
  `FoundationNetworking`** — Apple's own, behind `#if canImport`, false on iOS, present so BenKit
  compiles off Apple platforms. Named now, because a list offered as exhaustive has to be.
- `PrivacyInfo.xcprivacy`: seven data types, all linked, none for tracking, App Functionality.
- Eight usage strings; `ITSAppUsesNonExemptEncryption` false; both entitlements present.
- No StoreKit anywhere, and now no plan, price, "subscribe" or "upgrade" in any string the app can
  show.
- Microsoft: the app mentions it only to tell an Entra-born account to finish setting up on the
  website. It offers no Microsoft sign-in. §4 item 5 says so now.

## The screenshots did not change, and that is the finding

`FieldKitScreenshotTests` was re-run against a build of this tree on the iPhone 17 Pro Max. Every
frame came back the same screen. Nothing phase 7 changed appears in this set: its untitled-session
fix shows only on a session with no name and these have one; its "no base level was set" line shows
only when no base was taken and this night takes one.

**So the committed set is kept.** Re-capturing would have made it worse: this simulator carries
months of interrupted test sessions that fill the Field Kit list behind the frame, and the clean
run of 2026-09-04 does not.

One thing worth saying out loud in §5, which now does: the six Field Kit frames show a three-tab
bar and the four carried frames show five. That is the app doing what §4 item 7 describes — it
offers only the sections that apply — not an inconsistency in the capture.

## Verification

| check | result |
|---|---|
| `dotnet build Ben.slnx` | 0 warnings, 0 errors |
| `Ben.Web.Tests` | 4,570 passed, 0 failed |
| The new assertion against the old wording | failed, as it must (`Assert.Contains() Failure`) |
| iOS build from this tree | succeeded; `CFBundleShortVersionString` 1.0.2, `CFBundleVersion` 3 |
| BenKit (`Ben.iOS/scripts/test.sh`) | 330 passed, 0 failed |
| `EverySurfaceUITests` on the changed build | 8 of 8 passed |
| Playwright slice on a fresh `IsHauntedDb_p9` | 31 passed, 0 failed, 5 skipped |
| The softened refusal on the wire | `DELETE /api/field-sessions/{id}/publish` → **HTTP 402** with the new sentence, no mention of a plan |

**What I did not see on screen, and why.** The session review's comparison sentence renders only
where a session has archive context and the account is not covered. Reaching that state means
publishing a session to a place from the phone and waiting on the server, which I did not do. What
is proven is that the string changed in place inside an existing branch, that the app builds, that
the surface walk passes, and that no string the app can show mentions a plan. The other sentence —
the one a reviewer is far likelier to meet — was read off the wire as the API returned it.

## Left for Ben

Nothing here can be finished without his Apple account or his production site:

- Associated Domains on the App ID, and the association file live on ishaunted.com.
- **Deploy the website and API from this merge** before a reviewer opens the app — until then the
  live API still answers with the old "part of a paid plan" wording, which is the sentence this
  phase removed.
- `apple@apple.com` verified working and rostered on production the day of submission.
- Archive, upload, + Version 1.0.2, fill the page, select **build 3**, submit.
