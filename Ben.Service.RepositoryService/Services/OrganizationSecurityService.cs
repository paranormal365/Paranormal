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

        var hasDirectGrant = await dbContext.OrganizationAccessGrants
            .AsNoTracking()
            .AnyAsync(g =>
                g.OrganizationId == organizationId &&
                g.AppUserId == appUserId &&
                g.TableName == tableName &&
                (g.Actions & actionName) != OrganizationSecurityAction.None,
                token);

        if (hasDirectGrant) return true;

        // Check named role permissions (OR logic across all active roles assigned to the user)
        return await (
            from roleMembership in dbContext.OrganizationRoleMemberships
            join role in dbContext.OrganizationRoles on roleMembership.OrganizationRoleId equals role.Id
            join permission in dbContext.OrganizationRolePermissions on role.Id equals permission.OrganizationRoleId
            join userMembership in dbContext.OrganizationUserMemberships on roleMembership.OrganizationUserMembershipId equals userMembership.Id
            where userMembership.OrganizationId == organizationId
                && userMembership.AppUserId == appUserId
                && userMembership.IsActive
                && role.IsActive
                && permission.TableName == tableName
                && (permission.Actions & actionName) != OrganizationSecurityAction.None
            select role.Id
        ).AnyAsync(token);
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
        // Lowercased, not merely trimmed. The admin path already did this and registration did
        // not, so an organization's public address worked or did not depending on which door it
        // came through — and on SQL Server's case-insensitive collation, mostly by luck.
        var normalizedUrlName = Ben.Data.Common.SlugText.NormalizeOrEmpty(urlName);

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new InvalidOperationException("Organization name is required.");
        }

        var appUserExists = await dbContext.AppUsers.AnyAsync(u => u.Id == appUserId, token);
        if (!appUserExists)
        {
            throw new InvalidOperationException("AppUser not found.");
        }

        // The third door onto the same column. This one checked uniqueness against current names
        // only, validated the shape not at all, and knew nothing about retired addresses — so
        // registration could take a name a group had renamed away from and inherit its links.
        var refusal = await Ben.Data.Source.Services.OrganizationUrlNames
            .RefusalForAsync(dbContext, normalizedUrlName, null, token);

        if (refusal is not null)
        {
            throw new InvalidOperationException(refusal);
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

        Ben.Data.Source.Services.OrgCalendarDefaults.AddDefaultEventTypes(dbContext, organization.Id, appUserId);
        Ben.Data.Source.Services.OrgMemberLevelDefaults.AddDefaultLevels(dbContext, organization.Id, appUserId);
        Ben.Data.Source.Services.OrgInvestigationDutyDefaults.AddDefaultDuties(dbContext, organization.Id, appUserId);


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

    /// <summary>
    /// Creates or updates a member's role/active status within an organization.
    /// </summary>
    /// <remarks>
    /// <see cref="EnsureCanManageOrganizationAsync"/> alone previously treated <c>Owner</c> and
    /// <c>Administrator</c> as equally authorized to call this at all, with no further check on
    /// what a non-Owner caller could actually set — letting an <c>Administrator</c> self-promote
    /// to <c>Owner</c>, or demote/deactivate the real <c>Owner</c>. Since
    /// <see cref="EnsureCanManageOrganizationAsync"/> only lets Owner/Administrator-tier members
    /// (or SuperAdmin) reach this point at all, this method now additionally restricts the
    /// <b>Administrator</b> case specifically — Owner and SuperAdmin callers retain full control:
    /// <list type="bullet">
    /// <item><description>An <c>Administrator</c> caller may not assign the
    /// <see cref="OrganizationMemberRole.Owner"/> role to anyone — exactly one <c>Owner</c>
    /// exists per org, set once at registration (<see cref="RegisterOrganizationAsync"/>); this
    /// generic membership endpoint isn't an ownership-transfer feature.</description></item>
    /// <item><description>An <c>Administrator</c> caller may only assign roles strictly below
    /// <c>Administrator</c> — they cannot mint a peer <c>Administrator</c>, let alone an
    /// <c>Owner</c>.</description></item>
    /// <item><description>Symmetrically, an <c>Administrator</c> caller may not modify a
    /// membership whose *current* role is already <c>Administrator</c> or <c>Owner</c> — no
    /// touching peers or the real Owner.</description></item>
    /// </list>
    /// Independently of caller rank: an org's last active <c>Owner</c> membership can never be
    /// deactivated or demoted through this method (including by SuperAdmin, or by that Owner
    /// themselves) — that would leave the org with no Owner at all, a state nothing else in the
    /// codebase expects or can recover from.
    /// </remarks>
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

        var isSuperAdmin = await IsSuperAdminAsync(dbContext, actingUserId, token);
        if (!isSuperAdmin)
        {
            var actingRole = await dbContext.OrganizationUserMemberships
                .AsNoTracking()
                .Where(m => m.OrganizationId == organizationId && m.AppUserId == actingUserId && m.IsActive)
                .Select(m => (OrganizationMemberRole?)m.Role)
                .FirstOrDefaultAsync(token);

            // EnsureCanManageOrganizationAsync only lets Owner or Administrator through; Owner
            // callers get full control (matches the class's existing "Owner == Administrator for
            // day-to-day management" stance everywhere else), so the restrictions below apply
            // only to the Administrator case.
            if (actingRole != OrganizationMemberRole.Owner)
            {
                // "Manage only roles strictly below Administrator" applies symmetrically: an
                // Administrator can neither ASSIGN a role at-or-above their own rank, nor touch a
                // membership whose CURRENT role is already at-or-above their own rank (a peer
                // Administrator, or the Owner).
                if (role <= OrganizationMemberRole.Administrator)
                    throw new UnauthorizedAccessException("An Administrator can only grant roles below Administrator.");
                if (existing?.Role <= OrganizationMemberRole.Administrator)
                    throw new UnauthorizedAccessException("An Administrator cannot modify a membership at or above their own rank.");
            }
        }

        if (existing is not null && existing.Role == OrganizationMemberRole.Owner && existing.IsActive
            && (role != OrganizationMemberRole.Owner || !isActive))
        {
            var otherActiveOwnerExists = await dbContext.OrganizationUserMemberships
                .AsNoTracking()
                .AnyAsync(m => m.OrganizationId == organizationId && m.Role == OrganizationMemberRole.Owner
                            && m.IsActive && m.AppUserId != targetUserId, token);
            if (!otherActiveOwnerExists)
                throw new InvalidOperationException("Cannot remove or demote the organization's last active Owner.");
        }

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

    public async Task<int> DeleteGrantAsync(Guid organizationId, Guid targetUserId, OrganizationSecurityTable? tableName, Guid actingUserId, CancellationToken token = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(token);
        await EnsureCanManageOrganizationAsync(dbContext, actingUserId, organizationId, token);

        IQueryable<OrganizationAccessGrant> query = dbContext.OrganizationAccessGrants
            .Where(g => g.OrganizationId == organizationId && g.AppUserId == targetUserId);

        if (tableName.HasValue)
            query = query.Where(g => g.TableName == tableName.Value);

        var rows = await query.ToListAsync(token);
        dbContext.OrganizationAccessGrants.RemoveRange(rows);
        await dbContext.SaveChangesAsync(token);
        return rows.Count;
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