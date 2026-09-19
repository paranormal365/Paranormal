using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Filters;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Ben.Data.WebApi.Controllers;

[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[TypeFilter(typeof(EnableRequestBufferingFilter))]
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
    public virtual async Task<ActionResult<IEnumerable<TRecord>>> GetAll(
        CancellationToken cancellationToken, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await dbContext.Set<TEntity>().AsNoTracking().ToListAsync(cancellationToken);
        var records = _mapper.Map<IEnumerable<TRecord>>(entities).ToList();
        return Ok(ListPaging.Apply(records, page, pageSize, Response));
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

        // Always overwrite audit fields from the authenticated principal — never trust client-sent values.
        // This prevents FK violations (e.g. Guid.Empty) and tampering regardless of entity type.
        var now           = DateTime.UtcNow;
        var currentUserId = GetCurrentUserId();
        SetPropertyIfExists(entity, "CreatedByAppUserId", currentUserId);
        SetPropertyIfExists(entity, "DateCreated",        now);

        // Default IsActive to true only when the caller didn't send it at all — a bound bool
        // property can't distinguish "the caller sent false" from "the caller sent nothing", so
        // this checks the raw JSON body (see EnableRequestBufferingFilter) rather than the
        // deserialized value.
        if (!await WasJsonPropertySetAsync("isActive", cancellationToken))
            SetPropertyIfExists(entity, "IsActive", true);

        dbContext.Set<TEntity>().Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var id = GetEntityId(entity);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(typeof(TEntity).Name, id, entity, currentUserId, AppSources.WebApi));

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

        // Preserve immutable creation fields that the client never sends back.
        // Without this, CreatedByAppUserId deserialises as Guid.Empty which
        // causes an FK violation on SQL Server.
        CopyProperty(entity, before, "CreatedByAppUserId");
        CopyProperty(entity, before, "DateCreated");

        dbContext.Entry(entity).State = EntityState.Modified;
        await dbContext.SaveChangesAsync(cancellationToken);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(typeof(TEntity).Name, id, before, entity, GetCurrentUserId(), AppSources.WebApi));

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

        dbContext.Set<TEntity>().Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Audited after the save, not before: a delete that throws (an FK still referencing the
        // row, a concurrency failure) used to leave behind an audit row saying it had been
        // deleted. The snapshot is still accurate here — SaveChanges detaches the entity but
        // does not clear its scalar values.
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(typeof(TEntity).Name, id, entity, GetCurrentUserId(), AppSources.WebApi));

        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // The silent `private new static TryAuditAsync` that used to live here has been removed. It
    // shadowed BenControllerBase.TryAuditAsync, so every audit failure across all 27 admin CRUD
    // controllers was discarded without a trace — and would have kept being discarded after the
    // base class started logging them, since the shadow won overload resolution here. The base
    // implementation is the only one now.

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

    /// <summary>Sets a property by name if it exists and is writable on the entity.</summary>
    private static void SetPropertyIfExists(TEntity entity, string propertyName, object value)
    {
        var prop = typeof(TEntity).GetProperty(propertyName);
        if (prop?.CanWrite == true)
            prop.SetValue(entity, value);
    }

    /// <summary>Copies a property value from <paramref name="source"/> to <paramref name="target"/>.</summary>
    private static void CopyProperty(TEntity target, TEntity source, string propertyName)
    {
        var prop = typeof(TEntity).GetProperty(propertyName);
        if (prop is { CanRead: true, CanWrite: true })
            prop.SetValue(target, prop.GetValue(source));
    }

    /// <summary>
    /// Re-reads the raw request body (buffered by <see cref="EnableRequestBufferingFilter"/>) to check
    /// whether the caller's JSON actually included the given property, case-insensitively — the bound
    /// value alone can't tell "sent as false" apart from "omitted" for a non-nullable bool.
    /// </summary>
    private async Task<bool> WasJsonPropertySetAsync(string propertyName, CancellationToken cancellationToken)
    {
        var body = HttpContext.Request.Body;
        body.Position = 0;
        try
        {
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            body.Position = 0;
        }
    }
}
