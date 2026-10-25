# feature/one-credit-one-run

Ben, 10/24/2026: "The credit covers one contiguous event." Then: "If the event is a performance like a
play or musical, one credit is for the run of the show. If it is a weekend of ghost hunting or seminars,
each weekend would be separate events." And: "For a show run, it cannot be performances beyond a month's
time."

- A stay (`DatesAreSeparate = false`) is days in a row by construction; nothing changes.
- A run (`DatesAreSeparate = true`) is refused at save, create or update, when a performance falls a month
  or more after the first (`HostedEventController.RunTooLong`: 10/01–10/31 fits, 11/01 does not). Before,
  one credit covered up to 366 dates.
- Form, help (organization-administration), pricing page, the billing card and the change log say so.
- Existing events are untouched until edited. Still open with Ben: moving a paid event's dates.

Tests: `HostedEventControllerTests` (a run within a month keeps its dates; a run past a month is refused in
words; the month boundary).
