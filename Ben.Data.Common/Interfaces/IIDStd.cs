namespace Ben.Data.Common.Interfaces;

/// <summary>
/// Base identity contract satisfied by all entities in the Ben data model.
/// </summary>
/// <remarks>
/// Every concrete entity class exposes a single <see cref="Guid"/> primary key
/// named <c>Id</c>, which EF Core maps as the table's clustered index.
/// Use this interface as a generic constraint whenever code must treat any
/// entity polymorphically — for example inside generic repositories or
/// <see cref="Ben.Data.Common.Helpers.AuditChangeTracker"/>.
/// <para>
/// Entities that also carry the four standard audit columns should implement
/// <see cref="IAuditableEntity"/> instead, which extends this interface.
/// </para>
/// </remarks>
public interface IIDStd
{
    /// <summary>
    /// Gets or sets the entity's unique primary-key value.
    /// Assigned via <c>Guid.NewGuid()</c> before the entity is inserted.
    /// </summary>
    Guid Id { get; set; }
}
