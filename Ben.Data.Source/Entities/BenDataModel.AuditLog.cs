using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities;

/// <summary>
/// Immutable record of a CRUD action performed by a user on any entity.
/// Intentionally has no FK to AppUser so audit records survive user deletion.
/// </summary>
public partial class AuditLog : IIDStd
{
    public Guid Id { get; set; }
}
