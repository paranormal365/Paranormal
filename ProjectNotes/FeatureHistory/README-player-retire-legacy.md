# Session player, phase 2: the first player is retired (10/28/2026)

Branch `feature/player-retire-legacy`. Ben chose it on 10/28/2026: the first player (the server kept
time) had stayed one setting away, `FieldKit:Player = legacy`, "for one release". That release (2.21.0)
shipped 10/24, and the setting was never set anywhere.

Removed:
- `Kit/FieldKitPlayer.razor` and `.razor.css` (2,098 lines), `Kit/FieldKitPlayerChoice.cs`, and
  `wwwroot/js/fieldKitMedia.js`, which only the first player imported.
- The setting's switch on `/field-sessions/{id}` and `/s/{token}`; both render `FieldKitEnginePlayer`.
- `GetSharedFieldSessionAsync` on the website's API client: only the first player called it. The API
  route stays, because the summary call reads it with `?document=false`.

`ReachableComponentTests.Both_halves_of_sharing_a_session_reach_a_screen` named the retired method; it
now names the two calls the share page makes (summary and timeline).

No migration and no change anybody can see.
