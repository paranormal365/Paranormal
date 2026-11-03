namespace Ben.Data.Common.Enums;

/// <summary>Where a group stands in a promotion (the founding groups, 11/02/2026).</summary>
public enum PromotionEntryState
{
    /// <summary>Claimed a spot; its week to gather members is running.</summary>
    Gathering = 0,

    /// <summary>Its week ended with enough counted members: on its free year.</summary>
    Qualified = 1,

    /// <summary>Its week ended without enough members; the spot went back.</summary>
    Released = 2,

    /// <summary>Taken out by an administrator, with a reason.</summary>
    Disqualified = 3,
}
