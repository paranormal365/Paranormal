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
}
