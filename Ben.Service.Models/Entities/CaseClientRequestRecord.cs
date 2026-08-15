using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>One file the client attached to their original request.</summary>
/// <param name="UploadFileId">Downloadable through the existing upload-file endpoints.</param>
/// <param name="FileName">Original name, for display.</param>
/// <param name="ContentType">So the UI can pick an icon or an inline preview.</param>
/// <param name="FileSize">Bytes.</param>
public sealed record CaseClientRequestFileRecord(
    Guid    UploadFileId,
    string  FileName,
    string? ContentType,
    long    FileSize);

/// <summary>
/// The client request a case was created from, as the investigating org may read it.
/// </summary>
/// <remarks>
/// <para>Accepting a request snapshots its description and address onto the new case, but the case
/// is then freely editable — so within a day or two the two disagree, and nothing on the case page
/// shows what the client actually said. This is that original text, deliberately read-only.</para>
///
/// <para>Attachments are referenced rather than copied into <c>CaseFile</c>: duplicating them would
/// create a second set of rows that can drift from the request and doubles the storage for no gain.
/// The ids here are downloadable through the normal file endpoints, which apply their own access
/// checks.</para>
///
/// <para>Deliberately narrower than the full <c>ClientRequest</c>: no geocoded latitude/longitude
/// (an org needs the address, not a mapping pin precise enough to publish), and no status or
/// per-org application rows, which are about the request's routing rather than its content.</para>
/// </remarks>
/// <param name="ClientRequestId">The originating request.</param>
/// <param name="SubmittedUtc">When the client created it — the "as submitted on" date.</param>
/// <param name="Description">The client's own narrative, stored as HTML.</param>
/// <param name="StreetAddress1">Address as given at submission.</param>
/// <param name="StreetAddress2">Second address line, when given.</param>
/// <param name="City">City as given.</param>
/// <param name="State">State as given.</param>
/// <param name="ZipCode">Postal code as given.</param>
/// <param name="Country">Country as given.</param>
/// <param name="Gender">Client's stated gender, or NotProvided.</param>
/// <param name="BirthYear">Client's birth year, or null when they chose not to say.</param>
/// <param name="Files">Files the client attached to the request.</param>
public sealed record CaseClientRequestRecord(
    Guid         ClientRequestId,
    DateTime     SubmittedUtc,
    string?      Description,
    string       StreetAddress1,
    string?      StreetAddress2,
    string       City,
    string       State,
    string       ZipCode,
    string       Country,
    ClientGender Gender,
    int?         BirthYear,
    IReadOnlyList<CaseClientRequestFileRecord> Files);
