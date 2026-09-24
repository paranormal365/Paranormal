namespace Ben.Data.Common.Mail;

/// <summary>
/// Letters already written, to start from rather than from a blank box (item 246).
/// </summary>
/// <remarks>
/// <para><b>A blank box is the reason a feature like this goes unused.</b> Somebody who opens the
/// editor meaning to reword one sentence should not first have to work out how email HTML is laid
/// out. Each of these is a whole letter built from <see cref="MailBlocks"/>, loaded into the
/// editor and then edited.</para>
///
/// <para><b><see cref="MailStarter.Suits"/> is checked, not decorative.</b> A starter offered for a
/// letter it does not fit would be refused the moment the author pressed Save — the tokens would
/// name tables that letter never carries. <c>MailStartersTests</c> renders every starter against
/// every kind it claims, so an unusable one cannot be offered.</para>
/// </remarks>
public sealed record MailStarter(
    string Key,
    string Title,
    string What,
    string Subject,
    string BodyHtml,
    IReadOnlyList<string> Suits);

public static class MailStarters
{
    private static string Join(params string[] parts) => string.Join("\n", parts);

    /// <summary>The site's own shell: name at the top, small print at the bottom.</summary>
    private static string Branded(params string[] middle)
        => Join(
            MailBlocks.TwoColumns(
                wide: "<strong>{SiteName}</strong>",
                logoOnTheRight: true),
            Join(middle),
            MailBlocks.Footer());

    public static readonly MailStarter Confirmation = new(
        "confirmation", "Confirm an address",
        "The logo, a line of explanation, and the button that confirms.",
        "Confirm your email",
        Branded(
            MailBlocks.Heading("Confirm your email"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName}, you are one click from finishing "
                               + "your account. Confirm this address and you can sign in."),
            "{ConfirmButton}",
            MailBlocks.Paragraph("If you did not create this account, ignore this message and "
                               + "nothing happens.")),
        [MailKinds.ConfirmYourAddress.Key]);

    public static readonly MailStarter PasswordReset = new(
        "password-reset", "Reset a password",
        "The button, with the code underneath for a mail client that mangles links.",
        "Reset your password",
        Branded(
            MailBlocks.Heading("Reset your password"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName}, use the button below to choose a "
                               + "new password."),
            "{ResetButton}",
            MailBlocks.Paragraph("If the button does not work, go to the reset page and enter this "
                               + "code: <strong>{ResetCode}</strong>"),
            MailBlocks.Paragraph("If you did not ask for this, ignore this message — your password "
                               + "will not change.")),
        [MailKinds.ResetYourPassword.Key]);

    public static readonly MailStarter Receipt = new(
        "receipt", "A receipt",
        "What was paid, for what, and when, with a total.",
        "Your receipt from {SiteName}",
        Branded(
            MailBlocks.Heading("Your receipt"),
            MailBlocks.Paragraph("Thank you, {AppUsers.DisplayName}. Here is what you paid on "
                               + "{FullDate}."),
            MailBlocks.LineItems(),
            MailBlocks.Paragraph("This receipt is for your records. Nothing further is needed.")),
        [MailKinds.PaymentReceipt.Key]);

    /// <summary>The store's receipt (S4.4).</summary>
    public static readonly MailStarter OrderConfirmation = new(
        "order-confirmation", "An order receipt",
        "Thanks, the order number, what was bought and what it cost, where it is going, and the order's page.",
        "Your order {StoreOrders.OrderNumber} from {SiteName}",
        Branded(
            MailBlocks.Heading("Thank you for your order"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName}. We have your order {StoreOrders.OrderNumber} "
                               + "and your payment. We will write again when it is on its way."),
            "{ItemsTable}",
            "{SummaryTable}",
            MailBlocks.Paragraph("It is going to:<br>{ShipTo}"),
            MailBlocks.Button("See your order", "{OrderUrl}"),
            MailBlocks.Paragraph("Keep this email: its button is how you get back to your order.")),
        [MailKinds.StoreOrderConfirmation.Key]);

    /// <summary>What every SuperAdmin hears when an order is paid (S4.4).</summary>
    public static readonly MailStarter NewOrderAlert = new(
        "new-order-alert", "A new order to pack",
        "The order number, what to pack, and the button to the order in administration.",
        "New store order {StoreOrders.OrderNumber}",
        Branded(
            MailBlocks.Heading("A new store order"),
            MailBlocks.Paragraph("Order {StoreOrders.OrderNumber} has been paid and is waiting to be packed."),
            "{ItemsTable}",
            MailBlocks.Button("Open the order", "{AdminOrderUrl}")),
        [MailKinds.StoreOrderPlaced.Key]);

    /// <summary>The way back to an order, for somebody who asked (S4.4).</summary>
    public static readonly MailStarter OrderLink = new(
        "order-link", "A link to an order",
        "One line of explanation and the button back to the order.",
        "Your order {StoreOrders.OrderNumber}",
        Branded(
            MailBlocks.Heading("Your order"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName}. You asked for a way back to order "
                               + "{StoreOrders.OrderNumber}. Here it is."),
            MailBlocks.Button("See your order", "{OrderUrl}"),
            MailBlocks.Paragraph("If you did not ask for this, you can ignore it — the link only shows "
                               + "the order to whoever has this email.")),
        [MailKinds.StoreOrderLink.Key]);

    /// <summary>The order is on its way (S5.2).</summary>
    public static readonly MailStarter OrderShipped = new(
        "order-shipped", "An order on its way",
        "The good news, the carrier and tracking number, what is in the parcel, and the order's page.",
        "Your order {StoreOrders.OrderNumber} is on its way",
        Branded(
            MailBlocks.Heading("Your order is on its way"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName}. Order {StoreOrders.OrderNumber} has shipped with "
                               + "{Carrier}. The tracking number is {TrackingNumber}."),
            "{ItemsTable}",
            MailBlocks.Button("Track it", "{TrackingUrl}"),
            MailBlocks.Paragraph("You can always see the order, and its tracking, on <a href=\"{OrderUrl}\">its page</a>.")),
        [MailKinds.StoreOrderShipped.Key]);

    /// <summary>Money on its way back (S5.2).</summary>
    public static readonly MailStarter OrderRefunded = new(
        "order-refunded", "A refund",
        "How much is coming back and why, what was refunded, and the order's page.",
        "A refund on your order {StoreOrders.OrderNumber}",
        Branded(
            MailBlocks.Heading("A refund on your order"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName}. We have refunded {RefundAmount} on order "
                               + "{StoreOrders.OrderNumber}: {RefundReason}."),
            "{RefundLines}",
            MailBlocks.Paragraph("It goes back to the card you paid with. Banks usually show it within 5–10 business days."),
            MailBlocks.Button("See your order", "{OrderUrl}")),
        [MailKinds.StoreOrderRefunded.Key]);

    /// <summary>The morning's low stock, for SuperAdmins (S5.6).</summary>
    public static readonly MailStarter LowStock = new(
        "low-stock", "Low stock",
        "The variants running low, and the button to the stock page.",
        "Store stock is running low",
        Branded(
            MailBlocks.Heading("Store stock is running low"),
            MailBlocks.Paragraph("These are at or under the store's low-stock number:"),
            "{StockTable}",
            MailBlocks.Button("Open the stock page", "{AdminStockUrl}")),
        [MailKinds.StoreLowStock.Key]);

    public static readonly MailStarter Invoice = new(
        "invoice", "An invoice",
        "The same table, worded as something still to pay, with a button.",
        "An invoice from {SiteName}",
        Branded(
            MailBlocks.Heading("An invoice"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName}. Here is what is due."),
            MailBlocks.LineItems(),
            MailBlocks.Button("Pay this", "{SiteUrl}"),
            MailBlocks.Paragraph("Any questions about this, reply to this message.")),
        [MailKinds.PaymentReceipt.Key, MailKinds.SubscriptionLapsing.Key]);

    public static readonly MailStarter Invitation = new(
        "invitation", "An invitation",
        "A card with the details, and a button to answer.",
        "You are invited",
        Branded(
            MailBlocks.Heading("You are invited"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName} — {Organizations.Name} would like "
                               + "you there."),
            MailBlocks.Card("The details", "Written out here: where, when, and anything to bring."),
            MailBlocks.Button("Answer", "{SiteUrl}"),
            MailBlocks.Paragraph("If this was not meant for you, ignore it.")),
        [MailKinds.StaffInvite.Key, MailKinds.EventAnnouncement.Key]);

    public static readonly MailStarter AccountMade = new(
        "account-made", "An account somebody else made",
        "What happened, who did it, and the button that puts the account in their hands.",
        "An account was made for you on {SiteName}",
        Branded(
            MailBlocks.Heading("An account was made for you"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName}. {MadeBy} made an account on "
                               + "{SiteName} using this address. You have not signed in to it."),
            MailBlocks.Paragraph("Choose your own password below. Until you do, the only password "
                               + "this account has is the one they typed."),
            "{SetPasswordButton}",
            MailBlocks.Paragraph("If you were not expecting this, choosing a password is still the "
                               + "safest thing to do — it takes the account out of anybody else's "
                               + "hands. Reply to this message if you would rather it was removed.")),
        [MailKinds.AccountMadeForYou.Key]);

    /// <summary>
    /// Holding the places somebody picked. Its own starter because the generic one has no hold link
    /// in it, and <see cref="For"/> rightly will not offer a letter that would be refused on save.
    /// </summary>
    public static readonly MailStarter HoldPlaces = new(
        "hold-places", "Hold the places you picked",
        "What they picked, when it goes back, and the button that holds it.",
        "Hold your places at {HostedEvents.Name} within 15 minutes",
        Branded(
            MailBlocks.Heading("Hold your places"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName}, you picked places at "
                               + "{HostedEvents.Name}. They are waiting for you until "
                               + "<strong>{HoldUntil}</strong> — press the button to hold them."),
            "{Places}",
            "{HoldButton}",
            MailBlocks.Paragraph("Once they are held, {Organizations.Name} answers you, and nobody "
                               + "else can take them in the meantime."),
            MailBlocks.Paragraph("If this wasn't you, do nothing: the places go back by themselves "
                               + "and no account is made.")),
        [MailKinds.HoldYourPlaces.Key]);

    /// <summary>A request waiting under somebody's address, and the link that claims it.</summary>
    public static readonly MailStarter RequestUnderYourAddress = new(
        "request-under-your-address", "A request made with your address",
        "Where it was asked for, and the button that claims it.",
        "An investigation request was made using your {SiteName} email",
        Branded(
            MailBlocks.Heading("Was this you?"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName}. Somebody asked for an investigation at "
                               + "<strong>{StreetAddress}</strong> using this email address, which "
                               + "already has an account."),
            MailBlocks.Paragraph("If that was you, sign in to finish it — the button below opens the "
                               + "request so you can add it to your account."),
            "{FinishButton}",
            MailBlocks.Paragraph("If it was not you, there is nothing to do. The request will be "
                               + "discarded on its own.")),
        [MailKinds.RequestMadeUnderYourAddress.Key]);

    /// <summary>The code that proves who runs a place.</summary>
    public static readonly MailStarter VenueCode = new(
        "venue-code", "A code to confirm a venue",
        "Who is asking, the code, and what to do if it is nobody you know.",
        "A code to confirm who runs {Places.Name}",
        Branded(
            MailBlocks.Heading("A code to confirm who runs {Places.Name}"),
            MailBlocks.Paragraph("Somebody from <strong>{Organizations.Name}</strong> says they run "
                               + "{Places.Name} and wants to be confirmed as its venue."),
            MailBlocks.Paragraph("If that is you, give them this code: <strong>{ClaimCode}</strong>"),
            MailBlocks.Paragraph("It works for 24 hours. If you don't know who this is, don't share it "
                               + "— nothing happens without the code.")),
        [MailKinds.VenueClaimCode.Key]);

    public static readonly MailStarter BookingConfirmed = new(
        "booking-confirmed", "A booking, with the pass",
        "The confirmation and the code they are admitted on.",
        "You're coming to {HostedEvents.Name}",
        Branded(
            MailBlocks.Heading("You're coming"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName} — your place at "
                               + "<strong>{HostedEvents.Name}</strong> is confirmed."),
            MailBlocks.Card("Show this at the door",
                "One code admits your whole party, so nobody else needs their own.<br/>{PassImage}"),
            MailBlocks.Paragraph("If the code above did not load, open it here: {PassUrl}")),
        [MailKinds.BookingDecided.Key]);

    public static readonly MailStarter Plain = new(
        "plain", "Something plain",
        "A heading and words, with nothing else in the way.",
        "A message from {SiteName}",
        Branded(
            MailBlocks.Heading("A heading"),
            MailBlocks.Paragraph("Hello {AppUsers.DisplayName}. Something worth saying.")),
        []);

    public static readonly IReadOnlyList<MailStarter> All =
    [
        Confirmation, PasswordReset, Receipt, Invoice, Invitation, AccountMade, HoldPlaces,
        RequestUnderYourAddress, VenueCode, BookingConfirmed, OrderConfirmation, NewOrderAlert, OrderLink,
        OrderShipped, OrderRefunded, LowStock, Plain,
    ];

    /// <summary>
    /// The starters worth offering for one letter.
    /// </summary>
    /// <remarks>
    /// A starter with an empty <c>Suits</c> fits anything, because it names nothing but the person
    /// the letter is going to — which every kind carries.
    /// </remarks>
    public static IReadOnlyList<MailStarter> For(MailKindInfo kind)
        => All
            .Where(s => s.Suits.Count == 0 || s.Suits.Contains(kind.Key))
            // "Fits anything" cannot mean a letter that would be refused on sight. A plain starter
            // has no confirmation link in it, so it is not a starting point for a confirmation
            // email — offering it would hand somebody a template that fails the moment they save.
            .Where(s => MailTokens.MissingRequired(s.Subject, s.BodyHtml, kind).Count == 0)
            .ToList();

    public static MailStarter? Find(string? key)
        => key is null ? null : All.FirstOrDefault(s => s.Key == key);
}
