using System.Text.RegularExpressions;

namespace Ben.Service.Models.Store;

/// <summary>
/// What a checkout form must hold before it is sent (storefront S4.10) — the one copy the server
/// refuses with and the page checks with, so the two can never disagree on a sentence.
/// </summary>
public static partial class StoreCheckoutRules
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailShape();

    [GeneratedRegex(@"^\d{5}(-\d{4})?$")]
    private static partial Regex ZipShape();

    /// <summary>The first thing wrong with what was typed, in the buyer's words; null when all is well.</summary>
    public static string? Problem(StoreCheckoutRequest r)
        => EmailProblem(r.Email)
           ?? AddressProblem(r.Shipping)
           ?? (r.Billing is null ? null : AddressProblem(r.Billing))
           ?? (r.AgreedToTerms ? null : StoreCheckoutSentences.AgreeToTerms);

    public static string? EmailProblem(string? email)
        => string.IsNullOrWhiteSpace(email) || !EmailShape().IsMatch(email.Trim()) || email.Length > 256
            ? StoreCheckoutSentences.EmailInvalid
            : null;

    public static string? AddressProblem(StoreAddressInput? a)
    {
        if (a is null) return StoreCheckoutSentences.Required("The address");
        if (string.IsNullOrWhiteSpace(a.FullName)) return StoreCheckoutSentences.Required("Full name");
        if (string.IsNullOrWhiteSpace(a.Phone)) return StoreCheckoutSentences.Required("Phone");
        if (string.IsNullOrWhiteSpace(a.Street1)) return StoreCheckoutSentences.Required("Street address");
        if (string.IsNullOrWhiteSpace(a.City)) return StoreCheckoutSentences.Required("City");
        if (!UsStates.IsValid(a.State)) return StoreCheckoutSentences.ChooseAState;
        if (string.IsNullOrWhiteSpace(a.Zip) || !ZipShape().IsMatch(a.Zip.Trim())) return StoreCheckoutSentences.ZipInvalid;
        return null;
    }
}
