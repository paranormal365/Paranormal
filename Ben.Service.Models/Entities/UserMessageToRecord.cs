namespace Ben.Service.Models.Entities;

public record UserMessageToRecord
{
    public Guid Id { get; init; }
    public Guid MessageId { get; init; }
    public Guid ToAppUserId { get; init; }
    public DateTime? DateLastRead { get; init; }
    public int LastReadCount { get; init; }
}
