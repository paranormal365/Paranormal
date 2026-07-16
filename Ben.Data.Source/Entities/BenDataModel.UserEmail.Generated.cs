using System;
using System.Collections.Generic;

namespace Ben.Data.Source.Entities
{
    public partial class UserEmail
    {
        public Guid UserEmailTypeId { get; set; }
        public Guid AppUserId { get; set; }
        public string EmailAddress { get; set; } = null!;
        public bool IsHidden { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsPublic { get; set; }
        public bool IsValidated { get; set; }
        public string? ValidationToken { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public DateTime? DateValidated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual UserEmailType UserEmailType { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
