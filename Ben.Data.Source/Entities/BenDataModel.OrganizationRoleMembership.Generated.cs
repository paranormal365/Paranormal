namespace Ben.Data.Source.Entities
{
    public partial class OrganizationRoleMembership
    {
        public Guid OrganizationRoleId { get; set; }
        public Guid OrganizationUserMembershipId { get; set; }
        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual OrganizationRole OrganizationRole { get; set; } = null!;
        public virtual OrganizationUserMembership OrganizationUserMembership { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
