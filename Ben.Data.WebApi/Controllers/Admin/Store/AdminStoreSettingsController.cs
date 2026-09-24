using System.Globalization;
using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin.Store;

/// <summary>
/// The store's settings page, and the checklist that says whether it could take an order right
/// now (storefront S1.7).
/// </summary>
/// <remarks>
/// <para><b>The one door for store settings.</b> Every value passes
/// <see cref="StoreSettingsValidation"/>; the generic site-settings door refuses the prefix. The
/// whole form is checked before anything is written, so a bad ZIP code does not leave the street
/// saved and the rest not.</para>
///
/// <para><b>Ready to sell</b> lists every reason the store could not take an order, in words, from
/// the settings, the catalogue, the Stripe keys and what the Stripe dashboard itself reports. The
/// switch that shows the shop (<c>features.store</c>) is not written here — it lives with the
/// other feature switches — but "switched off" is one of the reasons.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/store/settings")]
public sealed class AdminStoreSettingsController(
    IDbContextFactory<BenDataContext> dbFactory, SiteSettingsService settings, IAuditLogService auditLog,
    IStoreTaxProbe taxProbe, StorePaymentSetup payments) : BenControllerBase
{
    public const string NoShipFrom = "No ship-from address, so Stripe can't work out tax.";
    public const string NothingToSell = "No live product with stock.";
    public const string NoPublishableKey = "The publishable key isn't set — the payment form can't load.";
    public const string TaxNotActive = "Stripe Tax isn't active in the Stripe dashboard.";
    public const string SwitchedOff = "The store is switched off.";

    [HttpGet]
    public async Task<ActionResult<StoreSettingsAdminRecord>> Get(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Ok(await RecordAsync(db, ct));
    }

    /// <summary>Saves the whole form, or none of it.</summary>
    [HttpPut]
    public async Task<ActionResult<StoreSettingsAdminRecord>> Save([FromBody] SaveStoreSettingsRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();

        string? Money(decimal? d) => d?.ToString(CultureInfo.InvariantCulture);
        string? Whole(int? n) => n?.ToString(CultureInfo.InvariantCulture);
        var wanted = new (string Key, string? Raw)[]
        {
            (SiteSettingKeys.StoreCheckoutEnabled, request.CheckoutEnabled ? "true" : "false"),
            (SiteSettingKeys.StoreShippingFlatRateUsd, Money(request.ShippingFlatRate)),
            (SiteSettingKeys.StoreFreeShippingThresholdUsd, Money(request.FreeShippingThreshold)),
            (SiteSettingKeys.StoreLowStockThreshold, Whole(request.LowStockThreshold)),
            (SiteSettingKeys.StoreShipFromStreet, request.ShipFromStreet),
            (SiteSettingKeys.StoreShipFromCity, request.ShipFromCity),
            (SiteSettingKeys.StoreShipFromState, request.ShipFromState),
            (SiteSettingKeys.StoreShipFromZip, request.ShipFromZip),
            (SiteSettingKeys.StoreSupportEmail, request.SupportEmail),
            (SiteSettingKeys.StoreReturnsWindowDays, Whole(request.ReturnsWindowDays)),
            (SiteSettingKeys.StoreReservationMinutes, Whole(request.ReservationMinutes)),
            (SiteSettingKeys.StoreLinkEnabled, request.LinkEnabled ? "true" : "false"),
        };

        var checkedValues = new List<(string Key, string? Value)>();
        foreach (var (key, raw) in wanted)
        {
            var (value, refusal) = StoreSettingsValidation.Check(key, raw);
            if (refusal is not null) return BadRequest(refusal);
            checkedValues.Add((key, value));
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var current = await db.SiteSettings.AsNoTracking()
            .Where(s => s.Key.StartsWith(SiteSettingKeys.StorePrefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        foreach (var (key, value) in checkedValues)
        {
            var was = current.GetValueOrDefault(key);
            if (string.Equals(was ?? "", value ?? "", StringComparison.Ordinal)) continue;
            var row = await settings.SetAsync(key, value, userId, ct);
            await TryAuditAsync(auditLog.LogUpdateAsync(nameof(SiteSetting), row.Id,
                new SiteSetting { Id = row.Id, Key = key, Value = was }, row, userId, AppSources.WebApi));
        }

        return Ok(await RecordAsync(db, ct));
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private async Task<StoreSettingsAdminRecord> RecordAsync(BenDataContext db, CancellationToken ct)
    {
        var s = await StoreSettingsReader.ReadAsync(db, ct);
        var raw = await db.SiteSettings.AsNoTracking()
            .Where(x => x.Key.StartsWith(SiteSettingKeys.StorePrefix))
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        string? Text(string key) => raw.GetValueOrDefault(key) is { Length: > 0 } v ? v : null;
        decimal? Money(string key) => Text(key) is { } v && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        int? Whole(string key) => Text(key) is { } v && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

        var tax = await taxProbe.ProbeAsync(ct);
        var reasons = await NotReadyBecauseAsync(db, s, tax, ct);

        return new StoreSettingsAdminRecord(
            s.Enabled, s.CheckoutEnabled,
            Money(SiteSettingKeys.StoreShippingFlatRateUsd), Money(SiteSettingKeys.StoreFreeShippingThresholdUsd),
            Whole(SiteSettingKeys.StoreLowStockThreshold),
            Text(SiteSettingKeys.StoreShipFromStreet), Text(SiteSettingKeys.StoreShipFromCity),
            Text(SiteSettingKeys.StoreShipFromState), Text(SiteSettingKeys.StoreShipFromZip),
            Text(SiteSettingKeys.StoreSupportEmail),
            Whole(SiteSettingKeys.StoreReturnsWindowDays), Whole(SiteSettingKeys.StoreReservationMinutes),
            s.LinkEnabled, reasons.Count == 0, reasons, tax.RegisteredStates);
    }

    /// <summary>Every reason the store could not take an order now, in the order to fix them.</summary>
    private async Task<List<string>> NotReadyBecauseAsync(
        BenDataContext db, StoreSettingsSnapshot s, StoreTaxReadiness tax, CancellationToken ct)
    {
        var reasons = new List<string>();
        if (!s.HasShipFrom) reasons.Add(NoShipFrom);

        var sellable = await db.StoreProductVariants.AnyAsync(v =>
            v.IsActive && v.StockOnHand - v.StockReserved > 0 && v.Price > 0
            && v.Product.IsActive && v.Product.Category.IsActive
            && (v.Product.Category.ParentCategoryId == null || v.Product.Category.ParentCategory!.IsActive), ct);
        if (!sellable) reasons.Add(NothingToSell);

        if (!payments.Fake)
        {
            if (!payments.HasSecretKey) reasons.Add(StripeTaxProbe.NotSetUp);
            else if (!payments.HasPublishableKey) reasons.Add(NoPublishableKey);
        }

        // The dashboard's own answer. "Not set up" is already said above; a Stripe that did not
        // answer is said in its own words; otherwise the tax switch itself.
        if (tax.Problem is { } problem && problem != StripeTaxProbe.NotSetUp) reasons.Add(problem);
        else if (tax.Problem is null && !tax.Active) reasons.Add(TaxNotActive);

        if (!s.Enabled) reasons.Add(SwitchedOff);
        return reasons;
    }
}
