using Ben.Web.Services.WebApi;
using Ben.Service.Models.Store;
using Ben.Web.Services;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>The circuit's one copy of a person's hearts (storefront S6.3).</summary>
public sealed class StoreFavouriteStateTests
{
    private sealed class User(bool signedIn) : IBenUserState
    {
        public event Action? StateChanged;
        public void Changed() => StateChanged?.Invoke();
        public Task AuthReady => Task.CompletedTask;
        public bool IsAuthenticated { get; set; } = signedIn;
        public bool IsSuperAdmin => false;
        public bool IsAdmin => false;
        public bool IsModerator => false;
        public bool IsImpersonating => false;
        public Guid? UserId => null;
        public string? UserEmail => null;
        public TimeZoneInfo BrowserTimeZone => TimeZoneInfo.Utc;
    }

    private static StoreProductCard Card(Guid id) => new(id, "K-II", "k-ii", "EMF Meters", "emf-meters", null, null,
        59.99m, 59.99m, null, null, 0m, 0, true, null, false, false, 1, null);

    [Fact]
    public async Task Nothing_is_believed_or_fetched_during_the_server_render()
    {
        var client = new Mock<IBenAdminClient>(MockBehavior.Strict);
        var state = new StoreFavouriteState(client.Object, new User(signedIn: true));

        await state.EnsureStartedAsync(isInteractive: false);

        Assert.False(state.IsReady);   // the heart draws itself still, not as "sign in"
    }

    [Fact]
    public async Task A_signed_in_person_hearts_and_unhearts()
    {
        var kept = Guid.NewGuid();
        var other = Guid.NewGuid();
        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetMyStoreFavouritesAsync(default)).ReturnsAsync(LoadResult<StoreProductCard>.Ok([Card(kept)]));
        client.Setup(c => c.AddStoreFavouriteAsync(other, default)).ReturnsAsync((new StoreFavouriteCount(2), (string?)null));
        client.Setup(c => c.RemoveStoreFavouriteAsync(kept, default)).ReturnsAsync((new StoreFavouriteCount(1), (string?)null));
        var state = new StoreFavouriteState(client.Object, new User(signedIn: true));

        await state.EnsureStartedAsync(isInteractive: true);
        Assert.True(state.IsReady);
        Assert.True(state.IsFavourite(kept));

        Assert.Null(await state.ToggleAsync(other));
        Assert.Equal(2, state.Count);
        Assert.Null(await state.RemoveAsync(kept));
        Assert.False(state.IsFavourite(kept));
        Assert.True(state.IsFavourite(other));
    }

    [Fact]
    public async Task A_refusal_is_said_and_nothing_changes()
    {
        var product = Guid.NewGuid();
        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetMyStoreFavouritesAsync(default)).ReturnsAsync(LoadResult<StoreProductCard>.Ok([]));
        client.Setup(c => c.AddStoreFavouriteAsync(product, default)).ReturnsAsync(((StoreFavouriteCount?)null, StoreReviewSentences.NotOnSale));
        var state = new StoreFavouriteState(client.Object, new User(signedIn: true));
        await state.EnsureStartedAsync(isInteractive: true);

        Assert.Equal(StoreReviewSentences.NotOnSale, await state.ToggleAsync(product));
        Assert.False(state.IsFavourite(product));
        Assert.Equal(0, state.Count);
    }

    [Fact]
    public async Task A_guest_is_ready_with_no_hearts_and_asked_to_sign_in()
    {
        var client = new Mock<IBenAdminClient>(MockBehavior.Strict);
        var state = new StoreFavouriteState(client.Object, new User(signedIn: false));

        await state.EnsureStartedAsync(isInteractive: true);

        Assert.True(state.IsReady);
        Assert.False(state.IsSignedIn);
        Assert.Equal("Sign in to keep favourites.", await state.ToggleAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Signing_in_reads_the_hearts()
    {
        var kept = Guid.NewGuid();
        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetMyStoreFavouritesAsync(default)).ReturnsAsync(LoadResult<StoreProductCard>.Ok([Card(kept)]));
        var user = new User(signedIn: false);
        var state = new StoreFavouriteState(client.Object, user);
        await state.EnsureStartedAsync(isInteractive: true);
        Assert.False(state.IsFavourite(kept));

        user.IsAuthenticated = true;
        user.Changed();
        await Task.Yield();

        Assert.True(state.IsFavourite(kept));
    }
}
