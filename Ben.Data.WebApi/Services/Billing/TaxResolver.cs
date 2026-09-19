using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// Turns "which group?" into "what tax?" (item 168). The rate is resolved at write time and
/// frozen onto whatever document asked — a later rule edit never rewrites a bill already sent.
/// </summary>
public static class TaxResolver
{
    /// <summary>
    /// The rate for this group: its first address's state matched against the rules. No address
    /// or no rule means zero — an honest zero on the bill beats a guessed rate nobody entered.
    /// </summary>
    public static async Task<(string? State, decimal RatePercent)> ForOrganizationAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct)
    {
        var state = await db.OrganizationAddresses.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.State != null && a.State != "")
            .OrderBy(a => a.DateCreated)
            .Select(a => a.State)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(state)) return (null, 0m);

        var normalized = state.Trim().ToUpperInvariant();
        var rate = await db.TaxRateRules.AsNoTracking()
            .Where(r => r.State == normalized)
            .Select(r => (decimal?)r.RatePercent)
            .FirstOrDefaultAsync(ct);
        return (normalized, rate ?? 0m);
    }

    /// <summary>Tax in dollars for an amount at a percent rate, rounded to cents half-up —
    /// the rounding every register uses.</summary>
    public static decimal TaxOn(decimal amount, decimal ratePercent)
        => Math.Round(amount * ratePercent / 100m, 2, MidpointRounding.AwayFromZero);
}
