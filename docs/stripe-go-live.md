# Stripe go-live — the whole runbook

What has to be true before real cards are charged, and exactly where each value goes. Written
08/30/2026, when phases 1–3 (checkout, fulfillment, billing page, renewals) shipped against test
mode. Everything below is the flip from test to live.

## The two secrets, and where they live

| Key | Looks like | What it can do | Where it goes |
|---|---|---|---|
| Secret key | `sk_live_…` | **Move money.** Create charges against any saved card | API app pool env: `Stripe__SecretKey` |
| Webhook signing secret | `whsec_…` | Authenticate Stripe's callbacks — the one anonymous route that can move a subscription | API app pool env: `Stripe__WebhookSecret` |

The publishable key (`pk_live_…`) is not needed: checkout is Stripe-hosted, so no key ever runs
in the browser. The rule for the other two is the `Smtp__Password` rule: **pool environment,
never a deployed file, never source control, never chat.** The double underscore is how .NET maps
an environment variable onto the nested key — `Stripe__SecretKey` becomes `Stripe:SecretKey`.

## Path A — the deploy script does it (use this one)

`deploy-ishaunted.ps1` now carries both keys the same way it carries the SMTP password:

1. On the server, edit `C:\ishaunted-deploy\secrets.json` (Administrators-only folder) and fill:
   ```json
   "StripeSecretKey": "sk_live_…",
   "StripeWebhookSecret": "whsec_…"
   ```
2. Run the ordinary deploy:
   ```powershell
   .\scripts\deploy-ishaunted.ps1
   ```
   It sets `Stripe__SecretKey` and `Stripe__WebhookSecret` on the `IsHaunted.com-webapi`
   application pool and recycles it. Missing keys warn rather than fail — checkout reports
   itself unavailable in a sentence, and manual subscription entry keeps working.

That is the whole of path A. The rest of this document is the manual equivalent and the
surrounding one-time steps.

## Path B — by hand in IIS Manager

For when a key must be rotated without a deploy.

1. Open **IIS Manager** on the server.
2. Click the **server node** (the machine name, top of the left tree) — not the site.
3. Open **Configuration Editor** (Management section).
4. In the *Section* dropdown, navigate to
   `system.applicationHost/applicationPools`.
5. Click the **(Collection)** row → the **…** button. Find the pool named
   **`IsHaunted.com-webapi`**, select it, and open **its** `environmentVariables` collection
   the same way.
6. **Add** two entries:
   - `name` = `Stripe__SecretKey`, `value` = the `sk_live_…` key
   - `name` = `Stripe__WebhookSecret`, `value` = the `whsec_…` secret
7. Close the collection editors and click **Apply** (top right).
8. Recycle the pool: left tree → Application Pools → `IsHaunted.com-webapi` → **Recycle**.
   Environment variables are read at process start; without the recycle nothing changes.

Or the same thing as one-liners in an elevated PowerShell:

```powershell
Import-Module WebAdministration
$filter = "system.applicationHost/applicationPools/add[@name='IsHaunted.com-webapi']/environmentVariables"
Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $filter -Name '.' `
    -Value @{ name = 'Stripe__SecretKey'; value = 'sk_live_…' }
Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $filter -Name '.' `
    -Value @{ name = 'Stripe__WebhookSecret'; value = 'whsec_…' }
Restart-WebAppPool -Name 'IsHaunted.com-webapi'
```

These land in `applicationHost.config`, readable only by administrators — the same place the
deploy script writes them.

## Local development (Kestrel / IIS Express)

Local runs don't touch pools at all:

- **Kestrel (`dotnet run`) — what we actually use:** test keys sit in the gitignored
  `Ben.Data.WebApi/appsettings.Development.json` under `"Stripe"`. A local webhook secret can
  ride an environment variable for one run:
  `Stripe__WebhookSecret=whsec_… dotnet run` (macOS/Linux) or `$env:Stripe__WebhookSecret='…'`
  (PowerShell).
- **IIS Express (Visual Studio):** same file works. If an env var is ever preferred, it goes in
  `Properties/launchSettings.json` under `environmentVariables` — but that file is tracked, so
  only ever put **test** keys there, and better to leave it alone and use the settings file.

Never put a live key anywhere in the repo tree, gitignored or not — live keys exist only in the
Stripe dashboard, `C:\ishaunted-deploy\secrets.json`, and the pool environment.

## One-time steps around the keys

1. **Stripe account verification** must be complete (identity + payout bank account) — live mode
   refuses charges until it is.
2. **Register the webhook endpoint**: Stripe dashboard (LIVE mode) → Developers → Webhooks →
   Add endpoint:
   - URL: `https://ishaunted.com/webapi/api/stripe/webhook`
   - Events: `checkout.session.completed` and `payment_intent.succeeded` (the only two the
     parser acts on; extra events are acknowledged and ignored)
   - Copy the signing secret it shows — that is the `whsec_…` for the steps above. The TEST-mode
     dashboard issues a different secret for a different endpoint; don't mix them.
3. **Statement descriptor** (Settings → Business → Public details): something like
   `ISHAUNTED.COM`, so a card statement line doesn't read as fraud to a group treasurer.

## Proving it, once live

1. `https://ishaunted.com/organizations/<your-org>/billing` → Subscribe → pay a real card the
   smallest real amount (your own group; refund it from the dashboard afterwards — the refund
   becomes an Adjustment row when recorded via Admin → Billing).
2. Check the ledger on that page: a Charge and a Payment with a receipt number, tax at the TN
   rate.
3. Stripe dashboard → Webhooks → the endpoint → recent deliveries: the
   `checkout.session.completed` delivery should show **200**. A 400 there is a wrong
   `Stripe__WebhookSecret`; the API log will say `Stripe webhook refused` with the exception
   type.
4. The API log line to look for on success starts `Stripe fulfilled:`.

## What failure looks like (so it isn't debugged as something else)

- **Checkout button answers "Online payment isn't set up yet"** → `Stripe__SecretKey` missing
  or empty on the pool (or the pool wasn't recycled).
- **Payments succeed but webhook deliveries all 400** → wrong or missing
  `Stripe__WebhookSecret`. Not urgent-urgent: checkout and renewals both fulfill synchronously,
  the webhook is the belt to that braces — but fix it, because it is also the only path that
  catches a payment completed after a browser crash mid-checkout.
- **Renewals log `Renewal charge declined`** → normal for an expired card; the group gets the
  existing lapse treatment unless a later retry lands. Nothing to operate.
