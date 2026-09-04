# Case lifecycle walkthrough — request to report

**Run:** 2026-09-04, two passes, driven through a real browser.
**Isolation:** a throwaway database `IsHauntedDb_walk` and its own uploads directory, with the API,
website and WASM hosts started against it. Nothing touched the live site, the shared dev database,
or the player database.
**Cleanup:** hosts stopped, database dropped, uploads directory removed, all verified afterwards.
The four pre-existing databases were checked and are intact.

---

## What was exercised

**Pass 1 — the happy path, end to end.** Register a client → onboarding → submit a request →
register an owner → create a group → configure it for client work → the request finds the group →
accept → case created → schedule an investigation → add an attendee → assign a duty → write a
timeline entry → build and publish a report → read the whole thing back as the client.

**Pass 2 — the decline branch.** Second request → group declines → client is offered another
organization → the picker correctly reports there is nobody else.

Every account used was created through the sign-up form. No seeded persona credential was typed
into the browser.

---

## What works, and works well

- **Onboarding routes by intent.** "Something is happening at my place" dropped the client straight
  into the request wizard; "I run a group" dropped the owner straight into group creation. This is
  the best part of the first-run experience.
- **The spine is solid.** Request → distance match → accept → case, with the client's own words and
  address carried across verbatim into the case and its Original Request panel.
- **Address verification is real.** An invented street failed to geocode and **Submit stayed
  disabled**, with a link back. A real address verified and matched the group at 0.3 miles.
- **The paid-lane gate reads beautifully.** Before the group had a subscription the client was told,
  in plain words, that nearby groups only investigate public places and could not take a case at a
  home — with a checkbox to see them anyway. That is exactly the right shape for a refusal.
- **Item 160 is live and correct.** A brand-new group came up with the Associate → Lead Investigator
  ladder, five duties including the split Equipment / Equipment Assist, and the matrix seeded to the
  worked example. Assigning the Lead Investigator duty to a titleless member produced the exact
  sentence the resolver is written to produce, with **Assign anyway** working and marking the row.
- **The client's case page is the strongest screen in the product.** Status in plain English, the
  visit with a cancel-by deadline, shared access, pseudonym choice with suggestions, the message
  board carrying the automatic "your report is published" note, and the report with a PDF link.
- **The privacy boundary held.** A timeline entry filed as *Investigator Note / Internal* does not
  appear anywhere in the client's view.
- **Decline works as designed.** The client's request shows **Declined** with **Choose Another
  Organization**, and the picker says honestly that nobody else is available.

---

## Programming gaps

### 1. Sign-up is a dead end wherever SMTP is not configured — and the code believes otherwise
`Ben.Data.WebApi/Services/IdentityEmailSender.cs`, `TrySendConfirmationAsync`.

Registering says "Check your email — we've sent a link". Nothing is sent, and **nothing anywhere
shows the link**, so the account can never be completed by its owner. The only way in is a
SuperAdmin ticking *Email Confirmed*, which a new user cannot ask for before they can sign in.

This is a defect rather than a limitation because the configuration file documents the opposite in
its own comment: *"With Host null … IdentityEmailSender logs the confirmation link so a local flow
can still be finished."* It does not. The method is handed `confirmationLink` and drops it.

**Fix:** when `!_email.IsConfigured`, log the link — Development only, or behind a flag. A
confirmation link in a production log is itself a way into an account, so the guard matters.
**Also:** the failure is logged **twice** per sign-up.

### 2. "Go back and verify your address" leaves the wizard
`Ben.Web.Website.Library/Client/ClientRequestWizard.razor:202`

```razor
<a @onclick="@(() => _step = 1)" href="#" class="alert-link">Go back and verify your address.</a>
```

No `@onclick:preventDefault`, so the browser follows the anchor and navigates to the home page. The
draft survives — the wizard autosaves, and the abandoned attempt was waiting in *My Investigation
Requests* — but the person is thrown out of the flow with no message and has to find their way back.

**Fix:** add `@onclick:preventDefault`, or make it a `<button class="btn btn-link">`.
**Scope:** grepped the whole site; this is the only anchor with a handler and no `preventDefault`.

### 3. Screens do not refresh after the action you just took
Two instances, same shape. Both clear on a **hard reload**, so the data is right; it is the live
circuit not re-reading after a successful write.

- **Team panel.** After adding the first attendee, the summary still says *"0 of 0 arrived"* and
  *"Nobody has been added to this investigation yet"* — and the **duty board stays hidden**, because
  the panel believes the roster is empty. The thing you want next is missing.
- **Action-needed banner.** After declining the last pending request, the group page still shows
  *"has work waiting: 1 investigation request"* while the Requests tab beneath it says *"No pending
  investigation requests."* It survives tab navigation. (Accepting cleared it; declining does not.)

**Fix:** re-read the roster / the banner count after the write, the way the duty board already does.

### 4. Client request cards cannot be reached from a keyboard
`Ben.Web.Website.Library/Client/ClientRequests.razor:45`

Each request is a `<div class="card" style="cursor:pointer" @onclick=…>` with no `role`, no
`tabindex` and no key handler. It is the **only** way into a request's detail page, so a keyboard or
screen-reader user cannot open their own request.

**Fix:** `role="button"`, `tabindex="0"` and an `@onkeydown` for Enter/Space — or wrap it in an
`<a href="/my-requests/{id}">`, which also gives middle-click and open-in-new-tab.

---

## The gap that matters most

### A brand-new group is invisible to every client, and nothing tells it so

The *Start a group* wizard's "First settings" step offers two things: accept membership
applications, and a public contact email. It never mentions client work. A group created through it
has `IsAcceptingClients = false` and no operating area, so it appears in **no** client's search.

My client's request — real geocoded Springfield address, a real Springfield group created minutes
earlier — reached step 4 and was told *"No organizations are currently accepting cases in your
area."*

**Two settings are needed, not one:** *Accept client cases* **and** an *Area of Operation* (centre
plus radius). A group with one and not the other is still invisible.

**Where they live:** `/organizations/{id}/client-settings`, reachable only from **Organizations →
the row's "More actions" dropdown → Clients**. It is **not** on the group's own page — which has
thirteen tabs including Settings — and not in the wizard.

This is the site's entire client funnel. Every request a client writes ends at "nobody is
available" until a group happens to find a page nothing links to from the group itself.

**Suggested fix, in order of value**
1. Put **case acceptance and the operating area into the creation wizard's First settings step.**
   For an investigation group it is the single most important switch there is.
2. Add a **Clients** tab to the group page, beside Requests.
3. Failing both, show the group page a standing notice — *"You are not accepting client cases, so
   clients cannot find you"* — with a link to the page that fixes it.

**FIXED 2026-09-04** (branch `feature/client-funnel-visible`). All three were built, not just the
first:

- The wizard's First settings step now asks **Take cases from clients** — ticked by default for an
  investigation group — with *where you work* and a radius beneath it, prefilled from the address
  given a step earlier. It refuses to go on with the box ticked and no town. On create it writes
  acceptance first and the geocoded area second, so a town that will not geocode leaves the group
  accepting rather than silently invisible.
- The group page has a **Clients** tab beside Requests, and its Details tab states client reach
  where anyone managing the group will see it.
- A group taking cases that no client search can return carries a standing warning naming the
  reason. The rule behind it mirrors the public search query exactly — accepting, listed, and an
  operating area — and three tests pin each condition so the notice cannot drift from the query.

Verified end to end on a throwaway database: two groups created through the wizard alone, both
returned by the public client search for Springfield TN, one of them after fixing a deliberately
un-geocodable town from the new tab. The warning cleared the moment the area was saved.

A second defect surfaced while verifying it. The wizard asked to accept membership applications and
reported *"Applications: Open"* on its review, but the server refuses that for every brand-new
group — the free lane is one person, so the paid gate turns the whole save away, taking the
founder's public contact email with it. The email is now written separately and lands; the
applications attempt goes last and is expected to be refused; and the review says *"Open once your
plan includes members"* instead of promising something the site will not do.

---

## Visual and wording gaps

- **A draft says it was "Submitted".** `ClientRequests.razor:76` prints `Submitted @r.DateCreated`
  unconditionally, and the detail page has a `Submitted` term too. Both show for cards whose badge
  says **Draft** — and `DateCreated` is when the draft was *started*. The client's list is exactly
  where that distinction matters. Label by state: "Started" or "Saved" for a draft, "Submitted"
  once it is.
- **The client's desk shows group-member widgets to a pure client** — "Gear checked out to you",
  "requests waiting on you". Harmless, but it is the first screen a client sees after signing in and
  most of it can never apply to them.

---

## Ideas worth considering

- **A "before clients can find you" checklist on the group page.** Accepting cases, operating area,
  a public contact, at least one duty holder. New groups do not know what they have not done, and
  the wizard deliberately only asks for the first step.
- **Say what a decline means to the client.** The decline dialog asks only "are you sure". A
  one-line optional reason, shown to the client, would turn a dead end into information — and the
  client already gets a "choose another organization" button, so the reason has somewhere to go.
- **The onboarding "first stop" choice is a good hook to extend.** It already routes to the right
  door; it could also pre-tick the group settings that door implies (an investigation group that
  says it wants client work could arrive with acceptance already on).

---

## Not covered

- The report **PDF** download (the browser pane cannot take file downloads), files and media
  attachment to a case, equipment, the case going public, and the mobile Field Kit.
- The **refusal half** of the new duty-capability rule (a visit lead who is not an administrator
  being refused an override) — covered by `InvestigationDutyTests` rather than in the browser,
  because it needs a second member.

## One correction to my own notes

I first recorded that the operating-area label prints twice. It does not: after a successful
**Look Up** the dialog auto-fills the label, and I typed the same sentence into the already-filled
box. Checked the markup and the dialog before reporting it, and withdrew it.
