using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Services;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Whether a group's plan lets it take on, publish, or receive private-residence casework
/// (item 184 Phase C, gating <see cref="TierCapability.PrivateResidenceCases"/>).
/// </summary>
/// <remarks>
/// <para>Sentence-or-null, the house shape: null means proceed, a string is the exact message to
/// return as a 400 — and every caller's UI must render it (the server-guards-need-a-UI-path rule).</para>
///
/// <para><b>Grandfathering:</b> callers gate the moment a case would BECOME private-lane work —
/// accepting a client request, binding a residence place, receiving a private case, publishing
/// one. An already-designated case being worked is never re-gated; the plan governs new
/// commitments and publication, not work in hand.</para>
///
/// <para>Fail-open like every capability: only a tier with an explicit exclusion row refuses.
/// SuperAdmin excludes this from the free tier via AdminSubscriptionTiers; nothing is seeded.</para>
/// </remarks>
public static class PrivateCaseGate
{
    /// <summary>Refusal for the caller's own group ("Your group's plan …"), or null to proceed.</summary>
    public static async Task<string?> RefusalAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct)
    {
        var (may, tier) = await TierAreaResolution.HasCapabilityAsync(
            db, organizationId, TierCapability.PrivateResidenceCases, ct);
        return may ? null
            : $"Your group's plan{TierSuffix(tier)} does not include private-residence cases. "
            + "See the Pricing page for what each plan includes.";
    }

    /// <summary>Refusal about another group ("That group's plan …"), or null to proceed.</summary>
    /// <remarks>For the client picking a new group: said at pick time, in their dialog, rather
    /// than left to fail later at the group's accept.</remarks>
    public static async Task<string?> RefusalForOtherAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct)
    {
        var (may, tier) = await TierAreaResolution.HasCapabilityAsync(
            db, organizationId, TierCapability.PrivateResidenceCases, ct);
        return may ? null
            : $"That group's plan{TierSuffix(tier)} does not include private-residence cases, "
            + "so they cannot take this case on.";
    }

    private static string TierSuffix(string? tier) => tier is null ? "" : $" ({tier})";
}
