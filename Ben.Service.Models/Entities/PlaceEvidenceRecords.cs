namespace Ben.Service.Models.Entities;

/// <summary>
/// One file somebody added straight to a public place (item 250).
/// </summary>
/// <remarks>
/// Named <c>Added</c> rather than simply <c>PlaceEvidence</c> because the place page already has a
/// <c>PlaceEvidenceRow</c>, which means evidence a guest offered at an EVENT. Two names one letter
/// apart for two different routes is how a page ends up drawing the wrong list.
/// </remarks>
/// <param name="AddedBy">The contributor's name — evidence nobody stands behind is worth less.</param>
/// <param name="UploadFileId">
/// The file itself, which is what a vote is keyed on. Carried so a vote cast on a place's page is
/// the SAME vote the file holds wherever else it appears — the tally above the list means nothing
/// if the page keeps its own second opinion. It does not open the bytes: those come only through
/// the place-scoped door, which re-asks the publication rule.
/// </param>
public sealed record PlaceAddedEvidenceRow(
    Guid Id,
    Guid UploadFileId,
    string FileName,
    string ContentType,
    string? Caption,
    string AddedBy,
    string? AddedByHandle,
    DateTime AddedUtc);

/// <summary>What the uploader is told.</summary>
/// <param name="Showing">
/// Whether a stranger can see it yet. False is not a failure — it is the held pile, and the
/// sentence says so.
/// </param>
public sealed record PlaceEvidenceAdded(Guid Id, bool Showing, string Says);

/// <summary>What somebody types to put a public location on the map (item 250).</summary>
/// <remarks>
/// There is deliberately no <c>Kind</c> on this. A private residence is somebody's home and every
/// route that makes one runs through a client relationship; this door has none, so the kind is set
/// by the server and is not the caller's to offer.
/// </remarks>
public sealed record NewPublicPlaceRequest(
    string? Name,
    string? StreetAddress1,
    string? StreetAddress2,
    string? City,
    string? State,
    string? ZipCode,
    string? Country,
    decimal? Latitude = null,
    decimal? Longitude = null);

/// <summary>The answer to creating one.</summary>
/// <param name="AlreadyExisted">
/// The address was already here. Not a failure: somebody wanting a page for Cragfont gets
/// Cragfont, and two rows for one building would split its evidence in half.
/// </param>
public sealed record PlaceCreated(Guid Id, bool AlreadyExisted, string Says);

/// <summary>
/// What the evidence at one place adds up to (item 250).
/// </summary>
/// <remarks>
/// <para>Reported per place and never compared across places: Ben decided 2026-09-21 that the site
/// shows a place's own figures and does not order one property against another. Cragfont is a real
/// building with real owners.</para>
///
/// <para>The score always travels with <c>VoteCount</c>, which is the rule <c>EvidenceVoteScore</c>
/// sets — a sum read without its weight says nothing.</para>
/// </remarks>
/// <param name="EvidenceCount">Files counted once each, whatever route they arrived by.</param>
/// <param name="VotedOnCount">
/// How many of them anybody has weighed in on. Forty files and two votes is a different place from
/// two files and forty, and a total alone cannot tell them apart.
/// </param>
/// <param name="ConfirmsShare">
/// The share of opinion each way of seeing it holds, 0..1 — null when nobody has voted, because
/// "nobody has said" and "everybody said no" are different facts.
/// </param>
public sealed record PlaceEvidenceFigures(
    int EvidenceCount,
    int VotedOnCount,
    int VoteCount,
    int Confirms,
    int Disputes,
    int Inconclusive,
    int Score,
    double? ConfirmsShare,
    double? DisputesShare,
    double? InconclusiveShare)
{
    /// <summary>A place nobody has contributed to yet.</summary>
    public static readonly PlaceEvidenceFigures Nothing =
        new(0, 0, 0, 0, 0, 0, 0, null, null, null);
}

/// <summary>What somebody writes about a public location (item 250).</summary>
/// <remarks>
/// Plain text. The server strips markup rather than refusing it — somebody describing a house
/// should not have to know what an angle bracket does — because this is written by whoever gets
/// there first and rendered on a page any stranger reads.
/// </remarks>
public sealed record SetPlaceDescriptionRequest(string? Description);
