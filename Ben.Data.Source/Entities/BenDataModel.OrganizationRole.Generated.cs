namespace Ben.Data.Source.Entities
{
    public partial class OrganizationRole
    {
        public Guid OrganizationId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual ICollection<OrganizationRolePermission> Permissions { get; set; } = new List<OrganizationRolePermission>();
        public virtual ICollection<OrganizationRoleMembership> Members { get; set; } = new List<OrganizationRoleMembership>();
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
