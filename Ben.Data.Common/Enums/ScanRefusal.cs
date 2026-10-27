namespace Ben.Data.Common.Enums;

/// <summary>Why somebody may not scan a field session (phase 9, 10/27/2026).</summary>
public enum ScanRefusal
{
    None = 0,
    NotSignedIn = 1,
    NoSuchSession = 2,

    /// <summary>Not the person who recorded it, nor on any team it was sent to.</summary>
    NotYours = 3,

    /// <summary>It is in a public archive, which lets anybody watch it and nobody but its people scan it.</summary>
    PublicViewOnly = 4,

    /// <summary>It has no sound recordings.</summary>
    NoAudio = 5,
}
