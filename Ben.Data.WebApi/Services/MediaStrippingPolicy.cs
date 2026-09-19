using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Services;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>Why audio and video are, or are not, being stripped for a group (item 181).</summary>
/// <param name="Strips">The answer the upload path acts on.</param>
/// <param name="Reason">Why not, for a screen to show. Null when it strips.</param>
/// <param name="NeedsUpgrade">
/// True when the only thing standing in the way is the plan — the one refusal a group can act on
/// themselves, and the one a settings screen should say out loud rather than graying in silence.
/// </param>
/// <param name="CanChoose">
/// Whether the group's own toggle is worth offering: the host has a tool AND the plan includes
/// the capability. False means the switch would save a preference nothing can honour, so the
/// screen disables it and shows <paramref name="Reason"/> instead of a control that lies.
/// </param>
public readonly record struct MediaStrippingDecision(
    bool Strips, string? Reason, bool NeedsUpgrade, bool CanChoose);

/// <summary>
/// Decides whether a group's audio and video uploads get their embedded metadata stripped
/// (item 181). Three things must agree: the host can remux, the group's plan includes the
/// capability, and the group has left the setting on.
/// </summary>
/// <remarks>
/// <para><b>Images are not subject to any of this.</b> They are stripped for everybody, always —
/// a case photo is the commonest way a client's address escapes, and the re-encode is already
/// paid for on every upload. Only A/V, which costs a remux per file, is a plan-level capability.
/// </para>
/// </remarks>
public static class MediaStrippingPolicy
{
    public static async Task<MediaStrippingDecision> ForOrganizationAsync(
        BenDataContext db, IAvMetadataStripper stripper, Guid organizationId, CancellationToken ct)
    {
        if (!stripper.IsAvailable)
        {
            return new(false,
                "This site cannot strip audio and video metadata — no media tool is configured on the server.",
                NeedsUpgrade: false, CanChoose: false);
        }

        var (included, tierName) = await TierAreaResolution.HasCapabilityAsync(
            db, organizationId, TierCapability.MediaMetadataStripping, ct);
        if (!included)
        {
            return new(false,
                $"Stripping location data from audio and video is not part of {tierName ?? "your plan"}.",
                NeedsUpgrade: true, CanChoose: false);
        }

        var on = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => (bool?)o.StripMediaMetadata)
            .FirstOrDefaultAsync(ct);

        // A group that has switched it off has chosen to keep the location on its recordings —
        // a real choice for a group documenting a public landmark rather than somebody's home.
        return on == false
            ? new(false, "Your group has turned off stripping for audio and video.",
                  NeedsUpgrade: false, CanChoose: true)
            : new(true, null, NeedsUpgrade: false, CanChoose: true);
    }
}
