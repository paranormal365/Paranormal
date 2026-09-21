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
public sealed record PlaceAddedEvidenceRow(
    Guid Id,
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
