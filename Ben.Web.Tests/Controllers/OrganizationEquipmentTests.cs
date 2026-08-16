using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A group's own equipment, and the Equipment permission that governs it (item #55, phase 3).
/// </summary>
/// <remarks>
/// <para>The split these tests hold: <b>reading</b> the group's gear needs only active membership,
/// while <b>changing</b> it — and seeing its serial numbers — needs the Equipment permission. A
/// plain member sees the kit list without serials; someone with the permission sees everything.</para>
///
/// <para>Authorization is delegated to <c>IOrganizationSecurityService</c>, which is mocked here.
/// That is deliberate: its own resolution order (SuperAdmin, owner/administrator, direct grant,
/// named role) is proven in <c>OrganizationSecurityServiceRepositoryTests</c>, and re-testing it
/// through this controller would assert the mock rather than the rule.</para>
/// </remarks>
public class OrganizationEquipmentTests
{
    private static readonly Guid ManagerId = Guid.NewGuid();
    private static readonly Guid PlainMemberId = Guid.NewGuid();
    private static readonly Guid OutsiderId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid ModelId, Guid ItemId);

    /// <summary>
    /// Builds the controller with the security service answering true only for <paramref name="canManageUserId"/>.
    /// </summary>
    private static OrganizationEquipmentController Build(
        IDbContextFactory<BenDataContext> f, Guid userId, Guid? canManageUserId = null,
        Mock<IFileStorageService>? storage = null)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid u, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _)
                    => canManageUserId is not null && u == canManageUserId);

        return new OrganizationEquipmentController(
            f, security.Object,
            (storage ?? new Mock<IFileStorageService>()).Object,
            new Mock<IAuditLogService>().Object)
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
    }

    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[]
                 {
                     (ManagerId, "Equipment Manager"),
                     (PlainMemberId, "Plain Member"),
                     (OutsiderId, "Outsider"),
                 })
        {
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });
        }

        db.Organizations.Add(new Organization { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });

        foreach (var userId in new[] { ManagerId, PlainMemberId })
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
            });
        }

        var categoryId = Guid.NewGuid();
        var brandId    = Guid.NewGuid();
        var modelId    = Guid.NewGuid();
        var itemId     = Guid.NewGuid();

        db.EquipmentCategories.Add(new EquipmentCategory
        { Id = categoryId, Name = "Thermal Imaging", SortOrder = 1, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ManagerId });
        db.EquipmentBrands.Add(new EquipmentBrand
        { Id = brandId, Name = "FLIR", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ManagerId });
        db.EquipmentModels.Add(new EquipmentModel
        {
            Id = modelId, EquipmentBrandId = brandId, EquipmentCategoryId = categoryId,
            Name = "One Pro", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ManagerId,
        });
        db.EquipmentItems.Add(new EquipmentItem
        {
            Id = itemId, OwningOrganizationId = OrgId, OwnerAppUserId = null, EquipmentModelId = modelId,
            DisplayName = "Group thermal camera", SerialNumber = "ORG-SERIAL-1",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ManagerId,
        });

        await db.SaveChangesAsync();
        return new World(factory, modelId, itemId);
    }

    private static UpsertOrgEquipmentItemRequest Upsert(Guid modelId, string name = "New kit", string? serial = "SN-1")
        => new(modelId, name, serial, null, null);

    // ── Reading: membership is enough, but serials are not ───────────────────

    [Fact]
    public async Task PlainMember_SeesTheKitList_ButNoSerialNumbers()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, PlainMemberId, canManageUserId: ManagerId).GetOrgEquipment(OrgId, default);
        var payload = Assert.IsType<OrgEquipmentListRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(payload.CanManage);
        var item = Assert.Single(payload.Items);
        Assert.Equal("Group thermal camera", item.DisplayName);
        Assert.Null(item.SerialNumber);            // withheld, not merely flagged
        Assert.False(item.Flags.CanSeeSerial);
        Assert.False(item.Flags.CanEdit);
    }

    [Fact]
    public async Task SomeoneWithTheEquipmentPermission_SeesSerialsAndCanEdit()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, ManagerId, canManageUserId: ManagerId).GetOrgEquipment(OrgId, default);
        var payload = Assert.IsType<OrgEquipmentListRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.True(payload.CanManage);
        var item = Assert.Single(payload.Items);
        Assert.Equal("ORG-SERIAL-1", item.SerialNumber);
        Assert.True(item.Flags.CanSeeSerial);
        Assert.True(item.Flags.CanEdit);
        Assert.True(item.Flags.CanManageServiceLog);
    }

    /// <summary>
    /// A group with no equipment yet must still be told it may add the first piece. Deriving the
    /// verdict from the rows would leave the "Add equipment" button permanently hidden on an empty
    /// list — the feature dead on arrival for exactly the groups that need to start using it.
    /// </summary>
    [Fact]
    public async Task AnEmptyList_StillCarriesThePermissionToAddTheFirstPiece()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.EquipmentItems.RemoveRange(await db.EquipmentItems.ToListAsync());
            await db.SaveChangesAsync();
        }

        var result = await Build(w.Factory, ManagerId, canManageUserId: ManagerId).GetOrgEquipment(OrgId, default);
        var payload = Assert.IsType<OrgEquipmentListRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Empty(payload.Items);
        Assert.True(payload.CanManage);
    }

    [Fact]
    public async Task Outsider_GetsNotFound_RatherThanAnEmptyList()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, OutsiderId, canManageUserId: ManagerId).GetOrgEquipment(OrgId, default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GroupGear_IsNeverShareable_EvenByAManager()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, ManagerId, canManageUserId: ManagerId).GetOrgEquipment(OrgId, default);
        var payload = Assert.IsType<OrgEquipmentListRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        // Sharing is a personal-item idea; this gear already belongs to a group.
        Assert.False(payload.Items.Single().Flags.CanManageSharing);
    }

    // ── Writing: the permission is required ──────────────────────────────────

    [Fact]
    public async Task PlainMember_CannotCreateGroupEquipment()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, PlainMemberId, canManageUserId: ManagerId)
            .CreateOrgEquipment(OrgId, Upsert(w.ModelId), default);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Manager_CreatesGroupOwnedItem_WithNoPersonalOwner()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, ManagerId, canManageUserId: ManagerId)
            .CreateOrgEquipment(OrgId, Upsert(w.ModelId, "Second camera"), default);
        var record = Assert.IsType<EquipmentItemRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(OrgId, record.OwningOrganizationId);
        Assert.Null(record.OwnerAppUserId);          // the XOR the entity documents
    }

    [Fact]
    public async Task PlainMember_CannotEditOrDeleteGroupEquipment()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, PlainMemberId, canManageUserId: ManagerId);

        Assert.IsType<ForbidResult>((await ctrl.UpdateOrgEquipment(OrgId, w.ItemId, Upsert(w.ModelId), default)).Result);
        Assert.IsType<ForbidResult>(await ctrl.DeleteOrgEquipment(OrgId, w.ItemId, default));
    }

    [Fact]
    public async Task Manager_CannotReachAnotherGroupsItem()
    {
        var w = await SeedAsync();
        var otherOrgId = Guid.NewGuid();

        var result = await Build(w.Factory, ManagerId, canManageUserId: ManagerId)
            .UpdateOrgEquipment(otherOrgId, w.ItemId, Upsert(w.ModelId), default);

        // The permission mock says yes for any org; the item simply isn't that org's.
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Holder ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Holder_CanBeSetToAMember_AndClearedAgain()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, ManagerId, canManageUserId: ManagerId);

        var set = await ctrl.SetHolder(OrgId, w.ItemId, new SetEquipmentHolderRequest(PlainMemberId), default);
        var record = Assert.IsType<EquipmentItemRecord>(Assert.IsType<OkObjectResult>(set.Result).Value);
        Assert.Equal(PlainMemberId, record.CurrentHolderAppUserId);
        Assert.Equal("Plain Member", record.CurrentHolderDisplayName);

        var cleared = await ctrl.SetHolder(OrgId, w.ItemId, new SetEquipmentHolderRequest(null), default);
        var clearedRecord = Assert.IsType<EquipmentItemRecord>(Assert.IsType<OkObjectResult>(cleared.Result).Value);
        Assert.Null(clearedRecord.CurrentHolderAppUserId);
    }

    [Fact]
    public async Task Holder_CannotBeSomeoneOutsideTheGroup()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, ManagerId, canManageUserId: ManagerId)
            .SetHolder(OrgId, w.ItemId, new SetEquipmentHolderRequest(OutsiderId), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Service log, and the item fields it keeps in step ────────────────────

    [Fact]
    public async Task ReportingADefect_MarksTheItemFaulty_InTheSameSave()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, ManagerId, canManageUserId: ManagerId).AddServiceLogEntry(
            OrgId, w.ItemId,
            new AddEquipmentServiceLogRequest(EquipmentServiceLogType.DefectReported, DateTime.UtcNow, "Lens cracked", null),
            default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        var item = await db.EquipmentItems.SingleAsync(i => i.Id == w.ItemId);
        Assert.Equal("Lens cracked", item.DefectNotes);
    }

    [Fact]
    public async Task ResolvingADefect_ClearsTheItemsDefectNote()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, ManagerId, canManageUserId: ManagerId);
        await ctrl.AddServiceLogEntry(OrgId, w.ItemId,
            new AddEquipmentServiceLogRequest(EquipmentServiceLogType.DefectReported, DateTime.UtcNow, "Lens cracked", null), default);

        await ctrl.AddServiceLogEntry(OrgId, w.ItemId,
            new AddEquipmentServiceLogRequest(EquipmentServiceLogType.DefectResolved, DateTime.UtcNow, "Lens replaced", null), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var item = await db.EquipmentItems.SingleAsync(i => i.Id == w.ItemId);
        Assert.Null(item.DefectNotes);
        // Both entries survive — the log is the history, the field is only its latest word.
        Assert.Equal(2, await db.EquipmentServiceLogs.CountAsync(l => l.EquipmentItemId == w.ItemId));
    }

    [Fact]
    public async Task AServiceEntry_MovesTheLastServicedDate()
    {
        var w = await SeedAsync();
        var serviced = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        await Build(w.Factory, ManagerId, canManageUserId: ManagerId).AddServiceLogEntry(
            OrgId, w.ItemId,
            new AddEquipmentServiceLogRequest(EquipmentServiceLogType.Service, serviced, "Calibrated", null),
            default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var item = await db.EquipmentItems.SingleAsync(i => i.Id == w.ItemId);
        Assert.Equal(serviced, item.LastServicedDate);
    }

    [Fact]
    public async Task PlainMember_CanReadTheServiceLog_ButNotAddToIt()
    {
        var w = await SeedAsync();
        await Build(w.Factory, ManagerId, canManageUserId: ManagerId).AddServiceLogEntry(
            OrgId, w.ItemId,
            new AddEquipmentServiceLogRequest(EquipmentServiceLogType.Service, DateTime.UtcNow, "Calibrated", null), default);

        var member = Build(w.Factory, PlainMemberId, canManageUserId: ManagerId);

        var read = await member.GetServiceLog(OrgId, w.ItemId, default);
        var entries = Assert.IsAssignableFrom<IEnumerable<EquipmentServiceLogRecord>>(
            Assert.IsType<OkObjectResult>(read.Result).Value);
        Assert.Single(entries);

        var write = await member.AddServiceLogEntry(OrgId, w.ItemId,
            new AddEquipmentServiceLogRequest(EquipmentServiceLogType.Service, DateTime.UtcNow, "Nope", null), default);
        Assert.IsType<ForbidResult>(write.Result);
    }

    [Fact]
    public async Task AServiceEntry_RequiresANote()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, ManagerId, canManageUserId: ManagerId).AddServiceLogEntry(
            OrgId, w.ItemId,
            new AddEquipmentServiceLogRequest(EquipmentServiceLogType.Service, DateTime.UtcNow, "   ", null),
            default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task WorkCannotBeAttributedToSomeoneOutsideTheGroup()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, ManagerId, canManageUserId: ManagerId).AddServiceLogEntry(
            OrgId, w.ItemId,
            new AddEquipmentServiceLogRequest(EquipmentServiceLogType.Service, DateTime.UtcNow, "Calibrated", OutsiderId),
            default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Delete vs retire ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingAnItemWithServiceHistory_IsRefused()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, ManagerId, canManageUserId: ManagerId);
        await ctrl.AddServiceLogEntry(OrgId, w.ItemId,
            new AddEquipmentServiceLogRequest(EquipmentServiceLogType.Service, DateTime.UtcNow, "Calibrated", null), default);

        var result = await ctrl.DeleteOrgEquipment(OrgId, w.ItemId, default);

        Assert.IsType<ConflictObjectResult>(result);
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.True(await db.EquipmentItems.AnyAsync(i => i.Id == w.ItemId));
    }

    [Fact]
    public async Task DeletingAnItemWithNoHistory_Succeeds()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, ManagerId, canManageUserId: ManagerId)
            .DeleteOrgEquipment(OrgId, w.ItemId, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.False(await db.EquipmentItems.AnyAsync(i => i.Id == w.ItemId));
    }
}
