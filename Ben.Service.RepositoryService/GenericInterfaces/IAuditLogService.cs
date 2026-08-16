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
/// back a successful CRUD operation — but they are logged, never silent.
/// </para>
/// <para>
/// <b>These methods deliberately take no <see cref="CancellationToken"/>.</b> Callers fire them
/// without awaiting, after the mutation they describe has already committed, so binding them to
/// the request's token meant a client that disconnected at the wrong moment cancelled the audit
/// write for a change that had already happened — losing the record of it, silently. An audit row
/// describes something that is already true; there is no caller whose going away should prevent
/// it being written. The absence of the parameter is the fix: it cannot be passed wrongly.
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
    Task LogCreateAsync(string entityType, Guid entityId, object entity, Guid userId, string source);

    /// <summary>
    /// Logs an Update operation — captures only the properties whose values changed.
    /// </summary>
    /// <param name="entityType">Display name of the entity type.</param>
    /// <param name="entityId">Primary key of the entity that was updated.</param>
    /// <param name="before">The entity state <em>before</em> the update (loaded with <c>AsNoTracking</c>).</param>
    /// <param name="after">The entity state <em>after</em> the update.</param>
    /// <param name="userId">ID of the user who performed the update.</param>
    /// <param name="source">Application that originated the action.</param>
    /// <remarks>
    /// The diff is computed by <see cref="Ben.Data.Common.Helpers.AuditChangeTracker.GetChanges"/>.
    /// If no properties changed the <c>ChangesJson</c> column stores an empty JSON array.
    /// </remarks>
    Task LogUpdateAsync(string entityType, Guid entityId, object before, object after, Guid userId, string source);

    /// <summary>
    /// Logs a Delete operation — captures a full scalar-property snapshot of the entity before it was removed.
    /// </summary>
    /// <param name="entityType">Display name of the entity type.</param>
    /// <param name="entityId">Primary key of the entity that was deleted.</param>
    /// <param name="entity">The entity object <em>before</em> deletion was committed to the database.</param>
    /// <param name="userId">ID of the user who performed the delete.</param>
    /// <param name="source">Application that originated the action.</param>
    Task LogDeleteAsync(string entityType, Guid entityId, object entity, Guid userId, string source);
}
