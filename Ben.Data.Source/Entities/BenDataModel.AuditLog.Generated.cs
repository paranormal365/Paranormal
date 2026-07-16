using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    public partial class AuditLog
    {
        public Guid UserId { get; set; }
        public AuditAction Action { get; set; }
        public string EntityType { get; set; } = null!;
        public Guid EntityId { get; set; }
        public string Source { get; set; } = null!;
        public DateTime OccurredAt { get; set; }
        public string? ChangesJson { get; set; }
    }
}
