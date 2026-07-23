using System.Linq.Expressions;

namespace Ben.Service.RepositoryService.GenericInterfaces;

/// <summary>
/// Generic repository contract providing standard read/write operations over an entity type.
/// </summary>
/// <typeparam name="T">The entity type managed by this repository.</typeparam>
/// <remarks>
/// Concrete repositories implement this interface and are registered with
/// <c>IRepositoryManager</c>.  The <paramref name="trackChanges"/> parameter on
/// query methods controls whether EF Core change tracking is enabled:
/// pass <c>false</c> for read-only scenarios (better performance) and
/// <c>true</c> when the result will be modified and saved.
/// </remarks>
public interface IRepositoryBase<T>
{
    /// <summary>Retrieves all entities without any navigation properties loaded.</summary>
    /// <param name="trackChanges">When <c>true</c>, returned entities are tracked by EF Core for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    Task<IEnumerable<T>> GetAllAsync(bool trackChanges, CancellationToken token);

    /// <summary>Retrieves all entities and eagerly loads the specified navigation properties.</summary>
    /// <param name="includes">Expressions selecting navigation properties to include, or <c>null</c> for none.</param>
    /// <param name="trackChanges">When <c>true</c>, returned entities are tracked by EF Core for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, object>>[]? includes, bool trackChanges, CancellationToken token);

    /// <summary>Retrieves all entities and optionally loads every navigation property.</summary>
    /// <param name="includeAllNavigations">When <c>true</c>, all navigation properties are eagerly loaded via <c>AutoInclude</c>.</param>
    /// <param name="trackChanges">When <c>true</c>, returned entities are tracked by EF Core for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    Task<IEnumerable<T>> GetAllAsync(bool includeAllNavigations, bool trackChanges, CancellationToken token);

    /// <summary>Retrieves entities that satisfy a predicate without loading navigation properties.</summary>
    /// <param name="expressionPredicate">Filter expression applied in SQL via EF Core.</param>
    /// <param name="trackChanges">When <c>true</c>, returned entities are tracked by EF Core for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    Task<IEnumerable<T>> FindListAsync(Expression<Func<T, bool>> expressionPredicate, bool trackChanges, CancellationToken token);

    /// <summary>Retrieves entities that satisfy a predicate and eagerly loads specified navigation properties.</summary>
    /// <param name="expressionPredicate">Filter expression applied in SQL.</param>
    /// <param name="includes">Navigation properties to include, or <c>null</c> for none.</param>
    /// <param name="trackChanges">When <c>true</c>, returned entities are tracked for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    Task<IEnumerable<T>> FindListAsync(Expression<Func<T, bool>> expressionPredicate, Expression<Func<T, object>>[]? includes, bool trackChanges, CancellationToken token);

    /// <summary>Retrieves entities that satisfy a predicate and optionally loads every navigation property.</summary>
    /// <param name="expressionPredicate">Filter expression applied in SQL.</param>
    /// <param name="includeAllNavigations">When <c>true</c>, all navigation properties are eagerly loaded.</param>
    /// <param name="trackChanges">When <c>true</c>, returned entities are tracked for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    Task<IEnumerable<T>> FindListAsync(Expression<Func<T, bool>> expressionPredicate, bool includeAllNavigations, bool trackChanges, CancellationToken token);

    /// <summary>Retrieves a single entity by its primary key without loading navigation properties.</summary>
    /// <param name="id">The entity's primary key value.</param>
    /// <param name="trackChanges">When <c>true</c>, the returned entity is tracked for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    /// <returns>The matching entity, or <c>null</c> if not found.</returns>
    Task<T?> GetByIdAsync(Guid id, bool trackChanges, CancellationToken token);

    /// <summary>Retrieves a single entity by its primary key and eagerly loads specified navigation properties.</summary>
    /// <param name="id">The entity's primary key value.</param>
    /// <param name="includes">Navigation properties to include, or <c>null</c> for none.</param>
    /// <param name="trackChanges">When <c>true</c>, the returned entity is tracked for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    /// <returns>The matching entity, or <c>null</c> if not found.</returns>
    Task<T?> GetByIdAsync(Guid id, Expression<Func<T, object>>[]? includes, bool trackChanges, CancellationToken token);

    /// <summary>Retrieves a single entity by its primary key and optionally loads every navigation property.</summary>
    /// <param name="id">The entity's primary key value.</param>
    /// <param name="includeAllNavigations">When <c>true</c>, all navigation properties are eagerly loaded.</param>
    /// <param name="trackChanges">When <c>true</c>, the returned entity is tracked for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    /// <returns>The matching entity, or <c>null</c> if not found.</returns>
    Task<T?> GetByIdAsync(Guid id, bool includeAllNavigations, bool trackChanges, CancellationToken token);

    /// <summary>Retrieves the first entity matching a predicate without loading navigation properties.</summary>
    /// <param name="expressionPredicate">Filter expression applied in SQL.</param>
    /// <param name="trackChanges">When <c>true</c>, the returned entity is tracked for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    /// <returns>The first matching entity, or <c>null</c> if none found.</returns>
    Task<T?> FindOneAsync(Expression<Func<T, bool>> expressionPredicate, bool trackChanges, CancellationToken token);

    /// <summary>Retrieves the first entity matching a predicate and eagerly loads specified navigation properties.</summary>
    /// <param name="expressionPredicate">Filter expression applied in SQL.</param>
    /// <param name="includes">Navigation properties to include, or <c>null</c> for none.</param>
    /// <param name="trackChanges">When <c>true</c>, the returned entity is tracked for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    /// <returns>The first matching entity, or <c>null</c> if none found.</returns>
    Task<T?> FindOneAsync(Expression<Func<T, bool>> expressionPredicate, Expression<Func<T, object>>[]? includes, bool trackChanges, CancellationToken token);

    /// <summary>Retrieves the first entity matching a predicate and optionally loads every navigation property.</summary>
    /// <param name="expressionPredicate">Filter expression applied in SQL.</param>
    /// <param name="includeAllNavigations">When <c>true</c>, all navigation properties are eagerly loaded.</param>
    /// <param name="trackChanges">When <c>true</c>, the returned entity is tracked for change detection.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    /// <returns>The first matching entity, or <c>null</c> if none found.</returns>
    Task<T?> FindOneAsync(Expression<Func<T, bool>> expressionPredicate, bool includeAllNavigations, bool trackChanges, CancellationToken token);

    /// <summary>Returns the total number of entities in the table.</summary>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    Task<int> CountAllAsync(CancellationToken token);

    /// <summary>Returns the number of entities that satisfy a predicate.</summary>
    /// <param name="expressionPredicate">Filter expression applied in SQL.</param>
    /// <param name="token">Propagates cancellation to the underlying query.</param>
    Task<int> CountFindAsync(Expression<Func<T, bool>> expressionPredicate, CancellationToken token);
}
