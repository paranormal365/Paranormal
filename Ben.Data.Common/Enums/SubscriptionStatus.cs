namespace Ben.Data.Common.Enums;

/// <summary>
/// Where an organization stands with the platform's subscription.
/// </summary>
/// <remarks>
/// <para><b>Closing is not a state.</b> Ben's rule for the wind-down (item 84) is that billing
/// stops at the end of the cycle and everything stays available until the paid period ends — "a
/// group that closes is simply active with a known end date". So an organization that has cancelled
/// is <see cref="Active"/> with <c>CancelAtPeriodEnd</c> set, not a fourth value here. Modelling it
/// as a state would make every read ask "is closing the same as active?" and get it wrong
/// somewhere.</para>
/// </remarks>
public enum SubscriptionStatus
{
    /// <summary>
    /// Inside a tier that costs nothing. Not a lesser kind of active — no money is owed, so there
    /// is no period to lapse and nothing to wind down.
    /// </summary>
    Free = 0,

    /// <summary>Paid and inside the current period, whether or not it is set to end.</summary>
    Active = 1,

    /// <summary>
    /// The paid period ended without renewal. Read access continues; the organization stops being
    /// able to add records, and its cases pause. See item 84.
    /// </summary>
    Lapsed = 2,
}
