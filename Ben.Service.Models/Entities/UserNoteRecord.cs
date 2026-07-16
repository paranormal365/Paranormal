namespace Ben.Service.Models.Entities;

public record UserNoteRecord
{
    public Guid Id { get; init; }
    public Guid UserNoteTypeId { get; init; }
    public string? NoteSubject { get; init; }
    public required string NoteBody { get; init; }
    public Guid? ParentNoteId { get; init; }
    public Guid? ItemRecordId { get; init; }
    public required string TableName { get; init; }
    public int SortOrder { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
