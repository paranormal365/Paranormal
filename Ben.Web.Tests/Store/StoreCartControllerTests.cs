using System.Security.Claims;
using Ben.Data.Common.Constants;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Store;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The cart's doors (storefront S3.3): who the cart belongs to, and what each refusal answers.
/// </summary>
public sealed class StoreCartControllerTests : IAsyncLifetime
{
    private SqliteTestDb _sqlite = null!;
    private AppUser _admin = null!;
    private StoreProductVariant _variant = null!;

    public async Task InitializeAsync()
    {
        _sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await _sqlite.NewContextAsync();
        _admin = StoreTestData.Person(db);
        _variant = StoreTestData.Variant(db, _admin, onHand: 3);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _sqlite.DisposeAsync().AsTask();

    private static string NewToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private StoreCartController Controller(string? cartHeader = null, ClaimsPrincipal? user = null, Guid? entraUser = null)
    {
        var context = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) };
        if (cartHeader is not null) context.Request.Headers[StoreCartController.CartHeader] = cartHeader;
        if (entraUser is { } who) context.RequestServices = EntraSignedIn(who);
        return new StoreCartController(_sqlite.Factory) { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    /// <summary>
    /// A request whose only sign-in is Microsoft's: the local bearer is absent, so <c>User</c> is
    /// empty, and the Entra scheme — asked by hand — answers with the account.
    /// </summary>
    private static IServiceProvider EntraSignedIn(Guid who)
    {
        var schemes = new Mock<IAuthenticationSchemeProvider>();
        schemes.Setup(s => s.GetSchemeAsync(AuthPolicyNames.EntraScheme))
            .ReturnsAsync(new AuthenticationScheme(AuthPolicyNames.EntraScheme, null, typeof(DummyHandler)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(EntraClaimsTransformation.AppUserIdClaimType, who.ToString())], AuthPolicyNames.EntraScheme));
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), AuthPolicyNames.EntraScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthPolicyNames.EntraScheme)));

        return new ServiceCollection()
            .AddSingleton(schemes.Object)
            .AddSingleton(auth.Object)
            .BuildServiceProvider();
    }

    private sealed class DummyHandler : IAuthenticationHandler
    {
        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context) => Task.CompletedTask;
        public Task<AuthenticateResult> AuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private static T Ok<T>(ActionResult<T> r) => (T)Assert.IsType<OkObjectResult>(r.Result).Value!;

    [Fact]
    public async Task A_microsoft_sign_in_lands_on_the_account_cart()
    {
        AppUser member;
        await using (var db = await _sqlite.NewContextAsync())
        {
            member = StoreTestData.Person(db, "member");
            await db.SaveChangesAsync();
        }

        Ok(await Controller(entraUser: member.Id).Add(new AddToCartRequest(_variant.Id, 1), default));

        await using var check = await _sqlite.NewContextAsync();
        var cart = await check.StoreCarts.SingleAsync();
        Assert.Equal(member.Id, cart.AppUserId);
        Assert.Null(cart.GuestTokenHash);
    }

    [Fact]
    public async Task A_malformed_header_is_a_visitor_with_no_cart()
    {
        var twenty = new string('a', 20);

        var view = Ok(await Controller(twenty).Get(default));
        var add = await Controller(twenty).Add(new AddToCartRequest(_variant.Id, 1), default);

        Assert.True(view.IsEmpty);
        Assert.Equal(StoreCartSentences.NoCart, Assert.IsType<BadRequestObjectResult>(add.Result).Value);
        await using var check = await _sqlite.NewContextAsync();
        Assert.Equal(0, await check.StoreCarts.CountAsync());
    }

    [Fact]
    public async Task A_capped_add_answers_409_with_the_whole_cart()
    {
        var token = NewToken();

        var add = await Controller(token).Add(new AddToCartRequest(_variant.Id, 5), default);

        var view = Assert.IsType<StoreCartView>(Assert.IsType<ConflictObjectResult>(add.Result).Value);
        Assert.Equal("Only 3 left.", view.Notice);
        Assert.Equal(3, view.Count);
        Assert.Equal(3, Ok(await Controller(token).Count(default)).Count);
    }

    [Fact]
    public async Task Refusals_are_sentences_with_their_statuses()
    {
        var token = NewToken();

        var none = await Controller(token).Add(new AddToCartRequest(_variant.Id, 0), default);
        var gone = await Controller(token).Add(new AddToCartRequest(Guid.NewGuid(), 1), default);
        var notInCart = await Controller(token).Remove(_variant.Id, default);
        var noCode = await Controller(token).RemoveCoupon(default);

        Assert.Equal(StoreCartSentences.ChooseHowMany, Assert.IsType<BadRequestObjectResult>(none.Result).Value);
        Assert.Equal(StoreCartSentences.NoLongerAvailable, Assert.IsType<NotFoundObjectResult>(gone.Result).Value);
        Assert.Equal(StoreCartSentences.NotInCart, Assert.IsType<NotFoundObjectResult>(notInCart.Result).Value);
        Assert.Equal(StoreCartSentences.NoCode, Assert.IsType<NotFoundObjectResult>(noCode.Result).Value);
    }

    [Fact]
    public async Task The_same_token_finds_the_same_cart()
    {
        var token = NewToken();
        Ok(await Controller(token).Add(new AddToCartRequest(_variant.Id, 1), default));

        var mine = Ok(await Controller(token).Get(default));
        var someoneElse = Ok(await Controller(NewToken()).Get(default));

        Assert.Single(mine.Lines);
        Assert.True(someoneElse.IsEmpty);
    }
}
