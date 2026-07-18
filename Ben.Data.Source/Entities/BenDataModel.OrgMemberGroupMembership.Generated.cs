namespace Ben.Data.Source.Entities
{
    public partial class OrgMemberGroupMembership
    {
        public Guid OrgMemberGroupId { get; set; }
        public Guid OrganizationUserMembershipId { get; set; }
        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual OrgMemberGroup OrgMemberGroup { get; set; } = null!;
        public virtual OrganizationUserMembership OrganizationUserMembership { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
