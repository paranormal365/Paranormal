using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.SeedData;

internal static class OrganizationSeeder
{
    internal static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var enabled = config.GetValue<bool>("SeedData:SeedOrganization:Enabled");
        if (!enabled) return;

        var orgName    = config["SeedData:SeedOrganization:OrgName"];
        var orgUrlName = config["SeedData:SeedOrganization:OrgUrlName"];
        var ownerEmail = config["SeedData:SuperAdmin:Email"];

        if (string.IsNullOrWhiteSpace(orgName) || string.IsNullOrWhiteSpace(ownerEmail))
            return;

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var dbFactory   = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();

        // ── Resolve owner ─────────────────────────────────────────────────────
        var owner = await userManager.FindByEmailAsync(ownerEmail);
        if (owner is null)
        {
            Console.WriteLine($"[OrganizationSeeder] Owner '{ownerEmail}' not found — skipping.");
            return;
        }

        // ── Create / find seed users ──────────────────────────────────────────
        var userConfigs = config.GetSection("SeedData:SeedOrganization:Users").GetChildren().ToList();
        var seededUsers = new List<(AppUser User, bool IsMember, bool IsOrgAdmin)>();

        foreach (var uc in userConfigs)
        {
            var email       = uc["Email"];
            var displayName = uc["DisplayName"];
            var password    = uc["Password"];
            var isMember    = uc.GetValue<bool>("IsMember");
            var isOrgAdmin  = uc.GetValue<bool>("IsOrgAdmin");

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                continue;

            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new AppUser
                {
                    UserName      = email,
                    Email         = email,
                    DisplayName   = displayName,
                    EmailConfirmed = true,
                    DateCreated   = DateTime.UtcNow
                };
                var result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to create user '{email}': {string.Join(", ", result.Errors.Select(e => e.Description))}");

                Console.WriteLine($"[OrganizationSeeder] Created user: {email}");
            }

            seededUsers.Add((user, isMember, isOrgAdmin));
        }

        // ── Create / find organization ────────────────────────────────────────
        await using var db = await dbFactory.CreateDbContextAsync();

        var org = await db.Organizations
            .FirstOrDefaultAsync(o => o.UrlName == orgUrlName);

        if (org is null)
        {
            org = new Organization
            {
                Id                  = Guid.NewGuid(),
                Name                = orgName!,
                UrlName             = orgUrlName ?? orgName!.ToLowerInvariant().Replace(" ", "-"),
                DateCreated         = DateTime.UtcNow,
                CreatedByAppUserId  = owner.Id
            };
            db.Organizations.Add(org);
            await db.SaveChangesAsync();
            Console.WriteLine($"[OrganizationSeeder] Created organization: {orgName}");
        }

        // ── Add owner membership (Role = Owner) ───────────────────────────────
        await EnsureMembership(db, org.Id, owner.Id, OrganizationMemberRole.Owner, createdBy: owner.Id);

        // ── Add seed user memberships ─────────────────────────────────────────
        foreach (var (user, isMember, isOrgAdmin) in seededUsers)
        {
            if (!isMember) continue;
            await EnsureMembership(db, org.Id, user.Id, isOrgAdmin ? OrganizationMemberRole.Administrator : OrganizationMemberRole.Member, createdBy: owner.Id);
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"[OrganizationSeeder] Organization seed complete — org: '{orgName}', members: {seededUsers.Count(u => u.IsMember) + 1}.");

        // ── Seed UserMessageType for membership responses ────────────────────
        await EnsureMembershipResponseMessageType(db, owner.Id);
        await EnsureTaxonomyReviewMessageType(db, owner.Id);
    }

    /// <summary>
    /// Fixed ID for the "Organization Membership Response" UserMessageType.
    /// Controllers that create the response message reference this constant.
    /// </summary>
    internal static readonly Guid MembershipResponseMessageTypeId =
        new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    /// <summary>
    /// Fixed ID for the "Experience Taxonomy Review" UserMessageType — the notice app
    /// administrators get when a group adds a new experience type.
    /// </summary>
    internal static readonly Guid TaxonomyReviewMessageTypeId =
        new("b2c3d4e5-f607-8901-bcde-f23456789012");

    private static async Task EnsureTaxonomyReviewMessageType(BenDataContext db, Guid createdByUserId)
    {
        if (await db.UserMessageTypes.AnyAsync(t => t.Id == TaxonomyReviewMessageTypeId))
            return;

        db.UserMessageTypes.Add(new UserMessageType
        {
            Id                 = TaxonomyReviewMessageTypeId,
            Name               = "Experience Taxonomy Review",
            Description        = "System-generated notice to app administrators that a group added a new experience type, which is live and awaiting review.",
            IconClass          = "fa-tags",
            ColorClass         = "text-warning",
            IsActive           = true,
            IsPublic           = false,
            SortOrder          = 110,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = createdByUserId,
        });
        await db.SaveChangesAsync();
        Console.WriteLine("[OrganizationSeeder] Seeded 'Experience Taxonomy Review' UserMessageType.");
    }

    private static async Task EnsureMembershipResponseMessageType(BenDataContext db, Guid createdByUserId)
    {
        if (await db.UserMessageTypes.AnyAsync(t => t.Id == MembershipResponseMessageTypeId))
            return;

        db.UserMessageTypes.Add(new UserMessageType
        {
            Id                 = MembershipResponseMessageTypeId,
            Name               = "Organization Membership Response",
            Description        = "System-generated message sent to users when their organization membership application is accepted or denied.",
            IconClass          = "fa-building",
            ColorClass         = "text-info",
            IsActive           = true,
            IsPublic           = false,
            SortOrder          = 100,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = createdByUserId,
        });
        await db.SaveChangesAsync();
        Console.WriteLine("[OrganizationSeeder] Seeded 'Organization Membership Response' UserMessageType.");
    }

    private static async Task EnsureMembership(
        BenDataContext db, Guid orgId, Guid userId, OrganizationMemberRole role, Guid createdBy)
    {
        var exists = await db.OrganizationUserMemberships
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId);

        if (!exists)
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id                  = Guid.NewGuid(),
                OrganizationId      = orgId,
                AppUserId           = userId,
                Role                = role,
                IsActive            = true,
                DateCreated         = DateTime.UtcNow,
                CreatedByAppUserId  = createdBy
            });
        }
    }
}
