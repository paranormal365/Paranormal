using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    public partial class OrganizationRolePermission
    {
        public Guid OrganizationRoleId { get; set; }
        public OrganizationSecurityTable TableName { get; set; }
        public OrganizationSecurityAction Actions { get; set; }
        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual OrganizationRole OrganizationRole { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
