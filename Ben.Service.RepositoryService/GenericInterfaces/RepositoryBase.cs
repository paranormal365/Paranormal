using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;

namespace Ben.Service.RepositoryService.GenericInterfaces;

public abstract class RepositoryBase<T> : IRepositoryBase<T> where T : class, IIDStd
{

    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly ILogger? _logger; // logger instance (optional)
    

    public RepositoryBase(IDbContextFactory<BenDataContext> dbContextFactory, ILoggerFactory? loggerFactory = null)
    {
        _dbContextFactory = dbContextFactory;
        _logger = loggerFactory?.CreateLogger(typeof(RepositoryBase<T>).FullName!);
    }

    
    private void LogError(Exception ex, string operation)
    {
        _logger?.LogError(ex, "Error during {Operation} on {EntityType}", operation, typeof(T).Name);
        // To persist to a database table, configure a logging provider (e.g., Serilog or custom) that writes to the DB.
    }

    public async Task<int> CountAllAsync(CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            return await dbContext.Set<T>().CountAsync(token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(CountAllAsync));
            throw;
        }
    }

    public async Task<int> CountFindAsync(Expression<Func<T, bool>> expressionPredicate,  CancellationToken token = default)
    {
               using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            return await dbContext.Set<T>().CountAsync(expressionPredicate, token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(CountFindAsync));
            throw;
        }

    }

    public async Task<IEnumerable<T>> FindListAsync(Expression<Func<T, bool>> expressionPredicate, bool trackChanges = true, CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            var result = (trackChanges) 
                ? dbContext.Set<T>().Where(expressionPredicate) 
                : dbContext.Set<T>().AsNoTracking().Where(expressionPredicate);

            return await result.ToListAsync(token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(FindListAsync));
            throw;
        }
    }

    public async Task<IEnumerable<T>> FindListAsync(Expression<Func<T, bool>> expressionPredicate, Expression<Func<T, object>>[]? includes, bool trackChanges = true, CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            IQueryable<T> query = trackChanges
                ? dbContext.Set<T>()
                : dbContext.Set<T>().AsNoTracking();

            query = ApplyIncludes(query, includes);
            query = query.Where(expressionPredicate);
            return await query.ToListAsync(token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(FindListAsync));
            throw;
        }
    }

    public async Task<IEnumerable<T>> FindListAsync(Expression<Func<T, bool>> expressionPredicate, bool includeAllNavigations, bool trackChanges = true, CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            IQueryable<T> query = trackChanges
                ? dbContext.Set<T>()
                : dbContext.Set<T>().AsNoTracking();
            if (includeAllNavigations)
            {
                query = IncludeAllNavigations(dbContext, query);
            }
            query = query.Where(expressionPredicate);
            return await query.ToListAsync(token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(FindListAsync));
            throw;
        }
    }

    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> expressionPredicate, bool trackChanges = true, CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);

        try
        {
            var result = (trackChanges)
                ? dbContext.Set<T>().FirstOrDefaultAsync(expressionPredicate, token)
                : dbContext.Set<T>().AsNoTracking().FirstOrDefaultAsync(expressionPredicate, token);
            return await result;
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(FindOneAsync));
            throw;
        }
    }

    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> expressionPredicate, Expression<Func<T, object>>[]? includes, bool trackChanges = true, CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            IQueryable<T> query = trackChanges
                ? dbContext.Set<T>()
                : dbContext.Set<T>().AsNoTracking();
            query = ApplyIncludes(query, includes);
            return await query.FirstOrDefaultAsync(expressionPredicate, token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(FindOneAsync));
            throw;
        }
    }

    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> expressionPredicate, bool includeAllNavigations, bool trackChanges = true, CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            IQueryable<T> query = trackChanges
                ? dbContext.Set<T>()
                : dbContext.Set<T>().AsNoTracking();
            if (includeAllNavigations)
            {
                query = IncludeAllNavigations(dbContext, query);
            }
            return await query.FirstOrDefaultAsync(expressionPredicate, token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(FindOneAsync));
            throw;
        }
    }

    public async Task<IEnumerable<T>> GetAllAsync(bool trackChanges = true, CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            var result = (trackChanges)
                ? dbContext.Set<T>()
                : dbContext.Set<T>().AsNoTracking();
            return await result.ToListAsync(token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(GetAllAsync));
            throw;
        }
    }

    public async Task<IEnumerable<T>> GetAllAsync(Expression<Func<T, object>>[]? includes, bool trackChanges = true, CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            IQueryable<T> query = trackChanges
                ? dbContext.Set<T>()
                : dbContext.Set<T>().AsNoTracking();
            if (null != includes && includes.Count() >0)
            {
                query = ApplyIncludes(query, includes);
            }
            return await query.ToListAsync(token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(GetAllAsync));
            throw;
        }
    }

    public async Task<IEnumerable<T>> GetAllAsync(bool includeAllNavigations, bool trackChanges = true, CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            var query = (trackChanges)
                ? dbContext.Set<T>()
                : dbContext.Set<T>().AsNoTracking();

            if (includeAllNavigations)
            {
                query = IncludeAllNavigations(dbContext, query);
            }
            return await query.ToListAsync(token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(GetAllAsync));
            throw;
        }
    }

    public async Task<T?> GetByIdAsync(Guid id, bool trackChanges = true, CancellationToken token = default)
    {
        try
        {
            return await FindOneAsync(item => item.Id == id, trackChanges, token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(GetByIdAsync));
            throw;
        }
    }

    public async Task<T?> GetByIdAsync(Guid id, Expression<Func<T, object>>[]? includes, bool trackChanges = true, CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            IQueryable<T> query = trackChanges
                ? dbContext.Set<T>()
                : dbContext.Set<T>().AsNoTracking();

            query = query.Where(item => item.Id == id);

            if (null != includes && includes.Count() >0)
            {
                query = ApplyIncludes(query, includes);
            }
            return await query.FirstOrDefaultAsync(token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(GetByIdAsync));
            throw;
        }
    }

    public async Task<T?> GetByIdAsync(Guid id, bool includeAllNavigations, bool trackChanges = true, CancellationToken token = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        try
        {
            IQueryable<T> query = trackChanges
                ? dbContext.Set<T>()
                : dbContext.Set<T>().AsNoTracking();

            query = query.Where(item => item.Id == id);

            if (includeAllNavigations)
            {
                query = IncludeAllNavigations(dbContext, query);
            }
            return await query.FirstOrDefaultAsync(token);
        }
        catch (Exception ex)
        {
            LogError(ex, nameof(GetByIdAsync));
            throw;
        }
    }

    private static IQueryable<T> ApplyIncludes(IQueryable<T> query, Expression<Func<T, object>>[]? includes)
    {
        if (includes != null && includes.Length !=0)
        {
            foreach (var include in includes)
            {
                query = query.Include(include);
            }
        }
        return query;
    }

    private static IQueryable<T> IncludeAllNavigations(BenDataContext dbContext, IQueryable<T> query)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(T));
        if (entityType is null) return query;
        var navigations = entityType.GetNavigations();
        if (null != navigations && navigations.Count() >0)
        {
            foreach (var nav in navigations)
            {
                query = query.Include(nav.Name);
            }
        }
        return query;
    }

}
