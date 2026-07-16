namespace Ben.Service.Models.Admin;

public record UserMessageAdminRecord
{
    public Guid Id { get; init; }
    public Guid UserMessageTypeId { get; init; }
    public string? MessageSubject { get; init; }
    public required string MessageBody { get; init; }
    public Guid? ParentMessageId { get; init; }
    public DateTime? DateArchived { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
