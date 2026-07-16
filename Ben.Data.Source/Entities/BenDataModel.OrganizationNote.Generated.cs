using System;
using System.Collections.Generic;

namespace Ben.Data.Source.Entities
{
    public partial class OrganizationNote
    {
        public Guid OrganizationId { get; set; }
        public Guid OrganizationNoteTypeId { get; set; }
        public Guid? ParentNoteId { get; set; }
        public string TableName { get; set; } = null!;
        public string NoteBody { get; set; } = null!;
        public string? NoteSubject { get; set; }
        public Guid? ItemRecordId { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual OrganizationNoteType OrganizationNoteType { get; set; } = null!;
        public virtual OrganizationNote? ParentNote { get; set; }
        public virtual ICollection<OrganizationNote> ChildNotes { get; set; } = new List<OrganizationNote>();
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
