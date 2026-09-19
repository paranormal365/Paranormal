using Ben.Data.Common.Enums;
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
/// Borrowing equipment: eligibility, the state machine, and who may drive each transition
/// (item #55, phase 4).
/// </summary>
/// <remarks>
/// <para>The rules held here: the loan audience decides who may ask and whether the loan is
/// attributed to a group; the approver is a property of the <i>item</i> (its owner for personal
/// gear, the checkout permission for group gear); each party confirms only the transfer coming
/// toward them; and every transition refuses a state it did not expect rather than quietly
/// double-applying.</para>
///
/// <para>Identity always comes from claims. Several tests deliberately drive an endpoint as the
/// wrong person to prove the body cannot smuggle a different one in.</para>
/// </remarks>
public class EquipmentCheckoutTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid FellowMemberId = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();
    private static readonly Guid OrgManagerId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid ModelId, Guid PersonalItemId, Guid OrgItemId);

    private static EquipmentCheckoutController Build(
        IDbContextFactory<BenDataContext> f, Guid userId, Guid? checkoutApproverId = null)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid u, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _)
                    => checkoutApproverId is not null && u == checkoutApproverId);

        return new EquipmentCheckoutController(f, security.Object, new Mock<IAuditLogService>().Object, new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(f))
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

    /// <summary>
    /// One personal item owned by OwnerId and one group-owned item. OwnerId, FellowMemberId and
    /// OrgManagerId are all in OrgId; StrangerId is in nothing.
    /// </summary>
    private static async Task<World> SeedAsync(
        EquipmentLoanAudience audience = EquipmentLoanAudience.NotLoanable, bool shareWithOrg = false)
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[]
                 {
                     (OwnerId, "The Owner"), (FellowMemberId, "Fellow Member"),
                     (StrangerId, "Stranger"), (OrgManagerId, "Kit Manager"),
                 })
        {
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });
        }

        db.Organizations.Add(new Organization { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        foreach (var userId in new[] { OwnerId, FellowMemberId, OrgManagerId })
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
            });
        }

        var categoryId = Guid.NewGuid(); var brandId = Guid.NewGuid(); var modelId = Guid.NewGuid();
        db.EquipmentCategories.Add(new EquipmentCategory
        { Id = categoryId, Name = "Audio Recorder", SortOrder = 1, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId });
        db.EquipmentBrands.Add(new EquipmentBrand
        { Id = brandId, Name = "Zoom", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId });
        db.EquipmentModels.Add(new EquipmentModel
        {
            Id = modelId, EquipmentBrandId = brandId, EquipmentCategoryId = categoryId,
            Name = "H1n", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        var personalItemId = Guid.NewGuid();
        db.EquipmentItems.Add(new EquipmentItem
        {
            Id = personalItemId, OwnerAppUserId = OwnerId, EquipmentModelId = modelId,
            DisplayName = "My H1n", LoanAudience = audience,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        var orgItemId = Guid.NewGuid();
        db.EquipmentItems.Add(new EquipmentItem
        {
            Id = orgItemId, OwningOrganizationId = OrgId, EquipmentModelId = modelId,
            DisplayName = "Group recorder",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OrgManagerId,
        });

        if (shareWithOrg)
        {
            db.EquipmentItemShares.Add(new EquipmentItemShare
            {
                Id = Guid.NewGuid(), EquipmentItemId = personalItemId, OrganizationId = OrgId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
        }

        await db.SaveChangesAsync();
        return new World(factory, modelId, personalItemId, orgItemId);
    }

    private static async Task<Guid> RequestAsync(World w, Guid itemId, Guid borrowerId, Guid? forOrgId, Guid? approverId = null)
    {
        var result = await Build(w.Factory, borrowerId, approverId).RequestCheckout(
            new RequestEquipmentCheckoutRequest(itemId, forOrgId, null, null, "Please"), default);
        var record = Assert.IsType<EquipmentCheckoutRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        return record.Id;
    }

    // ── Eligibility: what each loan-audience flag actually opens ──────────────

    [Fact]
    public async Task NotLoanable_OffersNobodyAnything()
    {
        var w = await SeedAsync(EquipmentLoanAudience.NotLoanable, shareWithOrg: true);
        var result = await Build(w.Factory, FellowMemberId).GetEligibility(w.PersonalItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(eligibility.CanRequest);
        Assert.Empty(eligibility.Options);
        Assert.NotNull(eligibility.Reason);   // a missing button explains nothing
    }

    [Fact]
    public async Task SharedGroups_OffersTheGroup_AndAttributesTheLoanToIt()
    {
        var w = await SeedAsync(EquipmentLoanAudience.SharedGroups, shareWithOrg: true);

        var result = await Build(w.Factory, FellowMemberId).GetEligibility(w.PersonalItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.True(eligibility.CanRequest);
        var option = Assert.Single(eligibility.Options);
        Assert.Equal(OrgId, option.OrganizationId);

        var checkoutId = await RequestAsync(w, w.PersonalItemId, FellowMemberId, OrgId);
        await using var db = await w.Factory.CreateDbContextAsync();
        var checkout = await db.EquipmentCheckouts.SingleAsync(c => c.Id == checkoutId);
        Assert.Equal(OrgId, checkout.BorrowedForOrganizationId);
    }

    [Fact]
    public async Task SharedGroups_DoesNotOfferAPersonalLoan()
    {
        var w = await SeedAsync(EquipmentLoanAudience.SharedGroups, shareWithOrg: true);
        var result = await Build(w.Factory, FellowMemberId).GetEligibility(w.PersonalItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.DoesNotContain(eligibility.Options, o => o.OrganizationId is null);
    }

    [Fact]
    public async Task GroupMembers_OffersAPersonalLoan_WithNoBorrowingGroup()
    {
        var w = await SeedAsync(EquipmentLoanAudience.GroupMembers);

        var result = await Build(w.Factory, FellowMemberId).GetEligibility(w.PersonalItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.True(eligibility.CanRequest);
        Assert.Null(Assert.Single(eligibility.Options).OrganizationId);

        var checkoutId = await RequestAsync(w, w.PersonalItemId, FellowMemberId, null);
        await using var db = await w.Factory.CreateDbContextAsync();
        var checkout = await db.EquipmentCheckouts.SingleAsync(c => c.Id == checkoutId);

        // The nullable organization is the whole point: a personal loan represents nobody.
        Assert.Null(checkout.BorrowedForOrganizationId);
    }

    [Fact]
    public async Task GroupMembers_DoesNotReachSomeoneWithNoGroupInCommon()
    {
        var w = await SeedAsync(EquipmentLoanAudience.GroupMembers);
        var result = await Build(w.Factory, StrangerId).GetEligibility(w.PersonalItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(eligibility.CanRequest);
    }

    [Fact]
    public async Task IndividualUsers_ReachesAnyoneSignedIn()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var result = await Build(w.Factory, StrangerId).GetEligibility(w.PersonalItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.True(eligibility.CanRequest);
        Assert.Null(Assert.Single(eligibility.Options).OrganizationId);
    }

    [Fact]
    public async Task BothFlags_OfferTheGroupAndAPersonalLoan()
    {
        var w = await SeedAsync(
            EquipmentLoanAudience.SharedGroups | EquipmentLoanAudience.GroupMembers, shareWithOrg: true);

        var result = await Build(w.Factory, FellowMemberId).GetEligibility(w.PersonalItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(2, eligibility.Options.Count);
        Assert.Contains(eligibility.Options, o => o.OrganizationId == OrgId);
        Assert.Contains(eligibility.Options, o => o.OrganizationId is null);
    }

    [Fact]
    public async Task AnOwnerCannotBorrowTheirOwnEquipment()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var result = await Build(w.Factory, OwnerId).GetEligibility(w.PersonalItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(eligibility.CanRequest);
    }

    [Fact]
    public async Task RetiredEquipmentIsNotBorrowable()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var item = await db.EquipmentItems.SingleAsync(i => i.Id == w.PersonalItemId);
            item.IsRetired = true;
            await db.SaveChangesAsync();
        }

        var result = await Build(w.Factory, StrangerId).GetEligibility(w.PersonalItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(eligibility.CanRequest);
    }

    // ── Requesting ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ARequestCannotClaimAGroupTheServerDidNotOffer()
    {
        var w = await SeedAsync(EquipmentLoanAudience.GroupMembers);   // personal loans only

        var result = await Build(w.Factory, FellowMemberId).RequestCheckout(
            new RequestEquipmentCheckoutRequest(w.PersonalItemId, OrgId, null, null, null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ASecondOpenRequestForTheSameItemIsRefused()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        await RequestAsync(w, w.PersonalItemId, StrangerId, null);

        var second = await Build(w.Factory, StrangerId).RequestCheckout(
            new RequestEquipmentCheckoutRequest(w.PersonalItemId, null, null, null, null), default);

        Assert.IsType<ConflictObjectResult>(second.Result);
    }

    [Fact]
    public async Task GroupOwnedGear_IsBorrowableByMembers_AlwaysForThatGroup()
    {
        var w = await SeedAsync();

        var eligibilityResult = await Build(w.Factory, FellowMemberId).GetEligibility(w.OrgItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(eligibilityResult.Result).Value);
        Assert.True(eligibility.CanRequest);
        Assert.Equal(OrgId, Assert.Single(eligibility.Options).OrganizationId);

        var checkoutId = await RequestAsync(w, w.OrgItemId, FellowMemberId, OrgId);
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(OrgId, (await db.EquipmentCheckouts.SingleAsync(c => c.Id == checkoutId)).BorrowedForOrganizationId);
    }

    [Fact]
    public async Task GroupOwnedGear_IsNotBorrowableByAnOutsider()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, StrangerId).GetEligibility(w.OrgItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(eligibility.CanRequest);
    }

    // ── Who approves ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PersonalGear_IsApprovedByItsOwner_NotByAGroupPermission()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        // OrgManagerId holds the checkout permission, but this is somebody's personal recorder.
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null, approverId: OrgManagerId);

        var byManager = await Build(w.Factory, OrgManagerId, checkoutApproverId: OrgManagerId)
            .Approve(checkoutId, new ApproveEquipmentCheckoutRequest(null, null), default);
        Assert.IsType<NotFoundResult>(byManager.Result);   // not a party to this loan at all

        var byOwner = await Build(w.Factory, OwnerId).Approve(checkoutId, new ApproveEquipmentCheckoutRequest(null, null), default);
        Assert.IsType<OkObjectResult>(byOwner.Result);
    }

    [Fact]
    public async Task GroupGear_IsApprovedByTheCheckoutPermission_NotByAnyMember()
    {
        var w = await SeedAsync();
        var checkoutId = await RequestAsync(w, w.OrgItemId, FellowMemberId, OrgId, approverId: OrgManagerId);

        var byPlainMember = await Build(w.Factory, OwnerId, checkoutApproverId: OrgManagerId)
            .Approve(checkoutId, new ApproveEquipmentCheckoutRequest(null, null), default);
        Assert.IsType<NotFoundResult>(byPlainMember.Result);

        var byManager = await Build(w.Factory, OrgManagerId, checkoutApproverId: OrgManagerId)
            .Approve(checkoutId, new ApproveEquipmentCheckoutRequest(null, null), default);
        Assert.IsType<OkObjectResult>(byManager.Result);
    }

    // ── The transitions ──────────────────────────────────────────────────────

    [Fact]
    public async Task AFullLoanRunsRequestApproveHandoffReturn()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null);

        var due = DateTime.UtcNow.AddDays(7);
        Assert.IsType<OkObjectResult>((await Build(w.Factory, OwnerId)
            .Approve(checkoutId, new ApproveEquipmentCheckoutRequest(due, "Take care of it"), default)).Result);

        Assert.IsType<OkObjectResult>((await Build(w.Factory, StrangerId).ConfirmHandoff(checkoutId, default)).Result);

        Assert.IsType<OkObjectResult>((await Build(w.Factory, OwnerId)
            .Return(checkoutId, new ReturnEquipmentCheckoutRequest("Back in one piece"), default)).Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        var checkout = await db.EquipmentCheckouts.SingleAsync(c => c.Id == checkoutId);
        Assert.Equal(EquipmentCheckoutStatus.Returned, checkout.Status);
        Assert.Equal(StrangerId, checkout.CheckedOutConfirmedByAppUserId);
        Assert.Equal(OwnerId, checkout.ReturnedReceivedByAppUserId);
        Assert.Equal("Back in one piece", checkout.ReturnConditionNotes);
    }

    [Fact]
    public async Task TheBorrowerConfirmsTheHandoff_NotTheLender()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null);
        await Build(w.Factory, OwnerId).Approve(checkoutId, new ApproveEquipmentCheckoutRequest(null, null), default);

        var byLender = await Build(w.Factory, OwnerId).ConfirmHandoff(checkoutId, default);
        Assert.IsType<ForbidResult>(byLender.Result);
    }

    [Fact]
    public async Task TheLenderConfirmsTheReturn_NotTheBorrower()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null);
        await Build(w.Factory, OwnerId).Approve(checkoutId, new ApproveEquipmentCheckoutRequest(null, null), default);
        await Build(w.Factory, StrangerId).ConfirmHandoff(checkoutId, default);

        var byBorrower = await Build(w.Factory, StrangerId)
            .Return(checkoutId, new ReturnEquipmentCheckoutRequest(null), default);
        Assert.IsType<ForbidResult>(byBorrower.Result);
    }

    [Fact]
    public async Task DenyingRequiresAReason()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null);

        var blank = await Build(w.Factory, OwnerId).Deny(checkoutId, new DenyEquipmentCheckoutRequest("   "), default);
        Assert.IsType<BadRequestObjectResult>(blank.Result);

        var withReason = await Build(w.Factory, OwnerId).Deny(checkoutId, new DenyEquipmentCheckoutRequest("It's away being repaired"), default);
        Assert.IsType<OkObjectResult>(withReason.Result);
    }

    [Fact]
    public async Task TheBorrowerCanCancelBeforeTakingIt_ButNotAfter()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null);
        await Build(w.Factory, OwnerId).Approve(checkoutId, new ApproveEquipmentCheckoutRequest(null, null), default);

        // Approved but not yet collected — still cancellable.
        Assert.IsType<OkObjectResult>((await Build(w.Factory, StrangerId).Cancel(checkoutId, default)).Result);

        var second = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var id2 = await RequestAsync(second, second.PersonalItemId, StrangerId, null);
        await Build(second.Factory, OwnerId).Approve(id2, new ApproveEquipmentCheckoutRequest(null, null), default);
        await Build(second.Factory, StrangerId).ConfirmHandoff(id2, default);

        var afterCollection = await Build(second.Factory, StrangerId).Cancel(id2, default);
        Assert.IsType<ConflictObjectResult>(afterCollection.Result);
    }

    [Fact]
    public async Task ATerminalLoanRefusesEveryFurtherTransition()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null);
        await Build(w.Factory, OwnerId).Deny(checkoutId, new DenyEquipmentCheckoutRequest("No"), default);

        Assert.IsType<ConflictObjectResult>((await Build(w.Factory, OwnerId)
            .Approve(checkoutId, new ApproveEquipmentCheckoutRequest(null, null), default)).Result);
        Assert.IsType<ConflictObjectResult>((await Build(w.Factory, StrangerId).Cancel(checkoutId, default)).Result);
        Assert.IsType<ConflictObjectResult>((await Build(w.Factory, StrangerId).ConfirmHandoff(checkoutId, default)).Result);
    }

    [Fact]
    public async Task SomeoneWhoIsNeitherPartyCannotEvenSeeALoanExists()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null);

        var byOutsider = await Build(w.Factory, FellowMemberId).Cancel(checkoutId, default);
        Assert.IsType<NotFoundResult>(byOutsider.Result);   // 404, not 403 — no id probing
    }

    // ── One item, one holder ─────────────────────────────────────────────────

    /// <summary>
    /// The bug this covers: the duplicate-request check was per person, and approval had no guard
    /// at all, so two people could both be approved for the same physical recorder.
    /// </summary>
    [Fact]
    public async Task AnItemAlreadyPromisedToSomeoneCannotBeApprovedForSomeoneElse()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);

        var first = await RequestAsync(w, w.PersonalItemId, StrangerId, null);
        var second = await RequestAsync(w, w.PersonalItemId, FellowMemberId, null);

        // Both may ASK — that is a queue, and telling the second person "no" early would lose it.
        Assert.IsType<OkObjectResult>((await Build(w.Factory, OwnerId)
            .Approve(first, new ApproveEquipmentCheckoutRequest(null, null), default)).Result);

        // But only one can be granted.
        var clash = await Build(w.Factory, OwnerId)
            .Approve(second, new ApproveEquipmentCheckoutRequest(null, null), default);
        Assert.IsType<ConflictObjectResult>(clash.Result);
    }

    [Fact]
    public async Task OnceItIsBackTheQueuedRequestCanBeApproved()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var first = await RequestAsync(w, w.PersonalItemId, StrangerId, null);
        var second = await RequestAsync(w, w.PersonalItemId, FellowMemberId, null);

        await Build(w.Factory, OwnerId).Approve(first, new ApproveEquipmentCheckoutRequest(null, null), default);
        await Build(w.Factory, StrangerId).ConfirmHandoff(first, default);
        await Build(w.Factory, OwnerId).Return(first, new ReturnEquipmentCheckoutRequest(null), default);

        var now = await Build(w.Factory, OwnerId)
            .Approve(second, new ApproveEquipmentCheckoutRequest(null, null), default);
        Assert.IsType<OkObjectResult>(now.Result);
    }

    [Fact]
    public async Task EligibilitySaysWhenAPieceIsAlreadyOut_AndWhenItIsDueBack()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var due = DateTime.UtcNow.AddDays(5);

        var first = await RequestAsync(w, w.PersonalItemId, StrangerId, null);
        await Build(w.Factory, OwnerId).Approve(first, new ApproveEquipmentCheckoutRequest(due, null), default);
        await Build(w.Factory, StrangerId).ConfirmHandoff(first, default);

        var result = await Build(w.Factory, FellowMemberId).GetEligibility(w.PersonalItemId, default);
        var eligibility = Assert.IsType<BorrowEligibilityRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.True(eligibility.IsCurrentlyOut);
        Assert.Equal(due, eligibility.ExpectedBackOn);
        // Still askable — the queue is the point.
        Assert.True(eligibility.CanRequest);
    }

    // ── Holder tracking and overdue ──────────────────────────────────────────

    [Fact]
    public async Task GroupGear_TracksItsHolderAcrossTheLoan()
    {
        var w = await SeedAsync();
        var checkoutId = await RequestAsync(w, w.OrgItemId, FellowMemberId, OrgId, approverId: OrgManagerId);
        var manager = Build(w.Factory, OrgManagerId, checkoutApproverId: OrgManagerId);

        await manager.Approve(checkoutId, new ApproveEquipmentCheckoutRequest(null, null), default);
        await Build(w.Factory, FellowMemberId, checkoutApproverId: OrgManagerId).ConfirmHandoff(checkoutId, default);

        await using (var db = await w.Factory.CreateDbContextAsync())
            Assert.Equal(FellowMemberId, (await db.EquipmentItems.SingleAsync(i => i.Id == w.OrgItemId)).CurrentHolderAppUserId);

        await Build(w.Factory, OrgManagerId, checkoutApproverId: OrgManagerId)
            .Return(checkoutId, new ReturnEquipmentCheckoutRequest(null), default);

        await using (var db = await w.Factory.CreateDbContextAsync())
            Assert.Null((await db.EquipmentItems.SingleAsync(i => i.Id == w.OrgItemId)).CurrentHolderAppUserId);
    }

    [Fact]
    public async Task OverdueIsComputed_NotStored()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null);
        await Build(w.Factory, OwnerId).Approve(checkoutId, new ApproveEquipmentCheckoutRequest(DateTime.UtcNow.AddDays(-1), null), default);
        await Build(w.Factory, StrangerId).ConfirmHandoff(checkoutId, default);

        var mine = await Build(w.Factory, StrangerId).GetMine("borrower", default);
        var records = Assert.IsAssignableFrom<IEnumerable<EquipmentCheckoutRecord>>(
            Assert.IsType<OkObjectResult>(mine.Result).Value).ToList();

        Assert.True(records.Single().IsOverdue);

        // Nothing on the row says "overdue" — it is a comparison, made fresh on every read.
        await using var db = await w.Factory.CreateDbContextAsync();
        var stored = await db.EquipmentCheckouts.SingleAsync(c => c.Id == checkoutId);
        Assert.Equal(EquipmentCheckoutStatus.CheckedOut, stored.Status);
    }

    [Fact]
    public async Task ARequestedLoanIsNotOverdueEvenWithAPastDueDate()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null);
        await Build(w.Factory, OwnerId).Approve(checkoutId, new ApproveEquipmentCheckoutRequest(DateTime.UtcNow.AddDays(-1), null), default);

        // Approved but never collected: nobody has it, so nothing is late.
        var mine = await Build(w.Factory, StrangerId).GetMine("borrower", default);
        var records = Assert.IsAssignableFrom<IEnumerable<EquipmentCheckoutRecord>>(
            Assert.IsType<OkObjectResult>(mine.Result).Value).ToList();
        Assert.False(records.Single().IsOverdue);
    }

    // ── Notifications ────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestingNotifiesTheOwner_InTheSameSaveAsTheRequest()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        await RequestAsync(w, w.PersonalItemId, StrangerId, null);

        await using var db = await w.Factory.CreateDbContextAsync();
        var notice = await db.UserMessageTos.Include(t => t.UserMessage).SingleAsync();
        Assert.Equal(OwnerId, notice.ToAppUserId);
        Assert.Contains("borrow", notice.UserMessage.MessageBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DecidingNotifiesTheBorrower()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null);
        await Build(w.Factory, OwnerId).Deny(checkoutId, new DenyEquipmentCheckoutRequest("Away for repair"), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var toBorrower = await db.UserMessageTos.Include(t => t.UserMessage)
            .Where(t => t.ToAppUserId == StrangerId).ToListAsync();

        var message = Assert.Single(toBorrower).UserMessage;
        Assert.Contains("declined", message.MessageSubject!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Away for repair", message.MessageBody);
    }

    // ── Notification bodies are HTML ─────────────────────────────────────────

    /// <summary>
    /// Message bodies are rendered as markup, deliberately — the platform's notices use tags to
    /// pick out names. That makes anything a person typed an injection vector, and a decline reason
    /// is typed by the lender and read by the borrower.
    /// </summary>
    [Fact]
    public async Task ADeclineReasonCannotSmuggleMarkupIntoTheBorrowersInbox()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        var checkoutId = await RequestAsync(w, w.PersonalItemId, StrangerId, null);

        await Build(w.Factory, OwnerId).Deny(checkoutId,
            new DenyEquipmentCheckoutRequest("<script>alert('xss')</script>Away for repair"), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var body = (await db.UserMessageTos.Include(t => t.UserMessage)
            .SingleAsync(t => t.ToAppUserId == StrangerId)).UserMessage.MessageBody;

        Assert.DoesNotContain("<script>", body);
        Assert.Contains("&lt;script&gt;", body);      // encoded, not stripped — the reason survives
        Assert.Contains("Away for repair", body);
    }

    [Fact]
    public async Task AnItemNamedWithMarkupCannotInjectIntoTheApproversNotice()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var item = await db.EquipmentItems.SingleAsync(i => i.Id == w.PersonalItemId);
            item.DisplayName = "<img src=x onerror=alert(1)>";
            await db.SaveChangesAsync();
        }

        await RequestAsync(w, w.PersonalItemId, StrangerId, null);

        await using var check = await w.Factory.CreateDbContextAsync();
        var body = (await check.UserMessageTos.Include(t => t.UserMessage)
            .SingleAsync(t => t.ToAppUserId == OwnerId)).UserMessage.MessageBody;

        Assert.DoesNotContain("<img", body);
    }

    // ── Queues ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheApproverQueueShowsLoansOfMyGear_NotMyOwnBorrowing()
    {
        var w = await SeedAsync(EquipmentLoanAudience.IndividualUsers);
        await RequestAsync(w, w.PersonalItemId, StrangerId, null);

        var asApprover = await Build(w.Factory, OwnerId).GetMine("approver", default);
        var approverRows = Assert.IsAssignableFrom<IEnumerable<EquipmentCheckoutRecord>>(
            Assert.IsType<OkObjectResult>(asApprover.Result).Value).ToList();
        Assert.Single(approverRows);
        Assert.True(approverRows[0].Flags.CanApprove);

        var asBorrower = await Build(w.Factory, OwnerId).GetMine("borrower", default);
        var borrowerRows = Assert.IsAssignableFrom<IEnumerable<EquipmentCheckoutRecord>>(
            Assert.IsType<OkObjectResult>(asBorrower.Result).Value);
        Assert.Empty(borrowerRows);
    }

    [Fact]
    public async Task TheOrgQueueNeedsTheCheckoutPermission()
    {
        var w = await SeedAsync();
        await RequestAsync(w, w.OrgItemId, FellowMemberId, OrgId, approverId: OrgManagerId);

        var byPlainMember = await Build(w.Factory, FellowMemberId, checkoutApproverId: OrgManagerId)
            .GetForOrg(OrgId, default);
        Assert.IsType<NotFoundResult>(byPlainMember.Result);

        var byManager = await Build(w.Factory, OrgManagerId, checkoutApproverId: OrgManagerId)
            .GetForOrg(OrgId, default);
        var payload = Assert.IsType<OrgCheckoutListRecord>(Assert.IsType<OkObjectResult>(byManager.Result).Value);
        Assert.True(payload.CanReviewLoans);
        Assert.Single(payload.Items);
    }

    /// <summary>
    /// The loans tab rendered on row count, so an approver at a group whose gear had never been
    /// borrowed saw no tab at all and no way to discover the surface existed.
    /// </summary>
    [Fact]
    public async Task AnApproverWithNoLoansYetIsStillToldTheyMayReviewThem()
    {
        var w = await SeedAsync();   // nobody has asked for anything

        var result = await Build(w.Factory, OrgManagerId, checkoutApproverId: OrgManagerId)
            .GetForOrg(OrgId, default);
        var payload = Assert.IsType<OrgCheckoutListRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Empty(payload.Items);
        Assert.True(payload.CanReviewLoans);
    }
}
