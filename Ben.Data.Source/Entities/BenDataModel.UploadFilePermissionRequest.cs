using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    public partial class UploadFilePermissionRequest : IAuditableEntity
    {
        public Guid Id { get; set; }
    }
}
