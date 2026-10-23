# fix/menu-groups-no-shift-259

Item 259 (Future-Improvements.md). The menu drew first and fetched the person's groups afterwards; when
they arrived they were inserted under Home and every entry below moved down a row, so a click in that
moment landed on the entry above the one aimed at. Every full page load, for anybody in a group.

## Fix

- `Ben.Web.Website/Services/NavGroupsMemory.cs`: names and ids of each person's groups, per user id, for
  10 minutes after the last fetch (IMemoryCache, singleton).
- `BenNav.razor`: draws from it in `OnInitialized` and on identity change (sign-in, impersonation);
  writes it after every successful fetch; a failed fetch keeps what was remembered.
- Still shifts on the first page after signing in (nothing remembered yet).

## Tests

`NavGroupsMemoryTests`: per-user recall, replace, nothing for nobody; and the menu's first draw rendered
with HtmlRenderer has the group between Home and Notifications. That render test fails with the Recall
line removed. `PricingFindableTests` registers the new service.

Not yet run in the browser suite: the test database server (192.168.1.71) was unreachable on 10/23.
