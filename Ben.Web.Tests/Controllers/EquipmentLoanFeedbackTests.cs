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
/// Mutual loan feedback (item #55, phase 6d).
/// </summary>
/// <remarks>
/// <para>One rule carries all the weight: <b>the subject never sees it</b>. Every read excludes them,
/// and the tests below were each run against code with that exclusion deleted — they fail. A guard
/// that passes either way proves nothing.</para>
///
/// <para>The second thing worth testing is the deliberate <b>asymmetry</b>. Lender-about-borrower is
/// attributed (lender-to-lender context); borrower-about-lender is not (a borrower has more to lose
/// by being named). The shapes differ accordingly, and reflection asserts the unattributed one
/// cannot carry an author at all.</para>
/// </remarks>
public class EquipmentLoanFeedbackTests
{
    private static readonly Guid LenderId = Guid.NewGuid();
    private static readonly Guid BorrowerId = Guid.NewGuid();
    private static readonly Guid OtherLenderId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Guid ModelId, Guid ItemId, Guid OtherItemId,
        Guid ReturnedLoanId, Guid PendingLoanId, Guid PastLoanId);

    private static EquipmentLoanFeedbackController Build(IDbContextFactory<BenDataContext> f, Guid? userId)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                    It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

        var identity = userId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer");

        return new EquipmentLoanFeedbackController(f, security.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    /// <summary>
    /// Two lenders' gear, and three loans to the same borrower: one returned and awaiting feedback,
    /// one still out, and one already-finished loan from a different lender to build history with.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[]
                 {
                     (LenderId, "The Lender"), (BorrowerId, "The Borrower"),
                     (OtherLenderId, "Another Lender"), (AdminId, "Group Admin"),
                 })
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = AdminId,
            Role = OrganizationMemberRole.Administrator, IsActive = true, DateCreated = DateTime.UtcNow,
        });

        var categoryId = Guid.NewGuid(); var brandId = Guid.NewGuid(); var modelId = Guid.NewGuid();
        db.EquipmentCategories.Add(new EquipmentCategory
        { Id = categoryId, Name = "Audio Recorder", SortOrder = 1, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = LenderId });
        db.EquipmentBrands.Add(new EquipmentBrand
        { Id = brandId, Name = "Zoom", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = LenderId });
        db.EquipmentModels.Add(new EquipmentModel
        {
            Id = modelId, EquipmentBrandId = brandId, EquipmentCategoryId = categoryId,
            Name = "H1n", IsApproved = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = LenderId,
        });

        Guid AddItem(Guid owner, string name)
        {
            var itemId = Guid.NewGuid();
            db.EquipmentItems.Add(new EquipmentItem
            {
                Id = itemId, OwnerAppUserId = owner, EquipmentModelId = modelId, DisplayName = name,
                IncludeInGlobalCatalog = true, LoanAudience = EquipmentLoanAudience.IndividualUsers,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            return itemId;
        }

        var itemId      = AddItem(LenderId, "The recorder");
        var otherItemId = AddItem(OtherLenderId, "Another recorder");

        Guid AddLoan(Guid item, EquipmentCheckoutStatus status, DateTime? returned)
        {
            var loanId = Guid.NewGuid();
            db.EquipmentCheckouts.Add(new EquipmentCheckout
            {
                Id = loanId, EquipmentItemId = item, BorrowerAppUserId = BorrowerId,
                Status = status, DateReturned = returned,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = BorrowerId,
            });
            return loanId;
        }

        var returnedLoanId = AddLoan(itemId, EquipmentCheckoutStatus.Returned, DateTime.UtcNow.AddDays(-1));
        var pendingLoanId  = AddLoan(itemId, EquipmentCheckoutStatus.Requested, null);
        var pastLoanId     = AddLoan(otherItemId, EquipmentCheckoutStatus.Returned, DateTime.UtcNow.AddDays(-30));

        await db.SaveChangesAsync();
        return new World(factory, modelId, itemId, otherItemId, returnedLoanId, pendingLoanId, pastLoanId);
    }

    private static async Task LeaveLenderFeedbackAsync(
        World w, Guid loanId, Guid lender, string? comment = "Careful and prompt.", int? rating = 5)
    {
        var result = await Build(w.Factory, lender)
            .SubmitFeedback(loanId, new SubmitLoanFeedbackRequest(comment, rating, null), default);
        Assert.IsType<NoContentResult>(result);
    }

    // ── The subject never sees it ────────────────────────────────────────────

    /// <summary>
    /// The borrower cannot read what lenders wrote about them, through the endpoint that carries it.
    /// </summary>
    /// <remarks>
    /// Verified by deletion: removing the <c>CanReviewCheckoutAsync</c> gate makes this pass a 200
    /// back to the borrower, and the test fails.
    /// </remarks>
    [Fact]
    public async Task ABorrowerCannotReadTheirOwnFile()
    {
        var w = await SeedAsync();
        await LeaveLenderFeedbackAsync(w, w.PastLoanId, OtherLenderId);

        var asBorrower = await Build(w.Factory, BorrowerId).GetBorrowerFeedback(w.PendingLoanId, default);
        Assert.IsType<NotFoundResult>(asBorrower.Result);

        // ...while the person actually deciding the request does see it.
        var asLender = await Build(w.Factory, LenderId).GetBorrowerFeedback(w.PendingLoanId, default);
        var panel = Assert.IsType<BorrowerFeedbackPanelRecord>(Assert.IsType<OkObjectResult>(asLender.Result).Value);
        Assert.Equal("Careful and prompt.", Assert.Single(panel.Comments).Comment);
    }

    /// <summary>
    /// A lender cannot read what borrowers wrote about them — the exclusion that matters most, since
    /// the owner of a piece is the person most likely to open its page.
    /// </summary>
    [Fact]
    public async Task ALenderCannotReadWhatBorrowersSaidAboutThem()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, BorrowerId).SubmitFeedback(
            w.ReturnedLoanId, new SubmitLoanFeedbackRequest("Slow to reply.", 3, null), default);
        Assert.IsType<NoContentResult>(result);

        Assert.IsType<NotFoundResult>(
            (await Build(w.Factory, LenderId).GetLenderFeedback(w.ItemId, default)).Result);

        // Somebody considering asking them does see it.
        var asOther = await Build(w.Factory, OtherLenderId).GetLenderFeedback(w.ItemId, default);
        var panel = Assert.IsType<LenderFeedbackPanelRecord>(Assert.IsType<OkObjectResult>(asOther.Result).Value);
        Assert.Equal("Slow to reply.", Assert.Single(panel.Comments).Comment);
    }

    [Fact]
    public async Task TheApproverPanelExcludesFeedbackFromTheRequestBeingDecided()
    {
        var w = await SeedAsync();
        // Feedback exists on the returned loan of this very item, from this very lender.
        await LeaveLenderFeedbackAsync(w, w.ReturnedLoanId, LenderId, "Fine.");

        var result = await Build(w.Factory, LenderId).GetBorrowerFeedback(w.ReturnedLoanId, default);
        var panel = Assert.IsType<BorrowerFeedbackPanelRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        // Your own note on this same loan is not "past feedback" — it is what you just wrote.
        Assert.Empty(panel.Comments);
    }

    // ── Attribution, and its deliberate asymmetry ────────────────────────────

    [Fact]
    public void TheBorrowerFacingShapeCannotCarryAnAuthor()
    {
        var names = typeof(LenderFeedbackRecord).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Author", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("AppUser", StringComparison.OrdinalIgnoreCase));

        // The other direction is attributed on purpose — asserted so the asymmetry stays deliberate
        // rather than becoming an accident somebody "fixes".
        Assert.Contains(typeof(BorrowerFeedbackRecord).GetProperties().Select(p => p.Name), n => n == "AuthorDisplayName");
    }

    [Fact]
    public void AProductReviewCarriesNothingButItsWordsAndItsDate()
    {
        var names = typeof(ProductReviewRecord).GetProperties().Select(p => p.Name).ToList();
        Assert.Equal(["Comment", "DateCreated"], names.Order().ToList());
    }

    // ── Write guards ─────────────────────────────────────────────────────────

    [Fact]
    public async Task FeedbackCannotBeLeftBeforeTheGearIsBack()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, LenderId).SubmitFeedback(
            w.PendingLoanId, new SubmitLoanFeedbackRequest("Too early.", null, null), default);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task EachSideCanOnlySpeakOnce()
    {
        var w = await SeedAsync();
        await LeaveLenderFeedbackAsync(w, w.ReturnedLoanId, LenderId);

        var second = await Build(w.Factory, LenderId).SubmitFeedback(
            w.ReturnedLoanId, new SubmitLoanFeedbackRequest("Changed my mind.", 1, null), default);
        Assert.IsType<ConflictObjectResult>(second);

        // The other side is a separate row and is unaffected.
        Assert.IsType<NoContentResult>(await Build(w.Factory, BorrowerId).SubmitFeedback(
            w.ReturnedLoanId, new SubmitLoanFeedbackRequest("All good.", 5, null), default));
    }

    [Fact]
    public async Task ALenderCannotReviewTheirOwnGear()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, LenderId).SubmitFeedback(
            w.ReturnedLoanId, new SubmitLoanFeedbackRequest(null, null, "Best recorder ever!"), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SomebodyWhoWasNotPartyToTheLoanIsNotToldItExists()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, OtherLenderId).SubmitFeedback(
            w.ReturnedLoanId, new SubmitLoanFeedbackRequest("Nothing to do with me.", null, null), default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ARatingOutsideOneToFiveIsRefused()
    {
        var w = await SeedAsync();
        foreach (var bad in new[] { 0, 6 })
            Assert.IsType<BadRequestObjectResult>(await Build(w.Factory, LenderId).SubmitFeedback(
                w.ReturnedLoanId, new SubmitLoanFeedbackRequest(null, bad, null), default));
    }

    // ── Averages ─────────────────────────────────────────────────────────────

    /// <summary>
    /// No average below three ratings — one sour opinion rendered as "2.0" reads as a verdict when
    /// it is one voice. The count is always carried, so the caller can say something honest either
    /// way.
    /// </summary>
    [Fact]
    public async Task NoAverageIsShownFromTooFewRatings()
    {
        var w = await SeedAsync();
        await LeaveLenderFeedbackAsync(w, w.PastLoanId, OtherLenderId, "Fine.", rating: 2);

        var result = await Build(w.Factory, LenderId).GetBorrowerFeedback(w.PendingLoanId, default);
        var panel = Assert.IsType<BorrowerFeedbackPanelRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Null(panel.Summary.AverageRating);
        Assert.Equal(1, panel.Summary.RatingCount);
    }

    // ── Product reviews ──────────────────────────────────────────────────────

    [Fact]
    public async Task AProductReviewReachesTheModelPage_ButOnlyFromAPubliclyListedCopy()
    {
        var w = await SeedAsync();
        await Build(w.Factory, BorrowerId).SubmitFeedback(
            w.ReturnedLoanId, new SubmitLoanFeedbackRequest(null, null, "Handles wind badly."), default);

        var visible = await Build(w.Factory, userId: null).GetProductReviews(w.ModelId, default);
        Assert.Equal("Handles wind badly.",
            Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ProductReviewRecord>>(
                Assert.IsType<OkObjectResult>(visible.Result).Value)).Comment);

        // Unlist the item and the review leaves with it: the review is about the product, but its
        // presence would still say that somebody nearby owns one.
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var item = await db.EquipmentItems.SingleAsync(i => i.Id == w.ItemId);
            item.IncludeInGlobalCatalog = false;
            await db.SaveChangesAsync();
        }

        var hidden = await Build(w.Factory, userId: null).GetProductReviews(w.ModelId, default);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ProductReviewRecord>>(
            Assert.IsType<OkObjectResult>(hidden.Result).Value));
    }

    // ── Moderation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyAGroupAdministratorReachesTheModerationList()
    {
        var w = await SeedAsync();

        Assert.IsType<NotFoundResult>(
            (await Build(w.Factory, BorrowerId).GetModerationList(OrgId, default)).Result);
        Assert.IsType<OkObjectResult>(
            (await Build(w.Factory, AdminId).GetModerationList(OrgId, default)).Result);
    }

    [Fact]
    public void TheModerationShapeIsTheOnlyOneThatNamesBothSides()
    {
        var names = typeof(ModeratedFeedbackRecord).GetProperties().Select(p => p.Name).ToList();

        Assert.Contains("AuthorDisplayName", names);
        Assert.Contains("SubjectDisplayName", names);
    }
}
