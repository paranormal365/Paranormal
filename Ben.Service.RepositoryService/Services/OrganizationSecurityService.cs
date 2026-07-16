using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Service.RepositoryService.Services;

public class OrganizationSecurityService : IOrganizationSecurityService
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;

    public OrganizationSecurityService(IDbContextFactory<BenDataContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IReadOnlyList<AppUser>> SearchUsersAsync(Guid actingUserId, string? query, int skip = 0, int take = 25, CancellationToken token = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);

        var normalizedQuery = query?.Trim();
        var validatedSkip = Math.Max(skip, 0);
        var cappedTake = Math.Clamp(take, 1, 100);

        IQueryable<AppUser> userQuery;

        if (await IsSuperAdminAsync(dbContext, actingUserId, token))
        {
            userQuery = dbContext.AppUsers.AsNoTracking();
        }
        else
        {
            var orgIds = dbContext.OrganizationUserMemberships
                .AsNoTracking()
                .Where(m => m.AppUserId == actingUserId && m.IsActive)
                .Select(m => m.OrganizationId);

            userQuery =
                (from candidate in dbContext.AppUsers.AsNoTracking()
                 join membership in dbContext.OrganizationUserMemberships.AsNoTracking() on candidate.Id equals membership.AppUserId
                 where membership.IsActive && orgIds.Contains(membership.OrganizationId)
                 select candidate)
                .Distinct();
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            var like = $"%{normalizedQuery}%";
            userQuery = userQuery.Where(u =>
                EF.Functions.Like(u.Email ?? string.Empty, like) ||
                EF.Functions.Like(u.UserName ?? string.Empty, like) ||
                EF.Functions.Like(u.DisplayName ?? string.Empty, like));
        }

        return await userQuery
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .Skip(validatedSkip)
            .Take(cappedTake)
            .ToListAsync(token);
    }

    public async Task<bool> HasAccessAsync(Guid appUserId, Guid organizationId, OrganizationSecurityTable tableName, OrganizationSecurityAction actionName, CancellationToken token = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);

        if (await IsSuperAdminAsync(dbContext, appUserId, token))
        {
            return true;
        }

        var membership = await dbContext.OrganizationUserMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.AppUserId == appUserId && m.IsActive, token);

        if (membership is null)
        {
            return false;
        }

        if (membership.Role is OrganizationMemberRole.Owner or OrganizationMemberRole.Administrator)
        {
            return true;
        }

        return await dbContext.OrganizationAccessGrants
            .AsNoTracking()
            .AnyAsync(g =>
                g.OrganizationId == organizationId &&
                g.AppUserId == appUserId &&
                g.TableName == tableName &&
                (g.Actions & actionName) != OrganizationSecurityAction.None,
                token);
    }

    public async Task<IReadOnlyList<Organization>> GetOrganizationsForUserAsync(Guid appUserId, CancellationToken token = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);

        if (await IsSuperAdminAsync(dbContext, appUserId, token))
        {
            return await dbContext.Organizations
                .AsNoTracking()
                .OrderBy(o => o.Name)
                .ToListAsync(token);
        }

        return await
            (from membership in dbContext.OrganizationUserMemberships.AsNoTracking()
             join organization in dbContext.Organizations.AsNoTracking() on membership.OrganizationId equals organization.Id
             where membership.AppUserId == appUserId && membership.IsActive
             orderby organization.Name
             select organization)
            .Distinct()
            .ToListAsync(token);
    }

    public async Task<Organization> RegisterOrganizationAsync(Guid appUserId, string name, string urlName, CancellationToken token = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);

        var normalizedName = name.Trim();
        var normalizedUrlName = urlName.Trim();

        if (string.IsNullOrWhiteSpace(normalizedName) || string.IsNullOrWhiteSpace(normalizedUrlName))
        {
            throw new InvalidOperationException("Organization name and urlName are required.");
        }

        var appUserExists = await dbContext.AppUsers.AnyAsync(u => u.Id == appUserId, token);
        if (!appUserExists)
        {
            throw new InvalidOperationException("AppUser not found.");
        }

        var urlExists = await dbContext.Organizations.AnyAsync(o => o.UrlName == normalizedUrlName, token);
        if (urlExists)
        {
            throw new InvalidOperationException("An organization with this urlName already exists.");
        }

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            UrlName = normalizedUrlName,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = appUserId
        };

        dbContext.Organizations.Add(organization);

        dbContext.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            AppUserId = appUserId,
            Role = OrganizationMemberRole.Owner,
            IsActive = true,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = appUserId
        });

        await dbContext.SaveChangesAsync(token);
        return organization;
    }

    public async Task<IReadOnlyList<OrganizationUserMembership>> GetOrganizationUsersAsync(Guid organizationId, Guid actingUserId, CancellationToken token = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        await EnsureCanManageOrganizationAsync(dbContext, actingUserId, organizationId, token);

        var users = await dbContext.OrganizationUserMemberships
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.DateCreated)
            .ToListAsync(token);

        return users;
    }

    public async Task<OrganizationUserMembership> UpsertMembershipAsync(Guid organizationId, Guid targetUserId, OrganizationMemberRole role, bool isActive, Guid actingUserId, CancellationToken token = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        await EnsureCanManageOrganizationAsync(dbContext, actingUserId, organizationId, token);

        var exists = await dbContext.Organizations.AnyAsync(o => o.Id == organizationId, token);
        if (!exists)
        {
            throw new InvalidOperationException("Organization not found.");
        }

        var targetUserExists = await dbContext.AppUsers.AnyAsync(u => u.Id == targetUserId, token);
        if (!targetUserExists)
        {
            throw new InvalidOperationException("Target user not found.");
        }

        var existing = await dbContext.OrganizationUserMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.AppUserId == targetUserId, token);

        if (existing is null)
        {
            existing = new OrganizationUserMembership
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                AppUserId = targetUserId,
                Role = role,
                IsActive = isActive,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = actingUserId
            };

            dbContext.OrganizationUserMemberships.Add(existing);
        }
        else
        {
            existing.Role = role;
            existing.IsActive = isActive;
            existing.DateUpdated = DateTime.UtcNow;
            existing.UpdatedByAppUserId = actingUserId;
        }

        await dbContext.SaveChangesAsync(token);
        return existing;
    }

    public async Task<OrganizationAccessGrant> SetAccessGrantAsync(Guid organizationId, Guid targetUserId, OrganizationSecurityTable tableName, OrganizationSecurityAction actions, Guid actingUserId, CancellationToken token = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        await EnsureCanManageOrganizationAsync(dbContext, actingUserId, organizationId, token);

        var hasMembership = await dbContext.OrganizationUserMemberships
            .AnyAsync(m => m.OrganizationId == organizationId && m.AppUserId == targetUserId && m.IsActive, token);

        if (!hasMembership)
        {
            throw new InvalidOperationException("Target user is not an active member of the organization.");
        }

        var existing = await dbContext.OrganizationAccessGrants.FirstOrDefaultAsync(g =>
            g.OrganizationId == organizationId &&
            g.AppUserId == targetUserId &&
            g.TableName == tableName,
            token);

        if (existing is null)
        {
            existing = new OrganizationAccessGrant
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                AppUserId = targetUserId,
                TableName = tableName,
                Actions = actions,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = actingUserId
            };

            dbContext.OrganizationAccessGrants.Add(existing);
        }
        else
        {
            existing.Actions = actions;
            existing.DateUpdated = DateTime.UtcNow;
            existing.UpdatedByAppUserId = actingUserId;
        }

        await dbContext.SaveChangesAsync(token);
        return existing;
    }

    private static async Task<bool> IsSuperAdminAsync(BenDataContext dbContext, Guid appUserId, CancellationToken token)
    {
        return await
            (from userRole in dbContext.Set<IdentityUserRole<Guid>>()
             join role in dbContext.Set<IdentityRole<Guid>>() on userRole.RoleId equals role.Id
             where userRole.UserId == appUserId && role.Name == RoleNames.SuperAdmin
             select role.Id)
            .AnyAsync(token);
    }

    private async Task EnsureCanManageOrganizationAsync(BenDataContext dbContext, Guid actingUserId, Guid organizationId, CancellationToken token)
    {
        if (await IsSuperAdminAsync(dbContext, actingUserId, token))
        {
            return;
        }

        var isOrgAdmin = await dbContext.OrganizationUserMemberships
            .AsNoTracking()
            .AnyAsync(m =>
                m.OrganizationId == organizationId &&
                m.AppUserId == actingUserId &&
                m.IsActive &&
                (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator),
                token);

        if (!isOrgAdmin)
        {
            throw new UnauthorizedAccessException("Only superadmin or an active organization admin can manage organization access settings.");
        }
    }
}