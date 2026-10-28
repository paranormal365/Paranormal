# Session player, phase 3: links that don't die (10/28/2026)

Branch `fix/player-media-links`. Phase 3 of the session-player plan.

## Why

A recording's link (`/media/field-sessions/{s}/files/{f}?t=…`) carried a ticket, and the ticket held the
access token **as it was when the page drew the link**. The token lasts an hour and is renewed only when
the page next talks to the API, and a session player that is just playing talks to nobody. So a long
night's recording could start answering "not signed in" partway through. The page then renewed the link
**once**, with whatever token it held (possibly just as dead), and a second hiccup, or the first on a
share link or the public archive, marked a good file "won't play in this browser" until a reload.

## What changed

- `Ben.Web.Website/Services/MediaCaller.cs` (new): a ticket keeps the page's token source by reference
  (`CircuitMediaTokenSource`: the circuit's token store and `SessionRenewer`, the way
  `SessionRenewalHandler` already carries them for API calls). A request is sent with the page's current
  token, renewed first if it is about to run out. Only while the same person is signed in on that page;
  after a sign-out or a switch, the ticket falls back to the token it was made with.
- `BrowserTicketStore` / `MediaTicketService`: tickets carry the source; `CallerAsync` redeems one.
  Every media link `MediaUrlBuilder` draws now carries it, not only the player's.
- `MediaProxy`: a 401 is renewed once and asked again. `Content-Length` is passed on when the body is
  passed on as sent (no `Content-Encoding`): Safari wants it on a range of a recording. `ETag`,
  `Last-Modified`, `If-Range`/`If-None-Match`/`If-Modified-Since` go through; a 416 keeps its
  `Content-Range`; a browser letting go mid-file (every seek) is no longer logged as an error.
- 10 media routes in `Ben.Web.Website/Program.cs` redeem a caller (`tickets.CallerAsync`). The 7 upload
  relay routes are unchanged.
- Player: a recording that failed on the network, or had loaded before, is given a fresh link and tried
  again up to three times (1, 3, 8 s), on share links and the public archive too, and playing resumes by
  itself. One that never loaded and reads as undecodable is tried once more (a stale link reads the same),
  then said to be "won't play in this browser". One that is never reached says "couldn't be reached",
  with the download. The notice clears when the recording loads.

## Tests

- `Ben.Web.Tests/Services/MediaCallerTests.cs` (new, 7). Three fail when the redeem and the retry are put
  back to the old behaviour: a link drawn before a renewal is sent with the renewed token; a token about to
  run out is renewed first; a refusal is asked again with a renewed token. The others guard what must not
  change: somebody else signing in, a ticket for another file, a bare token, the headers.
- `FieldSessionTransportTests.A_recording_that_fails_to_load_once_is_tried_again_and_plays`: fails on the
  player as it was on develop (marked "won't play in this browser"), passes now. The transport set is 9/9.

No migration.
