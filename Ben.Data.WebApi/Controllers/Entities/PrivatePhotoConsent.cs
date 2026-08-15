using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The one place the private-photo consent rule is expressed.
/// </summary>
/// <remarks>
/// <para>Showing a member's private photo to a client requires <b>two</b> independent yeses: the
/// organization must permit it as policy, and the member must personally opt in. Either alone
/// means no. The org can't volunteer its members' faces, and a member can't override an org that
/// has decided its people stay unidentified to clients.</para>
///
/// <para>It lives in a helper rather than inline in the avatar endpoint because U3's resolution
/// logic, U4's client-side sharing, and any future report or export path all have to agree. A
/// consent rule that is re-typed at each call site is a consent rule that will eventually differ
/// at one of them, and the failure mode is showing someone's face to a person they never agreed
/// to show it to.</para>
/// </remarks>
internal static class PrivatePhotoConsent
{
    /// <summary>
    /// Whether <paramref name="member"/>'s private photo may be shown to a client of
    /// <paramref name="org"/>. Null on either argument is treated as "no", so a missing row can
    /// never read as permission.
    /// </summary>
    internal static bool MayShowToClient(AppUser? member, Organization? org)
        => member is { SharePrivatePhotoWithClients: true }
        && org is { AllowMemberPrivatePhotosToClients: true };

    /// <summary>
    /// Flag-only overload for callers that projected the two booleans out of a query rather than
    /// loading whole entities. Same rule, so the two can't drift apart.
    /// </summary>
    internal static bool MayShowToClient(bool memberOptedIn, bool orgAllows)
        => memberOptedIn && orgAllows;
}
