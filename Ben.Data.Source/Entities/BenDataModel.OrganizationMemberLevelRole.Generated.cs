namespace Ben.Data.Source.Entities
{
    public partial class OrganizationMemberLevelRole
    {
        public Guid OrganizationMemberLevelId { get; set; }
        public Guid OrganizationRoleId { get; set; }
        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual OrganizationMemberLevel OrganizationMemberLevel { get; set; } = null!;
        public virtual OrganizationRole OrganizationRole { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
