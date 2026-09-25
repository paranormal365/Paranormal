using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin.Store;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Controllers.Seller;
using Ben.Data.WebApi.Controllers.Store;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// A product's FAQ and shoppers' questions (store sellers, backlog 251, P12): a question goes privately
/// to the item's seller — or the store, for its own stock — without the asker's name; the answer is the
/// asker's alone unless it's copied into the FAQ; a closed account takes its questions with it.
/// </summary>
public sealed class StoreProductQuestionTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!, _hazel = null!, _ivan = null!, _sarah = null!;
    private Guid _hers, _ours, _hidden;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _hazel = StoreTestData.Person(db, "hazel");
        _hazel.DisplayName = "Hazel Marsh";
        _ivan = StoreTestData.Person(db, "ivan");
        _sarah = StoreTestData.Person(db, "sarah");
        _sarah.DisplayName = "Sarah Mitchell";
        var shelf = StoreTestData.Category(db, _admin, "Trigger Objects");
        var hers = StoreTestData.Product(db, _admin, shelf);
        hers.Name = "Hand-Built REM Pod";
        hers.SellerAppUserId = _hazel.Id;
        _hers = hers.Id;
        var ours = StoreTestData.Product(db, _admin, shelf);
        ours.Name = "K-II Meter";
        _ours = ours.Id;
        _hidden = StoreTestData.Product(db, _admin, shelf, active: false).Id;
        var seller = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.Seller, NormalizedName = "SELLER" };
        var super = new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = RoleNames.SuperAdmin, NormalizedName = "SUPERADMIN" };
        db.Roles.AddRange(seller, super);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _hazel.Id, RoleId = seller.Id });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _ivan.Id, RoleId = seller.Id });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = _admin.Id, RoleId = super.Id });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private StoreSellerAlerts Alerts() => new(_sqlite.Factory, new PlatformMessageService(_sqlite.Factory), NullLogger<StoreSellerAlerts>.Instance);

    private MyStoreQuestionController Shopper(AppUser who) => new(_sqlite.Factory, Alerts()) { ControllerContext = StoreTestData.SignedInAs(who.Id) };
    private SellerStoreQuestionController Inbox(AppUser who) => new(_sqlite.Factory, Alerts()) { ControllerContext = StoreTestData.SignedInAs(who.Id) };
    private AdminStoreQuestionController Store() => new(_sqlite.Factory, Alerts()) { ControllerContext = StoreTestData.SignedInAs(_admin.Id) };

    private SellerStoreProductEditController SellerEditor(AppUser who) => new(_sqlite.Factory, null!, new CmsMarkupSanitizer(), Alerts())
    {
        ControllerContext = StoreTestData.SignedInAs(who.Id),
    };

    private AdminStoreProductController AdminEditor() => new(_sqlite.Factory, new Mock<IAuditLogService>().Object, null!, new CmsMarkupSanitizer())
    {
        ControllerContext = StoreTestData.SignedInAs(_admin.Id),
    };

    private PublicStoreController Page() => new(_sqlite.Factory, null!, Options.Create(new Ben.Data.WebApi.Services.Billing.StripeIntegration.StripeOptions()))
    {
        ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
    };

    private static T Ok<T>(ActionResult<T> r) => (T)Assert.IsType<OkObjectResult>(r.Result).Value!;
    private static List<T> List<T>(ActionResult<IEnumerable<T>> r) => ((IEnumerable<T>)Assert.IsType<OkObjectResult>(r.Result).Value!).ToList();
    private static string Said<T>(ActionResult<T> r) => (string)Assert.IsAssignableFrom<ObjectResult>(r.Result).Value!;

    private async Task<List<string>> BellsAsync(AppUser who)
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.UserMessageTos.Where(t => t.ToAppUserId == who.Id).Select(t => t.UserMessage.MessageSubject!).ToListAsync();
    }

    private async Task<StoreAskedQuestionRecord> AskAsync(Guid product, string text = "Does it come with a carrying case?", AppUser? who = null)
        => Ok(await Shopper(who ?? _sarah).Ask(product, new AskStoreQuestionRequest(text), default));

    private async Task<string> SlugAsync(Guid product)
    {
        await using var db = await _sqlite.NewContextAsync();
        return await db.StoreProducts.Where(p => p.Id == product).Select(p => p.Slug).SingleAsync();
    }

    // ── asking ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_question_about_a_sellers_item_reaches_her_inbox_without_the_askers_name()
    {
        var asked = await AskAsync(_hers);
        Assert.Equal((StoreQuestionStatus.Open, "Hand-Built REM Pod"), (asked.Status, asked.ProductName));

        var inbox = List(await Inbox(_hazel).GetAll(open: true, default));
        var q = Assert.Single(inbox);
        Assert.Equal(("Does it come with a carrying case?", false), (q.Question, q.SiteStock));
        Assert.Contains("A question about Hand-Built REM Pod", await BellsAsync(_hazel));
        Assert.DoesNotContain("A question about Hand-Built REM Pod", await BellsAsync(_admin));   // her item is hers to answer

        Assert.Empty(List(await Inbox(_ivan).GetAll(open: null, default)));                          // another seller sees nothing

        // The shape itself has nowhere to put who asked.
        Assert.DoesNotContain(typeof(StoreReceivedQuestionRecord).GetProperties(),
            p => p.Name.Contains("Asker", StringComparison.Ordinal) || p.Name.Contains("AppUser", StringComparison.Ordinal));
        await using var db = await _sqlite.NewContextAsync();
        var bell = await db.UserMessageTos.Where(t => t.ToAppUserId == _hazel.Id).Select(t => t.UserMessage).SingleAsync();
        Assert.DoesNotContain("Sarah", bell.MessageSubject + bell.MessageBody);
        Assert.NotEqual(_sarah.Id, bell.CreatedByAppUserId);
    }

    [Fact]
    public async Task A_question_about_the_stores_own_stock_goes_to_the_store()
    {
        await AskAsync(_ours, "Can this be used outdoors in the rain?");

        Assert.Contains("A question about K-II Meter", await BellsAsync(_admin));
        var ours = List(await Store().GetAll(open: true, siteStock: true, default));
        Assert.True(Assert.Single(ours).SiteStock);
        Assert.Empty(List(await Inbox(_hazel).GetAll(open: null, default)));
    }

    [Fact]
    public async Task Asking_is_refused_in_words_when_it_should_be()
    {
        Assert.Contains("a few words", Said(await Shopper(_sarah).Ask(_hers, new AskStoreQuestionRequest("Hm?"), default)));
        Assert.Contains("your own item", Said(await Shopper(_hazel).Ask(_hers, new AskStoreQuestionRequest("Is mine any good?"), default)));
        Assert.IsType<NotFoundResult>((await Shopper(_sarah).Ask(_hidden, new AskStoreQuestionRequest("Is this coming back?"), default)).Result);

        for (var i = 0; i < StoreProductQuestions.MaxOpenPerProduct; i++) await AskAsync(_hers, $"Question number {i + 1}?");
        Assert.Contains("waiting already", Said(await Shopper(_sarah).Ask(_hers, new AskStoreQuestionRequest("One more thing?"), default)));

        // The day's limit counts every question, answered or not, across the store.
        await using (var db = await _sqlite.NewContextAsync())
        {
            for (var i = StoreProductQuestions.MaxOpenPerProduct; i < StoreProductQuestions.MaxPerDay; i++)
                db.StoreProductQuestions.Add(new StoreProductQuestion
                {
                    Id = Guid.NewGuid(), ProductId = _hers, AskerAppUserId = _sarah.Id, Question = $"Earlier today {i}?",
                    Status = StoreQuestionStatus.Answered, Answer = "Yes.", DateCreated = DateTime.UtcNow.AddHours(-2),
                });
            await db.SaveChangesAsync();
        }
        Assert.Contains("today", Said(await Shopper(_sarah).Ask(_ours, new AskStoreQuestionRequest("And another?"), default)));
        Ok(await Shopper(_ivan).Ask(_ours, new AskStoreQuestionRequest("Somebody else may still ask?"), default));
    }

    // ── answering ───────────────────────────────────────────────────────────

    [Fact]
    public async Task The_answer_reaches_the_asker_alone_and_only_once()
    {
        var asked = await AskAsync(_hers);
        var answered = Ok(await Inbox(_hazel).Answer(asked.Id, new AnswerStoreQuestionRequest("Yes — a padded pouch.", false), default));
        Assert.Equal((StoreQuestionStatus.Answered, "Yes — a padded pouch."), (answered.Status, answered.Answer));

        var mine = Assert.Single(List(await Shopper(_sarah).Mine(default)));
        Assert.Equal((StoreQuestionStatus.Answered, "Yes — a padded pouch."), (mine.Status, mine.Answer));
        Assert.Contains("Your question about Hand-Built REM Pod was answered", await BellsAsync(_sarah));
        Assert.Empty(List(await Shopper(_ivan).Mine(default)));

        Assert.Contains("already been answered", Said(await Inbox(_hazel).Answer(asked.Id, new AnswerStoreQuestionRequest("Actually, no.", false), default)));
        Assert.Contains("already been answered", Said(await Store().Answer(asked.Id, new AnswerStoreQuestionRequest("No pouch.", false), default)));
        Assert.Equal("Yes — a padded pouch.", Assert.Single(List(await Shopper(_sarah).Mine(default))).Answer);
    }

    [Fact]
    public async Task Another_seller_cannot_answer_but_the_store_can_and_a_decline_says_why()
    {
        var first = await AskAsync(_hers);
        Assert.IsType<NotFoundResult>((await Inbox(_ivan).Answer(first.Id, new AnswerStoreQuestionRequest("Ivan here.", false), default)).Result);
        Assert.Contains("Write an answer", Said(await Inbox(_hazel).Answer(first.Id, new AnswerStoreQuestionRequest("  ", false), default)));

        Ok(await Store().Answer(first.Id, new AnswerStoreQuestionRequest("The store: it ships in a box.", false), default));
        var second = await AskAsync(_hers, "Will you make a blue one?");
        var declined = Ok(await Inbox(_hazel).Answer(second.Id, new AnswerStoreQuestionRequest("I only make them in black.", true), default));
        Assert.Equal((StoreQuestionStatus.Declined, "I only make them in black."), (declined.Status, declined.Answer));
        Assert.Contains("Your question about Hand-Built REM Pod", await BellsAsync(_sarah));
    }

    // ── the FAQ ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_promoted_answer_is_copied_into_the_faq_once_and_shows_on_the_page()
    {
        var asked = await AskAsync(_hers);
        Assert.Contains("answered question", Said(await Inbox(_hazel).Promote(asked.Id, new PromoteStoreQuestionRequest("Case?", "Yes."), default)));
        Ok(await Inbox(_hazel).Answer(asked.Id, new AnswerStoreQuestionRequest("Yes — a padded pouch.", false), default));

        var promoted = Ok(await Inbox(_hazel).Promote(asked.Id,
            new PromoteStoreQuestionRequest("Does it come with a case?", "Yes — every one ships in a padded pouch."), default));
        Assert.True(promoted.Promoted);
        Assert.Contains("in the FAQ already", Said(await Inbox(_hazel).Promote(asked.Id, new PromoteStoreQuestionRequest("Again?", "Again."), default)));

        // The asker's own answer is untouched; the FAQ has the reworded copy.
        Assert.Equal("Yes — a padded pouch.", Assert.Single(List(await Shopper(_sarah).Mine(default))).Answer);
        var page = Ok(await Page().Product(await SlugAsync(_hers), null, default));
        var faq = Assert.Single(page.Faqs!);
        Assert.Equal(("Does it come with a case?", "Yes — every one ships in a padded pouch."), (faq.Question, faq.Answer));
        Assert.True(page.CanAsk);

        await using var db = await _sqlite.NewContextAsync();
        Assert.Contains(await db.StoreProductChanges.Where(c => c.ProductId == _hers && c.Area == StoreProductChangeArea.Faq).Select(c => c.Summary).ToListAsync(),
            s => s.StartsWith("Added a shopper's question to the FAQ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_faq_is_saved_whole_with_one_history_line_and_its_switch_hides_it()
    {
        var saved = Ok(await SellerEditor(_hazel).SaveFaqs(_hers, new SaveStoreFaqsRequest(true,
        [
            new(null, "How long do the batteries last?", "About 30 hours."),
            new(null, "Is it loud?", "Very."),
        ]), default));
        Assert.Equal(["How long do the batteries last?", "Is it loud?"], saved.Faqs.Select(f => f.Question));

        var (batteries, loud) = (saved.Faqs[0], saved.Faqs[1]);
        var again = Ok(await AdminEditor().SaveFaqs(_hers, new SaveStoreFaqsRequest(true,
        [
            loud with { Answer = "Loud enough for a hall." },
            new(null, "Does it need an app?", "No."),
        ]), default));
        Assert.Equal(["Is it loud?", "Does it need an app?"], again.Faqs.Select(f => f.Question));
        Assert.Equal(loud.Id, again.Faqs[0].Id);

        Assert.Equal(["Is it loud?", "Does it need an app?"], Ok(await Page().Product(await SlugAsync(_hers), null, default)).Faqs!.Select(f => f.Question));
        Ok(await SellerEditor(_hazel).SaveFaqs(_hers, new SaveStoreFaqsRequest(false, again.Faqs), default));
        Assert.Empty(Ok(await Page().Product(await SlugAsync(_hers), null, default)).Faqs!);

        await using var db = await _sqlite.NewContextAsync();
        var lines = await db.StoreProductChanges.Where(c => c.ProductId == _hers && c.Area == StoreProductChangeArea.Faq)
            .OrderBy(c => c.OccurredUtc).Select(c => c.Summary).ToListAsync();
        Assert.Equal(["FAQ: added 2.", "FAQ: added 1, changed 1, removed 1.", "Switched the FAQ off."], lines);
        _ = batteries;
    }

    [Fact]
    public async Task An_faq_entry_needs_both_halves_and_another_seller_cannot_touch_it()
    {
        Assert.Contains("Entry 2 needs both", Said(await SellerEditor(_hazel).SaveFaqs(_hers, new SaveStoreFaqsRequest(true,
            [new(null, "Fine?", "Fine."), new(null, "Answerless?", " ")]), default)));
        Assert.IsType<NotFoundResult>((await SellerEditor(_ivan).SaveFaqs(_hers, new SaveStoreFaqsRequest(true, [new(null, "Mine?", "Yes.")]), default)).Result);
        Assert.IsType<NotFoundResult>((await SellerEditor(_ivan).Faqs(_hers, default)).Result);

        await using var db = await _sqlite.NewContextAsync();
        Assert.False(await db.StoreProductFaqs.AnyAsync());
    }

    // ── the asker's account ─────────────────────────────────────────────────

    [Fact]
    public async Task Closing_an_account_takes_its_questions_but_leaves_a_promoted_faq()
    {
        var asked = await AskAsync(_hers);
        Ok(await Inbox(_hazel).Answer(asked.Id, new AnswerStoreQuestionRequest("Yes.", false), default));
        Ok(await Inbox(_hazel).Promote(asked.Id, new PromoteStoreQuestionRequest("Case?", "Yes, a pouch."), default));
        await AskAsync(_ours, "Does the meter need batteries?");

        await using (var db = await _sqlite.NewContextAsync())
        {
            var sarah = await db.AppUsers.SingleAsync(u => u.Id == _sarah.Id);
            await AccountClosureService.AnonymiseAsync(db, sarah, default);
        }

        await using (var db = await _sqlite.NewContextAsync())
        {
            Assert.False(await db.StoreProductQuestions.AnyAsync(q => q.AskerAppUserId == _sarah.Id));
            Assert.Single(await db.StoreProductFaqs.Where(f => f.ProductId == _hers).ToListAsync());
        }
    }
}
