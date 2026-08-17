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
