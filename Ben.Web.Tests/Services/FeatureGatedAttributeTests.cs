using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 154: the server half of a feature switch. The ratchet test proves a flag is READ
/// somewhere; this proves the read REFUSES — off means 404, unset means on (the SiteSettingKeys
/// rule: sections that already exist default on, so adding a gate never removes a feature).
/// </summary>
public sealed class FeatureGatedAttributeTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static async Task<(ActionExecutingContext Context, bool NextRan)> RunAsync(string? storedValue)
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        if (storedValue is not null)
        {
            await using var db = await factory.CreateDbContextAsync();
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = SiteSettingKeys.FeatureEvents, Value = storedValue,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<BenDataContext>>(factory)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());

        var nextRan = false;
        await new FeatureGatedAttribute(SiteSettingKeys.FeatureEvents)
            .OnActionExecutionAsync(context, () =>
            {
                nextRan = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
            });
        return (context, nextRan);
    }

    [Fact]
    public async Task Off_answers_404_and_the_action_never_runs()
    {
        var (context, nextRan) = await RunAsync("false");
        Assert.IsType<NotFoundResult>(context.Result);
        Assert.False(nextRan);
    }

    [Fact]
    public async Task On_and_unset_both_let_the_action_run()
    {
        var (_, ranWhenOn) = await RunAsync("true");
        var (_, ranWhenUnset) = await RunAsync(null);
        Assert.True(ranWhenOn);
        Assert.True(ranWhenUnset, "a flag nobody has set must read as ON — adding a gate must never silently remove a working feature");
    }

    /// <summary>
    /// A flag whose declared default is OFF reads off when nobody has written its row.
    /// </summary>
    /// <remarks>
    /// <para>R1 of the canvas plan review, 2026-09-14. The gate used to read every flag with
    /// <c>whenUnset: true</c>, which was right for the sections that already existed and wrong the
    /// first time an unbuilt feature was gated: <c>features.canvas-editor</c> has no row on
    /// production, so the canvas API — including the link-unfurl fetcher — would have answered the
    /// moment the API deployed, with the admin page showing the switch as Off.</para>
    ///
    /// <para>The test above still holds for every flag that defaults on; this one pins the other
    /// half of <see cref="SiteSettingKeys.DefaultFor"/>.</para>
    /// </remarks>
    [Fact]
    public async Task A_flag_that_defaults_off_is_off_while_nobody_has_set_it()
    {
        // Publications, since the canvas flag went on 2026-09-16: boards became the only way research
        // is written, and a switch whose off position leaves a case with no research is not a choice.
        var gate = new FeatureGatedAttribute(SiteSettingKeys.FeaturePublications);

        var (unsetResult, ranWhenUnset) = await Support.FeatureGateProbe.RunAsync(
            gate, await Support.FeatureGateProbe.SettingsAsync());
        Assert.False(ranWhenUnset,
            "features.publications has no settings row and defaults off, yet the gated action ran — "
            + "the gate is reading an unset flag as on");
        Assert.IsType<NotFoundResult>(unsetResult);

        var (_, ranWhenOn) = await Support.FeatureGateProbe.RunAsync(
            gate, await Support.FeatureGateProbe.SettingsAsync(SiteSettingKeys.FeaturePublications, "true"));
        Assert.True(ranWhenOn, "switching the flag on must let the action run");
    }

    /// <summary>The flags that were gated before any of this defaulted on, and still do.</summary>
    [Theory]
    [InlineData(SiteSettingKeys.FeatureCmsPages)]
    [InlineData(SiteSettingKeys.FeatureEvents)]
    [InlineData(SiteSettingKeys.FeatureVoting)]
    [InlineData(SiteSettingKeys.FeatureDiscovery)]
    public async Task Every_flag_that_was_already_gated_still_reads_on_when_unset(string key)
    {
        var (_, ran) = await Support.FeatureGateProbe.RunAsync(
            new FeatureGatedAttribute(key), await Support.FeatureGateProbe.SettingsAsync());
        Assert.True(ran, $"{key} was on for every site that never set it; it must stay on");
    }
}
