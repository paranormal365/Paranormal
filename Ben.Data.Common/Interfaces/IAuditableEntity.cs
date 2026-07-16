namespace Ben.Data.Common.Interfaces;

/// <summary>
/// Extends <see cref="IIDStd"/> with the four standard audit columns present
/// on the majority of entities in the Ben data model.
/// </summary>
/// <remarks>
/// Entities that are create-only (e.g. <c>UploadFileTypeExtension</c>)
/// do <b>not</b> implement this interface because they have no
/// <see cref="DateUpdated"/> or <see cref="UpdatedByAppUserId"/> columns.
/// Those entities implement <see cref="IIDStd"/> directly.
/// <para>
/// Values are set by the service or seeder that performs the write — EF Core
/// does not auto-populate them.  The convention is:
/// <list type="bullet">
/// <item><description><see cref="DateCreated"/> and <see cref="CreatedByAppUserId"/> are set once at insert time.</description></item>
/// <item><description><see cref="DateUpdated"/> and <see cref="UpdatedByAppUserId"/> are set on every subsequent save.</description></item>
/// </list>
/// </para>
/// </remarks>
public interface IAuditableEntity : IIDStd
{
    /// <summary>Gets or sets the UTC timestamp when the entity was first created.</summary>
    DateTime DateCreated { get; set; }

    /// <summary>Gets or sets the UTC timestamp of the most recent modification, or <c>null</c> if the entity has never been updated.</summary>
    DateTime? DateUpdated { get; set; }

    /// <summary>Gets or sets the <c>Id</c> of the <c>AppUser</c> who created this entity.</summary>
    Guid CreatedByAppUserId { get; set; }

    /// <summary>Gets or sets the <c>Id</c> of the <c>AppUser</c> who last modified this entity, or <c>null</c> if it has never been updated.</summary>
    Guid? UpdatedByAppUserId { get; set; }
}
