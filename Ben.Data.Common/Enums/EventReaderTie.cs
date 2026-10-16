namespace Ben.Data.Common.Enums;

/// <summary>
/// Why a signed-in reader is being shown an event on a list, when it is more than "it is public".
/// </summary>
/// <remarks>
/// <para>Ben, 10/16/2026: the location search and the event lists should show "events available for
/// the person logged in", not only the public ones. A row the reader can see only because of who they
/// are is marked quietly, so it is never mistaken for something a stranger can see or share.</para>
///
/// <para><see cref="None"/> is every row a signed-out visitor gets, and every row a signed-in reader
/// gets that a visitor would get too and the reader has no tie to.</para>
/// </remarks>
public enum EventReaderTie
{
    /// <summary>Public, and nothing ties this reader to it.</summary>
    None = 0,

    /// <summary>Public, and the reader said they are coming.</summary>
    Going = 1,

    /// <summary>Public, and the reader was asked to come and has not said yes.</summary>
    Invited = 2,

    /// <summary>Not public: shown only because the reader is an active member of the group running it.</summary>
    YourGroup = 3,
}
