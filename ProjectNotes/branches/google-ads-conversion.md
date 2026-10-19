# feature/google-ads-conversion (10/19/2026)

Ben is starting a Google Ads campaign and needs one subscription-confirmation URL to give Google,
plus the Google tag, used only with consent.

## The URL to give Google

`https://ishaunted.com/subscribed` (conversion fires on that page; match "URL contains /subscribed"
if a URL rule is used instead of the event).

## What changed

- **`/subscribed?org={id}&how=paid|free`** (`Ben.Web.Website.Library/Billing/Subscribed.razor`).
  Stripe's success URL and the 100%-off (coupon) path now return here instead of straight to
  `/organizations/{id}/billing?checkout=success|free`. The page awaits AuthReady, loads
  `GetMyMembershipOrganizationsAsync` (LoadResult, failure shown), and forwards after ~4s (or on
  the button) to the billing page with the original `checkout=` value, so OrganizationBilling's
  banner still shows. A group the visitor is not a member of, a bad id, or a signed-out visitor
  goes to `/organizations` (`SubscribedLanding.Destination`, no open redirect).
- **Not routed through:** seat-paid (one member paying their own overflow seat, not a new
  subscription; counting it would inflate conversions) and event credits (a one-off purchase).
  Both still return to the billing page directly. Cancel URLs unchanged.
- **Google tag** (`AdsConsent.razor` in App.razor + `wwwroot/js/ads-consent.js`). Config
  `GoogleAds:TagId` and `GoogleAds:SubscribeConversionLabel` in Ben.Web.Website/appsettings.json,
  both empty. Empty TagId: no script, no banner, no footer link. With a TagId: Consent Mode v2
  defaults all four purposes to denied; gtag.js is fetched only after Accept (now or remembered in
  localStorage `ih-ads-consent`). The conversion (`send_to: TagId/label`, nothing else) is sent
  from /subscribed only for paid/free on the visitor's own group and only if consent is granted.
- **Banner**: equal-weight Accept/Decline, Bootstrap card colors (both themes), fixed bottom,
  fits 320px. Footer "Cookie settings" (only with a TagId) reopens it.
- **Privacy page**: new "Measuring our ads, on the website only" section; short version, "What we
  do not do", "Who else touches it" and "Your choices" updated. App claims unchanged.
- Help: organization-administration.md (after paying). Changelog under Unreleased.

## Tests

- `Ben.Web.Tests/Website/GoogleAdsConsentTests.cs`: settings, render tests for AdsConsent and
  BenFooter, ads-consent.js run in Jint (nothing fetched before Accept, Decline, remembered Accept,
  reopen, conversion only with consent), SubscribedLanding refuses foreign/bad ids.
- `TrialCheckoutTests`: free and paid success URLs point at /subscribed.
- Playwright `Ben.Web.Playwright/Tests/GoogleAdsConsentTests.cs`, category `GoogleAdsConsent`,
  Widths matrix x dark/light. Written, not run. Needs the web host started with
  `GoogleAds__TagId=AW-000000000` or the tests are ignored.

## At deploy

- Put the real `AW-…` id and the conversion label in production config (appsettings or env
  `GoogleAds__TagId`, `GoogleAds__SubscribeConversionLabel`).
- No migration.
