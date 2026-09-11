namespace Ben.Data.Common.Enums;

/// <summary>
/// Somewhere else a tour can be found (item 233, Ben 2026-09-10).
/// </summary>
/// <remarks>
/// <para><b>Append-only.</b> The numbers are stored, so an existing one never changes meaning and
/// a new platform takes the next number. A platform that dies is left in place and simply stops
/// being offered.</para>
///
/// <para>Each one carries the hosts it is allowed to link to, in <see cref="SocialPlatforms"/>.
/// An icon that says Instagram and goes somewhere else is a link-laundering trick on a page the
/// business does not otherwise control, and it costs nothing to refuse.</para>
/// </remarks>
public enum SocialPlatform
{
    /// <summary>Their own site. The one entry with no host to check against.</summary>
    Website = 1,
    Instagram = 2,
    Facebook = 3,
    X = 4,
    TikTok = 5,
    YouTube = 6,
    BlueSky = 7,
    Rumble = 8,
    Threads = 9,
}
