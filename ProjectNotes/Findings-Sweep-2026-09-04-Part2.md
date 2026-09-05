# Third sweep — an authorization probe and the first full Playwright run in a while

2026-09-04, following the two sweeps in `Findings-Sweep-2026-09-04.md`. Same isolation rule: a
throwaway database (`IsHauntedDb_probe`) and its own uploads directory, destroyed afterwards.
Nothing touched production.

This round went looking in two places the earlier sweeps did not: the authorization surface, route
by route, and the Playwright suite, which had not been run since the two secrets went missing.

The headline is mixed and worth saying plainly. **The authorization surface held up** — I could not
get one piece of another group's data out of it. **The test suite cannot currently be trusted to
run**, for reasons that have nothing to do with the product, and that noise has been hiding one
real defect.

---

## 1. The users grid has a delete button that cannot work on your own row

`AdminUsers.razor` puts a trash button on every row. Clicking it on **your own** account refuses:

> Delete your own account from your profile, not from here.

The refusal is correct. Offering the button is not. This is the dead-end click that items 149 and
150 made policy against: the control should be absent or disabled on that row, with the reason in
the tooltip, rather than accepting a click and answering with an error at the top of the page.

It costs more than tidiness. Three Playwright tests click the first row's delete button and wait
for the delete screen, so all three fail whenever the signed-in SuperAdmin sorts first — which is
exactly what happens on a fresh database. The failure text is the refusal itself, three times in
the run log. On the shared dev database the SuperAdmin sorts elsewhere and the tests pass, which is
why this has never shown up before.

Two fixes, both needed: hide or disable the button on your own row, and stop the tests taking
`.First` on a grid whose order they do not control.

## 2. The sign-in helper turns one bad login into a locked account

`BenTestBase.LoginAsync` retries a submit up to five times. ASP.NET Identity's default lockout is
five failed attempts in five minutes, and nothing in this project raises it. So a single login
sequence that goes wrong — a wrong password, a dropped click, a circuit that was not up yet — locks
that account for everyone.

The seats are shared. Sarah, James, Daniel and Victor are used by nearly every test in the suite,
so one locked seat fails dozens of unrelated tests with "This account is locked for a few minutes
after too many attempts". My first run produced 62 failures; about 45 of them were this cascade.

The helper already backs off when the page says "Too many sign-in attempts", which is the API's
rate limiter. It does not stop for "Invalid email or password" or "This account is locked" — the
two answers where retrying cannot possibly help and actively makes things worse. Stopping on those
two is a few lines and would have saved this run.

## 3. There are two seeded password sets, and the runbook mentions one

`SeedData:SeedOrganization:Users` carries a password per user for Sarah, James, Emma and Daniel.
`SeedData:DevData:Password` covers the wider roster — Marcus, Olivia, Victor and the rest.

`BenTestBase` says to source the five secrets from `appsettings.Development.json`, without saying
that they come from two different places. Using one for all of them is the obvious reading, and it
fails only for the four accounts the suite leans on hardest — then finding 2 turns four wrong
passwords into fifty failures that look like product bugs. That is the "phantom e2e errors" shape
again.

A `README` line naming which variable comes from which key would end it.

## 4. One test enrols the shared account in two-factor and hopes

`EnrollingWithARealCodeTurnsItOn` enrols **Sarah** — the seat most of the suite signs in with — and
relies on its teardown to switch it off. Its own sibling, `SigningInWithTwoStepAsksForTheCodeAnd
AcceptsIt`, deliberately enrols a throwaway account and explains why in a comment ending "a test
whose answer depends on its neighbours is worse than no test". The rule was written down and then
not applied one method above.

While two-factor is on, a password-only sign-in as Sarah is refused, which feeds finding 2.

## 5. A group's plan is readable by any signed-in stranger

`GET /api/security/organizations/{id}/included-areas` answers any signed-in caller, for any group:

```json
{"areas":[1,2,3,4,5,6,7,8,9],"tierName":"Free"}
```

No membership, no grant, no relationship of any kind. It is not personal data, but it tells anyone
which plan any group is on, which is commercial information a group did not choose to publish.

## 6. What held up, which is worth recording

I probed every route I could address with a real resource id, as three callers: anonymous, a
signed-in stranger, and the owner of an unrelated group.

| Verb | Probed | Cross-tenant successes |
|------|-------:|-----------------------:|
| GET | 222 | 0 |
| POST | 117 | 0 |
| PUT | 16 | 0 |
| DELETE | 22 | 0 |

Every 200 was either a route that is deliberately public (the equipment catalogue, the experience
taxonomy, public search) or a "my own" route returning the caller's own empty list. Nothing
belonging to the other group came back, and no write was accepted.

Ninety GET routes could not be probed because I had no id to put in them — files, sessions,
photos, tokens, slugs. That is the honest limit of this pass, not a clean bill for those routes.

Two specific things I went looking for and did not find:

- **A published case leaks nothing precise.** The public listing carries an approximate position
  about 4 km from the real one, the pseudonym, and the town. The street address, the description
  and the true coordinates stayed server-side.
- **Equipment lending refuses correctly.** An item shared with a group but with every borrow
  audience left unticked answered "The owner isn't lending this out", from both the eligibility
  endpoint and the request endpoint.

## 7. Smaller things

- **The case update handler wipes the description when it is sent null.** `Title` is protected with
  `?? entity.Title`; `Description` is assigned unconditionally. Both UI call sites send the current
  value, so nothing is losing data today — it is one careless caller away from doing so.
- **The Playwright run was stopped, not completed.** After 45 minutes and 52 failures dominated by
  the lockout cascade, I killed it rather than keep the stack busy. There is no clean pass/fail
  count from this session, and the numbers above should not be quoted as a suite result.
- **`A_group_is_told_a_case_is_closed_rather_than_deleted` failed on a click that never
  navigated.** The handler navigates unconditionally, so this looks like the Blazor interactivity
  race under parallel load rather than a defect. Unresolved, and flagged rather than dismissed.

---

## What I would fix first

1. The delete button on your own row — a real dead end, and it unblocks three tests.
2. Stop the login helper retrying a refusal it cannot fix, and say in the README which password
   comes from which key. Together these are the difference between a suite that can run and one
   that cannot.
3. Move the two-factor enrolment test onto a throwaway account, the way its neighbour already does.
