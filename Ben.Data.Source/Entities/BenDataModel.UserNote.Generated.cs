using System;

namespace Ben.Data.Source.Entities
{
    public partial class UserNote
    {
        public Guid UserNoteTypeId { get; set; }
        public string? NoteSubject { get; set; }
        public string NoteBody { get; set; } = null!;
        public Guid? ParentNoteId { get; set; }
        public Guid? ItemRecordId { get; set; }
        public string TableName { get; set; } = null!;
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual UserNoteType UserNoteType { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
