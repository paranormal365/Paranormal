using System;
using System.Collections.Generic;

namespace Ben.Data.Source.Entities
{
    public partial class UserPhone
    {
        public Guid UserPhoneTypeId { get; set; }
        public Guid AppUserId { get; set; }
        public string? PhoneCountry { get; set; }
        public string PhoneNumber { get; set; } = null!;
        public bool IsPrimary { get; set; }
        public bool IsPublic { get; set; }
        public bool IsCellular { get; set; }
        public bool IsValidated { get; set; }
        public string ValidationToken { get; set; } = null!;
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public DateTime? DateValidated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual UserPhoneType UserPhoneType { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
