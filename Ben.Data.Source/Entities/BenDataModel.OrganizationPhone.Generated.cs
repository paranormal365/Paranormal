using System;

namespace Ben.Data.Source.Entities
{
    public partial class OrganizationPhone
    {
        public Guid OrganizationId { get; set; }
        public Guid OrganizationPhoneTypeId { get; set; }
        public bool IsValidated { get; set; }
        public string PhoneNumber { get; set; } = null!;
        public string ValidationToken { get; set; } = null!;
        public DateTime? DateValidated { get; set; }
        public string? PhoneCountry { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsPublic { get; set; }
        public bool IsCellular { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual OrganizationPhoneType OrganizationPhoneType { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
