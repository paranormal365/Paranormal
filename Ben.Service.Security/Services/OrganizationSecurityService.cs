using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Security.Enums;
using Ben.Service.Security.Extensions;
using Ben.Service.Security.Models;
using Microsoft.EntityFrameworkCore;
using DataCommonTable = Ben.Data.Common.Enums.OrganizationSecurityTable;
using DataAccessGrant = Ben.Data.Source.Entities.OrganizationAccessGrant;
using DataMembership = Ben.Data.Source.Entities.OrganizationUserMembership;
using OrganizationMemberRole = Ben.Data.Common.Enums.OrganizationMemberRole;

namespace Ben.Service.Security.Services;

public sealed class OrganizationSecurityService : IOrganizationSecurityService
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;

    public OrganizationSecurityService(IDbContextFactory<BenDataContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId,
        Guid organizationId,
        OrganizationSecurityTable table,
        OrganizationSecurityAction action,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Organization owners have all permissions
        var isOwner = await IsOwnerAsync(userId, organizationId, cancellationToken);
        if (isOwner)
            return true;

        var dataTable = (DataCommonTable)table;

        // Check specific access grants using the Actions bitmask
        var grant = await dbContext.Set<DataAccessGrant>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                g => g.OrganizationId == organizationId &&
                     g.AppUserId == userId &&
                     g.TableName == dataTable &&
                     (g.Actions & action) != OrganizationSecurityAction.None,
                cancellationToken);

        return grant is not null;
    }

    public async Task<bool> IsMemberAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var membership = await dbContext.Set<DataMembership>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.OrganizationId == organizationId &&
                     m.AppUserId == userId,
                cancellationToken);

        return membership is not null;
    }

    public async Task<IReadOnlyList<Guid>> GetUserOrganizationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var organizationIds = await dbContext.Set<DataMembership>()
            .AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return organizationIds;
    }

    public async Task<OrganizationMemberRole?> GetUserRoleAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var membership = await dbContext.Set<DataMembership>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.OrganizationId == organizationId &&
                     m.AppUserId == userId &&
                     m.IsActive,
                cancellationToken);

        if (membership is null)
            return null;

        return membership.Role;
    }

    public async Task<bool> IsOwnerAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var role = await GetUserRoleAsync(userId, organizationId, cancellationToken);
        return role == OrganizationMemberRole.Owner;
    }

    public async Task<IReadOnlyList<(Guid UserId, OrganizationMemberRole Role)>> GetOrganizationMembersAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var members = await dbContext.Set<DataMembership>()
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.IsActive)
            .Select(m => new
            {
                m.AppUserId,
                m.Role
            })
            .ToListAsync(cancellationToken);

        return members.Select(m => (m.AppUserId, m.Role)).ToList();
    }

    public async Task GrantAccessAsync(
        Guid organizationId,
        Guid userId,
        OrganizationSecurityTable table,
        OrganizationSecurityAction actions,
        Guid grantedByUserId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var dataTable = (DataCommonTable)table;

        var existingGrant = await dbContext.Set<DataAccessGrant>()
            .FirstOrDefaultAsync(
                g => g.OrganizationId == organizationId &&
                     g.AppUserId == userId &&
                     g.TableName == dataTable,
                cancellationToken);

        if (existingGrant is not null)
        {
            existingGrant.Actions |= actions;
            existingGrant.DateUpdated = DateTime.UtcNow;
            existingGrant.UpdatedByAppUserId = grantedByUserId;
            dbContext.Update(existingGrant);
        }
        else
        {
            var grant = new DataAccessGrant
            {
                OrganizationId = organizationId,
                AppUserId = userId,
                TableName = dataTable,
                Actions = actions,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = grantedByUserId
            };

            await dbContext.Set<DataAccessGrant>().AddAsync(grant, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAccessAsync(
        Guid organizationId,
        Guid userId,
        OrganizationSecurityTable table,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var dataTable = (DataCommonTable)table;

        var grant = await dbContext.Set<DataAccessGrant>()
            .FirstOrDefaultAsync(
                g => g.OrganizationId == organizationId &&
                     g.AppUserId == userId &&
                     g.TableName == dataTable,
                cancellationToken);

        if (grant is not null)
        {
            dbContext.Set<DataAccessGrant>().Remove(grant);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task AddMemberAsync(
        Guid organizationId,
        Guid userId,
        OrganizationMemberRole role,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existingMembership = await dbContext.Set<DataMembership>()
            .FirstOrDefaultAsync(
                m => m.OrganizationId == organizationId && m.AppUserId == userId,
                cancellationToken);

        if (existingMembership is not null)
        {
            existingMembership.Role = role;
            existingMembership.IsActive = true;
            existingMembership.DateUpdated = DateTime.UtcNow;
            dbContext.Update(existingMembership);
        }
        else
        {
            var membership = new DataMembership
            {
                OrganizationId = organizationId,
                AppUserId = userId,
                Role = role,
                IsActive = true,
                DateCreated = DateTime.UtcNow
            };

            await dbContext.Set<DataMembership>().AddAsync(membership, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveMemberAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var membership = await dbContext.Set<DataMembership>()
            .FirstOrDefaultAsync(
                m => m.OrganizationId == organizationId && m.AppUserId == userId,
                cancellationToken);

        if (membership is not null)
        {
            membership.IsActive = false;
            membership.DateUpdated = DateTime.UtcNow;
            dbContext.Update(membership);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
