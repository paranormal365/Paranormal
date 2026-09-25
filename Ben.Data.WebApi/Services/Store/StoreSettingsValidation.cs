using System.Globalization;
using System.Text.RegularExpressions;
using Ben.Service.Models.Store;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// What a store setting may hold, and the sentence that says so (storefront S1.1).
/// </summary>
/// <remarks>
/// <para>One place, because a store value reaches money: the shipping rate is charged, the
/// ship-from address is what Stripe Tax works sales tax out from, the reservation decides how long
/// the last unit is held. The store settings page is the only door that writes these, and every
/// value goes through <see cref="Check"/> on the way; the generic site-settings door refuses the
/// whole prefix rather than learning these rules a second time.</para>
///
/// <para>An empty value is always allowed — it clears the setting and the reader's default
/// applies. A cleared ship-from line is not refused here; the store settings page's ready-to-sell
/// checklist is what says the store cannot take an order without it.</para>
/// </remarks>
public static partial class StoreSettingsValidation
{
    /// <summary>The largest amount either money setting may hold. Well past any real parcel.</summary>
    public const decimal MaxDollars = 10_000m;

    /// <summary>The longest ship-from street or city.</summary>
    public const int MaxAddressLine = 200;

    /// <summary>
    /// The value to store for <paramref name="key"/>, or the sentence refusing it. The value is
    /// canonical — trimmed, a state upper-cased, a number written invariantly — so every reader
    /// parses the same text.
    /// </summary>
    public static (string? Value, string? Refusal) Check(string key, string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value)) return (null, null);

        return key switch
        {
            SiteSettingKeys.StoreCheckoutEnabled or SiteSettingKeys.StoreLinkEnabled
                => bool.TryParse(value, out var on)
                    ? (on ? "true" : "false", null)
                    : (null, $"{SiteSettingsService.LabelFor(key)} is on or off."),

            SiteSettingKeys.StoreShippingFlatRateUsd
                => Dollars(value, "Shipping can't be negative.", "Shipping"),
            SiteSettingKeys.StoreFreeShippingThresholdUsd
                => Dollars(value, "The free-shipping threshold can't be negative.", "The free-shipping threshold"),

            SiteSettingKeys.StoreFeeFixedUsd
                => Dollars(value, "The card fee can't be negative.", "The card fee per payment"),
            SiteSettingKeys.StoreMarkupPercent or SiteSettingKeys.StoreFeePercent
                => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var pct) && pct >= 0m && pct <= 100m
                   && pct == Math.Round(pct, 2)
                    ? (pct.ToString("0.##", CultureInfo.InvariantCulture), null)
                    : (null, $"{SiteSettingsService.LabelFor(key)} is a percentage from 0 to 100, like 2.9."),

            SiteSettingKeys.StoreLowStockThreshold   => Whole(value, 0, 100, "Low-stock warning is 0 to 100."),
            SiteSettingKeys.StoreReturnsWindowDays   => Whole(value, 0, 365, "Returns window is 0 to 365 days."),
            SiteSettingKeys.StoreReservationMinutes  => Whole(value, 5, 240, "Reservation is 5 to 240 minutes."),

            SiteSettingKeys.StoreShipFromState
                => UsStates.Normalize(value) is { } state
                    ? (state, null)
                    : (null, "Ship-from state is two letters, like TN."),
            SiteSettingKeys.StoreShipFromZip
                => Zip().IsMatch(value)
                    ? (value, null)
                    : (null, "A ZIP code is five digits, or ZIP+4 like 37201-1234."),
            SiteSettingKeys.StoreShipFromStreet or SiteSettingKeys.StoreShipFromCity
                => value.Length <= MaxAddressLine
                    ? (value, null)
                    : (null, $"{SiteSettingsService.LabelFor(key)} is {MaxAddressLine} characters at most."),

            SiteSettingKeys.StoreSupportEmail
                => System.Net.Mail.MailAddress.TryCreate(value, out var mail) && mail.Address == value
                    ? (value, null)
                    : (null, "That doesn't look like an email address."),

            _ => (null, $"'{key}' is not a store setting."),
        };
    }

    private static (string?, string?) Dollars(string value, string negative, string what)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return (null, $"{what} is an amount in dollars, like 7.50.");
        if (amount < 0m) return (null, negative);
        if (amount != StoreMoney.Round(amount)) return (null, $"{what} is dollars and cents — two decimal places at most.");
        if (amount > MaxDollars) return (null, $"{what} is {StoreMoney.Format(MaxDollars)} at most.");
        return (amount.ToString("0.00", CultureInfo.InvariantCulture), null);
    }

    private static (string?, string?) Whole(string value, int min, int max, string sentence)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max
            ? (n.ToString(CultureInfo.InvariantCulture), null)
            : (null, sentence);

    [GeneratedRegex(@"^\d{5}(-\d{4})?$")]
    private static partial Regex Zip();
}
