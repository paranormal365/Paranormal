namespace Ben.Service.Models.Entities;

public record OrganizationMembershipAnswerRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationMembershipRequestId { get; init; }
    public Guid OrganizationMembershipQuestionId { get; init; }
    public string? QuestionText { get; init; }
    public string? AnswerText { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
