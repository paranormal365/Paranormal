using Ben.Service.Models.Support;
using Ben.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// An older answer may never overwrite a newer one (2026-09-21).
/// </summary>
/// <remarks>
/// <para>Two refreshes can be in flight at once — the background one a stale timer started, and
/// the one an administrator's save runs immediately. Nothing ordered them, so the SLOWER won. A
/// slow background read started before a save wrote the announcement the administrator had just
/// cleared straight back over the cleared one, and the notice everybody is shown became the one
/// nobody could withdraw.</para>
///
/// <para>The e2e walk had been passing on timing alone; it only failed when an unrelated change
/// widened the window by a few milliseconds.</para>
/// </remarks>
public sealed class SiteFeaturesRefreshOrderTests
{
    private static SiteFeaturesProvider ProviderFor(IBenAdminClient client)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => client);
        var root = services.BuildServiceProvider();

        return new SiteFeaturesProvider(
            root.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SiteFeaturesProvider>.Instance);
    }

    private static SiteFeaturesInfo Info(string? announcement)
        => new(new Dictionary<string, bool>(SiteFeaturesProvider.Defaults), announcement, true, true, true);

    [Fact]
    public async Task A_slow_answer_from_before_the_save_does_not_bring_the_notice_back()
    {
        var slowStarted = new TaskCompletionSource();
        var releaseSlow = new TaskCompletionSource();
        var first = true;

        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetSiteFeaturesAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // The FIRST call is the background refresh that started before the save. It is
                // still waiting when the administrator clears the notice.
                if (first)
                {
                    first = false;
                    slowStarted.TrySetResult();
                    await releaseSlow.Task;
                    return Info("The old notice");
                }
                return Info(null);
            });

        var provider = ProviderFor(client.Object);

        var slow = provider.PrimeAsync();
        await slowStarted.Task;

        // The administrator saves. This one asks second and answers first.
        await provider.PrimeAsync();
        Assert.Null(provider.Announcement);

        // Now the stale one comes back. It must have nothing to say.
        releaseSlow.SetResult();
        await slow;

        Assert.Null(provider.Announcement);
    }

    [Fact]
    public async Task The_newest_answer_is_the_one_kept()
    {
        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetSiteFeaturesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Info("A notice"));

        var provider = ProviderFor(client.Object);
        await provider.PrimeAsync();

        Assert.Equal("A notice", provider.Announcement);
    }
}
