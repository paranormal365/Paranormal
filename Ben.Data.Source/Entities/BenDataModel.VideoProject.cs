using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>A saved Ben.Video project (.benvideo JSON) attached to a case.</summary>
    public class VideoProject : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }
        public string Name { get; set; } = null!;

        /// <summary>Full .benvideo JSON blob — stored as nvarchar(max).</summary>
        public string ProjectJson { get; set; } = null!;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
