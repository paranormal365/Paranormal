namespace Ben.Web.Website.Library.Shared;

/// <summary>One photo, video or recording shown in a place's gallery (PlaceMediaGallery).</summary>
/// <param name="Key">Unique within the gallery.</param>
/// <param name="Url">Where the browser loads it from.</param>
/// <param name="Source">What it is and where it came from: "The place", "Evidence", "From the Saturday walk".</param>
/// <param name="VoteFileId">The file a vote is cast on, for evidence that can be voted on; null otherwise.</param>
public sealed record PlaceMediaItem(
    Guid Key,
    string Url,
    string ContentType,
    string FileName,
    string? Caption,
    string Source,
    string? By,
    DateTime? When,
    Guid? VoteFileId = null)
{
    public bool IsPhoto => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public bool IsVideo => ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
    public bool IsAudio => ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
}
