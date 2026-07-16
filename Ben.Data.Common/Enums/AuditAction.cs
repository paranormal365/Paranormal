namespace Ben.Data.Common.Enums;

/// <summary>
/// Identifies the type of CRUD operation recorded in an <c>AuditLog</c> entry.
/// </summary>
/// <remarks>
/// Stored as an <c>int</c> column in <c>AuditLogs</c> and serialised by
/// <see cref="Ben.Data.Common.Helpers.AuditChangeTracker"/> when building
/// the <c>ChangesJson</c> payload.
/// </remarks>
public enum AuditAction
{
    /// <summary>A new entity was created; <c>ChangesJson</c> contains a full property snapshot.</summary>
    Create = 1,

    /// <summary>An existing entity was modified; <c>ChangesJson</c> contains only the changed properties with before/after values.</summary>
    Update = 2,

    /// <summary>An entity was permanently removed; <c>ChangesJson</c> contains a full property snapshot captured before deletion.</summary>
    Delete = 3
}
