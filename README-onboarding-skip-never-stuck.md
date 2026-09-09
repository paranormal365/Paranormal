# Skip could not skip: the first-run wizard nagged for ever

Found on 2026-09-09 while checking what App Review's demo account would see on production.

Ben signed in as `apple@apple.com` and was met by **Welcome to IsHaunted.com — a minute of setup**.
He clicked *Skip for now* and it went away. On the live database, `AppUsers.DateOnboarded` was
still **null** afterwards, and still null minutes later. The wizard had not been dismissed; it had
been navigated away from, and it would come back on the reviewer's next visit. On every visit.

## The mechanism

`OnboardingPage.SkipAsync` → `StampAndLeaveAsync` → `CompleteMyOnboardingAsync`, which posts to
`/api/me/onboarding/complete`. That endpoint is correct and returns **204**.

`WebApiClient.PostAsync<TRequest, TResponse>` then called `ReadFromJsonAsync` on the empty body,
**which throws**. `StampAndLeaveAsync` wraps the call in `catch { }` — "worst case the gate offers
once more next visit" — so the throw vanished and the navigation happened anyway. Every symptom of
success, none of the effect.

## It was written down as a contract

`WebApiClientTests` held `PostAsync_On204NoContent_ThrowsJsonException` and its PUT twin, with a
comment saying the fix was "to use PostVoidAsync/PutVoidAsync against endpoints that return 204".
So the defect was documented, tested, and left in place for whoever next used the ordinary method
against an ordinary void endpoint.

Meanwhile `GetAsync`, `SendItemAsync`, `SendExpectingReasonAsync` and
`PostMultipartExpectingReasonAsync` had each grown the guard separately, one of them carrying a
comment that says **"the guard belongs here and not in the dozen call sites"**. Seven methods still
did not have it.

## The fix

One private `BodyOrDefaultAsync<T>` on `WebApiClient`, used by `PostAsync`, `PutAsync`,
`PostAnonymousAsync` and `PostAnonymousReadingBodyAsync`. An empty success is a success with
nothing in it, said once.

The two tests that pinned the throw now assert the opposite and carry the story. Four new tests in
`WebApiClientEmptyBodyTests` cover POST, PUT and the anonymous post against a 204, plus a POST that
does answer — three of the four fail against the un-fixed client.

## Who this was hurting

Not only the reviewer. **Every account created on the live site since the wizard shipped** — the
seeders stamp `DateOnboarded`, so only genuine sign-ups were affected, and only they could not
make it stop.

**This needs a deploy to reach production**, and then one more *Skip for now* as
`apple@apple.com` so the reviewer never sees it.
