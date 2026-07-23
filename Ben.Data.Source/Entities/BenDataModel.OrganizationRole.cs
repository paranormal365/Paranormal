using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    public partial class OrganizationRole : IAuditableEntity
    {
        public Guid Id { get; set; }
    }
}
