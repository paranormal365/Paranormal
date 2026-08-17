namespace Ben.Service.Models.Entities;

/// <summary>
/// One case or investigation a group could put on one of its public pages.
/// </summary>
/// <param name="IsAlreadyPublic">
/// False means embedding this publishes something that was not public before — the moment the
/// editor must warn about, and the reason the acknowledgement is a separate decision from the
/// selection itself.
/// </param>
/// <param name="Where">Town or place name, for telling two similarly-titled records apart.</param>
/// <remarks>
/// Shared rather than mirrored: the picker and the endpoint must agree about what "already public"
/// means, and two copies of that answer would drift the first time the rule changed.
/// </remarks>
public sealed record EmbeddableRecord(
    Guid Id,
    string Title,
    DateTime? Date,
    bool IsAlreadyPublic,
    string? Where);

/// <summary>
/// One file from a case that a group is allowed to put on a public page.
/// </summary>
/// <param name="Context">The timeline entry's title — what the photo was of, so a picker is
/// choosable rather than a wall of thumbnails.</param>
/// <remarks>
/// Carries no case id, no author and no visibility flag. The caller already knows the case, and the
/// other two are how the answer was reached rather than part of it — a shape that cannot carry them
/// cannot leak them into a page.
/// </remarks>
public sealed record PublishableCaseFile(
    Guid UploadFileId,
    string? Context,
    DateTime? When,
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType);
