namespace Ben.Data.Source.Entities
{
    public partial class OrganizationAddressMemberAccess
    {
        public Guid OrganizationAddressId { get; set; }
        public Guid OrganizationUserMembershipId { get; set; }
        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual OrganizationAddress OrganizationAddress { get; set; } = null!;
        public virtual OrganizationUserMembership OrganizationUserMembership { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
