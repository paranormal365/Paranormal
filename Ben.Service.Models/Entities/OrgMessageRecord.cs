using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record OrgMessageRecord
{
    public Guid Id { get; init; }
    public Guid? OrganizationId { get; init; }
    public Guid AuthorAppUserId { get; init; }
    public string? AuthorDisplayName { get; init; }
    public Guid? ParentMessageId { get; init; }
    public OrgMessageChannel ChannelType { get; init; }
    public string? Subject { get; init; }
    public required string Body { get; init; }
    public bool IsEncrypted { get; init; }
    public bool IsPublic { get; init; }
    public Guid? CaseId { get; init; }
    public int ViewCount { get; init; }
    public int ReplyCount { get; init; }
    public bool IsReadByCurrentUser { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
