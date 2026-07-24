using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    public partial class ClientRequest : IAuditableEntity
    {
        public Guid Id { get; set; }
    }
}
