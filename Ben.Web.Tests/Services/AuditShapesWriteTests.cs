using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Backlog 257: changes whose audit compared two different shapes were saved and never audited — the
/// comparison refused, and the row was dropped. These run the real audit service and look for the row.
/// </summary>
public sealed class AuditShapesWriteTests
{
    private static readonly Guid Me = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> Factory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ControllerContext AsMe() => new()
    {
        HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Me.ToString())], "Bearer")) },
    };

    /// <summary>The audit is written after the response, so wait a moment for it.</summary>
    private static async Task<AuditLog> AuditRowAsync(IDbContextFactory<BenDataContext> f, string entityType)
    {
        for (var i = 0; i < 40; i++)
        {
            await using var db = await f.CreateDbContextAsync();
            if (await db.AuditLogs.FirstOrDefaultAsync(a => a.EntityType == entityType) is { } row) return row;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException($"no audit row for {entityType}");
    }

    [Fact]
    public async Task Retiring_a_piece_of_my_equipment_is_audited()
    {
        var f = Factory();
        var itemId = Guid.NewGuid();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.EquipmentItems.Add(new EquipmentItem { Id = itemId, OwnerAppUserId = Me, DisplayName = "Spirit box", DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var controller = new MyEquipmentController(f, new Mock<IFileStorageService>().Object, new AuditLogService(f),
            new Mock<IMediaIngestService>().Object) { ControllerContext = AsMe() };
        Assert.IsType<NoContentResult>(await controller.Retire(itemId, default));

        var row = await AuditRowAsync(f, nameof(EquipmentItem));
        Assert.Contains("IsRetired", row.ChangesJson);
    }

    [Fact]
    public async Task Reordering_a_pages_sections_is_audited_with_the_new_order()
    {
        var f = Factory();
        var org = Guid.NewGuid(); var page = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.OrganizationPages.Add(new OrganizationPage { Id = page, OrganizationId = org, PageTitle = "About", UrlName = "about", PageHtml = "", DateCreated = DateTime.UtcNow });
            db.CmsSections.Add(new CmsSection { Id = first, OrganizationPageId = page, SectionType = CmsSectionType.RichText, ContentJson = "{}", SortOrder = 1, DateCreated = DateTime.UtcNow });
            db.CmsSections.Add(new CmsSection { Id = second, OrganizationPageId = page, SectionType = CmsSectionType.RichText, ContentJson = "{}", SortOrder = 2, DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
            It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var controller = new CmsSectionController(f, new Mock<IMapper>().Object, security.Object, new AuditLogService(f), new CmsMarkupSanitizer())
        { ControllerContext = AsMe() };

        Assert.IsType<NoContentResult>(await controller.Reorder(org, page, new ReorderCmsSectionsRequest([second, first]), default));

        var row = await AuditRowAsync(f, "CmsSectionReorder");
        Assert.Contains("Order", row.ChangesJson);
        Assert.Contains($"{second}, {first}", row.ChangesJson);
    }
}
