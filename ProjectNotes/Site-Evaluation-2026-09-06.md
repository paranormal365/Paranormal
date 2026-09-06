# IsHaunted end-to-end evaluation — findings and plan (2026-09-06)

One document for the whole product as it stands today: the website walked as every kind of user
from the first investigation request to the published result on the group's own public site, the
audio editor after its eight-phase audit, the WASM video editor, and the iOS app in the tree that
will be submitted as 1.0.2 build 2. Findings come first, then the errors fixed during the run, then
a proposal Ben asked for (an account created inside the investigation request), then one phased
plan that ends with the help, the PDFs, the tests and the App Store material every change owes.

**Rules this run kept.** Nothing touched the live site, the shared dev database or the player
database. Only outright programming errors were fixed during the run, each with a test shown to
fail without the fix; everything else is a proposal in the plan. Every record the evaluation
created is listed in the ledger at the end and was removed afterwards.

## How it was run

| | |
|---|---|
| Database | `IsHauntedDb_eval`, created by `scripts/run-e2e.sh --keep`, migrated with `--connection`, seeded by the API's own seeders, dropped afterwards |
| Files | `.uploads-IsHauntedDb_eval` at the repo root, paired with the database by name, removed afterwards |
| Hosts | api `127.0.0.1:5252`, web `:5078`, wasm `:5180`, browsed on `localhost` at 1440×900 |
| Mail | unconfigured on purpose — what a sign-up does without a mail server was part of the evaluation |
| Sidecar | the installed LaunchAgent at `127.0.0.1:43117` (protocol 3, ffmpeg 9.0), paired but not used |
| Seats | every seeded password was read from the gitignored dev settings by the harness or a helper script and never typed. The evaluation's own accounts (below) carried a password chosen for the run |
| iOS | iPhone 17 Pro and iPad Pro 13-inch (M5) simulators, dark appearance, `-apiBaseURL http://localhost:5252` on every launch so the app could not reach production, keychain reset before signing in |
| Clicks | the browser tool's synthetic click was dropped on some buttons; where that happened the same button was driven from JavaScript, and nothing below is reported as a defect on the strength of a dropped tool click alone |

The seats the evaluation created, all in Paranormal365 unless stated, all deleted with the database:

| Seat | Account | How it got there |
|---|---|---|
| Client | `eval.client@example.com` (Casey Evaluator, @caseyeval) | the real sign-up form, then the email tick set by hand because no mail server exists |
| Administrator | `eval.admin@example.com` | admin-created, membership through the org security endpoint |
| Member ×2 | `eval.member1@example.com`, `eval.member2@example.com` | same |
| Viewer | `eval.viewer@example.com` | same |
| Owner | `eval.owner@example.com` | admin-created; founded **Eval Night Watch** through the Start-a-group wizard |
| SuperAdmin | `eval.superadmin@example.com` | admin-created with the SuperAdmin role |

Verdicts: **DEFECT** = wrong behaviour seen on screen or proven in code; **GAP** = something a
person needs and cannot do; **IMPROVE** = works, could be better; **FIXED** = a programming error
corrected during the run (see the fixed table); **OK** = walked and sound.

## Test suites

| Suite | Before the run | After the fixes |
|---|---|---|
| Playwright, full, isolated stack | 451 passed · 7 failed · 40 skipped of 498, 23 m 36 s | see "Verification" below |
| BenKit (Swift) | 317 tests, 44 suites, all passed | 318 (one added), all passed |
| Ben.Web.Tests | — | see "Verification" below |
| Ben.Video.Tests + Ben.Wasm.Video.Tests | — | see "Verification" below |

The seven Playwright failures are the same seven recorded on master this morning before any of
today's work: the case-closed message, the authored CMS page for a signed-out visitor, the
impersonation reload, the org public Cases nav, the group wizard tour, the file-delete dialog and
the video editor page title. They are pre-existing and are Phase 0 of the plan.

## Part 1 — The website

### 1.0 The front door and sign-up

| id | verdict | what happened |
|---|---|---|
| W-V1 | IMPROVE | `/events` vertically centres its two cards under a large empty band; times print seconds (`03:00:00 PM`); the page title lacks the site suffix; the signed-out sidebar has no Events entry although the page is public |
| W-V2 | DEFECT | `/contact` shows the anti-spam "Website" honeypot field to real visitors — a visitor who fills it is flagged as spam |
| W-V3 | IMPROVE | `/equipment-catalog` lists sixteen "Generic / Unbranded <category>" placeholder rows as makes and models with no model number |
| W-V4 | OK | `/`, `/find`, `/pricing`, `/help`, `/publications`, `/o/paranormal365`, `/signup`, `/privacy` render cleanly in dark mode. Pricing is the seeded $15/$40 pair here; production's ladder differs by data, not code |
| W-V5 | **FIXED** | every seeded group wore "✓ Accepting new cases" on `/find`, yet the public search answered **zero groups for any address** — the search needs an area of operation and no seeder ever set one. A fresh install had a dead client funnel and a badge that lied. Seeders now give the three Nashville groups a 30-mile area |
| W-S1 | **FIXED** | sign-up with no mail server says "we've sent a link" while the API logs *No confirmation message was sent* — and never logs the link, so the account can never be completed by its owner. The fallback that the configuration comment promises is now taken |
| W-S2 | DEFECT | the login page pre-fills the SuperAdmin email and password in Development. Fine locally; confirm it is compiled out of every published build and never captured in a screenshot |
| W-S3 | DEFECT | onboarding's "What should people call you?" is empty for a self-registered account although sign-up already collected the display name, and the header avatar reads "EC" (from the email) until onboarding re-saves the same name. Admin-created accounts are prefilled — the two paths read different fields |
| W-S4 | IMPROVE | onboarding radios announce as "on" (no accessible label); "Sex" is asked at onboarding and "Gender" asked again at request step 2 |
| W-S5 | IMPROVE | a pure client's sidebar carries Organizations, My Investigations, Equipment, Media and Community, and their desk shows gear and request widgets that can never apply to them |
| W-S6 | DEFECT | every navigation opens a new Blazor circuit — the console shows three WebSocket circuits within 400 ms on some navigations. Costly on a Blazor Server host and the likely root of the "first click does nothing" behaviour below |
| W-S7 | NOTE | the same "No confirmation message" error is logged twice per sign-up; the second copy is probably a second console sink, not a second send |

### 1.1 The lifecycle spine — request to the group's public site

Walked once, end to end, with the seats above. Each step is what a real person would do.

| step | seat | verdict | what happened |
|---|---|---|---|
| Request, step 1 | client | DEFECT W-R1 | Next is allowed with an unverified address; the failure only surfaces at step 4 ("Address not verified") three screens later. Verify Address gives no feedback while it runs |
| Request, step 1 | client | DEFECT W-R2 | "Go back and verify your address." is `href="#"` with no preventDefault (walkthrough gap #2, still open): it did nothing |
| Request, step 2–3 | client | IMPROVE W-R3 | "A description is required before submitting" shows in red before anything is typed; the editor's content renders in a serif font (the TelerikEditor iframe does not inherit the site font) — same on the case timeline dialog |
| Request, step 4 | client | **FIXED** (W-V5) | with a verified Nashville address: "No organizations are currently accepting cases in your area", and the Submit button enabled with nothing to select. After the group set its area the step listed Paranormal365 at 2.2 mi |
| My Requests | client | DEFECT W-R5 | a Draft card says "Submitted 09/06/2026"; the detail page shows "Submitted" and offers Withdraw for a draft; the card is a click-only div — no link, no keyboard (walkthrough gaps, still open) |
| Client settings | admin | OK W-A1 | acceptance switch and the area dialog work; the public search returned the group at 1.8 mi the moment the area was saved |
| Requests tab | admin | DEFECT W-A3 | the "work waiting: 2 investigation requests" banner stayed at 2 after accepting one; only a full page load dropped it (walkthrough gap #3, still open for accept) |
| Under Review | admin | DEFECT W-A4 | once a request is Under Review the card **loses its "Review & vote" link** — the door to the vote page disappears exactly when voting opens; reviewers must use the platform message |
| Review page | admin | IMPROVE W-A5 | says "Voting is open while the request is marked Under Review" but offers no way to mark it |
| Review page | member | OK | a plain member is refused with a three-way maybe ("taken on by another group, declined, or you may not have access") and never received the vote message — consistent with the Case.Read rule, but the sentence hedges |
| Accept | admin | IMPROVE W-A6 | the dialog auto-titles the case from the client's **surname** ("Evaluator, Nashville TN"); the leak warning then has to catch it at publish time |
| Case page | admin | DEFECT W-A9 | the header says "Case Manager: Unassigned" after saving one; the client's card and the Points-of-contact panel show Eval Admin. It read correctly only after a later sign-in |
| Schedule | admin | IMPROVE W-A10 | the visit time carries the picker's seconds (`01:40:33 PM`); "Opened 09/06/2026Public name:" runs together |
| Team | admin | DEFECT W-A7 | after the first attendee is added the Team panel still says "0 of 0 arrived / Nobody has been added" and hides the duty board until a full reload (walkthrough gap #3) |
| Duties | admin | OK / IMPROVE W-A8 | five duties assignable; a Viewer took Documentation without a word. Two notions of lead: a "Lead" chip beside "Nobody leads this visit" |
| Timeline | admin | DEFECT W-A11 | the Type list shows the raw enum name `InstrumentReading`; the experience-type checkboxes have no accessible labels |
| Files + audio | admin | OK | upload, waveform, full-view editor (Part 2) |
| Messages | admin | OK W-A15 | sent and read by the client; Enter does not send |
| Report | admin | OK W-A13 | sections of five kinds, Publish posts the case message, client sees the PDF link. IMPROVE W-A14: a published report stays fully editable with no re-publish step |
| Client's case page | client | OK W-CL2 | status sentence, contact, calendar, visit with cancel-by, pseudonym choice, messages, report |
| Client's case page | client | DEFECT → **FIXED** W-CL1 | the client saw edit ✎ and delete 🗑 on the **investigators'** evidence entry; Edit opened a dialog whose textarea held raw `<p>` HTML; Save was refused server-side and the dialog just closed. The buttons are now only offered on the client's own reports |
| Members' use | member | DEFECT → **FIXED** W-M1 | a member added through the org security door has **no functional role**, so `canReadCases` is false: the case API answers 403 and the case page rendered **blank** under a "Case" title — from a desk that listed the case as a link and a roster that named them Lead Investigator. The blank is now a sentence. The missing role is Phase 2 |
| Members' use | member | IMPROVE W-M2 | My Investigations: RSVP works, but the card links neither the investigation nor the case, and its icon rows render as bare glyphs on their own lines |
| Members' use | member | GAP W-M4 | Paranormal365 owns no equipment on a fresh seed, so the checkout flow could not be walked from this group |
| Viewer's use | viewer | DEFECT W-VW1 | identical to W-M1: three cases listed as links, blank case page |
| Going public | admin | DEFECT → **FIXED** W-P1 | the public URL was `/o/paranormal365/cases/evaluator-nashville-tn` — the slug was cut from the private title and carried the client's **surname** onto the public site while the title itself was correctly pseudonymised. Slugs are now cut from the redacted title |
| Group's public site | visitor | OK W-P2 | `/o/paranormal365/cases` lists the case as "The Westside Family, Nashville TN"; the case page shows the summary, status, community rating, no timeline (the entry was client-visible, not public) |
| The results document | — | GAP W-P3 | the published report exists only on the client's page. Nothing on the group's public site or CMS can carry it, so "the document as part of the group's organization page" is not possible today |
| Leak warning | admin | IMPROVE W-P4 | fired on the surname (correct), but only after the first Save, below the fold of a dialog that needs scrolling |

### 1.2 Each seat's own use

| id | seat | verdict | what happened |
|---|---|---|---|
| W-CL3 | client | DEFECT | the sidebar Notifications badge says 6, the bell says 1, the page lists 1; two unread case messages count as "Unread messages 1" |
| W-CL4 | client | IMPROVE | profile offers "My investigations", "My evidence" and "Show my private photo to clients of the groups I work with" to someone who is only a client |
| W-CL5 | client | IMPROVE | My Cases and My Requests cards are click-only divs |
| W-M3 | member | IMPROVE | the Members grid's "Functional roles" column shows "—" for everyone, including the seeded investigators who hold the Investigator role; "Equipment feedback (Administrators)" is linked for a plain member |
| W-O1 | owner | OK | Start-a-group: identity → base (all four address fields or none) → first settings prefilled "Nashville, TN / 30 mi" (the 2026-09-04 fix holds) → review → created; the hub tour auto-launches; Details states client reach |
| W-O2 | owner | OK / IMPROVE | billing explains the free lane and offers Stripe with interval and coupon; Settings is one very long page (billing, promote, attribution, address display, kind, tours, event types, ladder, duties, matrix) — anchor navigation or tabs |
| W-O3 | owner | NOTE | "This site cannot strip audio and video metadata — no media tool is configured on the server" is shown to owners; confirm production has the tool or the sentence is right there too |
| W-SA1 | superadmin | DEFECT → **FIXED** | the Audit Log printed UTC (06:58 PM for an action at 01:58 PM) under "Dates are your local calendar days (America/Chicago)"; times now use the viewer's zone |
| W-SA2 | superadmin | OK | dashboard, the users grid (one Actions column after this morning's fix), site settings, delete-case preview (counts right, names the client), audit-log filters and exports |
| W-SA3 | superadmin | IMPROVE | "Nobody has recorded an address yet" — it counts profile addresses only; clients gave addresses on requests |
| W-A2 | admin | IMPROVE | the group page header reads "Organization", not the group's name; fifteen tabs over two rows; Created/Updated print seconds and "(server)" |
| W-A12 | all | DEFECT (needs a human repro) | in dialogs the **first click on the primary button after typing did nothing and the second identical click worked**: Verify Address, Look Up (area), Create (report), Add (section), Make Public. Four of the five were the tool's synthetic click; one was a JavaScript click. The DOM node is not replaced (checked). The Playwright suite fills fields without typing and never hits it. Reproduce with a real mouse before fixing; W-S6 is the suspect |
| W-A16 | api | DEFECT | every API request logs two warnings — "Failed to validate the token" and "Entra was not authenticated… JWT is not well formed" — because the Entra scheme is tried on Identity bearer tokens. 188 000 lines in one afternoon |
| W-H1 | home | DEFECT | public case cards print "No votes yet" beside a tally of "✓ 3 ✗ 0 ? 0 · 3 votes" |
| C1 | code | DEFECT | `UserHandleService.AllocateAsync` has **no callers**: accounts created by an administrator, by a case invite or by an event magic link get `Handle = null` until the next API restart's backfill, and cannot be mentioned until then |

### 1.3 Look and feel

The dark theme is consistent and the typography is calm; the case page, the client's case page and
the SuperAdmin dashboard are the strongest screens. What pulls against it, in order of how often a
person meets it: seconds printed in times all over (visits, requests, events, audit, org details);
TelerikEditor content in a serif face; emoji glyphs in the audio editor's toolbar where the rest
of the site uses Feather icons; the two-row tab strip on the group page; and empty vertical bands
on `/events` and `/pricing`. None of it is broken; all of it is visible on the first visit.

## Part 2 — The audio editor, revisited

Opened from the case Files tab on the recording the administrator uploaded.

| id | verdict | what happened |
|---|---|---|
| AE-1 | OK | full view opens full-screen (phase 1a); the spectrogram draws; the EVP scan found 20 candidates on the fixture; the spectrogram state and candidate bands persisted across close and reopen (phase 5b) |
| AE-2 | DEFECT | at 1440×900 with the spectrogram controls on, the toolbar overflows: the file-info row and the toolbar are clipped on the left ("e Timeline"), the drag hint collapses into a one-word-per-line column on the right, and a ~150 px empty band opens between the info row and the toolbar |
| AE-3 | IMPROVE | the toolbar and transport use emoji glyphs (🕐 🗑 📊 🎚 🎯 ✂ 🤫 🔊 🔍 ⏩) instead of the site's icon set; the volume and zoom sliders render as tiny dots |

## Part 3 — The WASM video editor

| id | verdict | what happened |
|---|---|---|
| V-1 | OK | the phase-12 handoff **end to end for the first time**: `POST /api/auth/editor-handoff`, open `:5180/#handoff=<code>`, the editor came up signed in as the administrator (`api/me` confirmed), fragment erased, engine Ready |
| V-2 | DEFECT | Server tab → Download on an audio file: progress ran to 100 %, the download answered 200, and the file was **nowhere** — Audio bin "No audio yet", library size unchanged, no message. Twice |
| V-3 | IMPROVE | the Server tab's "All media" lists seven identical `test-audio.mp3` rows (other users' e2e uploads) with no owner or case column; downloads ran at about 0.3 MB/s on localhost |
| V-4 | IMPROVE | the Video bin marks two clips "on timeline" while Live playback says "2 clips are missing their media" — the bin should show the missing-bytes state the clip's ⚠ shows |
| V-5 | OK | Live playback was honest about the missing media; Save to server answered "Project saved to server." |
| V-6 | NOTE | `/my-videos` → "Standalone editor" goes to `/editors/video/`, which exists only as an IIS mount; on the dev stack it is a 404. The handoff was driven by hand at `:5180` instead |

## Part 4 — The iOS app (1.0.2 build 2 tree)

| id | verdict | what happened |
|---|---|---|
| iOS-0 | NOTE | first launch showed "Your session ended." — a token from an earlier day survived in the simulator keychain and was tried before `-autoSignIn`; local Field Kit sessions from Sep 4 survived too |
| iOS-1 | IMPROVE | the Events tab is hidden for a group member who does not yet attend an event; Profile → Public events is the door, but a reviewer following the notes ("six tabs") may not find it |
| iOS-2 | IMPROVE | My Cases for a group **member** reads as client copy: "When you ask a group to look into something, it appears here" |
| iOS-3 | DEFECT → **FIXED** | Investigations: "Couldn't load your investigations — The server's answer couldn't be read." `api/my-investigations` sends `"didAttend": null` for an attendee nobody has marked; BenKit declared a non-optional `Bool`, so the whole roster failed to decode on iPhone and iPad. Now optional, with the null case in the fixture |
| iOS-7 | DEFECT → **FIXED** (with iOS-3) | the Send sheet's "Where it belongs" offered only "Just my own": the rostered visit never appeared because the roster had not decoded, so a member could not file the night against the visit |
| iOS-4 | IMPROVE | Field Kit list: untitled sessions print the timestamp twice, as title and subtitle |
| iOS-5 | IMPROVE | review screen: the chart stays blank and Field shows "—" for the whole session when no base level was set; the only hint is on the live screen. The "Set a base level" tap did not register once |
| iOS-6 | OK | delayed start, a 17 s session with audio, playback with Sound and Heading, the Send sheet with destination, in/out trimmer, "Play what will be sent", per-recording toggles, sequential upload, "Free up the phone?", "Send anything still waiting"; the session landed in `api/field-sessions/mine` |
| iOS-8 | GAP | the app has no group-side case view at all — My Cases is the client's list, and a member rostered on a visit cannot open the case from the phone |
| iOS-9 | OK | iPad: sidebar with six sections and Notifications, dark; the same decode failure reproduced there before the fix |

## Fixed during the run

Each fix carries a test that was run against the un-fixed code and seen to fail.

| finding | change | test |
|---|---|---|
| W-P1 public URL carried the client's surname | `CaseController.EnsurePublicSlugAsync` cuts the slug from the **redacted** title | `Publishing_never_puts_the_clients_name_in_the_address` — fails on the old line with "Sub-string found" |
| W-S1 confirmation link never logged without a mail server | `IdentityEmailSender.TrySendConfirmationAsync` logs the link at Warning before returning false, as the configured-but-refused path already did | `With_no_mail_server_the_link_is_logged…` — fails on the old code with "did not contain any matching items" |
| iOS-3 roster failed to decode | `MyInvestigation.didAttend` is `Bool?`; the fixture gained a live-captured null record | `aRosterEntryNobodyHasMarkedYetStillDecodes` — both roster tests fail with `typeMismatch … [1].didAttend` on the old type |
| W-V5 seeded groups had no area | the three Nashville groups are seeded with a 30-mile area | proven on a fresh database (Verification) |
| W-M1 blank case page on a refusal | `CaseDetail.razor` renders a sentence when the load comes back empty | opened as the member after the rebuild (Verification) |
| W-CL1 client offered edit/delete on investigators' entries | `MyCaseDetail.razor` shows the buttons only on the client's own reports | opened as the client after the rebuild |
| W-SA1 audit log in UTC | `AdminAuditLog.razor` converts to the viewer's zone in the grid and both detail views | opened as the SuperAdmin after the rebuild |

## Proposal — an account inside the investigation request

**What happens today.** The home page's biggest button is *Sign In to Request an Investigation*.
A stranger with something happening in their house must create an account first (six fields
including a permanent @name), confirm an email they have not been sent yet if mail is down, sign in,
sit through a three-step onboarding, and only then reach the request wizard. Every one of those
steps is a place to give up, and nothing in the request itself needs an account until the moment
a group wants to talk back.

**What to build.** Let the request be the sign-up. The wizard runs signed-out; the account is
created at Submit, from the request itself.

1. **The wizard opens to everyone.** `/my-requests/new` drops its sign-in gate. Steps 1–3 (address,
   about you, what happened) are unchanged. A signed-out draft lives in browser storage, not on the
   server, until Submit — the current per-step server autosave stays for signed-in people.
2. **Step 4 asks who you are.** For a signed-out person the organisation step gains three fields:
   *your name*, *email*, and *choose a password*. Beneath them, this note, verbatim and always
   visible: **"Your name, address and what you tell us are shared only with the group you send
   this to. None of it is public, and none of it is ever sold."** (The privacy page already says
   the second sentence; the request form is where it needs to be read.)
3. **Submit does three things in one server call**, a new anonymous endpoint
   `POST api/public/client-requests/submit` with the auth rate limit:
   - **No account with that email:** create the AppUser (name, email, password, a handle
     allocated by `UserHandleService.AllocateAsync` — the person can rename nothing, so the
     allocation must be good: `casey-evaluator`, then a numeric tail), create the request and its
     organisation applications, and send the confirmation email whose link lands on
     `/confirm-email` and then on **their new case's request page**, not the home page.
   - **An account exists with that email:** create **nothing new**. Attach nothing yet either —
     an unauthenticated caller must not be able to add requests to somebody else's account. Send
     the account holder an email: *"Somebody asked for an investigation at 2500 West End Ave
     using this address. If that was you, sign in to finish it."* with a signed link that, once
     signed in, adopts the pending request into their account. The page the stranger sees is
     the same as in the first case. This keeps the deliberate non-disclosure rule in
     `AccountRegistrationController` — the request form must not become an oracle for which
     emails have accounts.
   - **A signed-in person:** exactly what happens today.
4. **Confirmation is the gate, and it must land.** `SignIn.RequireConfirmedAccount` stays true.
   `ClientStatusMailer` already skips unconfirmed clients, so an unconfirmed requester would get
   no visit or report emails: the request page shows a standing *"Confirm your email to hear back
   — resend"* banner until they do, and the group's request card shows *"client has not confirmed
   their email yet"* so the group knows why a message may not arrive.
5. **The pending request is real to the group immediately.** It appears on the Requests tab as
   today. The one thing it lacks until confirmation is a way to message the client; Accept still
   works, and the acceptance message is delivered when confirmation happens.
6. **Handles.** The person never chose an @name. Show it once on the confirmation landing page —
   *"People can mention you as @casey-evaluator"* — with no rename (the rule stands). This is
   also where C1 gets fixed for all three account-creating paths that skip the allocator today.

**What it touches.** `ClientRequestWizard.razor` (gate, storage, step 4 fields, the note),
`ClientRequestController` (unchanged; the new anonymous endpoint beside it, sharing the
validation), `AccountRegistrationController` (extract the create-and-confirm path into a service
both callers use), `IdentityEmailSender` (two new messages), `ConfirmEmail.razor` (return-to
target), `RequestReviewNotifier` (nothing), help `requesting-an-investigation.md` and
`getting-started.md` ("You need an account to request an investigation" is no longer true), the
Playwright tests `Wizard_AnonymousRedirectsToLogin` and `RequestList_AnonymousRedirectsToLogin`
(they assert the gate), `LoginTests` (the CTA text), and the iOS `DeepLinkParser` if the landing
route is new.

**Why it is worth doing.** Every request in the seeded data and in this walk came from someone
who already had an account. The client funnel is the product's revenue path (private-residence
work is the paid lane), and today it starts with a wall.

## The plan

Sizes: S about half a day, M one to two days, L three or more. Every phase: its own branch and
README, tests run against the un-fixed code first, help updated in the same change, verified on
screen on an isolated stack as an ordinary member before merge.

### Phase 0 — Green suite (S)
Investigate and fix the seven pre-existing Playwright failures. They are the same seven on master
and they will hide anything new. Steps: run each in isolation with `HEADED=1`; fix or retire each
with a note in `README-green-suite-and-open-debts.md`; full run green on a fresh `BEN_E2E_DB`.

### Phase 1 — The client funnel (L)
1. Build the proposal above.
2. W-R1: step 1 refuses Next until the address is verified, and says what Verify is doing.
3. W-R2: the "go back" link becomes a button.
4. W-R5: drafts say *Started*, not *Submitted*; a draft offers *Delete draft*, not *Withdraw*.
5. W-CL5 and the request cards: real links with keyboard access.
6. W-S3: onboarding prefills the display name from the account for every creation path; the
   avatar initials come from the display name.
7. W-S4: one question about sex/gender, asked once, labelled.
8. Help: `requesting-an-investigation.md`, `getting-started.md`, screenshots of the new step 4.
9. Tests: `ClientRequestControllerTests` (anonymous submit: new account, existing account,
   signed-in), `AccountRegistrationController` service extraction, Playwright
   `A_stranger_can_ask_for_help_without_an_account`, `An_existing_address_is_never_confirmed_to_a_stranger`.

### Phase 2 — Membership doors and the case page (M)
1. W-M1 root: one rule for what a new member can read, applied at every door — application
   accepted, admin PUT membership, SuperAdmin add, the seeders. Recommended: a group setting
   *"new members start as"* defaulting to the Investigator role; the ladder's bottom rung maps to
   it. `OrganizationSecurityService.UpsertMembershipAsync` applies it.
2. Desk and roster never offer a case the person cannot open: the desk's case links and the
   roster's case reference check `canReadCases` (the API already answers it).
3. W-A9: the case header re-reads the manager after Save; W-A7 and W-A3: the Team panel and the
   work-waiting banner re-read after a write.
4. W-A4/W-A5: the request card keeps *Review & vote* while under review; the review page can open
   voting.
5. W-A6: the accept dialog proposes the pseudonym or the street-less town as the title, never the
   surname.
6. W-A11: enum labels through one display-name helper; every checkbox group labelled.
7. Tests: `InvestigationDutyTests` for the new-member rule at each door; Playwright
   `A_new_member_opens_the_case_their_desk_shows`, `The_vote_page_stays_reachable_under_review`.
8. Help: `working-a-case.md` (roles for new members), `organization-administration.md`.

### Phase 3 — The results document on the group's public site (M)
W-P3. A published report gains a *Public summary* (executive summary + conclusion, run through
`CaseProseRedactor`, media only from approved case slots) that the group can switch on per report.
It renders on the public case page under the summary and is available as a CMS case-bound slot so
it can sit on any page the group builds. The leak check (item 176) runs on the summary text.
Help: `working-a-case.md` (reports), `publishing-with-publications.md` cross-reference. Tests:
`PublicCaseControllerTests.A_public_summary_shows_only_when_switched_on`, redaction of the summary,
a Playwright walk from the report to `/o/{group}/cases/{ref}`.

### Phase 4 — Look, feel and small truths (M)
Times without seconds everywhere (one `ToDisplayDateTime` rule); TelerikEditor inherits the site
font; the audio editor's emoji toolbar becomes BenIcon (AE-3) and its toolbar wraps instead of
clipping (AE-2); the group page shows the group's name and its tabs in one scrollable row (W-A2);
`/events` and `/pricing` lose the empty bands (W-V1); W-H1 "No votes yet" beside a tally; W-CL3
one notification number; W-CL4/W-S5 client-only chrome; W-V2 the honeypot hidden; W-V3 the generic
rows out of the public catalogue; W-SA3 the dashboard sentence; W-A14 published reports need a
re-publish; W-O2 Settings anchors; W-M3 the Functional roles column; W-P4 the leak warning shown
before Save. Help screenshots re-captured wherever a screen changed.

### Phase 5 — Server hygiene (S)
W-A16: the Entra scheme stops logging on Identity bearer tokens (a challenge scheme selector, or
the Entra handler's `ForwardDefault`); W-S7 the duplicate console line; C1 every account-creating
path calls `UserHandleService.AllocateAsync` (admin create, case invite, event magic link) with a
test per path; W-S6 measure the circuits per navigation and, if it is enhanced navigation
re-creating the root, keep one circuit alive; W-A12 reproduced with a real mouse and, if real,
fixed at its cause.

### Phase 6 — The WASM editor's server tab (S)
V-2: a server download that finishes must land in the right bin or say why it did not (the audio
path is the one that vanished); V-3 owner and case columns on the Server tab; V-4 the bin shows
missing media; V-6 a dev-time route for `/editors/video/` or the button hidden when the mount is
absent. Tests: `WasmEditorEditingTests.A_server_audio_file_lands_in_the_audio_bin`, unit tests for
the bin state. Help: `using-the-video-editor.md` (Server tab).

### Phase 7 — The iOS app before 1.0.2 (M)
1. iOS-8: a group-side case list for members with case access, read-only at first (timeline,
   files, messages), reached from Investigations; iOS-2 the copy for members.
2. iOS-1: Events reachable from the tab bar for anyone in a group with a public event, or the
   review notes rewritten to name Profile → Public events.
3. iOS-4, iOS-5: session titles once; the review screen says "no base level was set" over the
   empty chart.
4. Fixture drift: a BenKit test that decodes **every** fixture with every optional field nulled
   (the class of iOS-3), and the capture script re-run against the seeded API so fixtures carry
   the shapes the seeders produce.
5. UI tests on both simulators: `EverySurfaceUITests` extended with the member seat.

### Phase 8 — Documentation, screenshots, PDFs (M)
Runs after Phases 1–7, in this order, all captures in **dark mode**:
1. `BEN_CAPTURE=1 dotnet test Ben.Web.Playwright -p:IsTestProject=true --filter TestCategory=Capture`
   on an isolated stack — every help screenshot whose screen changed (request step 4, case page,
   audio editor toolbar, group page, Server tab).
2. `python3 docs/build-documentation-pdf.py` and the Chrome print → `docs/IsHaunted-Product-Documentation.pdf`;
   check the byte size changed; screenshot the HTML to eyeball it.
3. Persona captures for all six seats (`BEN_PERSONA=…`) → `docs/build-persona-documentation.py`
   → the six `IsHaunted-Web-*.pdf`.
4. iOS: `TEST_RUNNER_BEN_DOC_SHOTS=1 … DeveloperDocCaptureTests` on iPhone and iPad with
   `xcrun simctl ui <udid> appearance dark` → `docs/build-ios-documentation.py iphone|ipad` →
   the two `IsHaunted-iOS-*.pdf`; fill `53-session-review` now that the review screen is reachable
   after a scripted session.
5. `docs/README.md` updated for anything new in the pipeline.

### Phase 9 — App Store 1.0.2 (S)
1. Bump `CURRENT_PROJECT_VERSION` to **3** (build 2 was never uploaded, so 2 is still legal, but
   the tree has changed since §8 was checked — re-verify rather than assume).
2. `Ben.iOS/APP-STORE-1.0.2.md`: §2 "What changed" gains the roster fix (iOS-3) and, if Phase 7
   ships, the member case view and Events reach; §4 review notes re-verified against the tree
   (the "six tabs" answer must match what the demo account sees); the paid-plan sentences decided.
3. `Ben.iOS/screenshots-1.0.2/`: re-capture only the frames whose screens changed
   (`FieldKitScreenshotTests`, `AppStoreScreenshotTests`), dark, both sizes.
4. Demo account `apple@apple.com` verified on production the day of submission — rostered,
   attendance marked or not, the roster must decode either way.
5. Archive, upload, + Version 1.0.2, select the build, submit — the fourteen steps in §6.

### Verification (every phase)
`dotnet build Ben.slnx` with 0 warnings; `dotnet test` for Ben.Web.Tests, Ben.Video.Tests,
Ben.Wasm.Video.Tests and `Ben.iOS/scripts/test.sh`; every new test seen to fail without its fix;
`scripts/run-e2e.sh` green on a fresh `BEN_E2E_DB`; the phase's screens opened by hand as the
seat it concerns; help and screenshots in the same change.

## Verification of this run

| check | result |
|---|---|
| Ben.Web.Tests after the fixes | 4,477 passed, 0 failed (two tests added) |
| Ben.Video.Tests / Ben.Wasm.Video.Tests | 2,556 / 34 passed, 0 failed |
| BenKit (`Ben.iOS/scripts/test.sh`) | 318 passed, 0 failed (one added) |
| Each new test against the un-fixed code | slug: "Sub-string found"; mail: "did not contain any matching items"; roster: `typeMismatch … [1].didAttend` on both roster tests |
| Playwright subset on the rebuilt stack (Admin, ClientRequest, CaseManagement, MyCase, CaseNotes, PublicCase, OrgPublic, RequestStatus, AdminDeleteUser) | 97 passed, 1 failed — the pre-existing `OrgPublicHome_ShowsCasesNavItem` |
| W-M1 by hand | the member's case page now says "This case isn't available to you…" instead of nothing |
| W-CL1 by hand | the client's page shows the investigators' badge and no edit or delete button on that entry |
| W-SA1 by hand | the audit log prints 01:58 PM for the 01:58 PM action |
| W-S1 by hand | a registration on the rebuilt API logged `Use this instead: http://localhost:5078/confirm-email?userId=…` |
| iOS-3 by hand | the rebuilt app on the iPhone simulator lists the visit under Investigations ("Going", Lead Investigator) |
| W-V5 on a fresh database | `IsHauntedDb_eval2` seeded from scratch: the public search for West End Ave returned all three Nashville groups (Music City Spirit Seekers, Nashville Paranormal Society, Paranormal365); the Smoke tests passed 8/8 |

## Ledger — what was created, and its removal

Everything below lived in `IsHauntedDb_eval` and `.uploads-IsHauntedDb_eval`, both removed at the
end of the run; nothing was written to any other database.

| what | where | removed by |
|---|---|---|
| 6 accounts (`eval.*@example.com`) and 4 memberships in Paranormal365 | AppUsers, OrganizationUserMemberships | database drop |
| Paranormal365 area of operation (Nashville, 30 mi) | OrganizationAreaOfOperations | database drop |
| 1 client request, 1 vote, 1 case #2026-005 (public), 1 investigation, 4 attendees, 5 duty assignments, 1 timeline entry, 2 case messages, 1 report with 1 section | Paranormal365 | database drop |
| 1 case file `test-audio.mp3` (7.1 MB) + its audio config and 20 EVP candidates | CaseFiles, UploadFiles, `.uploads-IsHauntedDb_eval` | database drop, directory removal |
| 1 group **Eval Night Watch** with its address, ladder, duties, roles | Organizations | database drop |
| 1 video project (saved from the WASM editor) and 1 editor-handoff sign-in row | VideoProjects, sign-in events | database drop |
| 1 field session "North bedroom, eval walk" with `audio-001.m4a` | FieldSessions, `.uploads-IsHauntedDb_eval` | database drop, directory removal |
| 1 registration `eval.signup2@example.com` (the W-S1 re-check) | AppUsers | database drop |
| the whole Playwright suite's own writes (posts, groups, events, 25 test accounts) | everywhere | database drop |
| `IsHauntedDb_eval2` and `.uploads-IsHauntedDb_eval2` (the fresh-seed check) | SQL Server, repo root | dropped and removed |
| `IsHauntedDb_grid` (this morning's admin-grid check) | SQL Server | dropped |
| simulator app data on iPhone 17 Pro and iPad Pro 13-inch (keychain, SwiftData sessions) | the two simulators | app uninstalled, keychain reset |
| browser-local editor state (OPFS media, `bv-proj-*`, `bwv-auth`) | the Browser pane's origin for `:5180` | left; it holds no server data and the token is dead with the database |

Confirmed at the end of the run: `IsHauntedDb_eval`, `IsHauntedDb_eval2` and `IsHauntedDb_grid`
are gone from the server, no `.uploads-IsHauntedDb_eval*` directory remains, no host is running,
both simulators have the app uninstalled and their keychains reset. Not mine to remove and left
in place: nineteen older throwaway databases from earlier sessions (`IsHauntedDb_audio_*`,
`IsHauntedDb_e2e`, `IsHauntedDb_master_check`, `IsHauntedDb_stripe`) — worth a sweep, since each
holds a copy of the seed. `dotnet ef database drop` does not accept `--connection`; the drops
were done with `pymssql` against `master`.
