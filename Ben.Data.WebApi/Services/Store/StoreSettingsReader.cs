using System.Globalization;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>Where parcels leave from — the origin Stripe Tax works sales tax out from.</summary>
public sealed record StoreShipFrom(string Street, string City, string State, string Zip);

/// <summary>Every store setting, typed, with its default applied (storefront S1.1).</summary>
/// <param name="FreeShippingThreshold">0 means orders never ship free.</param>
/// <param name="HasShipFrom">True only when all four ship-from lines are set.</param>
public sealed record StoreSettingsSnapshot(
    bool Enabled,
    bool CheckoutEnabled,
    decimal ShippingFlatRate,
    decimal FreeShippingThreshold,
    int LowStockThreshold,
    StoreShipFrom ShipFrom,
    bool HasShipFrom,
    string? SupportEmail,
    int ReturnsWindowDays,
    int ReservationMinutes,
    bool LinkEnabled,
    decimal MarkupPercent = StoreSettingsReader.DefaultMarkupPercent,
    decimal FeePercent = StoreSettingsReader.DefaultFeePercent,
    decimal FeeFixed = StoreSettingsReader.DefaultFeeFixed);

/// <summary>
/// Reads the store's settings in one query (storefront S1.1).
/// </summary>
/// <remarks>
/// A value that does not parse, or is out of range, reads as unset and takes the default — the
/// same rule as <see cref="SiteSettingsService.GetDecimalAsync"/>. The settings page refuses such
/// a value, so one can only arrive by hand in the database, and a mistyped number there must not
/// take the checkout down or charge a negative shipping fee.
/// </remarks>
public static class StoreSettingsReader
{
    public const int DefaultLowStockThreshold = 3;
    public const int DefaultReturnsWindowDays = 30;
    public const int DefaultReservationMinutes = 15;
    public const decimal DefaultMarkupPercent = 30m;
    public const decimal DefaultFeePercent = 2.9m;
    public const decimal DefaultFeeFixed = 0.30m;

    public static async Task<StoreSettingsSnapshot> ReadAsync(BenDataContext db, CancellationToken ct = default)
    {
        var rows = await db.SiteSettings.AsNoTracking()
            .Where(s => s.Key == SiteSettingKeys.FeatureStore || s.Key.StartsWith(SiteSettingKeys.StorePrefix))
            .Select(s => new { s.Key, s.Value })
            .ToListAsync(ct);
        var values = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Value))
            .ToDictionary(r => r.Key, r => r.Value!.Trim(), StringComparer.Ordinal);

        string? Text(string key) => values.GetValueOrDefault(key);

        bool Bool(string key, bool whenUnset) => bool.TryParse(Text(key), out var on) ? on : whenUnset;

        decimal Money(string key)
            => decimal.TryParse(Text(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0m
                ? d : 0m;

        decimal Percent(string key, decimal whenUnset)
            => decimal.TryParse(Text(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d >= 0m && d <= 100m
                ? d : whenUnset;

        int Whole(string key, int min, int max, int whenUnset)
            => int.TryParse(Text(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max
                ? n : whenUnset;

        var shipFrom = new StoreShipFrom(
            Text(SiteSettingKeys.StoreShipFromStreet) ?? string.Empty,
            Text(SiteSettingKeys.StoreShipFromCity) ?? string.Empty,
            Ben.Service.Models.Store.UsStates.Normalize(Text(SiteSettingKeys.StoreShipFromState)) ?? string.Empty,
            Text(SiteSettingKeys.StoreShipFromZip) ?? string.Empty);

        return new StoreSettingsSnapshot(
            Enabled: Bool(SiteSettingKeys.FeatureStore, SiteSettingKeys.DefaultFor(SiteSettingKeys.FeatureStore)),
            CheckoutEnabled: Bool(SiteSettingKeys.StoreCheckoutEnabled, whenUnset: true),
            ShippingFlatRate: Money(SiteSettingKeys.StoreShippingFlatRateUsd),
            FreeShippingThreshold: Money(SiteSettingKeys.StoreFreeShippingThresholdUsd),
            LowStockThreshold: Whole(SiteSettingKeys.StoreLowStockThreshold, 0, 100, DefaultLowStockThreshold),
            ShipFrom: shipFrom,
            HasShipFrom: shipFrom is { Street.Length: > 0, City.Length: > 0, State.Length: > 0, Zip.Length: > 0 },
            SupportEmail: Text(SiteSettingKeys.StoreSupportEmail),
            ReturnsWindowDays: Whole(SiteSettingKeys.StoreReturnsWindowDays, 0, 365, DefaultReturnsWindowDays),
            ReservationMinutes: Whole(SiteSettingKeys.StoreReservationMinutes, 5, 240, DefaultReservationMinutes),
            LinkEnabled: Bool(SiteSettingKeys.StoreLinkEnabled, whenUnset: false),
            MarkupPercent: Percent(SiteSettingKeys.StoreMarkupPercent, DefaultMarkupPercent),
            FeePercent: Percent(SiteSettingKeys.StoreFeePercent, DefaultFeePercent),
            FeeFixed: Text(SiteSettingKeys.StoreFeeFixedUsd) is null ? DefaultFeeFixed : Money(SiteSettingKeys.StoreFeeFixedUsd));
    }
}
