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
        [MailKinds.StaffInvite.Key, MailKinds.EventAnnouncement.Key, MailKinds.AccountMadeForYou.Key]);

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
        Confirmation, PasswordReset, Receipt, Invoice, Invitation, BookingConfirmed, Plain,
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
