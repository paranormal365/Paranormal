namespace Ben.Data.Common.Mail;

/// <summary>
/// What kind of letter this is, declared rather than guessed (item 246).
/// </summary>
/// <remarks>
/// <para><b>Why a letter needs a name it chose.</b> Until now the outbox worked one out from the
/// subject line — "Your place at Halloween Lock-In is confirmed" became
/// <c>your-place-at-halloween</c>. That was a deliberate trade in item 239a: a guess good enough to
/// group a screen, bought without editing twenty callers. A template cannot be keyed to a guess.
/// Change a subject and the template silently stops applying; two letters that happen to open with
/// the same four words share one.</para>
///
/// <para><b>A string key, not an enum.</b> The key is stored in the outbox, joined to by a
/// template row, and will outlive several rewrites of the code that sends it. An enum would put
/// those same strings in a migration every time one was added, and an appended enum value is the
/// thing this codebase keeps having to add a guard for.</para>
///
/// <para><b>The context is the security boundary.</b> <see cref="MailKindInfo.Context"/> is what a
/// letter of this kind actually has in its hands when it is written, and therefore the only tables
/// a token in its template may read. Ben chose that over "every table with a column allowlist"
/// (2026-09-20): a token naming a table the letter never loaded has no row to resolve against and
/// would render blank, and allowing every table would be a way to put a password hash or a live
/// reset link into an email.</para>
/// </remarks>
/// <summary>
/// A value only the mailer can work out, offered to a template by name (item 246).
/// </summary>
/// <remarks>
/// <para>The third kind of token, and the one without which some letters are pointless: a
/// confirmation link, a reset code, the QR a guest is admitted on. None of them is a column of
/// anything — they are minted when the letter is written — so neither the table dropdown nor the
/// ready-made list can offer them.</para>
///
/// <para><b><see cref="Required"/> means the letter does not work without it.</b> A confirmation
/// email with no link is a letter nobody can act on, and it would look perfectly fine in the
/// preview. Saving a template that drops one is refused.</para>
///
/// <para><b><see cref="IsHtml"/> is a narrow exception to escaping.</b> A pass is an
/// <c>&lt;img&gt;</c> carrying five kilobytes of base64 that the SITE generated, so it goes in as
/// markup. Nothing a person typed is ever treated this way — table columns and ready-made tokens
/// stay escaped without exception.</para>
/// </remarks>
/// <param name="Provides">
/// What this token gives the reader, when more than one token gives the same thing. A confirmation
/// letter needs a way to confirm — <c>{ConfirmUrl}</c> in the author's own wording, or
/// <c>{ConfirmButton}</c> as the site's button — and either will do. Without this the required
/// check asked for one specific spelling and refused a template that used the other, which is
/// exactly what the starter tests caught.
/// </param>
public sealed record MailSuppliedToken(
    string Name, string What, bool Required = false, bool IsHtml = false, string? Provides = null);

public sealed record MailKindInfo(
    string Key,
    string Title,
    string Description,
    IReadOnlyList<string> Context,
    IReadOnlyList<MailSuppliedToken>? Supplied = null,
    bool CanDecline = false)
{
    /// <summary>Values the mailer hands in when this letter is written.</summary>
    public IReadOnlyList<MailSuppliedToken> Supplied { get; init; } = Supplied ?? [];

    /// <summary>
    /// Whether somebody may ask not to receive this.
    /// </summary>
    /// <remarks>
    /// <para><b>False unless somebody says otherwise</b>, deliberately. The alternative default
    /// would make every letter added in future optional the moment it is declared, including the
    /// next one that turns out to be a password reset — and a preferences screen that lets a
    /// person switch off the letter they need to get back into their account is a defect wearing
    /// a feature's clothes.</para>
    ///
    /// <para>So the essential ones — proving an address, resetting a password, a receipt, a
    /// warning that somebody used your address — simply never appear on the screen, and the rest
    /// are marked one at a time.</para>
    /// </remarks>
    public bool CanDecline { get; init; } = CanDecline;
}

/// <summary>Every letter the site sends, by name.</summary>
public static class MailKinds
{
    /// <summary>What the outbox records when nobody said (the old guess still applies).</summary>
    public const string Unknown = "unknown";

    // ── Getting in, and staying in ────────────────────────────────────────────
    public static readonly MailKindInfo ConfirmYourAddress = new(
        "confirm-your-address", "Confirm your address",
        "Sent when somebody signs up, or adds an address to an account.",
        ["AppUsers"],
        [new("ConfirmUrl", "The link that confirms the address.", Required: true, Provides: "a way to confirm"),
         new("ConfirmButton", "A ready-made button pointing at that link.", IsHtml: true, Provides: "a way to confirm")]);

    public static readonly MailKindInfo ResetYourPassword = new(
        "reset-your-password", "Reset your password",
        "The link, or the code, that lets somebody set a new password.",
        ["AppUsers"],
        [new("ResetUrl", "The link that opens the reset page.", Required: true, Provides: "a way to reset"),
         new("ResetButton", "A ready-made button pointing at that link.", IsHtml: true, Provides: "a way to reset"),
         new("ResetCode", "The code to type, for a mail client that mangles links.")]);

    /// <summary>
    /// Somebody else made this person an account, and until this letter they had no way to know.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it carries a way to set a password rather than a password.</b> The account was
    /// made with one somebody else chose and typed, which means the only person who currently
    /// knows how to get in is not its owner. Mailing that password would make it two people who
    /// know it, permanently, in a message that sits in an inbox. So the letter carries a link that
    /// lets the owner choose their own, which takes the account away from whoever set it up —
    /// which is the whole point of telling them.</para>
    ///
    /// <para><b>Not declinable.</b> "An account exists in your name" is not a preference. Somebody
    /// who has opted out of everything this site sends still has to be told that, or the opt-out
    /// becomes the reason they never find out.</para>
    ///
    /// <para><b>No Organizations table</b>, because the one thing that sends this has no
    /// organization to name: a group adding somebody sends them an invitation to accept, not an
    /// account they never asked for. Offering <c>{Organizations.Name}</c> here would be a token
    /// that renders empty every time.</para>
    /// </remarks>
    public static readonly MailKindInfo AccountMadeForYou = new(
        "account-made-for-you", "An account was made for you",
        "Somebody made an account under this address, and its owner has not signed in yet.",
        ["AppUsers"],
        [new("SetPasswordUrl", "The link that lets them choose their own password.",
             Required: true, Provides: "a way in"),
         new("SetPasswordButton", "A ready-made button pointing at that link.",
             IsHtml: true, Provides: "a way in"),
         new("MadeBy", "Who made the account, as the reader would recognise them.")]);

    public static readonly MailKindInfo SomebodyUsedYourAddress = new(
        "somebody-used-your-address", "Somebody tried to use your address",
        "Warns the holder of an address that it was entered on a new sign-up.",
        ["AppUsers"],
        [new("SignInUrl", "The sign-in page, for somebody who forgot they had an account."),
         new("SignInButton", "A ready-made button pointing at that link.", IsHtml: true)]);

    /// <summary>
    /// An investigation request made under an address that already has an account, and the one link
    /// that can claim it.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="SomebodyUsedYourAddress"/> on 2026-09-23. Both letters went out under
    /// that one kind, so a single template replaced both — and the one published on production had no
    /// link in it at all, so this letter would have told somebody a request was waiting and given
    /// them no way to claim it. Its link is REQUIRED here, so a template without it is refused.
    /// </remarks>
    public static readonly MailKindInfo RequestMadeUnderYourAddress = new(
        "request-made-under-your-address", "A request was made using your address",
        "Tells an account holder that an investigation request was made with their address, and how to claim it.",
        ["AppUsers"],
        [new("FinishUrl", "The link that opens the request so they can add it to their account.",
             Required: true, Provides: "a way to claim the request"),
         new("FinishButton", "A ready-made button pointing at that link.", IsHtml: true,
             Provides: "a way to claim the request"),
         new("StreetAddress", "Where the investigation was asked for — what lets them recognise it.")]);

    // ── A case, and its client ────────────────────────────────────────────────
    public static readonly MailKindInfo CaseStatusChanged = new(
        "case-status-changed", "Your case has moved on",
        "Tells a client their case reached a new state.",
        ["AppUsers", "Cases", "Organizations"]);

    public static readonly MailKindInfo VisitScheduled = new(
        "visit-scheduled", "A visit is booked",
        "Tells a client when somebody is coming.",
        ["AppUsers", "Cases", "Organizations", "Investigations"]);

    public static readonly MailKindInfo VisitRescheduled = new(
        "visit-rescheduled", "A visit has moved",
        "Tells a client a booked visit changed date or time.",
        ["AppUsers", "Cases", "Organizations", "Investigations"]);

    public static readonly MailKindInfo VisitCancelled = new(
        "visit-cancelled", "A visit is off",
        "Tells a client a booked visit will not happen.",
        ["AppUsers", "Cases", "Organizations", "Investigations"]);

    // ── Somebody asking a group for help ──────────────────────────────────────
    public static readonly MailKindInfo RequestOpenedForReview = new(
        "request-opened-for-review", "A request is open for review",
        "Tells groups that a new request may be voted on.",
        ["AppUsers", "Organizations"],
        CanDecline: true);

    public static readonly MailKindInfo RequestAccepted = new(
        "request-accepted", "A group accepted the request",
        "Tells the person who asked that somebody took it.",
        ["AppUsers", "Organizations", "Cases"]);

    public static readonly MailKindInfo RequestNoLongerAvailable = new(
        "request-no-longer-available", "A request has gone",
        "Tells the other groups that a request they could see was taken or withdrawn.",
        ["AppUsers", "Organizations"],
        CanDecline: true);

    // ── A tour ────────────────────────────────────────────────────────────────
    public static readonly MailKindInfo TourSignUp = new(
        "tour-sign-up", "You're coming on a tour",
        "Confirms a place on a walk, with the details of where and when.",
        ["AppUsers", "Tours", "Organizations"],
        // The same two a hosted event's confirmation carries, so somebody writing either letter
        // puts the pass where they want it rather than where the code decided (item 247).
        [new("PassImage", "The pass QR, drawn into the letter itself so a blocked image cannot "
                        + "leave a guest at the meeting point with nothing to show.", IsHtml: true),
         new("PassUrl", "Where the same code can be opened, if the picture did not load.")],
        CanDecline: true);

    public static readonly MailKindInfo TourReminder = new(
        "tour-reminder", "Your tour is soon",
        "The reminder before a walk.",
        ["AppUsers", "Tours", "Organizations"],
        CanDecline: true);

    // ── A hosted event, for the guest ─────────────────────────────────────────
    public static readonly MailKindInfo BookingAsked = new(
        "booking-asked", "We have your request",
        "Acknowledges a booking request before anybody has decided it.",
        ["AppUsers", "HostedEvents", "HostedEventBookings", "Organizations"]);

    public static readonly MailKindInfo BookingDecided = new(
        "booking-decided", "Your booking is confirmed",
        "The decision on a booking, and the pass when it is a yes.",
        ["AppUsers", "HostedEvents", "HostedEventBookings", "Organizations"],
        [new("PassImage", "The entry QR, drawn into the letter itself so a blocked image cannot "
                        + "leave a guest at the door without one.", IsHtml: true),
         new("PassUrl", "Where the same code can be opened, if the picture did not load.")]);

    public static readonly MailKindInfo HoldPlaced = new(
        "hold-placed", "We are holding your place",
        "Tells a guest a place is held, and until when.",
        ["AppUsers", "HostedEvents", "HostedEventBookings", "Organizations"]);

    public static readonly MailKindInfo HoldLapsed = new(
        "hold-lapsed", "Your hold has run out",
        "Tells a guest a held place was released.",
        ["AppUsers", "HostedEvents", "HostedEventBookings", "Organizations"]);

    public static readonly MailKindInfo EventGoingAhead = new(
        "event-going-ahead", "The event is going ahead",
        "The go decision, to everybody holding a booking.",
        ["AppUsers", "HostedEvents", "HostedEventBookings", "Organizations"],
        CanDecline: true);

    public static readonly MailKindInfo EventCalledOff = new(
        "event-called-off", "The event is off",
        "The no-go decision, to everybody holding a booking.",
        ["AppUsers", "HostedEvents", "HostedEventBookings", "Organizations"],
        CanDecline: true);

    public static readonly MailKindInfo EventAnnouncement = new(
        "event-announcement", "A message from the organisers",
        "Whatever the organisers wrote to everybody coming.",
        ["AppUsers", "HostedEvents", "Organizations"],
        CanDecline: true);

    public static readonly MailKindInfo SessionMoved = new(
        "session-moved", "A session has moved",
        "Tells somebody signed up to a session that it changed.",
        ["AppUsers", "HostedEvents", "HostedEventSessions", "Organizations"],
        CanDecline: true);

    public static readonly MailKindInfo SessionCancelled = new(
        "session-cancelled", "A session is off",
        "Tells somebody signed up to a session that it will not run.",
        ["AppUsers", "HostedEvents", "HostedEventSessions", "Organizations"],
        CanDecline: true);

    public static readonly MailKindInfo SessionPromoted = new(
        "session-promoted", "A place came free",
        "Tells somebody on a waiting list they are in.",
        ["AppUsers", "HostedEvents", "HostedEventSessions", "Organizations"],
        CanDecline: true);

    public static readonly MailKindInfo EventThankYou = new(
        "event-thank-you", "Thank you for coming",
        "After the event, with anything the organisers left for attendees.",
        ["AppUsers", "HostedEvents", "Organizations"],
        CanDecline: true);

    /// <summary>
    /// Asks somebody to help at an event, with the one link that says yes.
    /// </summary>
    /// <remarks>
    /// <para><b>Until 2026-09-23 this declared nothing a template could use to accept.</b> The link
    /// carries a fresh single-use token, which is no column of anything, so a template for this
    /// letter could not include it — publishing one would have sent every helper an invitation they
    /// had no way to answer. <c>AcceptUrl</c> is therefore required, and the editor refuses a
    /// template without it.</para>
    ///
    /// <para><b>The rest is what the built-in letter says and a table cannot:</b> the venue (a
    /// <c>Places</c> row this letter does not carry), the role, and what accepting lets them do —
    /// the same list the acceptance page shows, from the same method.</para>
    /// </remarks>
    public static readonly MailKindInfo StaffInvite = new(
        "staff-invite", "You have been asked to help",
        "Invites somebody to work an event, with the link that accepts.",
        ["AppUsers", "HostedEvents", "Organizations"],
        [new("AcceptUrl", "The link that accepts. It works once and lasts a fortnight.",
             Required: true, Provides: "a way to accept"),
         new("AcceptButton", "A ready-made button pointing at that link.",
             IsHtml: true, Provides: "a way to accept"),
         new("Venue", "Where the event is, when it has a place; empty when it does not."),
         new("Role", "What the group has them down as, e.g. \"Door\"; empty when not given."),
         new("CanDo", "What accepting lets them do, as a list.", IsHtml: true)]);

    /// <summary>
    /// Tells an event's organizers that IsHaunted took their event down, and how to appeal.
    /// </summary>
    /// <remarks>
    /// <para><b>Not a letter to a guest, whatever the key says.</b> Until 2026-09-23 this was titled
    /// "About your place" and described as telling somebody they were removed from an event, and the
    /// template written from that description greeted an organizer as a cancelled guest, with a blank
    /// reason and a Chicago time labelled UTC. The key stays <c>guest-removed</c> because stored
    /// templates are keyed to it; the words an author reads are what changed.</para>
    ///
    /// <para><b>There is no reason to print.</b> A removal clears <c>CancelledReason</c> on purpose —
    /// the moderator's note stays on the removal record, where naming a complainant cannot reach the
    /// organizer — so <c>{HostedEvents.CancelledReason}</c> is always empty here, and
    /// <c>CancelledAtUtc</c> is when it was removed.</para>
    /// </remarks>
    public static readonly MailKindInfo GuestRemoved = new(
        "guest-removed", "Your event was removed",
        "Tells an event's organizers that IsHaunted removed their event, and how to appeal.",
        ["AppUsers", "HostedEvents", "Organizations"],
        [new("AppealUrl", "The event's page, at the card where they appeal the removal."),
         new("AppealButton", "A ready-made button pointing at that link.", IsHtml: true),
         new("CreditNote", "\"The event credit spent on it has been returned…\" when it was, and nothing when it was not.")]);

    public static readonly MailKindInfo AppealAnswered = new(
        "appeal-answered", "Your appeal has been answered",
        "The answer to somebody appealing a removal.",
        ["AppUsers", "HostedEvents", "Organizations"],
        CanDecline: true);

    /// <summary>
    /// The fifteen-minute link that holds the seats or rooms a guest picked without signing in.
    /// </summary>
    /// <remarks>
    /// <para><b>This letter was filed as "choose-your-emails" — "Choose what we send you, the link
    /// that lets a guest change what they hear about" — from 2026-09-20 until this replaced it.</b>
    /// No such letter exists. The name reads like the method that sends this one,
    /// <c>SendEmailPickLinkAsync</c>, taken for "pick your emails" rather than "picked by email". So
    /// /admin/mail and the template editor both showed a seat hold as a preferences letter, and the
    /// kind declared no link: a template written for it would have been accepted with no way to hold
    /// anything, and a guest would have had fifteen minutes and nothing to press. Nothing was ever
    /// stored under the old key — no queued letter, no template, on either database (checked
    /// 2026-09-23) — so it was retired rather than aliased.</para>
    ///
    /// <para><b>AppUsers is the person, not an account.</b> Nobody has an account until they press
    /// the button; the row a template reads is built from the address and first name they gave.</para>
    /// </remarks>
    public static readonly MailKindInfo HoldYourPlaces = new(
        "hold-your-places", "Hold the places you picked",
        "The fifteen-minute link a guest follows to hold the seats or rooms they picked without signing in.",
        ["AppUsers", "HostedEvents", "Organizations"],
        [new("HoldUrl", "The link that holds the places they picked.", Required: true,
             Provides: "a way to hold the places"),
         new("HoldButton", "A ready-made button pointing at that link.", IsHtml: true,
             Provides: "a way to hold the places"),
         new("HoldUntil", "When the places go back if nobody presses it, on the venue's own clock."),
         new("Places", "What they picked, as a list.", IsHtml: true)]);

    // ── A hosted event, for the venue ─────────────────────────────────────────
    public static readonly MailKindInfo BookingsArrived = new(
        "bookings-arrived", "Bookings have arrived",
        "Tells the people who decide bookings that some are waiting.",
        ["AppUsers", "HostedEvents", "Organizations"],
        CanDecline: true);

    public static readonly MailKindInfo BookingsDigest = new(
        "bookings-digest", "Where your event stands",
        "The daily or weekly summary of an event's bookings.",
        ["AppUsers", "HostedEvents", "Organizations"],
        CanDecline: true);

    // ── Money, and the plan ───────────────────────────────────────────────────
    public static readonly MailKindInfo PaymentReceipt = new(
        "payment-receipt", "Your receipt",
        "What was paid, for what, and when.",
        ["AppUsers", "Organizations", "BillingLedgerEntries"]);

    public static readonly MailKindInfo SubscriptionLapsing = new(
        "subscription-lapsing", "Your plan is about to lapse",
        "Warns a group that payment has not arrived.",
        ["AppUsers", "Organizations"],
        CanDecline: true);

    public static readonly MailKindInfo PlanChanged = new(
        "plan-changed", "Your plan has changed",
        "Tells a group what a change to their band means for them.",
        ["AppUsers", "Organizations"],
        CanDecline: true);

    public static readonly MailKindInfo EventCreditExpiring = new(
        "event-credit-expiring", "An event credit expires soon",
        "The warning before a bought credit runs out.",
        ["AppUsers", "Organizations"],
        CanDecline: true);

    // ── Looking after a place ─────────────────────────────────────────────────
    /// <remarks>
    /// The code is the letter. It was not declared until 2026-09-23, so a template for this kind could
    /// be saved without it and would have proved nothing; it is REQUIRED now.
    /// </remarks>
    public static readonly MailKindInfo VenueClaimCode = new(
        "venue-claim-code", "A code to confirm who runs this place",
        "Sent to a venue's own published address to prove a claim.",
        ["AppUsers", "Places", "Organizations"],
        [new("ClaimCode", "The code to hand to whoever is making the claim.", Required: true,
             Provides: "the code")]);

    // ── The store (storefront S4.4) ────────────────────────────────────────────
    // AppUsers is the buyer as an account OR as the name and email a guest typed (MailRows.Person);
    // StoreOrders is the order row (its access token is never offered — MailTemplateSchema).

    public static readonly MailKindInfo StoreOrderConfirmation = new(
        "store-order-confirmation", "Thank you for your order",
        "The receipt a buyer gets when their store order is paid.",
        ["AppUsers", "StoreOrders"],
        [new("OrderUrl", "The order's own page — with its private link for a guest.", Required: true, Provides: "a way to see the order"),
         new("ItemsTable", "What was bought: each item, how many, and its price.", IsHtml: true),
         new("SummaryTable", "Products, discount, shipping, sales tax and the total.", IsHtml: true),
         new("ShipTo", "Where the parcel is going, on separate lines.", IsHtml: true)]);

    public static readonly MailKindInfo StoreOrderPlaced = new(
        "store-order-placed", "A new store order",
        "Tells every SuperAdmin a store order has been paid and is waiting to be packed.",
        ["AppUsers", "StoreOrders"],
        [new("AdminOrderUrl", "The order in the store's administration.", Required: true, Provides: "a way to open the order"),
         new("ItemsTable", "What was bought: each item, how many, and its price.", IsHtml: true)]);

    public static readonly MailKindInfo StoreOrderLink = new(
        "store-order-link", "A link to your order",
        "Sends a buyer the way back to an order they asked to find again.",
        ["AppUsers", "StoreOrders"],
        [new("OrderUrl", "The order's page — a private link for a guest, the sign-in page for a member.", Required: true,
             Provides: "a way to see the order")]);

    public static readonly MailKindInfo StoreOrderShipped = new(
        "store-order-shipped", "Your order is on its way",
        "Tells a buyer their store order has shipped, with the carrier and tracking number.",
        ["AppUsers", "StoreOrders"],
        [new("OrderUrl", "The order's own page — with its private link for a guest.", Required: true, Provides: "a way to see the order"),
         new("Carrier", "Who is carrying it — USPS, UPS, FedEx, DHL or Other."),
         new("TrackingNumber", "The carrier's tracking number."),
         new("TrackingUrl", "The carrier's own tracking page for that number; empty for Other."),
         new("ItemsTable", "What was shipped: each item, how many, and its price.", IsHtml: true),
         new("Package", "Which package this is, when an order ships as more than one — \"Package 1 of 2\"; empty otherwise.")]);

    public static readonly MailKindInfo StoreOrderRefunded = new(
        "store-order-refunded", "A refund on your order",
        "Tells a buyer money from their store order is on its way back to their card.",
        ["AppUsers", "StoreOrders"],
        [new("OrderUrl", "The order's own page — with its private link for a guest.", Required: true, Provides: "a way to see the order"),
         new("RefundAmount", "How much was refunded this time, in dollars."),
         new("RefundReason", "Why, in the words the admin gave."),
         new("RefundLines", "The items refunded and how many, when the refund was by item.", IsHtml: true)]);

    public static readonly MailKindInfo StoreSellerParcelToShip = new(
        "store-seller-parcel-to-ship", "A package for you to ship",
        "Tells a seller a paid store order has a package of their items to send, with where it's going.",
        ["AppUsers", "StoreOrders"],
        [new("SellerPackagesUrl", "The seller's packages page, where they pack and ship it.", Required: true, Provides: "a way to ship it"),
         new("ItemsTable", "What goes in the package: each item and how many.", IsHtml: true),
         new("ShipTo", "Where the package is going, on separate lines.", IsHtml: true)]);

    public static readonly MailKindInfo StoreLowStock = new(
        "store-low-stock", "Store stock is running low",
        "The daily note to every SuperAdmin of store variants at or under the low-stock number.",
        ["AppUsers"],
        [new("AdminStockUrl", "The store's stock page in administration.", Required: true, Provides: "a way to restock"),
         new("StockTable", "Each low variant: product, variant, SKU and how many are left.", IsHtml: true)]);

    /// <summary>Every kind, in the order a person should see them.</summary>
    public static readonly IReadOnlyList<MailKindInfo> All =
    [
        ConfirmYourAddress, ResetYourPassword, AccountMadeForYou, SomebodyUsedYourAddress,
        RequestMadeUnderYourAddress,
        CaseStatusChanged, VisitScheduled, VisitRescheduled, VisitCancelled,
        RequestOpenedForReview, RequestAccepted, RequestNoLongerAvailable,
        TourSignUp, TourReminder,
        BookingAsked, BookingDecided, HoldPlaced, HoldLapsed,
        EventGoingAhead, EventCalledOff, EventAnnouncement,
        SessionMoved, SessionCancelled, SessionPromoted,
        EventThankYou, StaffInvite, GuestRemoved, AppealAnswered, HoldYourPlaces,
        BookingsArrived, BookingsDigest,
        PaymentReceipt, SubscriptionLapsing, PlanChanged, EventCreditExpiring,
        VenueClaimCode,
        StoreOrderConfirmation, StoreOrderPlaced, StoreOrderLink, StoreOrderShipped, StoreOrderRefunded, StoreLowStock,
        StoreSellerParcelToShip,
    ];

    /// <summary>The kind with this key, or null when nothing declares it.</summary>
    public static MailKindInfo? Find(string? key)
        => key is null ? null : All.FirstOrDefault(k => k.Key == key);
}
