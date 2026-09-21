namespace Ben.Service.Models.Entities;

/// <summary>
/// The code a guide is showing, as a staff screen needs it (item 248).
/// </summary>
/// <remarks>
/// The token is here because the screen draws the QR from it and the printed sheet carries it.
/// It never leaves a screen a guide is already looking at — the guest-facing answers below carry
/// no token at all.
/// </remarks>
/// <param name="TypedCode">The short code printed beside the QR, for somebody to type.</param>
/// <param name="QrDataUri">
/// The QR as a picture, inline.
/// <para>Carried in the record rather than fetched from a second endpoint. An image URL keyed by
/// the token would put the token in every access log and proxy between here and the guide's
/// tablet, and would need a door open to anybody holding it; the bytes are a few kilobytes and
/// the screen already has the record.</para>
/// </param>
/// <param name="TypeItAt">
/// The address printed under the typed code, for somebody to type.
/// <para>The SITE's own address, not the one the guide's browser happens to be using. This is
/// read off paper by a stranger on their own phone, where "localhost" or a machine's LAN address
/// is not somewhere they can go.</para>
/// </param>
/// <param name="Holders">How many people are working tonight on the strength of this code.</param>
public sealed record InvestigationJoinCodeRecord(
    Guid Id,
    Guid InvestigationId,
    string Token,
    string TypedCode,
    string QrDataUri,
    string TypeItAt,
    DateTime ExpiresUtc,
    DateTime? RevokedUtc,
    int Holders);

/// <summary>One person holding a credential tonight, as the guide's list shows them.</summary>
/// <param name="Name">Their account's name, or what they typed when they joined.</param>
/// <param name="Sessions">What they have actually sent up, so a guide can see who is working.</param>
public sealed record InvestigationGuestPassRecord(
    Guid Id,
    Guid AppUserId,
    string Name,
    DateTime IssuedUtc,
    DateTime? RevokedUtc,
    int Sessions);

/// <summary>
/// What a code says about itself before anybody signs in (item 248).
/// </summary>
/// <remarks>
/// <para>Deliberately thin. A stranger who typed a code off a sheet gets the name of the group
/// and what the visit is called, which is what tells them they are in the right place — and
/// nothing about the case, the client, the address or who else is there. The code was printed to
/// be held up to a crowd, so everything this answers should be safe to say out loud to one.</para>
/// </remarks>
/// <param name="Says">Why it is not usable, in words. Null when it is.</param>
public sealed record InvestigationCodeInvitation(
    bool Recognised,
    string? Says,
    Guid? InvestigationId,
    string? InvestigationTitle,
    string? OrganizationName,
    DateTime? ScheduledUtc,
    DateTime? ExpiresUtc);

/// <summary>What a guest is told after they scan or type a code while signed in.</summary>
public sealed record InvestigationCodeRedemption(
    bool Joined,
    string Says,
    Guid? InvestigationId,
    string? InvestigationTitle,
    string? OrganizationName,
    DateTime? ExpiresUtc)
{
    /// <summary>A refusal, with the sentence the guest is shown.</summary>
    public static InvestigationCodeRedemption Refused(string why) => new(false, why, null, null, null, null);
}
