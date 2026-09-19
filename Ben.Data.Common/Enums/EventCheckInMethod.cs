namespace Ben.Data.Common.Enums;

/// <summary>How an arrival was recorded (item 235).</summary>
/// <remarks>
/// Kept because the three do not mean the same thing. A guest tapping "I'm here" on their own
/// phone is a claim; somebody at the door ticking them off, or scanning the pass issued when the
/// booking was confirmed, is a record. The door list shows the difference rather than flattening
/// it, so a self-report waits to be confirmed instead of silently counting as an arrival.
/// </remarks>
public enum EventCheckInMethod
{
    /// <summary>Recorded by staff at the door.</summary>
    Door = 0,

    /// <summary>The guest said so themselves. Unverified until the door agrees.</summary>
    Self = 1,

    /// <summary>The guest's pass was scanned.</summary>
    Qr = 2,
}
