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
/// Owner FAQs and the anonymous question channel (item #55, phase 6c).
/// </summary>
/// <remarks>
/// <para>The anonymity is the feature, so most of what follows tests absence rather than presence.
/// Two independent mechanisms have to hold: the <b>shape</b> the answerer receives must have no
/// slot for the asker, and the <b>notice</b> that announces the question must not name them in its
/// text or its sender. Phase 6a found the inbox falling back to a sender's email address, which
/// would have defeated the second on its own.</para>
///
/// <para>Both anonymity tests were run against deliberately broken code before being relied on:
/// interpolating the asker's display name into the notice body, and flipping
/// <c>HideSenderIdentity</c> to false, each fail them. A guard that passes either way proves
/// nothing.</para>
/// </remarks>
public class EquipmentQuestionsTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid AskerId = Guid.NewGuid();
    private static readonly Guid StrangerId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    /// <summary>Distinctive enough that a substring search for it cannot match by accident.</summary>
    private const string AskerName = "Zephyrina Quillsworth-Marchbank";

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid PublicItemId, Guid PrivateItemId);

    private static EquipmentQuestionsController Build(
        IDbContextFactory<BenDataContext> f, Guid? userId, Guid? equipmentPermissionHolder = null)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                    It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid u, Guid _, OrganizationSecurityTable _, OrganizationSecurityAction _, CancellationToken _)
                    => equipmentPermissionHolder is not null && u == equipmentPermissionHolder);

        var identity = userId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer");

        return new EquipmentQuestionsController(f, security.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        db.Users.Add(new AppUser { Id = OwnerId, UserName = "owner@t", Email = "owner@t", DisplayName = "The Owner" });
        db.Users.Add(new AppUser { Id = AskerId, UserName = "asker@t", Email = "asker@t", DisplayName = AskerName });
        db.Users.Add(new AppUser { Id = StrangerId, UserName = "s@t", Email = "s@t", DisplayName = "Stranger" });

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });

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

        var publicId = Guid.NewGuid();
        db.EquipmentItems.Add(new EquipmentItem
        {
            Id = publicId, OwnerAppUserId = OwnerId, EquipmentModelId = modelId,
            DisplayName = "My H1n", IncludeInGlobalCatalog = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        var privateId = Guid.NewGuid();
        db.EquipmentItems.Add(new EquipmentItem
        {
            Id = privateId, OwnerAppUserId = OwnerId, EquipmentModelId = modelId,
            DisplayName = "Kept private",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });

        await db.SaveChangesAsync();
        return new World(factory, publicId, privateId);
    }

    private static async Task<Guid> AskAsync(World w, Guid itemId, Guid asker, string text = "Does it take AAs?")
    {
        var result = await Build(w.Factory, asker).AskQuestion(itemId, new AskEquipmentQuestionRequest(text), default);
        var record = Assert.IsType<AskedQuestionRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        return record.Id;
    }

    // ── The anonymity, from both ends ────────────────────────────────────────

    /// <summary>
    /// The shape the answerer receives has no asker field at all — checked by reflection rather
    /// than by asserting a null, because a null is a value somebody can later fill in.
    /// </summary>
    [Fact]
    public void TheReceivedShapeCannotCarryWhoAsked()
    {
        var names = typeof(ReceivedQuestionRecord).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Asker", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("AskedBy", StringComparison.OrdinalIgnoreCase));
        // ...and nothing about who answered leaks back the other way either.
        Assert.DoesNotContain(typeof(AskedQuestionRecord).GetProperties().Select(p => p.Name),
            n => n.Contains("Answerer", StringComparison.OrdinalIgnoreCase)
              || n.Contains("AnsweredBy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheNoticeSentToTheOwnerNamesNeitherTheAskerNorTheirEmail()
    {
        var w = await SeedAsync();
        await AskAsync(w, w.PublicItemId, AskerId);

        await using var db = await w.Factory.CreateDbContextAsync();
        var message = await db.UserMessages.AsNoTracking()
            .OrderByDescending(m => m.DateCreated).FirstAsync();

        Assert.DoesNotContain(AskerName, message.MessageBody);
        Assert.DoesNotContain(AskerName, message.MessageSubject);
        Assert.DoesNotContain("asker@t", message.MessageBody);
        // The flag matters as much as the wording: without it the inbox names the sender itself.
        Assert.True(message.HideSenderIdentity);
        // The true sender is still stored — abuse has to remain traceable.
        Assert.Equal(AskerId, message.CreatedByAppUserId);
    }

    [Fact]
    public async Task TheAnswerNoticeDoesNotNameWhoAnswered()
    {
        var w = await SeedAsync();
        var questionId = await AskAsync(w, w.PublicItemId, AskerId);

        await Build(w.Factory, OwnerId).AnswerQuestion(
            questionId, new AnswerEquipmentQuestionRequest("Yes, two of them."), default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var message = await db.UserMessages.AsNoTracking()
            .OrderByDescending(m => m.DateCreated).FirstAsync();

        Assert.DoesNotContain("The Owner", message.MessageBody);
        Assert.True(message.HideSenderIdentity);
    }

    // ── Who may ask, and about what ──────────────────────────────────────────

    [Fact]
    public async Task AStrangerCannotAskAboutAPieceTheyCannotSee()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, StrangerId)
            .AskQuestion(w.PrivateItemId, new AskEquipmentQuestionRequest("?"), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task AnOwnerCannotAskThemselves()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, OwnerId)
            .AskQuestion(w.PublicItemId, new AskEquipmentQuestionRequest("?"), default);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task RetiredGearTakesNoMoreQuestions()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var item = await db.EquipmentItems.SingleAsync(i => i.Id == w.PublicItemId);
            item.IsRetired = true;
            await db.SaveChangesAsync();
        }

        // Retiring also closes the public route, so this is a 404 before it is ever a 409 —
        // the piece is simply not there for a stranger any more.
        var result = await Build(w.Factory, AskerId)
            .AskQuestion(w.PublicItemId, new AskEquipmentQuestionRequest("?"), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Answering ────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnlySomeoneWhoLooksAfterThePieceCanAnswer()
    {
        var w = await SeedAsync();
        var questionId = await AskAsync(w, w.PublicItemId, AskerId);

        var asStranger = await Build(w.Factory, StrangerId).AnswerQuestion(
            questionId, new AnswerEquipmentQuestionRequest("Nonsense"), default);
        Assert.IsType<NotFoundResult>(asStranger.Result);

        // ...and not the person who asked, either.
        var asAsker = await Build(w.Factory, AskerId).AnswerQuestion(
            questionId, new AnswerEquipmentQuestionRequest("Nonsense"), default);
        Assert.IsType<NotFoundResult>(asAsker.Result);
    }

    [Fact]
    public async Task AQuestionCannotBeAnsweredTwice()
    {
        var w = await SeedAsync();
        var questionId = await AskAsync(w, w.PublicItemId, AskerId);

        await Build(w.Factory, OwnerId).AnswerQuestion(
            questionId, new AnswerEquipmentQuestionRequest("Yes."), default);
        var second = await Build(w.Factory, OwnerId).AnswerQuestion(
            questionId, new AnswerEquipmentQuestionRequest("Actually, no."), default);

        Assert.IsType<ConflictObjectResult>(second.Result);
    }

    [Fact]
    public async Task DecliningNeedsNoAnswerAndStoresNone()
    {
        var w = await SeedAsync();
        var questionId = await AskAsync(w, w.PublicItemId, AskerId);

        var result = await Build(w.Factory, OwnerId).AnswerQuestion(
            questionId, new AnswerEquipmentQuestionRequest(null, Decline: true), default);
        var record = Assert.IsType<ReceivedQuestionRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(EquipmentQuestionStatus.Declined, record.Status);
        Assert.Null(record.AnswerText);
    }

    [Fact]
    public async Task TheAskerSeesTheAnswerOnTheirOwnList()
    {
        var w = await SeedAsync();
        var questionId = await AskAsync(w, w.PublicItemId, AskerId);
        await Build(w.Factory, OwnerId).AnswerQuestion(
            questionId, new AnswerEquipmentQuestionRequest("Two AAs."), default);

        var result = await Build(w.Factory, AskerId).GetAsked(default);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<AskedQuestionRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        var row = Assert.Single(rows);
        Assert.Equal("Two AAs.", row.AnswerText);
        Assert.Equal("My H1n", row.ItemDisplayName);
    }

    [Fact]
    public async Task TheOwnerSeesOnlyQuestionsAboutTheirOwnGear()
    {
        var w = await SeedAsync();
        await AskAsync(w, w.PublicItemId, AskerId);

        var mine = await Build(w.Factory, OwnerId).GetReceived(default);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ReceivedQuestionRecord>>(
            Assert.IsType<OkObjectResult>(mine.Result).Value));

        var theirs = await Build(w.Factory, StrangerId).GetReceived(default);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ReceivedQuestionRecord>>(
            Assert.IsType<OkObjectResult>(theirs.Result).Value));
    }

    // ── Promoting to the FAQ ─────────────────────────────────────────────────

    [Fact]
    public async Task PublishingCopiesTheTextRatherThanTheThread()
    {
        var w = await SeedAsync();
        var questionId = await AskAsync(w, w.PublicItemId, AskerId, "does it take AAs lol");
        await Build(w.Factory, OwnerId).AnswerQuestion(
            questionId, new AnswerEquipmentQuestionRequest("yeah 2x AA"), default);

        await Build(w.Factory, OwnerId).PromoteToFaq(
            questionId,
            new PromoteQuestionToFaqRequest("Does it take AA batteries?", "Yes — two AA cells."),
            default);

        await using var db = await w.Factory.CreateDbContextAsync();
        var faq = await db.EquipmentItemFaqs.AsNoTracking().SingleAsync();
        var question = await db.EquipmentQuestions.AsNoTracking().SingleAsync();

        // The tidied wording is what gets published; the thread keeps what was actually said.
        Assert.Equal("Does it take AA batteries?", faq.Question);
        Assert.Equal("does it take AAs lol", question.QuestionText);
        Assert.Equal(faq.Id, question.PromotedToFaqId);
    }

    [Fact]
    public async Task PublishingIsRefusedTwice_AndBeforeAnAnswerExists()
    {
        var w = await SeedAsync();
        var questionId = await AskAsync(w, w.PublicItemId, AskerId);
        var request = new PromoteQuestionToFaqRequest("Q", "A");

        var tooEarly = await Build(w.Factory, OwnerId).PromoteToFaq(questionId, request, default);
        Assert.IsType<ConflictObjectResult>(tooEarly.Result);

        await Build(w.Factory, OwnerId).AnswerQuestion(
            questionId, new AnswerEquipmentQuestionRequest("A"), default);

        Assert.IsType<OkObjectResult>((await Build(w.Factory, OwnerId).PromoteToFaq(questionId, request, default)).Result);
        Assert.IsType<ConflictObjectResult>((await Build(w.Factory, OwnerId).PromoteToFaq(questionId, request, default)).Result);
    }

    [Fact]
    public void APublishedFaqNamesNobody()
    {
        var faqNames = typeof(EquipmentFaqRecord).GetProperties().Select(p => p.Name).ToList();
        var catalogNames = typeof(CatalogFaqRecord).GetProperties().Select(p => p.Name).ToList();

        foreach (var names in new[] { faqNames, catalogNames })
        {
            Assert.DoesNotContain(names, n => n.Contains("Author", StringComparison.OrdinalIgnoreCase)
                                           || n.Contains("Owner", StringComparison.OrdinalIgnoreCase)
                                           || n.Contains("CreatedBy", StringComparison.OrdinalIgnoreCase)
                                           || n.Contains("AppUser", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ── FAQ visibility ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnFaqOnAPrivatePieceIsNotFoundForAStranger()
    {
        var w = await SeedAsync();
        await Build(w.Factory, OwnerId).AddFaq(
            w.PrivateItemId, new UpsertEquipmentFaqRequest("Q", "A"), default);

        Assert.IsType<NotFoundResult>((await Build(w.Factory, StrangerId).GetFaqs(w.PrivateItemId, default)).Result);
        Assert.IsType<OkObjectResult>((await Build(w.Factory, OwnerId).GetFaqs(w.PrivateItemId, default)).Result);
    }

    [Fact]
    public async Task OnlyACustodianCanWriteTheFaq()
    {
        var w = await SeedAsync();
        var result = await Build(w.Factory, AskerId).AddFaq(
            w.PublicItemId, new UpsertEquipmentFaqRequest("Q", "A"), default);

        // 404, not 403 — a stranger learns nothing about whether writing was even a possibility.
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task AnAnonymousVisitorReadsThePublicPiecesFaq()
    {
        var w = await SeedAsync();
        await Build(w.Factory, OwnerId).AddFaq(
            w.PublicItemId, new UpsertEquipmentFaqRequest("Batteries?", "Two AA."), default);

        var result = await Build(w.Factory, userId: null).GetFaqs(w.PublicItemId, default);
        var faqs = Assert.IsAssignableFrom<IReadOnlyList<EquipmentFaqRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("Batteries?", Assert.Single(faqs).Question);
    }
}
