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

    public static readonly MailKindInfo AccountMadeForYou = new(
        "account-made-for-you", "An account was made for you",
        "Somebody was added by a group or a client, and has not signed in yet.",
        ["AppUsers", "Organizations"]);

    public static readonly MailKindInfo SomebodyUsedYourAddress = new(
        "somebody-used-your-address", "Somebody tried to use your address",
        "Warns the holder of an address that it was entered on a new sign-up.",
        ["AppUsers"]);

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

    public static readonly MailKindInfo StaffInvite = new(
        "staff-invite", "You have been asked to help",
        "Invites somebody to work an event.",
        ["AppUsers", "HostedEvents", "Organizations"]);

    public static readonly MailKindInfo GuestRemoved = new(
        "guest-removed", "About your place",
        "Tells somebody they were removed from an event.",
        ["AppUsers", "HostedEvents", "Organizations"]);

    public static readonly MailKindInfo AppealAnswered = new(
        "appeal-answered", "Your appeal has been answered",
        "The answer to somebody appealing a removal.",
        ["AppUsers", "HostedEvents", "Organizations"],
        CanDecline: true);

    public static readonly MailKindInfo ChooseYourEmails = new(
        "choose-your-emails", "Choose what we send you",
        "The link that lets a guest change what they hear about.",
        ["AppUsers", "HostedEvents"]);

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
    public static readonly MailKindInfo VenueClaimCode = new(
        "venue-claim-code", "A code to confirm who runs this place",
        "Sent to a venue's own published address to prove a claim.",
        ["AppUsers", "Places", "Organizations"]);

    /// <summary>Every kind, in the order a person should see them.</summary>
    public static readonly IReadOnlyList<MailKindInfo> All =
    [
        ConfirmYourAddress, ResetYourPassword, AccountMadeForYou, SomebodyUsedYourAddress,
        CaseStatusChanged, VisitScheduled, VisitRescheduled, VisitCancelled,
        RequestOpenedForReview, RequestAccepted, RequestNoLongerAvailable,
        TourSignUp, TourReminder,
        BookingAsked, BookingDecided, HoldPlaced, HoldLapsed,
        EventGoingAhead, EventCalledOff, EventAnnouncement,
        SessionMoved, SessionCancelled, SessionPromoted,
        EventThankYou, StaffInvite, GuestRemoved, AppealAnswered, ChooseYourEmails,
        BookingsArrived, BookingsDigest,
        PaymentReceipt, SubscriptionLapsing, PlanChanged, EventCreditExpiring,
        VenueClaimCode,
    ];

    /// <summary>The kind with this key, or null when nothing declares it.</summary>
    public static MailKindInfo? Find(string? key)
        => key is null ? null : All.FirstOrDefault(k => k.Key == key);
}
