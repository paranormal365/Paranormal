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
    Delete = 3,

    /// <summary>
    /// Somebody LOOKED at something that is a copy of another person's business (item 245).
    /// </summary>
    /// <remarks>
    /// <para>The odd one out, and deliberately so: the other three record a change, and this
    /// records that no change was needed for the harm to be possible. Reading a queued letter
    /// hands over a guest's name, a working password-reset link or a pass that opens a door — so
    /// the question an audit table has to be able to answer about it is not "what changed" but
    /// "who looked".</para>
    ///
    /// <para><c>ChangesJson</c> carries what was looked at rather than a before/after, because
    /// there is no before and no after.</para>
    /// </remarks>
    Read = 4
}
