# Site evaluation 2026-09-06 — Phase 1: the client funnel

Branch: `feature/site-eval-phase-1-client-funnel`. Plan of record:
`ProjectNotes/Site-Evaluation-2026-09-06.md`, section *Proposal — an account inside the
investigation request* and *Phase 1 — The client funnel*.

## The problem

Every request in the seeded data and in the evaluation walk came from somebody who already had
an account. The home page's biggest button is *Sign In to Request an Investigation*: a stranger
with something happening in their house must create an account (six fields including a permanent
@name), confirm an email, sign in and sit through onboarding before reaching the wizard. Nothing
in the request needs an account until a group wants to talk back. The client funnel is the
product's revenue path and it starts with a wall.

## What this phase builds

1. **The request is the sign-up.** `/my-requests/new` opens to everyone. Steps 1–3 are unchanged.
   A signed-out draft lives in the browser (localStorage) until Submit; signed-in people keep the
   per-step server autosave. Step 4 gains *your name*, *email* and *choose a password* for a
   signed-out person, under the note, always visible, verbatim:
   > Your name, address and what you tell us are shared only with the group you send this to.
   > None of it is public, and none of it is ever sold.
2. **One anonymous server call**, `POST api/public/client-requests/submit` (auth rate limit):
   - *No account with that email:* create the account (handle allocated by
     `UserHandleService.AllocateAsync`), create the request as Submitted with its organisation
     applications, send the confirmation email whose link lands on `/confirm-email` and then on
     the new request's page.
   - *An account exists:* create nothing on that account. The request is parked in a new
     `PendingClientRequests` table keyed by an unguessable secret (at most three waiting per
     address, so the form cannot be aimed at somebody's inbox), and the account holder is
     emailed *"Somebody asked for an investigation at … using this address. If that was you, sign
     in to finish it."* The link, once signed in as that address, adopts the parked request into
     their account (or discards it). The stranger sees the same page either way — the form is
     never an oracle for which emails have accounts (`AccountRegistrationController`'s rule).
   - *A signed-in person:* exactly what happens today.
3. **Confirmation is the gate and it must land.** `RequireConfirmedAccount` stays. The group's
   Requests tab says *client has not confirmed their email yet* so a silent message is explained.
   The confirmation landing page names the request's state (waiting / accepted by …) and shows the
   allocated @name once — *People can mention you as @…* — with no rename.
4. **C1**: every account-creating path that skipped the allocator (case invite, event magic link,
   Entra first sign-in, administrator create) now allocates a handle at creation.
5. **W-R1** step 1 refuses Next until the address is verified and says what Verify does.
   **W-R2** the "go back" link is a button. **W-R5** drafts say *Started*, and a draft offers
   *Delete draft* (new `DELETE api/client-requests/{id}`, Draft only), not *Withdraw*.
   **W-CL5** My Requests and My Cases cards are real links. **W-S3** onboarding prefills the
   display name for every creation path and the header initials come from it. **W-S4** one
   labelled gender question, prefilled from the profile so it is asked once.
6. Help: `requesting-an-investigation.md` gains "you do not need an account first", the address
   verification, the privacy promise in full, confirming, the already-had-an-account path and
   drafts; `getting-started.md` stops saying an account is needed to ask for help. The help
   screenshots are **not** re-shot here — the plan's phase 8 re-shoots every one of them in dark
   mode after the screens settle, and shooting step 4 twice is waste.
7. Tests: `PublicClientRequestControllerTests` (new account, existing account, the refusals and
   their ordering), `ClientRequestControllerTests` (delete draft, adopt, discard, the wrong key,
   the wrong account, an expired row), a handle assertion on each creation path, Playwright
   `A_stranger_can_ask_for_help_without_an_account`, `An_existing_address_is_never_confirmed_to_a_stranger`,
   `A_stranger_reaches_the_form_without_signing_in`, `Step_one_will_not_advance_on_an_unverified_address`.
   `AccountCreationService` is covered through its two callers rather than directly.

   Retired or corrected because they asserted the wall this phase removed:
   `Wizard_AnonymousRedirectsToLogin` (gone; the list's own gate stays, since My Requests still
   needs an account), the two "click Next with nothing filled in" tests (Next is disabled now, and
   clicking a disabled button waits out its timeout), and `ClientRequestWizard.razor`'s entry in
   `NavigationIsAnAnchorTests` (its navigating buttons became anchors, and that guard said so).

## Key files

- `Ben.Data.Source/Entities/BenDataModel.PendingClientRequest.cs` + migration
  `AddPendingClientRequests` (**reaches the live database only at deploy — Ben runs it**)
- `Ben.Data.WebApi/Services/AccountCreationService.cs` — the create-and-confirm path, extracted
  from `AccountRegistrationController` so both callers share it
- `Ben.Data.WebApi/Services/ClientRequestRules.cs` — the submit validation both endpoints use
- `Ben.Data.WebApi/Controllers/Public/PublicClientRequestController.cs`
- `Ben.Data.WebApi/Controllers/Entities/ClientRequestController.cs` — delete draft, pending
  adopt/discard
- `Ben.Web.Website.Library/Client/ClientRequestWizard.razor`, `ClientRequests.razor`,
  `ClientRequestDetail.razor`, `AdoptClientRequest.razor` (new)
- `Ben.Web.Website/Components/Pages/ConfirmEmail.razor`, `Ben.Web.Website.Library/Shared/HomeHero.razor`
- `Ben.Web.Services/Help/Content/requesting-an-investigation.md`, `getting-started.md`

## Not in this phase

The iOS `DeepLinkParser` is untouched: the new routes (`/my-requests/adopt/…`) have no app screen
and the app's claimed universal-link paths are deliberately narrower than the parser.

## Verified

| check | result |
|---|---|
| `dotnet build Ben.slnx` | 0 errors, 0 warnings |
| Ben.Web.Tests | 4,496 passed, 0 failed (19 added) |
| Each new test against the un-fixed code | existing-account path made to write to the account: 3 failed; handle allocation removed: 4 failed across the four creation paths; validation order inverted so a weak password is refused only for a new address: 1 failed; the parked row's email check, expiry, secret check, delete-draft status guard and story-withholding each removed in turn: 5 failed |
| Playwright `ClientRequests` on an isolated stack | 9 passed, 0 failed, 0 skipped — the signed-out funnel ran the whole way through, so the account really was created from the request |
| Playwright `ClientRequestNav`, `MyCases`, `Account`, `OnboardingJourney`, `Navigation` | 48 passed, 1 skipped, 0 failed |
| The whole Playwright suite on the same stack | 453 passed, 8 failed, 40 skipped in 24 minutes. Seven of the eight are the pre-existing set recorded in `ProjectNotes/AudioEditor-Audit-2026-09-06.md`; the eighth is below |
| `MyCaseDetail_ShowsCaseTitle` | **Broke on this phase's change and was fixed, not excused.** It read the page body once instead of waiting for it. That passed only because the My Cases card used to be a div whose `@onclick` could not fire until the SignalR circuit was live, so the app was always warm by the time the detail page loaded; a real anchor navigates on the first click, and the snapshot began landing before the case had rendered. It now uses the same auto-waiting assertion as every sibling test in its file, and passed three runs in isolation plus a 40-test run of the three affected categories |
| On screen, signed out | `/my-requests/new` opens to step 1 with no sign-in panel; Next is disabled and says "Verify the address to continue" until the lookup succeeds |
| On screen, adopting | the emailed link lands on sign-in first; signed in, the page shows the name, address and group but not the story, and "Yes, this is mine" produced a Submitted request with its application |
| On screen, drafts | the list says *Started*, the detail page offers *Edit & Submit* and *Delete draft* and no *Withdraw*, and deleting removed it |
| On screen, the group's side | the two anonymous submissions carry "The client hasn't confirmed their email yet"; the seeded request does not |
| On screen, W-S3 | a self-registered account reaches onboarding with its name already in the box and "SC" in the header, not the initials of its email address |

## Data created while verifying

All of it in the throwaway `IsHauntedDb_p1` and `.uploads-IsHauntedDb_p1`, both removed at the end:
two accounts from the Playwright funnel test, one `phase1.screencheck@example.com` account with a
membership and role inserted by hand, two parked requests, one adopted request and one deleted
draft. Nothing was written to any other database.
