namespace Ben.Data.Common.Helpers;

/// <summary>
/// Stateless business rules for investigation cancellation deadlines.
/// From Case Concepts: clients must cancel 24 hrs before (72 hrs if org HQ > 75 miles away).
/// </summary>
public static class InvestigationCancellationHelper
{
    public const double DistanceThresholdMiles = 75.0;
    public const double ShortLeadHours         = 24.0;
    public const double LongLeadHours          = 72.0;

    public static double RequiredLeadHours(double distanceMiles)
        => distanceMiles > DistanceThresholdMiles ? LongLeadHours : ShortLeadHours;

    public static DateTime CancellationDeadlineUtc(DateTime scheduledUtc, double distanceMiles)
        => scheduledUtc.AddHours(-RequiredLeadHours(distanceMiles));

    public static bool IsCancellationAllowed(DateTime scheduledUtc, double distanceMiles, DateTime? nowUtc = null)
        => (nowUtc ?? DateTime.UtcNow) < CancellationDeadlineUtc(scheduledUtc, distanceMiles);
}
