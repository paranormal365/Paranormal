using Ben.Data.WebApi.Services;
using Ben.Service.Models.Support;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Ben.Web.Website.Components.Layout;
using Ben.Web.Website.Library.Kit;
using Ben.Web.Website.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The menu draws "Your groups" from what each person was shown last time, so it doesn't arrive late
/// and push every entry below it down a row (item 259).
/// </summary>
public sealed class NavGroupsMemoryTests
{
    private static NavGroupsMemory Memory() => new(new MemoryCache(new MemoryCacheOptions()));

    private static readonly Guid Sarah = Guid.NewGuid(), Daniel = Guid.NewGuid();

    [Fact]
    public void Nothing_is_remembered_before_the_first_fetch_or_for_nobody()
    {
        var memory = Memory();
        Assert.Empty(memory.Recall(Sarah));
        Assert.Empty(memory.Recall(null));
        memory.Remember(null, [new(Guid.NewGuid(), "Lost")]);
        Assert.Empty(memory.Recall(null));
    }

    [Fact]
    public void Each_person_gets_back_only_their_own_groups()
    {
        var memory = Memory();
        memory.Remember(Sarah, [new(Guid.NewGuid(), "Jim's Investigations")]);
        memory.Remember(Daniel, [new(Guid.NewGuid(), "Franklin Ghost Walks"), new(Guid.NewGuid(), "Nashville Paranormal")]);

        Assert.Equal(["Jim's Investigations"], memory.Recall(Sarah).Select(g => g.Name));
        Assert.Equal(2, memory.Recall(Daniel).Count);
    }

    [Fact]
    public void The_latest_fetch_replaces_what_was_remembered()
    {
        var memory = Memory();
        memory.Remember(Sarah, [new(Guid.NewGuid(), "Old group")]);
        memory.Remember(Sarah, []);
        Assert.Empty(memory.Recall(Sarah));
    }

    /// <summary>
    /// The bug itself. The menu's first draw happens before any fetch can answer; with the groups
    /// remembered, they are already in it, so nothing below them moves when the fetch lands.
    /// </summary>
    [Fact]
    public async Task The_menus_first_draw_already_has_your_groups_when_they_are_remembered()
    {
        var memory = Memory();
        memory.Remember(Sarah, [new(Guid.NewGuid(), "Jim's Investigations")]);

        // The drawer lists each group by name, right under Home and above everything that used to move.
        var remembered = await FirstDrawAsync(memory, Sarah);
        var group = remembered.IndexOf(">Jim's Investigations<", StringComparison.Ordinal);
        Assert.True(group > remembered.IndexOf(">Home<", StringComparison.Ordinal), "The group should be under Home.");
        Assert.True(group < remembered.IndexOf(">Notifications<", StringComparison.Ordinal), "The group should be above Notifications.");

        // Somebody with nothing remembered still gets the old behavior: the groups arrive with the fetch.
        Assert.DoesNotContain("Jim's Investigations", await FirstDrawAsync(memory, Daniel));
    }

    private sealed class FakeNav : NavigationManager
    {
        public FakeNav() => Initialize("https://unit.test/", "https://unit.test/");
        protected override void NavigateToCore(string uri, NavigationOptions options) { }
    }

    /// <summary>The drawer as it is first drawn, before OnAfterRender's fetch.</summary>
    private static async Task<string> FirstDrawAsync(NavGroupsMemory memory, Guid userId)
    {
        var userState = new Mock<IBenUserState>();
        userState.SetupGet(u => u.IsAuthenticated).Returns(true);
        userState.SetupGet(u => u.AuthReady).Returns(Task.CompletedTask);
        var tokens = new Mock<IWebApiTokenStore>();
        tokens.SetupGet(t => t.IsAuthenticated).Returns(true);
        tokens.SetupGet(t => t.UserId).Returns(userId);
        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetSiteFeaturesAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new SiteFeaturesInfo(new Dictionary<string, bool>(SiteFeaturesProvider.Defaults)));
        var featureServices = new ServiceCollection();
        featureServices.AddScoped(_ => client.Object);
        var features = new SiteFeaturesProvider(
            featureServices.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<SiteFeaturesProvider>.Instance);
        await features.PrimeAsync();

        var services = new ServiceCollection();
        var nav = new FakeNav();
        services.AddSingleton<NavigationManager>(nav);
        services.AddSingleton(userState.Object);
        services.AddSingleton(tokens.Object);
        services.AddSingleton(new Mock<IWebApiAuthService>().Object);
        services.AddSingleton(client.Object);
        services.AddSingleton(new NotificationState(client.Object, userState.Object, nav));
        services.AddSingleton(features);
        services.AddSingleton(new RailMenuState());
        services.AddSingleton(memory);
        services.AddSingleton(new Mock<IJSRuntime>().Object);
        services.AddSingleton<IOptions<Ben.Data.Common.SiteIdentity>>(Options.Create(new Ben.Data.Common.SiteIdentity()));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<BenNav>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(BenNav.IsAuthenticated)] = true }));
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }
}
