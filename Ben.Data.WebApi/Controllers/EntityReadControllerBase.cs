using AutoMapper;
using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Abstract base controller that exposes read-only (GET) endpoints for an entity type.
/// </summary>
/// <typeparam name="TEntity">The EF Core entity class stored in <c>BenDataContext</c>.</typeparam>
/// <typeparam name="TRecord">The AutoMapper projection record returned to callers.</typeparam>
/// <remarks>
/// Requires any authenticated user (<c>[Authorize]</c>) — no role restriction.
/// For SuperAdmin-only CRUD endpoints see <see cref="AdminEntityControllerBase{TEntity,TRecord}"/>.
/// <para>
/// Subclasses simply declare a route attribute and inject the two constructor
/// dependencies; all query logic lives here.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
public abstract class EntityReadControllerBase<TEntity, TRecord> : BenControllerBase
    where TEntity : class
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;

    /// <summary>Initialises the base controller with required EF and mapping dependencies.</summary>
    /// <param name="dbContextFactory">Factory used to create short-lived <c>BenDataContext</c> instances per request.</param>
    /// <param name="mapper">AutoMapper instance configured with the application's mapping profiles.</param>
    protected EntityReadControllerBase(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
    }

    /// <summary>Returns all entities of type <typeparamref name="TEntity"/> mapped to <typeparamref name="TRecord"/>.</summary>
    /// <param name="cancellationToken">Propagates cancellation from the HTTP request.</param>
    /// <returns>An <see cref="OkObjectResult"/> containing the full list of records.</returns>
    [HttpGet]
    public virtual async Task<ActionResult<IEnumerable<TRecord>>> GetAll(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await dbContext.Set<TEntity>().AsNoTracking().ToListAsync(cancellationToken);
        var records = _mapper.Map<IEnumerable<TRecord>>(entities);
        return Ok(records);
    }

    /// <summary>Returns a single entity by its primary key.</summary>
    /// <param name="id">The <see cref="Guid"/> primary key of the entity.</param>
    /// <param name="cancellationToken">Propagates cancellation from the HTTP request.</param>
    /// <returns>
    /// An <see cref="OkObjectResult"/> containing the mapped record,
    /// or <see cref="NotFoundResult"/> if no entity with <paramref name="id"/> exists.
    /// </returns>
    [HttpGet("{id:guid}")]
    public virtual async Task<ActionResult<TRecord>> GetById(Guid id, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Set<TEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        return Ok(_mapper.Map<TRecord>(entity));
    }
}
