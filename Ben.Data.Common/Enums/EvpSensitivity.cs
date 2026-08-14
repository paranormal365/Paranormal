namespace Ben.Data.Common.Enums;

/// <summary>How far above the local noise floor a sound has to rise before it's proposed.</summary>
public enum EvpSensitivity
{
    /// <summary>9 dB over the floor. Only obvious events; use on a noisy recording.</summary>
    Low = 0,
    /// <summary>6 dB over the floor. The default.</summary>
    Medium = 1,
    /// <summary>4 dB over the floor. Surfaces faint events, at the cost of more to review.</summary>
    High = 2,
}
