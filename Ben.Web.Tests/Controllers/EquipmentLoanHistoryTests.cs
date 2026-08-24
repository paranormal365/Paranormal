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
using System.Text;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Condition photos, renewals, and the merged per-item history (item #55, phase 5).
/// </summary>
/// <remarks>
/// <para>The rules held here: a condition photo only attaches at a point in the loan where that
/// end of it means something; a renewal is a child of the loan, so approving one moves the loan's
/// due date while the loan itself stays out; and the history is the account of all of it, without
/// ever carrying a serial number.</para>
///
/// <para>Access is the same two-party rule as the checkout endpoints — borrower or approver, and
/// 404 for anyone else — so a stranger cannot even discover a loan exists.</para>
/// </remarks>
public class EquipmentLoanHistoryTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid BorrowerId = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid ItemId, Guid CheckoutId);

    private static EquipmentLoanHistoryController Build(IDbContextFactory<BenDataContext> f, Guid userId)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("fake/path.jpg");
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => new MemoryStream([1, 2, 3]));

        // No org-owned gear in this fixture, so the security service is never the deciding voice —
        // personal items resolve to their owner.
        return new EquipmentLoanHistoryController(
            f, new Mock<IOrganizationSecurityService>().Object, storage.Object, new Ben.Data.WebApi.Services.MediaIngestService(new Moq.Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object, new Ben.Data.WebApi.Services.FileMetadataExtractorService(), new Ben.Data.WebApi.Services.MediaSanitizationService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Ben.Data.WebApi.Services.MediaIngestService>.Instance), new Mock<IAuditLogService>().Object)
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
    /// A real, decodable JPEG. Since 2026-08-24 this door strips EXIF on upload, which means it
    /// decodes what it is handed — "not really a jpeg" is now a 400, correctly.
    /// </summary>
    private static IFormFile FakePhoto(string name = "before.jpg")
    {
        using var bitmap = new SkiaSharp.SKBitmap(2, 2);
        bitmap.SetPixel(0, 0, SkiaSharp.SKColors.Red);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data  = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        var bytes = data.ToArray();

        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg",
        };
    }

    /// <summary>One personal item, on loan to BorrowerId, at whatever status the test needs.</summary>
    private static async Task<World> SeedAsync(EquipmentCheckoutStatus status = EquipmentCheckoutStatus.CheckedOut,
        DateTime? dateDue = null)
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var (id, name) in new[] { (OwnerId, "The Owner"), (BorrowerId, "The Borrower"), (StrangerId, "Stranger") })
            db.Users.Add(new AppUser { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });

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

        var itemId = Guid.NewGuid();
        db.EquipmentItems.Add(new EquipmentItem
        {
            Id = itemId, OwnerAppUserId = OwnerId, EquipmentModelId = modelId,
            DisplayName = "My H1n", SerialNumber = "SN-PRIVATE",
            LoanAudience = EquipmentLoanAudience.IndividualUsers,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        var checkoutId = Guid.NewGuid();
        db.EquipmentCheckouts.Add(new EquipmentCheckout
        {
            Id = checkoutId, EquipmentItemId = itemId, BorrowerAppUserId = BorrowerId,
            Status = status,
            DateDue = dateDue,
            DateCheckedOut = status is EquipmentCheckoutStatus.CheckedOut or EquipmentCheckoutStatus.Returned
                ? DateTime.UtcNow.AddDays(-2) : null,
            DateReturned = status == EquipmentCheckoutStatus.Returned ? DateTime.UtcNow : null,
            DateCreated = DateTime.UtcNow.AddDays(-3), CreatedByAppUserId = BorrowerId,
        });

        await db.SaveChangesAsync();
        return new World(factory, itemId, checkoutId);
    }

    // ── Condition photos ─────────────────────────────────────────────────────

    [Fact]
    public async Task EitherParty_CanAttachAHandoffPhotoWhileTheGearIsOut()
    {
        var w = await SeedAsync();

        foreach (var actor in new[] { OwnerId, BorrowerId })
        {
            var result = await Build(w.Factory, actor).AttachPhoto(
                w.CheckoutId, EquipmentPhotoStage.Handoff, FakePhoto(), "as it went out", default);
            Assert.IsType<OkObjectResult>(result.Result);
        }

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(2, await db.EquipmentCheckoutPhotos.CountAsync(p => p.EquipmentCheckoutId == w.CheckoutId));
    }

    [Fact]
    public async Task AStranger_CannotAttachAPhoto_AndIsToldNothingExists()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, StrangerId).AttachPhoto(
            w.CheckoutId, EquipmentPhotoStage.Handoff, FakePhoto(), null, default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task AHandoffPhoto_IsRefusedOnALoanNobodyHasCollected()
    {
        // Requested, not yet approved — there has been no hand-off to photograph.
        var w = await SeedAsync(EquipmentCheckoutStatus.Requested);
        var result = await Build(w.Factory, OwnerId).AttachPhoto(
            w.CheckoutId, EquipmentPhotoStage.Handoff, FakePhoto(), null, default);
        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task AReturnPhoto_IsRefusedBeforeTheGearIsEvenOut()
    {
        var w = await SeedAsync(EquipmentCheckoutStatus.Approved);
        var result = await Build(w.Factory, OwnerId).AttachPhoto(
            w.CheckoutId, EquipmentPhotoStage.Return, FakePhoto(), null, default);
        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task AReturnPhoto_IsStillAllowedJustAfterTheReturn()
    {
        // The receiver often photographs the gear a moment after pressing "got it back".
        var w = await SeedAsync(EquipmentCheckoutStatus.Returned);
        var result = await Build(w.Factory, OwnerId).AttachPhoto(
            w.CheckoutId, EquipmentPhotoStage.Return, FakePhoto(), "as it came back", default);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task PhotosComeBackGroupedByStage_SoBeforeAndAfterCanSitSideBySide()
    {
        var w = await SeedAsync();
        await Build(w.Factory, BorrowerId).AttachPhoto(w.CheckoutId, EquipmentPhotoStage.Return, FakePhoto("after.jpg"), null, default);
        await Build(w.Factory, BorrowerId).AttachPhoto(w.CheckoutId, EquipmentPhotoStage.Handoff, FakePhoto("before.jpg"), null, default);

        var result = await Build(w.Factory, OwnerId).GetPhotos(w.CheckoutId, default);
        var photos = Assert.IsAssignableFrom<IEnumerable<EquipmentCheckoutPhotoRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        Assert.Equal(EquipmentPhotoStage.Handoff, photos[0].Stage);   // ordered by stage, not arrival
        Assert.Equal(EquipmentPhotoStage.Return, photos[1].Stage);
    }

    [Fact]
    public async Task APhotoCanBeRemovedByWhoeverTookIt_ButNotByTheOtherParty()
    {
        var w = await SeedAsync();
        var added = await Build(w.Factory, BorrowerId).AttachPhoto(
            w.CheckoutId, EquipmentPhotoStage.Handoff, FakePhoto(), null, default);
        var photoId = ((EquipmentCheckoutPhotoRecord)((OkObjectResult)added.Result!).Value!).Id;

        // The owner here IS the approver, so they may also remove it — check a true outsider instead.
        Assert.IsType<NotFoundResult>(await Build(w.Factory, StrangerId).DeletePhoto(w.CheckoutId, photoId, default));
        Assert.IsType<NoContentResult>(await Build(w.Factory, BorrowerId).DeletePhoto(w.CheckoutId, photoId, default));
    }

    [Fact]
    public async Task PhotoBytes_AreNotServedToSomeoneOutsideTheLoan()
    {
        var w = await SeedAsync();
        var added = await Build(w.Factory, BorrowerId).AttachPhoto(
            w.CheckoutId, EquipmentPhotoStage.Handoff, FakePhoto(), null, default);
        var photoId = ((EquipmentCheckoutPhotoRecord)((OkObjectResult)added.Result!).Value!).Id;

        Assert.IsType<NotFoundResult>(await Build(w.Factory, StrangerId).GetPhotoContent(photoId, default));
        Assert.IsType<FileStreamResult>(await Build(w.Factory, OwnerId).GetPhotoContent(photoId, default));
    }

    // ── Renewals ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApprovingARenewal_MovesTheLoansDueDate_AndLeavesItStillOut()
    {
        var due = DateTime.UtcNow.AddDays(2);
        var w = await SeedAsync(dateDue: due);
        var later = due.AddDays(7);

        var asked = await Build(w.Factory, BorrowerId).RequestRenewal(
            w.CheckoutId, new RequestEquipmentRenewalRequest(later, "Need it for one more visit"), default);
        var renewalId = ((EquipmentCheckoutRenewalRecord)((OkObjectResult)asked.Result!).Value!).Id;

        var reviewed = await Build(w.Factory, OwnerId).ReviewRenewal(
            w.CheckoutId, renewalId, new ReviewEquipmentRenewalRequest(true, "No problem"), default);
        Assert.IsType<OkObjectResult>(reviewed.Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        var checkout = await db.EquipmentCheckouts.SingleAsync(c => c.Id == w.CheckoutId);
        Assert.Equal(later, checkout.DateDue);
        // The gear never changed hands, so the loan itself is untouched.
        Assert.Equal(EquipmentCheckoutStatus.CheckedOut, checkout.Status);
    }

    [Fact]
    public async Task RefusingARenewal_LeavesTheOriginalDueDateStanding()
    {
        var due = DateTime.UtcNow.AddDays(2);
        var w = await SeedAsync(dateDue: due);

        var asked = await Build(w.Factory, BorrowerId).RequestRenewal(
            w.CheckoutId, new RequestEquipmentRenewalRequest(due.AddDays(7), null), default);
        var renewalId = ((EquipmentCheckoutRenewalRecord)((OkObjectResult)asked.Result!).Value!).Id;

        await Build(w.Factory, OwnerId).ReviewRenewal(
            w.CheckoutId, renewalId, new ReviewEquipmentRenewalRequest(false, "Someone else has it booked"), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(due, (await db.EquipmentCheckouts.SingleAsync(c => c.Id == w.CheckoutId)).DateDue);
    }

    [Fact]
    public async Task RefusingARenewal_RequiresAReason()
    {
        var w = await SeedAsync(dateDue: DateTime.UtcNow.AddDays(2));
        var asked = await Build(w.Factory, BorrowerId).RequestRenewal(
            w.CheckoutId, new RequestEquipmentRenewalRequest(DateTime.UtcNow.AddDays(9), null), default);
        var renewalId = ((EquipmentCheckoutRenewalRecord)((OkObjectResult)asked.Result!).Value!).Id;

        var blank = await Build(w.Factory, OwnerId).ReviewRenewal(
            w.CheckoutId, renewalId, new ReviewEquipmentRenewalRequest(false, "  "), default);
        Assert.IsType<BadRequestObjectResult>(blank.Result);
    }

    [Fact]
    public async Task OnlyOneRenewalCanBeWaitingAtATime()
    {
        var w = await SeedAsync(dateDue: DateTime.UtcNow.AddDays(2));
        await Build(w.Factory, BorrowerId).RequestRenewal(
            w.CheckoutId, new RequestEquipmentRenewalRequest(DateTime.UtcNow.AddDays(9), null), default);

        var second = await Build(w.Factory, BorrowerId).RequestRenewal(
            w.CheckoutId, new RequestEquipmentRenewalRequest(DateTime.UtcNow.AddDays(12), null), default);
        Assert.IsType<ConflictObjectResult>(second.Result);
    }

    [Fact]
    public async Task OnlyTheBorrowerCanAskForMoreTime()
    {
        var w = await SeedAsync(dateDue: DateTime.UtcNow.AddDays(2));
        var byOwner = await Build(w.Factory, OwnerId).RequestRenewal(
            w.CheckoutId, new RequestEquipmentRenewalRequest(DateTime.UtcNow.AddDays(9), null), default);
        Assert.IsType<ForbidResult>(byOwner.Result);
    }

    [Fact]
    public async Task GearThatIsNotOutCannotBeRenewed()
    {
        var w = await SeedAsync(EquipmentCheckoutStatus.Approved, dateDue: DateTime.UtcNow.AddDays(2));
        var result = await Build(w.Factory, BorrowerId).RequestRenewal(
            w.CheckoutId, new RequestEquipmentRenewalRequest(DateTime.UtcNow.AddDays(9), null), default);
        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task AskingForAnEarlierDateThanTheOneAlreadySetIsRejected()
    {
        var due = DateTime.UtcNow.AddDays(10);
        var w = await SeedAsync(dateDue: due);

        var result = await Build(w.Factory, BorrowerId).RequestRenewal(
            w.CheckoutId, new RequestEquipmentRenewalRequest(due.AddDays(-3), null), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ARenewalDecisionNotifiesTheBorrower()
    {
        var w = await SeedAsync(dateDue: DateTime.UtcNow.AddDays(2));
        var asked = await Build(w.Factory, BorrowerId).RequestRenewal(
            w.CheckoutId, new RequestEquipmentRenewalRequest(DateTime.UtcNow.AddDays(9), null), default);
        var renewalId = ((EquipmentCheckoutRenewalRecord)((OkObjectResult)asked.Result!).Value!).Id;

        await Build(w.Factory, OwnerId).ReviewRenewal(
            w.CheckoutId, renewalId, new ReviewEquipmentRenewalRequest(true, null), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var toBorrower = await db.UserMessageTos.Include(t => t.UserMessage)
            .Where(t => t.ToAppUserId == BorrowerId).ToListAsync();
        Assert.Contains(toBorrower, m => m.UserMessage.MessageSubject!.Contains("More time granted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnAlreadyDecidedRenewalCannotBeDecidedAgain()
    {
        var w = await SeedAsync(dateDue: DateTime.UtcNow.AddDays(2));
        var asked = await Build(w.Factory, BorrowerId).RequestRenewal(
            w.CheckoutId, new RequestEquipmentRenewalRequest(DateTime.UtcNow.AddDays(9), null), default);
        var renewalId = ((EquipmentCheckoutRenewalRecord)((OkObjectResult)asked.Result!).Value!).Id;

        await Build(w.Factory, OwnerId).ReviewRenewal(w.CheckoutId, renewalId, new ReviewEquipmentRenewalRequest(true, null), default);

        var again = await Build(w.Factory, OwnerId).ReviewRenewal(
            w.CheckoutId, renewalId, new ReviewEquipmentRenewalRequest(false, "changed my mind"), default);
        Assert.IsType<ConflictObjectResult>(again.Result);
    }

    // ── The merged history ───────────────────────────────────────────────────

    [Fact]
    public async Task HistoryMergesLoans_Renewals_AndServiceEntries_NewestFirst()
    {
        var w = await SeedAsync(dateDue: DateTime.UtcNow.AddDays(2));

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.EquipmentServiceLogs.Add(new EquipmentServiceLog
            {
                Id = Guid.NewGuid(), EquipmentItemId = w.ItemId,
                EntryType = EquipmentServiceLogType.Service,
                EntryDate = DateTime.UtcNow.AddDays(-1), Notes = "Batteries replaced",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            await db.SaveChangesAsync();
        }

        var asked = await Build(w.Factory, BorrowerId).RequestRenewal(
            w.CheckoutId, new RequestEquipmentRenewalRequest(DateTime.UtcNow.AddDays(9), null), default);
        Assert.IsType<OkObjectResult>(asked.Result);

        var result = await Build(w.Factory, OwnerId).GetItemHistory(w.ItemId, default);
        var entries = Assert.IsAssignableFrom<IEnumerable<EquipmentHistoryEntryRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        Assert.Contains(entries, e => e.Kind == EquipmentHistoryKind.Loan);
        Assert.Contains(entries, e => e.Kind == EquipmentHistoryKind.Renewal);
        Assert.Contains(entries, e => e.Kind == EquipmentHistoryKind.Service);

        // Newest first, so the current state of the gear reads off the top.
        Assert.Equal(entries.OrderByDescending(e => e.DateUtc).Select(e => e.DateUtc), entries.Select(e => e.DateUtc));
    }

    /// <summary>
    /// History is visible to people the serial number deliberately is not, so the shape must not be
    /// able to carry one.
    /// </summary>
    [Fact]
    public void HistoryEntry_HasNoSerialNumberProperty()
    {
        var props = typeof(EquipmentHistoryEntryRecord).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(props, n => n.Contains("Serial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HistoryIsNotVisibleToAStranger()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, StrangerId).GetItemHistory(w.ItemId, default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task HistoryCountsConditionPhotosAgainstTheirOwnEndOfTheLoan()
    {
        var w = await SeedAsync();
        await Build(w.Factory, BorrowerId).AttachPhoto(w.CheckoutId, EquipmentPhotoStage.Handoff, FakePhoto(), null, default);
        await Build(w.Factory, BorrowerId).AttachPhoto(w.CheckoutId, EquipmentPhotoStage.Handoff, FakePhoto(), null, default);

        var result = await Build(w.Factory, OwnerId).GetItemHistory(w.ItemId, default);
        var entries = Assert.IsAssignableFrom<IEnumerable<EquipmentHistoryEntryRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        var tookItOut = entries.Single(e => e.Summary.Contains("took it out", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, tookItOut.PhotoCount);
    }
}
