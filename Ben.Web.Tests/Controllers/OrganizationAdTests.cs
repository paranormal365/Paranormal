using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 166 W3: the ad review chain. The load-bearing invariant — the public endpoints serve
/// APPROVED and nothing else, ever — is regressed the hardest, because its consumer is an
/// anonymous visitor and its failure mode is a group's unreviewed words on the front page.
/// </summary>
public sealed class OrganizationAdTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static async Task<(IDbContextFactory<BenDataContext> factory, Guid orgId, Guid ownerId)> SeedAsync()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = ownerId, UserName = "o@t.com", Email = "o@t.com" });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Night Watch", UrlName = "night-watch",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (factory, orgId, ownerId);
    }

    private static OrganizationAdController OrgController(
        IDbContextFactory<BenDataContext> factory, Guid userId, bool hasPermission = true)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasPermission);
        return new OrganizationAdController(factory, security.Object, new Mock<IAuditLogService>().Object)
        {
            ControllerContext = Context(userId, superAdmin: false),
        };
    }

    private static AdminOrganizationAdController AdminController(
        IDbContextFactory<BenDataContext> factory, Guid userId)
        => new(factory, new PlatformMessageService(factory))
        {
            ControllerContext = Context(userId, superAdmin: true),
        };

    private static ControllerContext Context(Guid userId, bool superAdmin)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, userId.ToString())];
        if (superAdmin) claims.Add(new Claim(ClaimTypes.Role, "SuperAdmin"));
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
            },
        };
    }

    private static SaveOrganizationAdRequest GoodAd(string headline = "Nashville's night watch")
        => new(headline, "Free investigations, real equipment. Ask us over.", null, "org");

    private static OrganizationAdRecord Body(ActionResult<OrganizationAdRecord> result)
        => Assert.IsType<OrganizationAdRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

    // ── The group's side ──────────────────────────────────────────────────────

    [Fact]
    public async Task Create_validates_lengths_and_the_target_whitelist()
    {
        var (factory, orgId, ownerId) = await SeedAsync();
        var ctrl = OrgController(factory, ownerId);

        Assert.IsType<BadRequestObjectResult>((await ctrl.Create(orgId,
            GoodAd(new string('x', 81)), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await ctrl.Create(orgId,
            new SaveOrganizationAdRequest("ok", new string('x', 301), null, "org"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await ctrl.Create(orgId,
            new SaveOrganizationAdRequest("ok", "ok", null, "https://elsewhere.example"), default)).Result);

        var created = Body(await ctrl.Create(orgId, GoodAd(), default));
        Assert.Equal(OrganizationAdStatus.Draft, created.Status);
    }

    [Fact]
    public async Task One_live_ad_per_group_but_a_rejection_does_not_block_a_fresh_start()
    {
        var (factory, orgId, ownerId) = await SeedAsync();
        var ctrl = OrgController(factory, ownerId);

        var first = Body(await ctrl.Create(orgId, GoodAd(), default));
        var second = await ctrl.Create(orgId, GoodAd("Another"), default);
        Assert.Contains("already has an ad",
            Assert.IsType<BadRequestObjectResult>(second.Result).Value!.ToString());

        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.OrganizationAds.SingleAsync(a => a.Id == first.Id)).Status = OrganizationAdStatus.Rejected;
            await db.SaveChangesAsync();
        }
        Assert.Equal(OrganizationAdStatus.Draft,
            Body(await ctrl.Create(orgId, GoodAd("Take two"), default)).Status);
    }

    [Fact]
    public async Task Editing_a_submitted_or_approved_ad_pulls_it_back_to_draft()
    {
        var (factory, orgId, ownerId) = await SeedAsync();
        var ctrl = OrgController(factory, ownerId);
        var ad = Body(await ctrl.Create(orgId, GoodAd(), default));
        Body(await ctrl.Submit(orgId, ad.Id, default));

        var edited = Body(await ctrl.Update(orgId, ad.Id, GoodAd("Reworded"), default));

        // The reviewed text is the approved text: any edit re-enters review from the start.
        Assert.Equal(OrganizationAdStatus.Draft, edited.Status);
        Assert.Null(edited.DateSubmitted);
    }

    [Fact]
    public async Task The_state_machine_refuses_out_of_order_moves_and_the_gate_refuses_outsiders()
    {
        var (factory, orgId, ownerId) = await SeedAsync();
        var ctrl = OrgController(factory, ownerId);
        var ad = Body(await ctrl.Create(orgId, GoodAd(), default));

        Assert.IsType<BadRequestObjectResult>((await ctrl.Withdraw(orgId, ad.Id, default)).Result);   // draft: nothing to withdraw
        Body(await ctrl.Submit(orgId, ad.Id, default));
        Assert.IsType<BadRequestObjectResult>((await ctrl.Submit(orgId, ad.Id, default)).Result);     // submitted twice

        var outsider = OrgController(factory, Guid.NewGuid(), hasPermission: false);
        Assert.IsType<ForbidResult>((await outsider.GetAll(orgId, default)).Result);
        Assert.IsType<ForbidResult>((await outsider.Create(orgId, GoodAd(), default)).Result);
    }

    // ── Review ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Review_only_touches_submitted_ads_rejection_needs_a_reason_and_the_group_hears()
    {
        var (factory, orgId, ownerId) = await SeedAsync();
        var ctrl = OrgController(factory, ownerId);
        var admin = AdminController(factory, Guid.NewGuid());
        var ad = Body(await ctrl.Create(orgId, GoodAd(), default));

        Assert.IsType<BadRequestObjectResult>(await admin.Approve(ad.Id, default));   // still a draft

        Body(await ctrl.Submit(orgId, ad.Id, default));
        Assert.IsType<BadRequestObjectResult>(
            await admin.Reject(ad.Id, new RejectOrganizationAdRequest("  "), default));

        Assert.IsType<NoContentResult>(
            await admin.Reject(ad.Id, new RejectOrganizationAdRequest("Too vague."), default));

        await using var db = await factory.CreateDbContextAsync();
        var row = await db.OrganizationAds.SingleAsync(a => a.Id == ad.Id);
        Assert.Equal(OrganizationAdStatus.Rejected, row.Status);
        Assert.Equal("Too vague.", row.RejectionReason);
        // The group's owner was told — a decision sitting silently in a table is the
        // write-only-feature shape.
        Assert.Single(await db.UserMessageTos.Where(t => t.ToAppUserId == ownerId).ToListAsync());
    }

    // ── The public invariant ──────────────────────────────────────────────────

    private static PublicPromotedGroupsController PublicController(IDbContextFactory<BenDataContext> factory)
        => new(factory, new Mock<IFileStorageService>().Object);

    [Fact]
    public async Task The_public_endpoint_serves_approved_and_nothing_else_ever()
    {
        var (factory, orgId, ownerId) = await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var status in new[]
                     { OrganizationAdStatus.Draft, OrganizationAdStatus.Submitted, OrganizationAdStatus.Rejected })
                db.OrganizationAds.Add(new OrganizationAd
                {
                    Id = Guid.NewGuid(), OrganizationId = orgId,
                    Headline = $"UNREVIEWED {status}", Body = "x", Status = status,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
                });
            db.OrganizationAds.Add(new OrganizationAd
            {
                Id = Guid.NewGuid(), OrganizationId = orgId,
                Headline = "The approved one", Body = "x", Status = OrganizationAdStatus.Approved,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var cards = Assert.IsAssignableFrom<IEnumerable<PromotedGroupCard>>(
            Assert.IsType<OkObjectResult>(
                (await PublicController(factory).Get(10, default)).Result).Value).ToList();

        var card = Assert.Single(cards);
        Assert.Equal("The approved one", card.Headline);
    }

    [Fact]
    public async Task The_image_route_is_as_unpublished_as_the_ad()
    {
        var (factory, orgId, ownerId) = await SeedAsync();
        Guid adId = Guid.NewGuid(), fileId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, AppUserId = ownerId, UploadFileTypeId = Guid.NewGuid(),
                FileName = "a.png", StoredFileName = "a.png", ContentType = "image/png",
                FileSize = 4, FileData = [1, 2, 3, 4], IsPublic = false,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.OrganizationAds.Add(new OrganizationAd
            {
                Id = adId, OrganizationId = orgId, Headline = "h", Body = "b",
                ImageUploadFileId = fileId, Status = OrganizationAdStatus.Submitted,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>(await PublicController(factory).Image(adId, default));

        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.OrganizationAds.SingleAsync(a => a.Id == adId)).Status = OrganizationAdStatus.Approved;
            await db.SaveChangesAsync();
        }
        Assert.IsType<FileContentResult>(await PublicController(factory).Image(adId, default));
    }
}
