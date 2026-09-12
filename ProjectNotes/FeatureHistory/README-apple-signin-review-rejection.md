# Sign in with Apple returned to the login page (App Review 2.1(a), build 4)

Branch: `fix/apple-signin-profile-sheet`, from `master`.

## What Apple said

> **Guideline 2.1(a) — Performance, App Completeness.** *"We were unable to access the app because
> the app came back to login page when we logged in with Apple."*
> Reviewed 2026-09-12, iPhone 17 Pro Max, iOS 26.6.2, version 1.0.2 (4).

## What was actually wrong

An Apple identity nobody here has yet cannot become an account without a display name and a
permanent @name, so the server answers `409 NeedsProfile` and the app opens the "Almost there"
form. It never opened.

`AppleSignInSection` attached that form's `.sheet` to the `Section` it draws. The section's own
rows change in the same instant the sheet is asked for — the in-flight spinner is inserted when the
request starts and removed as the answer arrives, and `defer { busy = false }` runs immediately
after `collecting = true`. SwiftUI resolves that by tearing the presentation down **and dismissing
the sign-in sheet that contains it**. No form, no error, no explanation: a signed-out profile,
which is exactly what the reviewer described.

Two things kept it hidden:

- Sign in with Apple cannot complete on an unprovisioned simulator, so nobody had ever run the
  flow past Apple's own sheet. The App Review tester was the first person to do it.
- `AppleSignInUITests` asserts the button exists and is hittable. It always was.

## Proof, not inference

Reproduced twice on an iPhone 17 Pro Max simulator, iOS 26.5 — the same device family as the
review — on 2026-09-12:

1. A standalone SwiftUI probe with the same shape (sheet on a `Section`, spinner row toggling
   around the flip, the whole form inside another sheet) dismissed the outer sheet and presented
   nothing. The same probe with the sheet hoisted to the form presented correctly. So did a variant
   with no row churn, which is why this looked fine in isolation and failed in the real screen.
2. The shipped `AppleSignInSection`, driven in the real app by a temporary button that reproduced
   the `409` branch without Apple, dismissed the sign-in sheet and returned to the signed-out
   Profile. With the fix in place and the same button, "Almost there" opens over the sign-in sheet
   with Apple's name prefilled. The temporary button exists in neither commit.

## The change

`Ben.iOS/IsHaunted/Features/Auth/AppleSignInSection.swift`,
`Ben.iOS/IsHaunted/Features/Auth/SignInView.swift`:

1. **`AppleSignInFlow`** — an `@Observable @MainActor` object holding everything the flow remembers
   between Apple's sheet, the server and the profile form. `SignInView` owns it and presents the
   form from its own navigation stack, so no section's rows can take the presentation down. The
   section is now only a button, a spinner and a message.
2. **A sign-in that does not end signed in no longer closes the door.** The old code adopted the
   token, then dismissed the sign-in form whatever `api/me` answered — so a session that died
   between the two produced the same silent "back to the login page". Dismissal now happens only
   when `SessionStore` really reaches `signedIn`; every other outcome writes a sentence where the
   person is looking (under the form when it is open, on the sign-in screen when it is not).

Nothing about the server changed, and nothing about the flow's rules changed: the two doors, the
permanent handle, the Hide My Email case and Apple's one-and-only offer of the real name all behave
as they did.

`CURRENT_PROJECT_VERSION` 4 → **5**. An uploaded build number is never reusable, even after a
rejection.

## What is left for Ben

- Archive, upload build 5, select it on the 1.0.2 page, reply in Resolution Center, resubmit.
  `Ben.iOS/APP-STORE-1.0.2.md` §1a has the wording and §6 the procedure.
- Nothing else in the package is affected: same listing, privacy answers, screenshots, previews,
  demo account and server.
