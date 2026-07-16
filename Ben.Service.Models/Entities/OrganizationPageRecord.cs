namespace Ben.Service.Models.Entities;

public record OrganizationPageRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public bool IsHome { get; init; }
    public required string PageTitle { get; init; }
    public required string UrlName { get; init; }
    public required string PageHtml { get; init; }
    public bool IsPublished { get; init; }
    public int SortOrder { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
