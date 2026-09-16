namespace Ben.Canvas.Editor.Services;

/// <summary>A picture or file stored in a case's files.</summary>
public sealed record CanvasMediaUpload(Guid UploadFileId, string ContentType, long FileSize);

/// <summary>
/// The signed-in person's access token, for the few requests the browser makes itself (pictures, uploads) rather
/// than through the host's HttpClient. Optional: without one, those requests are not made.
/// </summary>
/// <remarks>The editor only ever hands the token to addresses under the API base (R15).</remarks>
public interface ICanvasAccessTokenSource
{
    /// <summary>A current token, refreshed first when it has expired or when <paramref name="forceRefresh"/> is set; null when signed out.</summary>
    Task<string?> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken ct = default);
}

/// <summary>
/// Pictures and files that live in a case: uploaded when a board is saved to the case, and shown through blob:
/// addresses because the API's file routes need the bearer token, which a bare img src never sends.
/// </summary>
/// <remarks>The bytes stay in the browser both ways (R17): uploads read the device copy, and downloads become object URLs.</remarks>
public interface ICanvasMediaStore
{
    /// <summary>True when there is an API, a token source, and a signed-in person.</summary>
    bool IsAvailable { get; }

    /// <summary>Uploads the file at <paramref name="sourceUrl"/> (a blob: address from the device store) into the case's files.</summary>
    Task<(CanvasMediaUpload? Upload, string? Problem)> UploadAsync(Guid organizationId, Guid caseId, string sourceUrl, string fileName, string? description, CancellationToken ct = default);

    /// <summary>A displayable address for a case file, cached for the session; null on any failure.</summary>
    Task<string?> GetDisplayUrlAsync(Guid uploadFileId, bool thumbnail, CancellationToken ct = default);

    /// <summary>A displayable address for any API route that needs the token (the link card image proxy); null for an address outside the API.</summary>
    Task<string?> GetDisplayUrlAsync(string apiUrl, CancellationToken ct = default);

    /// <summary>
    /// The files the case already holds, newest first — what a board can reach for instead of
    /// being handed the same picture twice.
    /// </summary>
    /// <remarks>
    /// Ben, 2026-09-16: "files can be picked from the case files, or if you drop a file, it will
    /// upload to the case files". Dropping worked; reaching did not, so a photograph uploaded last
    /// week had to be found on the computer and uploaded again — a second copy of the same
    /// evidence, against the account's own storage.
    /// </remarks>
    Task<(IReadOnlyList<CanvasCaseFile> Files, string? Problem)> ListCaseFilesAsync(
        Guid organizationId, Guid caseId, CancellationToken ct = default);
}

/// <summary>One file already on the case, as the board's picker lists it.</summary>
public sealed record CanvasCaseFile(
    Guid UploadFileId, string FileName, string ContentType, long FileSize, string? Description);
