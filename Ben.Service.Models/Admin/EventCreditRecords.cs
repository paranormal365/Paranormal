namespace Ben.Service.Models.Admin;

/// <summary>
/// One event credit as its holder sees it (item 235).
/// </summary>
/// <param name="SpentOnEventName">
/// The event it went on, when it has been spent. Named rather than only identified, because
/// "have we paid for this one?" is a question people ask about an event by its name.
/// </param>
public record OrgEventCreditRecord(
    Guid Id,
    decimal PriceAtPurchase,
    string Currency,
    DateTime PurchasedUtc,
    DateTime ExpiresUtc,
    DateTime? SpentUtc,
    Guid? SpentOnHostedEventId,
    string? SpentOnEventName,
    DateTime? RefundedUtc,
    string? ReceiptNumber,
    /// <summary>
    /// Why it was handed over rather than bought, when it was. Shown to the group so a $0.00 in
    /// the Paid column reads as a gift rather than a bug.
    /// </summary>
    string? GrantedReason = null);

/// <summary>
/// The credits card on a group's billing page: what is on sale, and what the group holds.
/// </summary>
/// <remarks>
/// One record rather than a price endpoint and a list endpoint, because the card cannot be drawn
/// from either half alone — a Buy control with no price is a guess, and a list with no price says
/// nothing to a group holding none. One call, one refusal, one thing to render.
/// </remarks>
public record OrgEventCreditsView(
    bool OnSale,
    decimal UnitPrice,
    string Currency,
    int MaximumPerPurchase,
    int Spendable,
    IReadOnlyList<OrgEventCreditRecord> Credits);

/// <summary>A credit as a SuperAdmin sees it: whose it is, and who bought it.</summary>
public record AdminEventCreditRecord(
    Guid Id,
    Guid? OwnerOrganizationId,
    string OwnerName,
    string BuyerName,
    decimal PriceAtPurchase,
    string Currency,
    DateTime PurchasedUtc,
    DateTime ExpiresUtc,
    DateTime? ExpiryWarningSentUtc,
    DateTime? SpentUtc,
    Guid? SpentOnHostedEventId,
    string? SpentOnEventName,
    DateTime? RefundedUtc,
    string? RefundedByName,
    string? RefundedReason,
    string? ReceiptNumber,
    string? GrantedReason);

/// <summary>
/// Refunding a credit — a SuperAdmin's act, never a self-service button (item 235, phase 1B.4).
/// </summary>
/// <param name="Reason">
/// Required. A refund is a decision a person made, and a row that does not say why is one nobody
/// can answer for later.
/// </param>
/// <param name="RecordAdjustment">
/// Whether to write the matching credit adjustment on the group's ledger. Off when the money was
/// given back through Stripe and its own refund will land in the ledger by another route.
/// </param>
public record RefundEventCreditRequest(string Reason, bool RecordAdjustment = true);

/// <summary>
/// Handing a group credits nobody paid for (item 235, phase 1B.4).
/// </summary>
/// <remarks>
/// <para>The remedy for the cases a payment cannot answer: a purchase that never landed, an
/// apology, a credit somebody was promised. Before it, the only way to put a credit in a group's
/// hands was a live card payment — so support had nothing to offer, and the refund screen beside
/// this one could not be exercised at all.</para>
///
/// <para><b>Nothing reaches the ledger.</b> A $0 charge and payment pair would put a sale that
/// never happened into the money trail. Who granted it and why is the record instead.</para>
/// </remarks>
/// <param name="Reason">
/// Required. A free credit with no explanation is one nobody can answer for, and it is the only
/// thing separating a support fix from a giveaway.
/// </param>
public record GrantEventCreditsRequest(int Quantity, string Reason);
