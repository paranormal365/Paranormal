namespace Ben.Service.Models.Entities;

public record OrganizationMembershipQuestionRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string QuestionText { get; init; }
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
