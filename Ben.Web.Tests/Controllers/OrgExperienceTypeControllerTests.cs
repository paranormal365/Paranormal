using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for a group adding an experience type the shared taxonomy is missing, and for an app
/// administrator rejecting one afterwards.
/// </summary>
public class OrgExperienceTypeControllerTests
{
    private static readonly Guid CategoryId = Guid.NewGuid();

    private static ExperienceTypeRecord ToRecord(ExperienceType t) => new()
    {
        Id = t.Id,
        ExperienceCategoryId = t.ExperienceCategoryId,
        Name = t.Name,
        Description = t.Description,
        IconClass = t.IconClass,
        SortOrder = t.SortOrder,
        IsActive = t.IsActive,
        IsApproved = t.IsApproved,
        ProposedByOrganizationId = t.ProposedByOrganizationId,
        ApprovedByAppUserId = t.ApprovedByAppUserId,
        DateApproved = t.DateApproved,
        DateCreated = t.DateCreated,
        DateUpdated = t.DateUpdated,
        CreatedByAppUserId = t.CreatedByAppUserId,
        UpdatedByAppUserId = t.UpdatedByAppUserId,
    };

    private static IMapper BuildMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<ExperienceTypeRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is ExperienceType t ? ToRecord(t) : new ExperienceTypeRecord { Name = "" });
        m.Setup(x => x.Map<IEnumerable<ExperienceTypeRecord>>(It.IsAny<object>()))
         .Returns<object>(o => o is IEnumerable<ExperienceType> list ? list.Select(ToRecord) : []);
        return m.Object;
    }

    private static OrgExperienceTypeController BuildController(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new OrgExperienceTypeController(
            factory, BuildMapper(), new Mock<IAuditLogService>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
                }
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid UserId)>
        SeedAsync(OrganizationMemberRole role = OrganizationMemberRole.Owner, bool isActive = true)
    {
        var factory = TestDbFactory.Create();
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = role, IsActive = isActive, DateCreated = DateTime.UtcNow,
        });
        db.ExperienceCategories.Add(new ExperienceCategory
        {
            Id = CategoryId, Name = "Audible", IsActive = true, IsApproved = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();

        return (factory, orgId, userId);
    }

    private static AddOrgExperienceTypeRequest Request(string name)
        => new(CategoryId, name, null);

    // ── Adding ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Added_type_is_live_immediately_but_marked_unreviewed()
    {
        var (factory, orgId, userId) = await SeedAsync();

        var result = await BuildController(factory, userId).Add(orgId, Request("Knocking"), default);

        var record = Assert.IsType<ExperienceTypeRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Knocking", record.Name);
        // Live: approved and active, so the public taxonomy read returns it right away.
        Assert.True(record.IsApproved);
        Assert.True(record.IsActive);
        // Unreviewed: no human approver stamped, and the proposing org recorded.
        Assert.Null(record.ApprovedByAppUserId);
        Assert.Null(record.DateApproved);
        Assert.Equal(orgId, record.ProposedByOrganizationId);
    }

    [Fact]
    public async Task Added_type_is_returned_by_the_public_taxonomy_read()
    {
        // The whole point of going live immediately — assert it against the endpoint the picker
        // actually calls, not just the row we wrote.
        var (factory, orgId, userId) = await SeedAsync();
        await BuildController(factory, userId).Add(orgId, Request("Knocking"), default);

        var publicCtrl = new ExperienceCategoryController(factory, BuildMapper());
        var result = await publicCtrl.GetTypes(CategoryId, default);

        var types = Assert.IsAssignableFrom<IEnumerable<ExperienceTypeRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Contains(types, t => t.Name == "Knocking");
    }

    [Theory]
    [InlineData("knocking")]
    [InlineData("KNOCKING")]
    [InlineData("  Knocking  ")]
    public async Task Adding_a_name_that_already_exists_returns_the_existing_type(string secondAttempt)
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var first = await ctrl.Add(orgId, Request("Knocking"), default);
        var firstRecord = (ExperienceTypeRecord)((OkObjectResult)first.Result!).Value!;

        var second = await ctrl.Add(orgId, Request(secondAttempt), default);
        var secondRecord = (ExperienceTypeRecord)((OkObjectResult)second.Result!).Value!;

        Assert.Equal(firstRecord.Id, secondRecord.Id);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.ExperienceTypes.CountAsync());
    }

    [Theory]
    [InlineData(OrganizationMemberRole.Owner, true)]
    [InlineData(OrganizationMemberRole.Administrator, true)]
    [InlineData(OrganizationMemberRole.Manager, false)]
    [InlineData(OrganizationMemberRole.Member, false)]
    [InlineData(OrganizationMemberRole.Viewer, false)]
    public async Task Only_owners_and_administrators_may_extend_the_taxonomy(
        OrganizationMemberRole role, bool allowed)
    {
        var (factory, orgId, userId) = await SeedAsync(role);

        var result = await BuildController(factory, userId).Add(orgId, Request("Knocking"), default);

        if (allowed) Assert.IsType<OkObjectResult>(result.Result);
        else Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Former_administrator_of_the_group_may_not_add()
    {
        var (factory, orgId, userId) = await SeedAsync(OrganizationMemberRole.Owner, isActive: false);

        var result = await BuildController(factory, userId).Add(orgId, Request("Knocking"), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Blank_name_is_rejected()
    {
        var (factory, orgId, userId) = await SeedAsync();

        var result = await BuildController(factory, userId).Add(orgId, Request("   "), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Unknown_category_is_rejected()
    {
        var (factory, orgId, userId) = await SeedAsync();

        var result = await BuildController(factory, userId)
            .Add(orgId, new AddOrgExperienceTypeRequest(Guid.NewGuid(), "Knocking", null), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ── Notification ──────────────────────────────────────────────────────────

    [Fact]
    public async Task App_administrators_are_notified_and_ordinary_users_are_not()
    {
        var (factory, orgId, userId) = await SeedAsync();

        Guid superAdminId, adminId, bystanderId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var superRole = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.SuperAdmin };
            var adminRole = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.Admin };
            db.Roles.AddRange(superRole, adminRole);

            superAdminId = Guid.NewGuid();
            adminId = Guid.NewGuid();
            bystanderId = Guid.NewGuid();

            db.UserRoles.AddRange(
                new IdentityUserRole<Guid> { UserId = superAdminId, RoleId = superRole.Id },
                new IdentityUserRole<Guid> { UserId = adminId, RoleId = adminRole.Id });
            await db.SaveChangesAsync();
        }

        await BuildController(factory, userId).Add(orgId, Request("Knocking"), default);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var recipients = await db.UserMessageTos.Select(t => t.ToAppUserId).ToListAsync();

            Assert.Contains(superAdminId, recipients);
            Assert.Contains(adminId, recipients);
            Assert.DoesNotContain(bystanderId, recipients);

            var message = await db.UserMessages.SingleAsync();
            Assert.Contains("Knocking", message.MessageSubject);
        }
    }

    // ── Rejecting ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rejecting_removes_the_tagging_but_keeps_the_entry_itself()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var added = await BuildController(factory, userId).Add(orgId, Request("Knocking"), default);
        var typeId = ((ExperienceTypeRecord)((OkObjectResult)added.Result!).Value!).Id;

        var entryId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CaseTimelineEntries.Add(new CaseTimelineEntry
            {
                Id = entryId,
                CaseId = Guid.NewGuid(),
                Title = "Heard three knocks",
                Body = "Three distinct knocks from the upstairs hallway.",
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            });
            db.CaseTimelineEntryExperienceTypes.Add(new CaseTimelineEntryExperienceType
            {
                CaseTimelineEntryId = entryId,
                ExperienceTypeId = typeId,
            });
            await db.SaveChangesAsync();
        }

        var adminCtrl = new AdminExperienceTypeController(factory, BuildMapper());
        var result = await adminCtrl.Reject(CategoryId, typeId, default);

        var response = Assert.IsType<RejectExperienceTypeResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, response.UsagesRemoved);

        await using (var db = await factory.CreateDbContextAsync())
        {
            // The tag and the type are gone…
            Assert.Empty(await db.CaseTimelineEntryExperienceTypes.ToListAsync());
            Assert.Null(await db.ExperienceTypes.FirstOrDefaultAsync(t => t.Id == typeId));

            // …and the account of what happened is untouched.
            var entry = await db.CaseTimelineEntries.SingleAsync(e => e.Id == entryId);
            Assert.Equal("Heard three knocks", entry.Title);
            Assert.Equal("Three distinct knocks from the upstairs hallway.", entry.Body);
        }
    }

    [Fact]
    public async Task Confirming_clears_the_unreviewed_flag()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var added = await BuildController(factory, userId).Add(orgId, Request("Knocking"), default);
        var typeId = ((ExperienceTypeRecord)((OkObjectResult)added.Result!).Value!).Id;

        var reviewerId = Guid.NewGuid();
        var adminCtrl = new AdminExperienceTypeController(factory, BuildMapper())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, reviewerId.ToString())], "Bearer"))
                }
            }
        };

        var result = await adminCtrl.Approve(CategoryId, typeId, default);

        var record = Assert.IsType<ExperienceTypeRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        // The approver stamp is exactly what the admin screen reads to stop showing the marker.
        Assert.Equal(reviewerId, record.ApprovedByAppUserId);
        Assert.NotNull(record.DateApproved);
    }
}
