namespace Ben.Service.Models.FieldSessions;

/// <summary>
/// One published session from a place's public archive, as a visitor may see it: enough to head
/// the player, and the files they may be served.
/// </summary>
/// <remarks>
/// No position here and no ids that lead anywhere but the archive: where somebody stood is the
/// place's public point, said by the timeline under <see cref="TimelinePositionPolicy.PublicPoint"/>.
/// </remarks>
public sealed record PublicSessionSummary(
    Guid Id,
    Guid PlaceId,
    string PlaceName,
    string? LocationLabel,
    DateTime StartedAt,
    DateTime? EndedAt,
    string DeviceModel,
    string RecordedBy,
    /// <summary>Empty while the session's media is held for review.</summary>
    IReadOnlyList<PublicSessionFile> Files);

/// <summary>A file a visitor may be served, by the id the archive's media route takes.</summary>
public sealed record PublicSessionFile(Guid Id, string RelativePath, string ContentType);
