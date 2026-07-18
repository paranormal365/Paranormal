using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    public partial class CmsSection : IAuditableEntity
    {
        public Guid Id { get; set; }
    }
}
