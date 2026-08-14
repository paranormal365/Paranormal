namespace Ben.Data.Common.Enums;

/// <summary>
/// Where an EVP marker stands in review.
/// </summary>
/// <remarks>
/// <see cref="Confirmed"/> is 0 — the default — so every marker that existed before automatic
/// detection reads back as a human's confirmed finding without a backfill. A marker only starts
/// <see cref="Pending"/> if the detector created it.
/// </remarks>
public enum EvpReviewStatus
{
    /// <summary>A person marked this, or reviewed a candidate and kept it.</summary>
    Confirmed = 0,

    /// <summary>The detector proposed this and nobody has ruled on it yet.</summary>
    Pending = 1,

    /// <summary>Reviewed and rejected. Kept rather than deleted so a re-scan doesn't propose it again.</summary>
    Dismissed = 2,
}
