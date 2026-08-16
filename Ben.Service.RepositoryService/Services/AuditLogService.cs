using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Ben.Service.RepositoryService.Services;

/// <summary>
/// Concrete implementation of <see cref="IAuditLogService"/> that persists
/// audit entries to the <c>AuditLogs</c> table via <c>BenDataContext</c>.
/// </summary>
/// <remarks>
/// Each public method serialises the entity's scalar properties using
/// <see cref="AuditChangeTracker"/> before writing a single <see cref="AuditLog"/>
/// row.  Navigation properties and collection properties are excluded from all
/// JSON payloads.
/// <para>
/// A new <c>BenDataContext</c> is created per call to avoid interfering with
/// any context that is already tracking the entity being audited.
/// </para>
/// </remarks>
public sealed class AuditLogService : IAuditLogService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;

    public AuditLogService(IDbContextFactory<BenDataContext> dbContextFactory)
        => _dbContextFactory = dbContextFactory;

    public Task LogCreateAsync(string entityType, Guid entityId, object entity, Guid userId, string source)
    {
        var snapshot = AuditChangeTracker.ToPropertySnapshot(entity);
        var changesJson = JsonSerializer.Serialize(snapshot, _jsonOptions);
        return WriteAsync(AuditAction.Create, entityType, entityId, userId, source, changesJson);
    }

    public Task LogUpdateAsync(string entityType, Guid entityId, object before, object after, Guid userId, string source)
    {
        var changes = AuditChangeTracker.GetChanges(before, after);
        var changesJson = JsonSerializer.Serialize(changes, _jsonOptions);
        return WriteAsync(AuditAction.Update, entityType, entityId, userId, source, changesJson);
    }

    public Task LogDeleteAsync(string entityType, Guid entityId, object entity, Guid userId, string source)
    {
        var snapshot = AuditChangeTracker.ToPropertySnapshot(entity);
        var changesJson = JsonSerializer.Serialize(snapshot, _jsonOptions);
        return WriteAsync(AuditAction.Delete, entityType, entityId, userId, source, changesJson);
    }

    private async Task WriteAsync(AuditAction action, string entityType, Guid entityId, Guid userId,
        string source, string changesJson)
    {
        // CancellationToken.None, deliberately: see IAuditLogService. The row describes a change
        // that has already been committed, so nothing about the caller going away should stop it
        // being recorded.
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id          = Guid.NewGuid(),
            UserId      = userId,
            Action      = action,
            EntityType  = entityType,
            EntityId    = entityId,
            Source      = source,
            OccurredAt  = DateTime.UtcNow,
            ChangesJson = changesJson
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
