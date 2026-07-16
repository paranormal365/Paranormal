using Ben.Data.Common.Enums;

namespace Ben.Service.RepositoryService.GenericInterfaces;

/// <summary>
/// Records CRUD activity to the <c>AuditLogs</c> table for compliance and debugging.
/// </summary>
/// <remarks>
/// Each method serialises entity state to JSON using
/// <see cref="Ben.Data.Common.Helpers.AuditChangeTracker"/> and writes a
/// single <c>AuditLog</c> row per call.
/// <para>
/// Audit failures are intentionally swallowed by callers (e.g.
/// <c>AdminEntityControllerBase</c>) so that a logging outage never rolls
/// back a successful CRUD operation.
/// </para>
/// </remarks>
public interface IAuditLogService
{
    /// <summary>
    /// Logs a Create operation — captures a full scalar-property snapshot of the new entity.
    /// </summary>
    /// <param name="entityType">Display name of the entity type (e.g. <c>"Organization"</c>). Typically <c>typeof(TEntity).Name</c>.</param>
    /// <param name="entityId">Primary key of the newly created entity.</param>
    /// <param name="entity">The entity object after it was saved. Only scalar properties are serialised; navigation properties are excluded.</param>
    /// <param name="userId">ID of the user who performed the create action.</param>
    /// <param name="source">Application that originated the action (see <see cref="Ben.Data.Common.Constants.AppSources"/>).</param>
    /// <param name="ct">Propagates cancellation to the database write.</param>
    Task LogCreateAsync(string entityType, Guid entityId, object entity, Guid userId, string source, CancellationToken ct = default);

    /// <summary>
    /// Logs an Update operation — captures only the properties whose values changed.
    /// </summary>
    /// <param name="entityType">Display name of the entity type.</param>
    /// <param name="entityId">Primary key of the entity that was updated.</param>
    /// <param name="before">The entity state <em>before</em> the update (loaded with <c>AsNoTracking</c>).</param>
    /// <param name="after">The entity state <em>after</em> the update.</param>
    /// <param name="userId">ID of the user who performed the update.</param>
    /// <param name="source">Application that originated the action.</param>
    /// <param name="ct">Propagates cancellation to the database write.</param>
    /// <remarks>
    /// The diff is computed by <see cref="Ben.Data.Common.Helpers.AuditChangeTracker.GetChanges"/>.
    /// If no properties changed the <c>ChangesJson</c> column stores an empty JSON array.
    /// </remarks>
    Task LogUpdateAsync(string entityType, Guid entityId, object before, object after, Guid userId, string source, CancellationToken ct = default);

    /// <summary>
    /// Logs a Delete operation — captures a full scalar-property snapshot of the entity before it was removed.
    /// </summary>
    /// <param name="entityType">Display name of the entity type.</param>
    /// <param name="entityId">Primary key of the entity that was deleted.</param>
    /// <param name="entity">The entity object <em>before</em> deletion was committed to the database.</param>
    /// <param name="userId">ID of the user who performed the delete.</param>
    /// <param name="source">Application that originated the action.</param>
    /// <param name="ct">Propagates cancellation to the database write.</param>
    Task LogDeleteAsync(string entityType, Guid entityId, object entity, Guid userId, string source, CancellationToken ct = default);
}
