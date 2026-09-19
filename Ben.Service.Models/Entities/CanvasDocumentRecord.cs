using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>
/// One case canvas board as the API returns it: the editor's own JSON plus who, where and which
/// revision.
/// </summary>
/// <remarks>
/// <para><see cref="Revision"/> is the contract for saving: send it back in <c>If-Match</c>. The
/// same number is in the <c>ETag</c> header, but the development CORS policy does not expose
/// <c>ETag</c> to a cross-origin browser, so the editor reads it from here.</para>
///
/// <para><see cref="OrganizationId"/> is the case's group, carried so a WebAssembly client that
/// only knows a board id still knows where the case's files live.</para>
/// </remarks>
public record CanvasDocumentRecord
{
    /// <summary>The board's id.</summary>
    public Guid Id { get; init; }

    /// <summary>The case the board belongs to; null for a personal board.</summary>
    public Guid? CaseId { get; init; }

    /// <summary>The case's group; null for a personal board.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>The board's title (the document's own <c>title</c>, or "Untitled board").</summary>
    public required string Name { get; init; }

    /// <summary>The editor's document JSON, with message HTML already sanitised by the server.</summary>
    public required string DocumentJson { get; init; }

    /// <summary>Starts at 1 and rises by one per accepted save. Send it back as <c>If-Match</c>.</summary>
    public int Revision { get; init; }

    /// <summary>Whether the group can see this board at all: a board nobody has published is its writer's alone.</summary>
    public bool IsPublished { get; init; }

    /// <summary>
    /// What this caller may do with the board: read it, add to it, or change anything on it (Ben, 2026-09-16).
    /// </summary>
    public CanvasBoardAccess Access { get; init; } = CanvasBoardAccess.Read;

    /// <summary>
    /// The pieces this caller put on the board, which stay theirs to rework even when they may only add to it. Empty
    /// for somebody who may change everything or nothing, because neither has to ask.
    /// </summary>
    public IReadOnlyList<Guid> MyPieceIds { get; init; } = [];

    /// <summary>Published once and written on since — the group is reading an older board than the one being worked on.</summary>
    public bool HasUnpublishedChanges { get; init; }

    /// <summary>The upload holding the last published PNG snapshot, if any.</summary>
    public Guid? PublishedUploadFileId { get; init; }

    /// <summary>When the snapshot was published (UTC).</summary>
    public DateTime? PublishedAtUtc { get; init; }

    /// <summary>When the board was first saved (UTC).</summary>
    public DateTime DateCreated { get; init; }

    /// <summary>When the board was last saved or published (UTC).</summary>
    public DateTime? DateUpdated { get; init; }

    /// <summary>Who first saved it.</summary>
    public Guid CreatedByAppUserId { get; init; }

    /// <summary>
    /// The creator's display name, filled only in a case's list, where more than one person's
    /// boards appear. Null elsewhere.
    /// </summary>
    public string? CreatedByName { get; init; }

    /// <summary>
    /// Whether the caller may change this board: a case board when they hold Cases Update and the group's
    /// subscription allows writes, a personal board when it is theirs. Advice for the editor, which shows a
    /// view-only board otherwise; every write is still checked on its own.
    /// </summary>
    public bool CanEdit { get; init; }
}

/// <summary>
/// A board in a list: everything in <see cref="CanvasDocumentRecord"/> except the document itself.
/// </summary>
/// <remarks>
/// A case with thirty boards would otherwise ship thirty whole documents, pictures' metadata and
/// all, to draw thirty rows of titles.
/// </remarks>
public record CanvasDocumentSummaryRecord
{
    /// <summary>The board's id.</summary>
    public Guid Id { get; init; }

    /// <summary>The case the board belongs to; null for a personal board.</summary>
    public Guid? CaseId { get; init; }

    /// <summary>The case's group; null for a personal board.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>The board's title.</summary>
    public required string Name { get; init; }

    /// <summary>The board's current revision.</summary>
    public int Revision { get; init; }

    /// <summary>Whether the group can see this board at all: a board nobody has published is its writer's alone.</summary>
    public bool IsPublished { get; init; }

    /// <summary>
    /// What this caller may do with the board: read it, add to it, or change anything on it (Ben, 2026-09-16).
    /// </summary>
    public CanvasBoardAccess Access { get; init; } = CanvasBoardAccess.Read;

    /// <summary>Published once and written on since — the group is reading an older board than the one being worked on.</summary>
    public bool HasUnpublishedChanges { get; init; }

    /// <summary>The upload holding the last published PNG snapshot, if any.</summary>
    public Guid? PublishedUploadFileId { get; init; }

    /// <summary>When the snapshot was published (UTC).</summary>
    public DateTime? PublishedAtUtc { get; init; }

    /// <summary>When the board was first saved (UTC).</summary>
    public DateTime DateCreated { get; init; }

    /// <summary>When the board was last saved or published (UTC).</summary>
    public DateTime? DateUpdated { get; init; }

    /// <summary>Who first saved it.</summary>
    public Guid CreatedByAppUserId { get; init; }

    /// <summary>The creator's display name, in a case's list; null elsewhere.</summary>
    public string? CreatedByName { get; init; }

    /// <summary>Whether the caller may change this board; see <see cref="CanvasDocumentRecord.CanEdit"/>.</summary>
    public bool CanEdit { get; init; }
}
