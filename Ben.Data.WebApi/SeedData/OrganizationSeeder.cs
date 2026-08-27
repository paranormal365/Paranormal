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
                    DateOnboarded = DateTime.UtcNow, // seeded = established; no first-run wizard
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

            // The same three a real creation adds. This group DID get them before, but only
            // because the backfill seeders happen to run immediately after this one in Program.cs
            // — an ordering dependency nothing states and nothing checks. That is precisely how
            // the development and roster seeders came to produce groups with no roles, ladder or
            // duties: they were added later, below the backfills, and nobody noticed for as long
            // as the NEXT startup covered for them. Adding them here makes every seeder
            // self-sufficient, so the backfills go back to being what they are named for —
            // a net for databases that predate all this, not a thing correctness leans on.
            Ben.Data.Source.Services.OrgMemberLevelDefaults.AddDefaultLevels(db, org.Id, owner.Id);
            Ben.Data.Source.Services.OrgInvestigationDutyDefaults.AddDefaultDuties(db, org.Id, owner.Id);
            Ben.Data.Source.Services.OrgRoleDefaults.AddDefaultRoles(db, org.Id, owner.Id);
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
        await EnsureSupportTicketMessageType(db, owner.Id);
        await EnsureEquipmentCheckoutMessageType(db, owner.Id);
        await EnsureEquipmentQuestionMessageType(db, owner.Id);
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

    /// <summary>
    /// Fixed ID for the "Support Ticket" UserMessageType — the notice app administrators get when
    /// a visitor uses the contact form.
    /// </summary>
    internal static readonly Guid SupportTicketMessageTypeId =
        new("c3d4e5f6-0718-9012-cdef-345678901234");

    private static async Task EnsureSupportTicketMessageType(BenDataContext db, Guid createdByUserId)
    {
        if (await db.UserMessageTypes.AnyAsync(t => t.Id == SupportTicketMessageTypeId))
            return;

        db.UserMessageTypes.Add(new UserMessageType
        {
            Id                 = SupportTicketMessageTypeId,
            Name               = "Support Ticket",
            Description        = "System-generated notice to app administrators that a visitor submitted the contact form.",
            IconClass          = "fa-life-ring",
            ColorClass         = "text-primary",
            IsActive           = true,
            IsPublic           = false,
            SortOrder          = 120,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = createdByUserId,
        });
        await db.SaveChangesAsync();
        Console.WriteLine("[OrganizationSeeder] Seeded 'Support Ticket' UserMessageType.");
    }

    /// <summary>
    /// Fixed ID for the "Equipment Checkout" UserMessageType — the notices sent when somebody asks
    /// to borrow equipment and when that request is decided.
    /// </summary>
    internal static readonly Guid EquipmentCheckoutMessageTypeId =
        new("d4e5f607-1829-0123-def0-456789012345");

    private static async Task EnsureEquipmentCheckoutMessageType(BenDataContext db, Guid createdByUserId)
    {
        if (await db.UserMessageTypes.AnyAsync(t => t.Id == EquipmentCheckoutMessageTypeId))
            return;

        db.UserMessageTypes.Add(new UserMessageType
        {
            Id                 = EquipmentCheckoutMessageTypeId,
            Name               = "Equipment Checkout",
            Description        = "System-generated notice about an equipment borrowing request or its outcome.",
            IconClass          = "fa-toolbox",
            ColorClass         = "text-primary",
            IsActive           = true,
            IsPublic           = false,
            SortOrder          = 130,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = createdByUserId,
        });
        await db.SaveChangesAsync();
        Console.WriteLine("[OrganizationSeeder] Seeded 'Equipment Checkout' UserMessageType.");
    }

    /// <summary>
    /// Fixed ID for the "Equipment Question" UserMessageType — the notices carrying an anonymous
    /// question to an owner and their answer back.
    /// </summary>
    /// <remarks>
    /// Its own type rather than reusing Equipment Checkout, because these notices are the one place
    /// in the app where the sender is deliberately hidden. Keeping them separately typed means the
    /// inbox can say so, and a future change to loan notices cannot accidentally acquire — or lose
    /// — that property.
    /// </remarks>
    internal static readonly Guid EquipmentQuestionMessageTypeId =
        new("e5f60718-2930-1234-ef01-567890123456");

    private static async Task EnsureEquipmentQuestionMessageType(BenDataContext db, Guid createdByUserId)
    {
        if (await db.UserMessageTypes.AnyAsync(t => t.Id == EquipmentQuestionMessageTypeId))
            return;

        db.UserMessageTypes.Add(new UserMessageType
        {
            Id                 = EquipmentQuestionMessageTypeId,
            Name               = "Equipment Question",
            Description        = "An anonymous question about a piece of equipment, or its answer.",
            IconClass          = "fa-circle-question",
            ColorClass         = "text-primary",
            IsActive           = true,
            IsPublic           = false,
            SortOrder          = 135,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = createdByUserId,
        });
        await db.SaveChangesAsync();
        Console.WriteLine("[OrganizationSeeder] Seeded 'Equipment Question' UserMessageType.");
    }

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
