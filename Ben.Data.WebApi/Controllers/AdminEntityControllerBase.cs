using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
public abstract class AdminEntityControllerBase<TEntity, TRecord> : BenControllerBase
    where TEntity : class
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    protected AdminEntityControllerBase(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMapper mapper,
        IAuditLogService auditLog)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _auditLog = auditLog;
    }

    [HttpGet]
    public virtual async Task<ActionResult<IEnumerable<TRecord>>> GetAll(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await dbContext.Set<TEntity>().AsNoTracking().ToListAsync(cancellationToken);
        var records = _mapper.Map<IEnumerable<TRecord>>(entities);
        return Ok(records);
    }

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

    [HttpPost]
    public virtual async Task<ActionResult<TRecord>> Create([FromBody] TEntity entity, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        EnsureEntityId(entity);
        dbContext.Set<TEntity>().Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var id = GetEntityId(entity);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(typeof(TEntity).Name, id, entity, GetCurrentUserId(), AppSources.WebApi, cancellationToken));

        return CreatedAtAction(nameof(GetById), new { id }, _mapper.Map<TRecord>(entity));
    }

    [HttpPut("{id:guid}")]
    public virtual async Task<ActionResult<TRecord>> Update(Guid id, [FromBody] TEntity entity, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var before = await dbContext.Set<TEntity>().AsNoTracking()
            .FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken);
        if (before is null)
            return NotFound();

        SetEntityId(entity, id);
        dbContext.Entry(entity).State = EntityState.Modified;
        await dbContext.SaveChangesAsync(cancellationToken);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(typeof(TEntity).Name, id, before, entity, GetCurrentUserId(), AppSources.WebApi, cancellationToken));

        return Ok(_mapper.Map<TRecord>(entity));
    }

    [HttpDelete("{id:guid}")]
    public virtual async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Set<TEntity>().FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        _ = TryAuditAsync(_auditLog.LogDeleteAsync(typeof(TEntity).Name, id, entity, GetCurrentUserId(), AppSources.WebApi, cancellationToken));

        dbContext.Set<TEntity>().Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Awaits an audit task and silently swallows exceptions so that an audit
    /// failure never rolls back or masks the main CRUD operation.
    /// </summary>
    private static async Task TryAuditAsync(Task auditTask)
    {
        try { await auditTask; }
        catch { /* audit failure must not surface to the caller */ }
    }

    private static Guid GetEntityId(TEntity entity)
    {
        var idProp = typeof(TEntity).GetProperty("Id");
        return idProp?.GetValue(entity) is Guid id ? id : Guid.Empty;
    }

    private static void EnsureEntityId(TEntity entity)
    {
        var idProperty = typeof(TEntity).GetProperty("Id");
        if (idProperty?.CanRead != true || idProperty.PropertyType != typeof(Guid))
        {
            return;
        }

        var currentId = (Guid)(idProperty.GetValue(entity) ?? Guid.Empty);
        if (currentId == Guid.Empty && idProperty.CanWrite)
        {
            idProperty.SetValue(entity, Guid.NewGuid());
        }
    }

    private static void SetEntityId(TEntity entity, Guid id)
    {
        var idProperty = typeof(TEntity).GetProperty("Id");
        if (idProperty?.CanWrite == true && idProperty.PropertyType == typeof(Guid))
        {
            idProperty.SetValue(entity, id);
        }
    }
}
