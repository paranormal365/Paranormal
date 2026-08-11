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
/// <c>GetAll</c>/<c>GetById</c> return every row of <typeparamref name="TEntity"/>, completely
/// unfiltered — there is no ownership/visibility check of any kind here, and can't reasonably be
/// added generically (what "owns" a row varies per entity). <b>Every subclass MUST therefore
/// declare its own class-level <c>[Authorize]</c> deciding who may reach these two actions</b> —
/// this base class only requires <em>some</em> authenticated user, which is not an adequate bar
/// on its own for anything containing personal or private data. Two acceptable patterns, both
/// used in this codebase:
/// <list type="bullet">
/// <item><description>Add <c>[Authorize(Policy = RoleNames.SuperAdmin)]</c> at the subclass —
/// correct when the entity has no real per-user visibility model (e.g. <c>UserAddressController</c>,
/// <c>UserEmailController</c>, and the other thin subclasses in <c>Controllers/Entities/</c>).</description></item>
/// <item><description>Override <c>GetAll</c>/<c>GetById</c> as <c>[NonAction]</c> and replace them
/// with real permission-aware endpoints — see <see cref="Ben.Data.WebApi.Controllers.Entities.OrganizationController"/>.</description></item>
/// </list>
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
