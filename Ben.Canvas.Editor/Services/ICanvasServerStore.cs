namespace Ben.Canvas.Editor.Services;

/// <summary>A board as the server holds it.</summary>
/// <param name="DocumentJson">The board, serialised by CanvasSerializer. The server cleans message HTML on save, so adopt the returned copy.</param>
/// <param name="Revision">The server's revision; send it back as If-Match on the next save.</param>
/// <param name="CanEdit">
/// Whether this person may change the board. Advice for the screen only - the server checks every write
/// itself. A server that does not send it yet is read as <c>true</c>.
/// </param>
public sealed record CanvasServerDocument(
    Guid Id,
    Guid? CaseId,
    Guid? OrganizationId,
    string Name,
    string DocumentJson,
    int Revision,
    DateTime? PublishedAtUtc,
    Guid? PublishedUploadFileId,
    Guid CreatedByAppUserId,
    string? CreatedByName,
    DateTime DateCreated,
    DateTime? DateUpdated,
    bool CanEdit = true);

/// <summary>A board in a case's list: everything but the board itself.</summary>
public sealed record CanvasServerSummary(
    Guid Id,
    Guid? CaseId,
    Guid? OrganizationId,
    string Name,
    int Revision,
    DateTime? PublishedAtUtc,
    Guid CreatedByAppUserId,
    string? CreatedByName,
    DateTime DateCreated,
    DateTime? DateUpdated,
    bool CanEdit = true);

/// <summary>How a save to the server ended.</summary>
public enum CanvasSaveOutcome
{
    Saved,
    Conflict,
    Failed,
}

/// <summary>The end of a save.</summary>
/// <param name="Document">The saved board on <see cref="CanvasSaveOutcome.Saved"/>; the server's newer copy on <see cref="CanvasSaveOutcome.Conflict"/>.</param>
/// <param name="Problem">A sentence for the person when the save did not land.</param>
/// <param name="Forbidden">True when the server refused the person (403), which makes the board view-only.</param>
public sealed record CanvasSaveResult(CanvasSaveOutcome Outcome, CanvasServerDocument? Document, string? Problem, bool Forbidden = false);

/// <summary>
/// Loads, saves, publishes and deletes boards on the IsHaunted API. The editor works without it: everything
/// else happens on the device.
/// </summary>
/// <remarks>
/// A host replaces it by registering its own before AddBenCanvasEditor. Every method answers with a result
/// and a sentence rather than throwing, so a failed save never loses the work on the device.
/// </remarks>
public interface ICanvasServerStore
{
    /// <summary>
    /// True when there is an API to talk to, saving is switched on, and the person is signed in (or the host
    /// does not say). The editor hides Save to case and Publish while this is false.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>The boards on a case, newest first; without a case, the person's own boards.</summary>
    Task<(IReadOnlyList<CanvasServerSummary>? Items, string? Problem)> ListAsync(Guid? caseId, CancellationToken ct = default);

    /// <summary>One board, with its JSON.</summary>
    Task<(CanvasServerDocument? Document, string? Problem)> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Creates the board when <paramref name="existingId"/> is null, otherwise replaces it if the server is still at
    /// <paramref name="revision"/>. The board's name travels inside the JSON as its title.
    /// </summary>
    Task<CanvasSaveResult> SaveAsync(string documentJson, Guid? existingId, int revision, Guid? caseId, CancellationToken ct = default);

    /// <summary>Stores a PNG picture of the board in the case's files and marks the board published.</summary>
    Task<(CanvasServerDocument? Document, string? Problem)> PublishAsync(Guid id, byte[] png, string fileName, CancellationToken ct = default);

    /// <summary>Deletes a board from the server.</summary>
    Task<(bool Ok, string? Problem)> DeleteAsync(Guid id, CancellationToken ct = default);
}
