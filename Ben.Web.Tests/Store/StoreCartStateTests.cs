using Ben.Service.Models.Store;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>The circuit's one copy of the cart (storefront S3.4).</summary>
public sealed class StoreCartStateTests
{
    private static StoreCartView Cart(int count, decimal subtotal = 0m) => new(
        Guid.NewGuid(), count == 0 ? [] : [new StoreCartLineView(Guid.NewGuid(), Guid.NewGuid(), "K-II", null, "KII", "k-ii", null,
            subtotal / count, null, count, 10, subtotal, null, true, null)],
        count, subtotal, null, 0m, null, count == 0 ? null : 7.95m, false, 75m, 3, null, subtotal + 7.95m, true, count > 0, null);

    private sealed class User : IBenUserState
    {
        public event Action? StateChanged;
        public void SignIn() => StateChanged?.Invoke();
        public Task AuthReady => Task.CompletedTask;
        // Everything else is irrelevant to the cart.
        public bool IsAuthenticated => false;
        public bool IsSuperAdmin => false;
        public bool IsAdmin => false;
        public bool IsModerator => false;
        public bool IsSeller => false;
        public bool IsImpersonating => false;
        public Guid? UserId => null;
        public string? UserEmail => null;
        public TimeZoneInfo BrowserTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task During_the_server_render_nothing_is_fetched()
    {
        var client = new Mock<IBenAdminClient>(MockBehavior.Strict);
        var state = new StoreCartState(client.Object, new User());

        await state.EnsureStartedAsync(isInteractive: false);

        Assert.Null(state.Cart);   // a strict mock would have thrown on any call
    }

    [Fact]
    public async Task Adding_takes_the_servers_cart_and_says_so()
    {
        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetCartAsync(default)).ReturnsAsync(ItemResult<StoreCartView>.Ok(Cart(0)));
        client.Setup(c => c.AddToCartAsync(It.IsAny<AddToCartRequest>(), default))
            .ReturnsAsync(new StoreCartChange(Cart(3, 30m) with { Notice = "Only 3 left." }, "Only 3 left."));
        var state = new StoreCartState(client.Object, new User());
        await state.EnsureStartedAsync(isInteractive: true);
        var raised = 0;
        state.Changed += () => raised++;

        var change = await state.AddAsync(Guid.NewGuid(), 5);

        Assert.Equal("Only 3 left.", change.Message);
        Assert.Equal(3, state.Count);
        Assert.True(raised > 0);
    }

    [Fact]
    public async Task A_refused_add_keeps_the_cart_that_was_showing()
    {
        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetCartAsync(default)).ReturnsAsync(ItemResult<StoreCartView>.Ok(Cart(2, 20m)));
        client.Setup(c => c.AddToCartAsync(It.IsAny<AddToCartRequest>(), default))
            .ReturnsAsync(new StoreCartChange(null, StoreCartSentences.NoLongerAvailable));
        var state = new StoreCartState(client.Object, new User());
        await state.EnsureStartedAsync(isInteractive: true);

        var change = await state.AddAsync(Guid.NewGuid(), 1);

        Assert.Null(change.Cart);
        Assert.Equal(StoreCartSentences.NoLongerAvailable, change.Message);
        Assert.Equal(2, state.Count);
    }

    [Fact]
    public async Task Signing_in_re_reads_the_cart()
    {
        var client = new Mock<IBenAdminClient>();
        client.SetupSequence(c => c.GetCartAsync(default))
            .ReturnsAsync(ItemResult<StoreCartView>.Ok(Cart(1, 10m)))
            .ReturnsAsync(ItemResult<StoreCartView>.Ok(Cart(4, 40m)));   // the account's, merged
        var user = new User();
        var state = new StoreCartState(client.Object, user);
        await state.EnsureStartedAsync(isInteractive: true);
        var reread = new TaskCompletionSource();
        state.Changed += () => { if (state.Count == 4) reread.TrySetResult(); };

        user.SignIn();

        await reread.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, state.Count);
    }

    [Fact]
    public async Task A_failed_read_says_so_rather_than_showing_an_empty_cart()
    {
        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetCartAsync(default)).ReturnsAsync(ItemResult<StoreCartView>.Failure("The server answered 500"));
        var state = new StoreCartState(client.Object, new User());

        await state.EnsureStartedAsync(isInteractive: true);

        Assert.Null(state.Cart);
        Assert.Equal("The server answered 500", state.LoadError);
    }
}
