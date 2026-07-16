namespace Ben.Service.Models.Entities;

public record OrganizationNoteRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid OrganizationNoteTypeId { get; init; }
    public Guid? ParentNoteId { get; init; }
    public required string TableName { get; init; }
    public required string NoteBody { get; init; }
    public string? NoteSubject { get; init; }
    public Guid? ItemRecordId { get; init; }
    public int SortOrder { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
