namespace Ben.Data.Common.Enums;

/// <summary>
/// Whether an event that set a minimum number is going ahead (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para><b>Three values and not a nullable bool</b>, because "nobody has decided yet" is a real
/// state that the site acts on: it is what the reminder at seven days and one day is about, and
/// what the organizer's bell row is counting. A null bool would have made "undecided" and "not
/// using minimum numbers" the same thing, and they are opposites.</para>
///
/// <para><b>Append only.</b> The numbers are stored.</para>
/// </remarks>
public enum HostedEventGoNoGo
{
    /// <summary>Nobody has said yet. The state of every event that never set a minimum.</summary>
    Undecided = 0,

    /// <summary>It is going ahead, whatever the numbers came to.</summary>
    Go = 1,

    /// <summary>It is not. This routes through cancelling, so the guests are told and the credit
    /// comes back under the same rule as any other cancellation.</summary>
    NoGo = 2,
}
