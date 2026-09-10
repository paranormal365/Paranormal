# Captured answers, not invented ones

Every file here is a real response from a real `Ben.Data.WebApi`, saved verbatim. None of them was
written by hand, and none should be.

The reason is not tidiness. A fixture somebody writes agrees with whatever the person writing it
already believed, so it confirms the client's assumptions instead of testing them — and the bugs
worth catching are exactly the ones where the assumption was wrong. This repo has shipped that
mistake before: an invented JSON fixture on the iOS side let a decoding bug through to a real
screen, because the fixture and the decoder were wrong in the same way.

## How these were produced (2026-09-10, item 225)

Against an API pointed at a scratch database, never the shared one:

```bash
dotnet ef database update --project Ben.Data.Source --startup-project Ben.Data.WebApi \
  --connection "Server=...;Database=IsHauntedDb_desktop225;..."
```

then the API started from `Ben.Data.WebApi/` with that connection string, and:

| File | How |
| --- | --- |
| `login-200.json` | `POST /login` with a seeded account. Token values redacted; the shape is the contract. |
| `refresh-200.json` | `POST /refresh` with that session's refresh token. Same redaction. |
| `me-200.json` | `GET /api/me` with the access token. Email replaced. |
| `login-401-failed.json` | `POST /login`, real account, deliberately wrong password. |
| `login-401-not-allowed.json` | Registered an account, then signed in **before confirming the address**. |
| `login-401-requires-two-factor.json` | Turned two-factor on for real (`/api/me/2fa/setup` then `/enable` with a computed code), then signed in with no code. |
| `login-429.json` | Lowered `ratelimit.auth-per-minute` to 3 through the SuperAdmin site-settings endpoint, waited out the 30-second refresh, then over-asked. |
| `account-register-200.json` | `POST /api/account/register` with a free handle. |
| `account-register-refused.json` | The same handle a second time. |
| `handle-available-200.json` | `GET /api/account/handle-available` for a name nobody holds. |

## What they already proved

- The three `/login` refusals really are the same 401 separated only by `detail`. A client that
  reads the status alone cannot tell "enter your code" from "your password is wrong".
- The 429 carries `Retry-After: 60`. A rate-limited person can be shown a countdown rather than a
  button certain to be refused.
- **Registration answers a refusal as JSON**, not prose: `{"succeeded":false,"message":"That name
  is taken.","field":"Handle"}`. The ordinary refusal handling discards any body starting with `{`,
  so the one sentence that tells somebody what to change would have been thrown away. `AccountClient`
  reads that body on purpose.

## Recapturing

Do it whenever the server's auth surface changes, and never against the shared development
database — it is the one ishaunted.com uses.
