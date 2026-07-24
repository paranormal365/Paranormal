using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Admin;

public record AuditLogRecord
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public AuditAction Action { get; init; }
    public required string EntityType { get; init; }
    public Guid EntityId { get; init; }
    public required string Source { get; init; }
    public DateTime OccurredAt { get; init; }
    public string? ChangesJson { get; init; }
}

public record AuditLogPagedResponse(IReadOnlyList<AuditLogRecord> Items, int TotalCount);

public record AuditLogQueryRequest(
    int      Page       = 1,
    int      PageSize   = 50,
    string?  EntityType = null,
    AuditAction? Action = null,
    Guid?    UserId     = null,
    DateTime? DateFrom  = null,
    DateTime? DateTo    = null);

public record SendAuditLogMessageRequest(
    Guid             AuditLogId,
    IList<Guid>      RecipientUserIds,
    string           Subject,
    string           Body);
