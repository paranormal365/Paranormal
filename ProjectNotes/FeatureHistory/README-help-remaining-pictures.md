# The nine help pictures the Signal recapture could not take

Branch `feature/help-remaining-pictures` (2026-10-02). Each needed something the capture database did
not have; each is now arranged by the capture itself, so the next recapture takes all of them.

| Picture | What it needed | How it is arranged now |
|---|---|---|
| `the-feed/launch-card.png`, `organization-administration/tour-date-field-sessions.png` | A lead's launch with a session sent up — made by a three-simulator phone role-play (`BEN_RP_FILE`) | Built over the API when no file is given: a tour date starting in 30 minutes, the guest registered and accepted, the launch, and the Field Kit fixture sent to it. The group's view is taken as whichever seeded account is on the tour group's roster. |
| `site-administration/reading-a-letter.png` | A letter on screen | The filter is called **Everything** (the capture clicked "All", which no longer exists); it opens a letter that is not a sign-in key. |
| `getting-started/investigate-alone.png` | Somebody in no group | A brand-new account, signed up and confirmed, when the seeded loner has been given a space. `NewConfirmedUserAsync` moved to `BenTestBase`. |
| `site-administration/sidecar-telemetry.png` | Installs to show (and a capture step at all) | `Capture_SidecarTelemetry` reports three installs the way the installer does, then photographs the screen. |
| `organization-administration/event-dietary(-phone).png` | Parties with dietary notes | The guest asks for a place for three with their notes; the sheet is read with undecided parties counted in. |
| `shopping-at-the-store/checkout-payment.png` | Stripe's card form | Run with `BEN_STRIPE_E2E=1` (test keys only; the harness refuses live ones). |
| `working-a-case/field-session-player.png` | — | `FieldSessionStageTests.Capture_the_player_for_the_help`, `BEN_CAPTURE=1`. |

The product documentation PDF is rebuilt with them.
