namespace Ben.Service.Models.Entities;

public record CaseTimelineFileRecord
{
    public Guid   FileId      { get; init; }
    public string FileName    { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public long   FileSize    { get; init; }
}
