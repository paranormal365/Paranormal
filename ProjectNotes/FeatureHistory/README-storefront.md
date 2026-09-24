# Storefront — the gear store (branch `storefront`, from master d2d27c26, 09/23/2026)

## Goal
Sell physical electronic devices for ghost hunts to anyone — guests included — with our own
checkout page and the Stripe Payment Element, Stripe Tax, flat-rate US shipping, per-variant
stock, discount codes, favourites, moderated buyer reviews, and an admin fulfilment desk with
Stripe refunds and printable invoices. Ships dark behind `features.store` (default OFF).

## Ben's decisions (verbatim)
PAYMENT UI: our own checkout page (Smarty's layout) with Stripe Payment Element; card data never
touches our server. SALES TAX: Stripe Tax — Calculation API before payment, Tax Transaction after;
TaxResolver/TaxRateRule are NOT reused. SHIPPING: flat rate per order, free over a threshold, both
admin-set; ship-to US only. STOCK: per variant; sold out stays listed but cannot be bought;
"3 left" note; checkout refuses more than on hand. BUYERS: anyone — guest checkout with email +
address; signed-in orders attach to the account (My Orders); guests get their order by emailed
link. OPTIONS: variants with own price/SKU/stock chosen with swatches/pills; NO price-changing
add-ons. EXTRAS: discount codes (store coupon table), favourites (signed-in), reviews (buyers only,
moderated). NOT brands. FULFILMENT: Paid → Packed → Shipped (carrier + tracking, emails the buyer)
→ Delivered; Cancelled / Refunded; full or partial Stripe refunds that restore stock; printable
invoice. STORE SWITCH: site-wide, default OFF; each product/variant active or inactive. ADMIN: a
"Store" section in Administration.

## The three rules
DESIGN RULE — Smarty supplies the structure, our site the skin (Bootstrap 5.3 in smartapp.min.css,
night.min.css, ben-kit.css, our header/nav/footer, light AND dark). No Smarty CSS/JS/jQuery/icon
font; the few Smarty-only classes are recreated as ben-* classes in wwwroot/kit/ben-store.css.
Smarty utility names (link-normal, link-muted, text-danger-hover, article-format, bg-cover,
text-dashed, gap-auto-*) are never copied into markup — ComponentClassesAreStyledTests denies them.
Product descriptions use `ben-store-description` (global, in ben-store.css) — NOT `ben-article-body`,
whose rules live only in the CSS-isolated PublicationPostReader.razor.css and never reach a store page.
CART RULE — the four cart surfaces (header dropdown, slide-out drawer, cart page, empty-cart page)
follow Smarty's HTML: −/+ quantity, remove, coupon entry (popup on the page, collapsible in the
drawer), "N in stock", unit price, old price + "You save $x", the step indicator Cart → Place order
→ Payment → Complete (all four labels from the first sitting; the checkout page marks Payment
active once the Stripe element is mounted), "Secured checkout", Returns/Privacy/Terms + "Need help?".
Substitutions: Smarty's summary Products / Shipping / Fees / Total (vat incl.) becomes Products /
Discount / Shipping amount·Free·– / Sales tax / Total before tax (cart) or Total (checkout) —
Fees → Sales tax (a `@* fees slot *@` marks where a fee line would go), "vat incl." → tax on its
own line, the drawer's gift line is a `@* gift slot *@`, reward points are a `@* reward-points
slot *@` only. The header dropdown shows three small rows (Products / Shipping / Total before tax),
not Smarty's one Subtotal line. Smarty's "Update cart" button is dropped — quantities update live.
When `store.checkout-enabled` is off every cart surface and the product page say "The store isn't
taking orders at the moment." instead of offering checkout.

DEVICE RULE (Ben, 09/23) — the store runs in browsers on phones, tablets and computers, all first-class:
every store screen, public and admin, is designed for touch and checked at 375×812 (phone), 768×1024 and
1024×768 (tablet) and desktop. Concretely (the plan's §6): the listing sidebar, the product gallery/buy-box
and the checkout form/summary stack below Bootstrap's `lg` (992 px) and sit side by side from there; the
Categories/Filters full-screen panels and the `.ben-checkout-bar` show below `lg`; the cart drawer is
`min(420px, 100vw)`; every tappable control is ≥ 44×44 CSS px on `(hover: none)` devices; the product card's
heart/cart rail is ALWAYS visible under `(hover: none)` (Smarty's hover-only rail leaves a touchscreen with no
buttons); the gallery is swipeable with the browser's own pinch-zoom; admin grids scroll inside the page on a
tablet; `VisualAuditWalk` gains tablet passes (768×1024, 1024×768); `StoreDeviceTests` checks the main screens
at 375, 768, 1024 and 1280 for sideways scroll, stacking and the primary control being on screen.

## Where things live
Entities `Ben.Data.Source/Entities/BenDataModel.Store*.cs`, config `Context/BenDataContext.Store.cs`,
enums `Ben.Data.Common/Enums/Store*.cs`; services `Ben.Data.WebApi/Services/Store/`; Stripe seams
`Services/Billing/Stripe/StoreStripeGateway.cs` (+ Fake) and `Services/Store/StoreTaxProbe.cs`,
`StoreTaxService.cs` (namespace Ben.Data.WebApi.Services.Billing.StripeIntegration for the gateway);
jobs `Ben.Data.WebApi/Services/Scheduling/Store*Job.cs` (beside MediaRetentionJob.cs);
controllers `Controllers/Public/PublicStore*.cs`, `Controllers/Store/*.cs`, `Controllers/Admin/Store/*.cs`;
DTOs `Ben.Service.Models/Store/` (incl. StoreMoney.Cents — the ONE dollars→cents conversion);
client `Ben.Web.Services/IBenStoreClient.cs`, `IBenStoreAdminClient.cs`,
`WebApi/BenAdminClientAdapter.Store*.cs`; state `Ben.Web.Services/Store/`; pages
`Ben.Web.Website.Library/Store/` and `SuperAdmin/Store/`; CSS
`Ben.Web.Website.Library/wwwroot/kit/ben-store.css`; help `Ben.Web.Services/Help/Content/
shopping-at-the-store.md` + `site-administration.md` "The store"; tests `Ben.Web.Tests/Store/`,
`Ben.Web.Playwright/Tests/Store*.cs`; webhook fixtures `Ben.Web.Tests/Fixtures/stripe/`.

## Rules that bit, written down
- Every FK to AppUsers is NoAction; the purge decides. Orders are detached and scrubbed, never
  deleted; in-flight orders keep the address until delivered (PendingAnonymisationSinceUtc).
  The scrub uses tracked entities + one SaveChanges inside the caller's transaction, so the
  InMemory AccountClosureTests keeps working; the discriminating closure fact is on SqliteTestDb.
- UploadFile.AppUserId is the model's ONE Cascade FK to AppUsers, so store images are ownerless,
  site-owned uploads: AppUserId and OwnerOrganizationId null, CreatedByAppUserId = the admin,
  ExpiresAtUtc null (the retention sweep never takes them). Storage accounting has never seen this.
- The fixed UploadFileType id series is 30…/40…/50…/60…/70… AND 80… (Board Snapshot) AND 90…
  (Research); Store Image is A0000000-…-0001. UploadFileTypeSeederTests asserts every fixed id is
  distinct so nobody repeats a slot again.
- Child rows cascade from ONE root only (SQL Server refuses two paths); Image→Variant,
  VariantOptionValue→OptionValue, RefundItem→OrderItem are NoAction with app-side rules.
- No rowversion anywhere: every race is a one-statement conditional ExecuteUpdateAsync (stock
  reservation, status transitions, coupon cap, idempotent MarkPaid, order reuse, release) —
  identical on SQL Server and SqliteTestDb. ReleaseAsync is idempotent because its FIRST
  statement is the conditional order update; a second release moves nothing.
- The stock guard is `StockOnHand + @delta >= StockReserved`, never `>= 0`: a CHECK violation is a
  provider exception, not DbUpdateException (item 220), and the admin would see a 500.
- Reserve lines in VariantId order: opposite orders deadlock on SQL Server (SqlException 1205),
  which SqliteTestDb can never show. The victim gets "Stock is being updated — try again."
- Coupons are reserved at PLACEMENT inside the stock transaction and released with the stock; the
  discount is clamped to the subtotal and never touches shipping or tax (CouponMath.cs:199 kept).
- "Sellable" is one predicate: variant AND product AND category active — the cart view, the stock
  reserve and favourites all use it, or a hidden category stays purchasable from a cart.
- The buyer's own open reservation is credited back when the same cart prepares again; otherwise
  the last unit reads "Sold out." to the person holding it.
- SQLite money rule: never SumAsync/OrderBy over a decimal column in a service a SQLite test
  runs; load the lines and sum in C#. SqliteTestDb for anything that executes an update/delete.
- Migrations: `dotnet ef migrations add <Name> --project Ben.Data.Source --startup-project
  Ben.Data.WebApi`; apply with `--connection "<player|e2e|live>"` only (`dotnet ef` ignores the env
  var; IsHauntedDb is production). M1 StoreCatalog, M2 StoreCartsAndOrders,
  M3 StoreFavouritesAndReviews, M4 StoreProductSeller (09/24, Ben's Seller role: one nullable
  column + index + FK on StoreProducts, nothing else).
- The confirmation, shipped and refund letters are queued through IOutboxEmailQueue INSIDE the
  transaction that changes the order — a refused letter refuses the change.
- NO Stripe call inside a SQL transaction: MarkPaid commits first, the tax transaction is a second
  short save; the store StripeClient gets a 20 s timeout through StripeClientOptions.HttpClient
  (there is no HttpTimeout member) and IStoreStripeGateway forwards to the ONE StripeGateway singleton.
- The fake Stripe gateway registers only in Development with no secret key and
  Stripe:AllowFakeCheckout=true; a Production container never resolves it (unit-tested), and a
  production-shaped container always gives StripeFulfillmentService its store dependency — a store
  intent with a missing dependency THROWS (Stripe retries) instead of being "ignored" with 200.
- Admin store controllers, the admin nav group, the image endpoint, every order-viewing door AND
  the thank-you page (/store/checkout/complete, Stripe's return_url) are not feature-gated: the
  switch hides the shop, not the back office or a buyer's money. Favourites/reviews/votes ARE
  gated (MyStoreEngagementController). `store.checkout-enabled` is the rollback lever.
- The API is public: never key a rate limit on a client-supplied value. The store policies key on
  the existing RateLimiting.ClientKey (user: else ip:) — reused, not copied as a "StoreClientKey"
  that could drift — through RateLimiting.StorePartition, the one function both the registered
  policies and StoreRateLimitPartitionTests call; the website forwards the visitor's address (X-Forwarded-For, trusted from loopback) so ip:
  means the visitor. HostedEmailPickPolicy inherits the same repair.
- A reservation costs the caller nothing: 3 open checkouts per address/email, 15-minute holds, the
  expiry job every minute, "Units held by open checkouts" on the dashboard with a Release action.
- StoreCheckoutPolicy sits on the three checkout POST actions, never the class: the complete page
  polls the status door up to 30× a minute.
- A class-level [AllowAnonymous] silences every [Authorize] action on the controller; the precedent
  is LinkPreviewsController.cs — two classes, one anonymous, one authorised (no mixed controller).
- Anonymous store endpoints authenticate the Entra scheme by hand
  (GetCurrentUserIdOrNullAcrossSchemesAsync) or a Microsoft-signed-in member shops as a guest forever.
- MarkPaid: an intent id that differs from the row's is NOT this order's payment — attention only,
  no status change, no stock (dashboard-made payments carry editable metadata). Only a Cancelled
  order paid on its OWN intent re-takes stock, and then it falls into the normal path (letter,
  cart, tax) with the flag set; the one early return is "stock is gone", whose reason says no tax
  transaction was filed. The amount check runs only when the event carries an amount; the
  calculation committed is the intent's ih_tax_calc, not the row's, and the row is rewritten from
  it when they disagree.
- Flow-B webhook handlers are each one conditional UPDATE … WHERE Status = PendingPayment: Stripe
  delivers out of order and more than once.
- A failed payment past the reservation cancels the intent AT STRIPE first (the element is still
  mounted on it) and releases only on Cancelled.
- refund.created/refund.updated/refund.failed, never charge.refunded; our refund id rides in the
  refund's metadata; a created refund is not a succeeded one — CompleteAsync runs only on
  `succeeded`, a pending refund waits for its event, a failed one marks the row Failed with no
  stock, letter or reversal to undo.
- Stripe idempotency keys live 24 h and replay the ORIGINAL response, errors included: Retry LISTS
  the refunds first and adopts what it finds; a fresh create uses store-refund-{Id}-{Attempt}.
- Stripe Tax `reference` is unique across the whole account: ours is {OrderNumber}-{8 hex of the
  order id}, reversals -R{n}; an invalid_request_error on commit is terminal (attention, no more
  retries); transient retries cap at 20; a refund that precedes the tax transaction is reversed by
  the retry job right after the commit; `full` only with no prior reversal and amount == Total.
- A buyer's bad address is customer_tax_location_invalid — an invalid_request_error that is the
  BUYER's problem (400, no bell), classified by StripeError.Code before Type.
- Explicit payment_method_types may only name ACTIVATED types: Link is opt-in
  (store.link-enabled) and a refusal falls back to card once with a warning.
- Webhook payloads are rendered in the ENDPOINT's pinned API version: pin both endpoints to the
  SDK's version, record it here, assert it on every fixture; a null latest_charge is tolerated.
- The Stripe.net XML doc lacks comments for AddressOptions and RefundCreateOptions.Amount — compile
  is the proof; the Tax line-item pager is ListLineItemsAutoPagingAsync(id,
  CalculationLineItemListOptions) — CalculationListLineItemsOptions does not exist.
- Webhook fixtures are captured (stripe listen --print-json … + stripe trigger), never typed.
- The checkout form locks once a ClientSecret is held; "Edit" disposes the element and re-prepares.
- Dollars→cents is StoreMoney.Cents (AwayFromZero) everywhere: decimal.Round(x, 0) is banker's.
- The cookie middleware is NEW (Program.cs:398 sets no cookie); Secure = Request.IsHttps.
- An email correction re-mints the order's AccessToken: the old mailbox loses the link.
- Every public store page is an AuthReadyOnRoutablePagesTests.Exempt row (works signed out).
- Playwright OFF-exclusions match by StartsWith: a NotBehind set keeps /store/orders* and
  /store/checkout/complete crawled while the switch is off; OrderId lives only in the admin
  prefix's _routeIds, never in TokenPlaceholders.
- SaveInOneTransactionAsync is protected static on BenControllerBase: static helpers open their
  own transaction with the IsRelational() guard.
- No seeder had a SeedCoreAsync before StoreDemoSeeder introduced one; tests call the core.
- The S4 sittings go mailer → alerts → payments → checkout service → jobs → controllers (+ e2e
  script) → client → pages → seed orders → account pages; StatCard has Href; a link never
  precedes its page (StoreLinksResolveTests) — dashboard tiles are inert until S5.5.
- The buyer help stub (S2.0) exists before any page links to it, like the admin one (S1.10).
- Every buying Playwright test purchases investigators-field-bag; single-unit-probe is the
  refusal test's own product and is restored in teardown.
- Guards born green are broken once on purpose (StoreReviewQueueHasAnEntranceTests,
  StoreRefusalReachesThePageTests) and the slice row below says when.
- (S0, 09/24) The FIRST StoreCatalog migration was generated before the catalogue's audit keys were
  NoAction: nine cascading keys from AppUsers and two cascade paths to product pictures — SQL
  Server refuses that. Every model guard passed (they read the model, which was right), and the
  next migration "fixed" it by dropping and re-adding M1's keys. Both were regenerated;
  MigrationsMatchTheModelTests (HasPendingModelChanges, no database) now fails on a model with no
  migration, and all three migrations were applied to IsHauntedDb_e2e with --connection to prove
  SQL Server accepts them. After `migrations add`, check the new migration touches no EXISTING table.
- (S0) The order scrub lives in the SHARED AccountClosureService.AnonymiseAsync — one call that
  closure and the SuperAdmin purge both reach — not in each caller. StoreOrder and
  StoreCouponRedemption are detached, not deleted, so they sit in the purge's sweptEntities and in
  AppUserPurgeCoverageTests' clearedByAnonymise, with StoreOrderScrubTests as the proof.
- (S0) The purge brings each review's HelpfulCount down BEFORE it sweeps the person's votes, or
  "12 people found this helpful" outlives the 12.
- (S0) Emails compare through StoreEmail.Normalize (trim + upper-case, Identity's rule) — checkout,
  the per-buyer coupon cap and order lookup must agree it is the same buyer.
- (S0) Moved: the "PrepareAsync reserves in VariantId order" fact needs the checkout service, so it
  lands in S4. The AdminDeleteUser kept-orders proof is a JSON contract test (service record → DTO)
  plus the sentence, not an HtmlRenderer fact: the page loads its preview in OnAfterRenderAsync,
  which a static render never runs. StoreMoney's "on the seeded totals" identity runs on sample
  totals now; the seeded orders arrive in S4.
- (S1) One stock sentence, not two: the product page's single adjust and the stock page's bulk
  save both go through StoreStock.AdjustManyAsync and say S0's words ("Nothing was changed — {SKU}
  can't go below the {n} held by open checkouts."), where the plan had a second wording per door.
- (S1) Store settings validation adds a ZIP rule ("A ZIP code is five digits, or ZIP+4 like
  37201-1234."), dollars-and-cents and a $10,000 ceiling on the two money settings; a state must
  be a real US state, not just two letters. SaveStoreSettingsRequest carries nullable values
  (null = unset, the default applies) and the page saves the whole form or none of it.
- (S1) AdminStoreProductController adds "This product is on sale, so the variant needs a price above
  $0.00." — a live product could otherwise be set to sell for nothing.
- (S1) IStoreTaxProbe has no IsAvailable: the ship-from address is a setting the probe cannot see,
  so ReadyToSell reads it from StoreSettingsReader. StorePaymentSetup (Fake, HasSecretKey,
  HasPublishableKey) is computed once at startup by the same rule the fakes use (StoreStripeMode).
  The Link-not-activated sentence waits for the hourly bell it reads (S4).
- (S1) Stock export is a button that downloads through the admin's client
  (DownloadStoreStockCsvAsync + downloadFileFromBase64), not an anchor: a plain link to the API
  cannot carry the admin's token.
- (S1) The Store section of Site Settings is a component (StoreSettingsReadOnly) proven by an
  HtmlRenderer fact plus AdminSiteSettings.IsEditedElsewhere; the page itself loads only when
  interactive, which HtmlRenderer never is.
- (S1) Help: the store's sections are `##` headings, not `###` under "The store" —
  HelpLinkTargetTests resolves anchors against the contents list, which lists `##` only. Reviews'
  anchor is `store-reviews`.
- (S1) BenTabs drew the active pane from the tab's content one render late (Blazor hands a child
  its parameters after the parent draws). Found by AdminStoreCatalogTests — "Generate variants"
  stayed disabled after saving options. Each BenTab now draws its own pane; the strip redraws when
  a tab's title/icon/badge/disabled changes (compared by value — a delegate comparison loops,
  because the content is a new delegate on every redraw). 36 browser tests over the other twelve
  tabbed screens re-run green.
- (S1) The product editor refreshes only the drafts of the part a save changed (Drafts flags): a
  late answer to one save used to wipe what had been typed since in another part.
- (S2) The public listing filters, counts, sorts and pages in memory (StoreCatalogue) after one
  load of the live catalogue: identical on SQL Server and SQLite (which cannot order by a
  decimal), and fine for a catalogue of hundreds. Push it into SQL if the store grows to thousands.
- (S2) The product card component is StoreCard, not StoreProductCard — a component named like the
  record it draws hides the record inside itself.
- (S2) Add to cart AND the quantity box wait for S3.5 (a quantity with nothing to add it to does
  nothing). Delivery/Returns/Need help are <details>, not a Bootstrap accordion: no script, every
  device. The sort is a <select>, not a BenDropdown.
- (S2) StoreListing watches NavigationManager.LocationChanged: a query-only change never reaches
  OnParametersSetAsync (Blazor skips setting parameters when the route values are unchanged).
- (S2) StoreNotFound: a 404 from the store API (hidden, retired, mistyped — or the API switched off
  before this website's 30-second feature snapshot) draws the ordinary "Page not found".
- (S2) VisualAuditWalk runs at desktop, 375, 768 and 1024-landscape for every seat (addendum §6.7).
  Its two non-store dark-mode contrast findings (Telerik pager text on admin grids, /events times)
  were spun off as a separate task.
- (S2) Seeded shelf pictures carry no words (the hero draws the title); their file name is their
  version, and an older one is repainted into the row on startup.
- (S1) StoreDemoSeeder.SeedCoreAsync(db, ownerId, ct) — the owner, not a storage service: the
  pictures live in the row (FileData), so nothing is written to disk.
- (S3) A cart write answers 409 with the whole cart plus StoreCartView.Notice ("Only 3 left.",
  "You can buy up to 100 at a time.", the 50-line cap) — the plan's record had nowhere to say
  what happened to a capped add. The client uses ONE call, SendExpectingConflictAsync (200 / 409
  with the cart / prose refusal), rather than the plan's pair; StoreCartChange.Cart is null only
  when nothing changed, which is how the product page knows not to open the drawer.
- (S3) Merge on sign-in: the guest cart's DELETE referees (its lines are read first). Two requests
  merging at once add the guest lines once — tested by running the competitor just before the
  first one's transaction (SqliteTestDb shares one connection, so they cannot overlap inside it).
- (S3) X-Ben-Cart goes only to /api/store/* and /api/me/store/*. X-Forwarded-For (the visitor's
  address) goes on EVERY API call, as the plan says — which changes more than the store: every
  API rate limit keyed by address now counts each website visitor separately instead of the
  whole website as one (loopback) caller. The API trusts the header only from loopback.
- (S3) The ben.cart cookie is made only for page requests (GET, text/html, no file extension, not
  /_blazor or /media) and only while features.store is on: nobody gets a cookie from a dark shop.
  A malformed value is replaced.
- (S3) The four surfaces share StoreCartLine (four modes) and StoreCartView.ShippingText; the page
  uses StoreOrderSummary, the drawer and the header menu their compact rows with the same
  data-testids — StoreCartSurfacesAgreeTests (source) and The_four_surfaces_agree (screen).
- (S3) Until S4.11 there was no "To checkout". S4.11 adds it to the cart page's summary and to the
  drawer (Smarty's red button, only when the cart can check out), and makes the steps' "Place order"
  a link from the cart (and "Cart" a link back from the checkout); later steps are never links.
- (S4.10) The checkout's validation lives once, in StoreCheckoutRules (Ben.Service.Models): the
  server refuses with it and the page checks with it, so a sentence shown before sending is the one
  the server would say. CheckoutForm refuses every change while its order is being paid for; "Edit"
  unlocks and forgets the placed order.
- (S4.11) The checkout is one page in two steps: "Continue to payment" places the order and mounts
  Stripe's Payment Element (Stripe.js loaded only then); the steps move to Payment once it is up. A
  countdown shows the hold; the status is re-read every 30 s and a lapsed hold re-prepares by itself
  with "Your reservation expired — we've checked stock again." An expired hold not yet released is
  REUSED by the server with a fresh expiry, so the same PaymentIntent (and client secret) carries on;
  the plan's "a new ClientSecret" was not what the service does, and the test checks the notice and a
  fresh countdown instead.
- (S4.11) The thank-you page is not behind the switch. It reads the status with the cart header
  (the browser that placed the order, for an hour), and the status's own OrderUrl token opens the
  items and totals. Polls back off over about a minute, then says the email will follow.
- (S4.11, found by the browser suite) The checkout's rate limit (10 a minute per address) answered
  429 with a JSON body, which the client's prose test drops — and the page said "The checkout
  couldn't be started just now", which was false. SendExpectingReasonAsync and
  SendExpectingConflictAsync now answer a 429 with WebApiClient.TooManyTries ("wait a minute and try
  again") — RateLimitedSendsSayWaitTests, both seen failing without the fix. Every e2e browser is a
  guest from 127.0.0.1, so the checkout fixture meets the limit; its presses wait a minute and press
  again when told to, as a buyer would.
- (S4.11, by eye 09/24, isolated hosts, phone and desktop, light and dark)
  - Floating labels drew their placeholder underneath: app.css makes every placeholder visible
    with !important, which beat Bootstrap's floating-label rule. app.css now exempts
    `.form-floating > .form-control` (the store is the only user of floating labels).
  - The phone's checkout bar was sticky and never stuck (the layout scrolls in a container): it sat
    at the end of the page. Now fixed-position like Smarty's, with a spacer; Checkout_fits_a_phone
    asserts it is at the bottom of the screen, which the first version of the test did not.
  - The lock beside "All transactions are secured" and the thank-you tick's colour.
- (S4.11) Not in the browser suite: "Without Stripe the checkout says so" needs a host with no keys
  and no fake — the refusal (PaymentsNotSetUp, 503 prose) is covered by StoreCheckoutServiceTests.
- (S3) StoreCartState takes IBenAdminClient, not IBenStoreClient: the website registers the one
  adapter under that name only, and the narrower interface stopped the site from STARTING. No unit
  test builds the website's real container — the e2e harness (host refused to start) found it.
- (S3, found by eye and a flake) Every store page BLINKED: the server drew it, then the live page
  rebuilt it from nothing ("Opening the store…" and back). StoreHome, StoreListing and StoreProduct
  now carry what the server fetched into the live page ([PersistentState]).
  - That broke the listing and the front page outright. The carried state is sent back in the
    circuit's first message: 35 KB and 36 KB, over SignalR's 32 KB default, so the server hung
    up and the pages were drawn but dead. The limit is now 128 KB (Program.cs AddHubOptions).
  - The first blink test PASSED against those dead pages: a page that never comes alive never
    blinks. It now also fails on a refused connection and opens the cart menu.
    The_carried_state_fits_the_connection keeps each page under 64 KB.
  - Shown to discriminate: all 3 pages blink without the fix, and 2 fail at the old 32 KB limit.
- (S3) The admin stock and product lists kept a stale answer: the first, unfiltered load (slow
  with 124 variants) arrived after the search's and refilled the list. Only the newest load's
  answer is kept now.
- (S3) Not written: the plan's Cart_page_shows_a_retry_when_the_api_refuses browser test — there
  is no clean way to make the e2e API refuse the cart on demand. The refusal is covered by
  StoreRefusalReachesThePageTests (cart-refusal row) and StoreCartStateTests (a failed read keeps
  LoadError, never an empty cart).

## Slices (status)
| Slice | What | Status |
|---|---|---|
| S0 | flag, 21 entities, 3 migrations, purge, static helpers, rate-limit partition | Built 09/24/2026 — every new fact seen failing first (mutations recorded in the commit) |
| S1 | admin catalogue (categories, products, options, variants, pictures, stock + CSV both ways, coupons, reviews moderation, settings, dashboard), tax probe, image serving, demo seed | Built 09/24/2026 — unit facts each seen failing on a deliberate break (64 breaks across S1.1–S1.12, all caught but one that exposed a redundant check, removed); AdminStoreCatalogTests 8/8 and AdminPageTests on IsHauntedDb_e2e with the shop off; new guards StoreClientRoutesTests, StoreRefusalReachesThePageTests, StoreReviewQueueHasAnEntranceTests each broken once |
| S2 | buyer help stub, public catalogue (home, listing, product + SuperAdmin preview), ben-store.css, nav | Built 09/24/2026 — PublicStoreController 12 facts (9 breaks caught), shared components 16 render facts (10 breaks), query string 8 facts; StoreBrowseTests/StoreProductTests/StoreFeatureFlagTests + crawls + admin 25/25 on IsHauntedDb_e2e with the store on; visual audit light+dark at four widths; guards StoreControllersAreGated, StoreLinksResolve, StoreProductProseClassIsGlobal, the Smarty deny-list, each broken once |
| S3 | cart — four surfaces, cookie identity, coupons at the cart, paused-store sentence | Built 09/24/2026 — server: StoreCartService/Controller 30 facts, 20 breaks caught; website: cookie, client headers, cart state, card button 20 facts, 14 breaks caught; guards StorePublicWritesAreRateLimited, StoreCartSurfacesAgree (incl. the badge outside the signed-in branch), cart-refusal and the two Apply rows, each broken once; StoreCartTests 7/7 + store/crawl/header suite 33/33 on IsHauntedDb_e2e. Between S3.3 and S3.4, at Ben's request: the Seller role and the editor's live preview (below) |
| S4 | checkout (locked form, Payment phase), Stripe gateway + tax, orders, confirmation letter, admin alert, seed orders, My Orders, invoice, guest/member lookup | |
| S5 | admin orders, pack/ship/deliver/cancel, refunds (status-aware, list-then-retry), address edit, re-send, release, exports, shipped/refunded letters, stock digest | |
| S6 | favourites, reviews, helpful votes (MyStoreEngagementController, gated) | |
| S7 | screenshots (incl. drawer, header menu, payment phase), product PDF, changelog, final guards | |
| S8 | rollout | |

## Rejected (and why)
- Extending BillingLedgerEntry, or a second StoreLedgerEntries table — the order row plus
  append-only refunds and events is the money record; a ledger nobody reads is a table without a
  consumer. Consequence: store money is not on /admin/billing-ledger (note + anchor there);
  reconcile from /admin/store/orders, the CSV exports and the order number in the Stripe
  statement descriptor.
- Reusing TaxResolver/TaxRateRule or the subscription Coupon — Ben's decision; both are
  period-shaped.
- Hosted Checkout instead of the Payment Element — Ben's decision.
- A shipping UserAddressType — the address is a snapshot on the order.
- Cart token in localStorage / a Guid in the route — an HttpOnly cookie set by middleware is
  never in JS and renders the badge on first interactive render.
- Product-level low-stock threshold — one site setting keeps card, page, dashboard and digest
  agreeing.
- Hashing the order access token — three letters over an order's life carry the link (it is
  re-minted only when the buyer email is corrected).
- A SuperAdmin bypass of the store switch — QA happens on e2e/UAT with the flag on; a preview of
  an INACTIVE product (?preview=1) is built instead.
- Feature-gating the image endpoint — the back office needs pictures while the shop is dark.
- Automatic payment methods — ACH/BNPL sit in processing for days; card (+ opt-in Link) only.
- Stripe's own receipt email — two reference numbers for one order.
- Line-level tax reversals — they need transaction line-item ids we never store; flat amounts.
- Keying rate limits by cart token or lookup token — client-supplied values are not a limit.
- Re-sending a refund with the same idempotency key — replays the original for 24 h, doubles after.
- The `ben-article-body` prose class — scoped to PublicationPostReader; a global class instead.

## NOT built (v1)
Reward points (slot comment only), brands and brand pages (the product page links to the one
equipment-catalogue model, labelled as such), compare, quick-view, grid/list toggle, countdown and
typed-text offer cards, newsletter, promo popup, Smarty's 0–10 recommendation box, customer
photos on reviews, a review-request letter, non-US shipping, price-changing add-ons, category
parents (one nullable column later), admin-managed hero banners (slides are categories with a
picture), Size Guide popup (no sizes to guide), "+ gift" badge and gift lines (no gifts), "Pickup
in store" (no store), billing VAT/bank/IBAN fields (US-only, card-only), account sub-pages My
reviews / My refunds / My coupons / My addresses (reviews and refunds are on the order and product
pages; addresses are typed per order), product-page file upload (nothing to customise), the
benefit strip repeated above the footer (once is enough), bank debits/BNPL (PaymentMethodTypes is
explicit), line-level tax reversals, per-star rating checkboxes with counts ("& up" is one query
with the same intent), the checkout's "New client (creates account)" radio (sign-up is one anchor
away: "Create an account" → /register?returnUrl=/store/checkout), the 250×292 empty-cart
illustration (a Smarty asset; a sprite icon instead). Smarty's "Popular First" review sort IS built
(popular = most helpful first, the default).

## Ben's rules added 09/24/2026 (mid-S4)
- "When categories and subcategories are created, they are not displayed to an end user as an
  option unless there is something for sale under the category or subcategory. They are just
  available for sellers to use when adding their products. Only superadmins manage the categories
  and subcategories."
  - Already true:
    - Shelf tiles, the listing's category tree and the hero slides come from
      PublicStoreController.CategoryCardsAsync, which keeps only shelves with a live product.
    - Category CRUD is SuperAdmin-only.
  - Gap 1: `/store/c/{slug}` for an active shelf with nothing on sale opens an empty listing. It
    should answer "Page not found" like a hidden one.
  - Gap 2: SUBCATEGORIES do not exist (v1 left "category parents" out). Needs:
    - a nullable parent on StoreCategory (migration)
    - admin tree editing
    - listing tree nesting
    - a parent that shows only if it or a child has something for sale
    - the product editor's category picker grouped by parent
  - Scheduled as its own sitting straight after S4, before S5.
- "The initial state of the store is going to be turned off, but the superadmin has access to the
  records and pages we have already created for the store in order to populate it." Already the
  design:
  - features.store defaults OFF.
  - Every /admin/store page and API works with it off (AdminStoreCatalogTests run dark).
  - The editor's live Preview works dark.
- Documentation, when everything is debugged, gap-free and green:
  - help for the general audience, sellers and administration, like the rest of the help, with
    screenshots (S7);
  - the product PDF regenerated with it once merged to master.
  - Sellers' help covers what the Seller role means today; the seller workspace itself is
    backlog item 251.

## Sellers (built 09/24/2026, between S3.3 and S3.4 at Ben's request)
- The additive **Seller** site role (RoleNames.Seller, seeded by SuperAdminSeeder, given on the
  person's Site Roles tab). Ben: "A seller only has control over their individual items in the
  store, not the admin part or pricing."
- `StoreProduct.SellerAppUserId`: the product editor's Seller field lists only role holders
  (`GET api/admin/store/products/sellers`); a person who later loses the role stays on what they
  had; the product list has a Seller column. Closure/purge clears it (shared AnonymiseAsync), and
  the purge census skips that one column (`clearedColumns`), so a seller stays deletable.
- The seller's own workspace follows the store (backlog item 251), with Ben's rules: sellers add
  hidden drafts, an admin sets the price and puts an item on sale, a seller can take their own
  item off sale, and edits to a live item show immediately.

## Live preview in the product editor (built 09/24/2026, Ben's request)
- Ben: "have a preview button so you can see a preview of the store page as it is being
  configured and created." The editor's **Preview** tab (and its header button, and the products
  list's Preview action, `/admin/store/products/{id}/edit?tab=preview`) draws the product page
  from the form as it stands, saved or not.
- ONE component: the product page's body moved into `Store/Shared/StoreProductView.razor`, which
  both `/store/p/{slug}` and the editor draw. `StoreProductPreview.Build` (Ben.Service.Models)
  applies the public page's rules to the admin record plus the unsaved drafts: live variants
  only, option values a live variant uses, an old price only when higher, "New" to the end of the
  day.
- Found on the way: the old Preview (`/store/p/{slug}?preview=1`) is behind the store switch, so
  with the shop dark — the catalogue-entry period it existed for — it answered "Page not found".
  The editor's preview is built in the page and works dark; "Open the saved page" shows only
  when the store is on.

## Queued after S8: Ben's store enhancements (09/24/2026)
Ben's `Store Enhancements.md` list is backlog item 251 in `ProjectNotes/Future-Improvements.md`.
It is to be started once the store ships. Nothing in it changes S3–S8.

Where it overlaps what this branch builds, it widens something that exists:
- the admin product grid gets cost, fee and seller columns
- `StoreOrderEvent` is already the status ledger with a time on every row; it gains seller steps and a refund-request flow
- the orders page and invoice gain a running-total view with a receipt modal
- reviews gain a per-product switch
- the store-wide returns window gains per-product return and warranty text

The rest is new:
- sellers who are members
- a parts list and cost basis per unit
- seller files and videos
- FAQs
- questions to the seller
- product versions

Whether sellers make the store a marketplace (payouts, merchant of record) is the first thing to decide.

## Stripe test-mode run
(date, payment intent id, tax transaction id, refund id — filled in at S4 exit)
Fixture capture: (command, date, webhook endpoint api_version — filled in at S4.2)

## Rollout state
Flag OFF in production until Ben turns it on; §8 of the plan is the checklist.
