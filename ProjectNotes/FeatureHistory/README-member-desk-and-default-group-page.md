# Items 204 and 205 — a member's desk, and a group page before the group has written one

Branch: `feature/member-desk-and-default-group-page`, cut from `develop` at `7f9ea67`.

## 204 — a member's Home that is a desk, not a poster

The "has work waiting" banners already knew what was waiting; the Home page beneath them repeated
the visitor hero. Now a signed-in member who belongs to at least one group lands on **their desk**:

- **Next investigation** — the soonest one they are on (not declined, not cancelled, from now on
  or still running), with where, when, who leads, how many attending; a link to all upcoming.
- **Open cases** in their groups, the ones they are a contact on first, with the org, the case
  number and its status; the honest total in the header.
- **Unread messages** and **requests waiting on you** as two tiles that open what they count.
- **Gear checked out to you**, due dates, an *overdue* badge where the due date has passed.

One call, `GET api/me/desk` (`MyDeskController`), returning `MemberDeskResponse`. Every query is
the same one another screen already runs — the banners' pending rule (Pending, Viewed,
UnderReview), the bell's unread count, My Equipment's checked-out state — so the desk cannot
contradict the screen a tile opens. "Open" is the site's own rule (status at most Summarized;
Paused deliberately sits after Transferred so it does not count). Overdue is CheckedOut plus a
past due date, never a state.

A visitor, and a signed-in person in no group, still gets the hero: `MemberDesk` renders nothing
for them and tells `Home.razor` through `OnHasGroups`, so the hero is the default and only steps
aside once the desk knows it has something to stand on. What sits below the hero — nearby groups,
the feed teaser, the promoted group, the public case map — stays for everybody.

## 205 — a default group page

"This organization has not published a home page yet" was what every public group showed. Now,
when a group has authored no home page, the public endpoint adds `Facts` built from records the
group already keeps, and the page shows them:

> Investigation group serving **Nashville, TN**, on IsHaunted.com since 2026.
> 4 members · 2 public cases · **Taking new cases** · Next public event: Open Meeting — 09/30/2026

Area served is the group's own label, else "within N miles of City, ST" from its area of
operation and address, else the city. Members are active memberships. Cases are the public ones.
The next event is the soonest public one from now. Nothing is written on the group's behalf, and
the moment they publish a page of their own `Facts` is null and that page is what they get.

**No verified badge.** The model has no organisation-level verification — only verified links —
so the page does not claim one. If verification is ever built, it belongs here.

## Proof

- `MyDeskControllerTests` (2): a seeded member's numbers come out exactly (the past investigation
  and closed case excluded, the contact's case first, the returned checkout excluded, the overdue
  one flagged); somebody in no group gets an honest empty desk.
- `OrgPublicDefaultPageTests` (2): no home page → facts (inactive member not counted, private case
  not counted, members-only and past events skipped); an authored page → no facts.
- Browser: `MemberDeskTests` (member sees the desk and no hero, every tile present, unread opens
  /notifications; a visitor sees the hero and no desk) and `DefaultGroupPageTests` (a listed group
  without a page shows the default page and not the old sentence). Both green on the side database
  with screenshots. Full unit suite 3,943.
