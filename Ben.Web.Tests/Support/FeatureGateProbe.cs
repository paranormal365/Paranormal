using System.Reflection;
using System.Security.Claims;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ben.Web.Tests.Support;

/// <summary>
/// Runs the <see cref="FeatureGatedAttribute"/> a controller actually carries, the way MVC would
/// before the action, and reports whether the action would have run.
/// </summary>
/// <remarks>
/// <para>A controller test that calls an action method directly never passes through filters, so
/// it cannot see a gate at all — a test that "the canvas API answers 404 while the flag is off"
/// written that way would pass with the attribute deleted. This reads the attribute off the type
/// (so removing it fails the probe) and executes it against a real settings table.</para>
///
/// <para>Signed in on purpose. <c>[Authorize]</c> answers anonymous callers with 401 before any
/// action filter runs, so the case that matters for the gate is a signed-in person on a site where
/// nobody has ever written the flag's row — the state production is in the day the API deploys.</para>
/// </remarks>
internal static class FeatureGateProbe
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(options));
    }

    /// <summary>A fresh settings table, optionally holding one stored value for one key.</summary>
    public static async Task<IDbContextFactory<BenDataContext>> SettingsAsync(string? key = null, string? value = null)
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        if (key is not null && value is not null)
        {
            await using var db = await factory.CreateDbContextAsync();
            db.SiteSettings.Add(new Ben.Data.Source.Entities.SiteSetting
            {
                Id = Guid.NewGuid(), Key = key, Value = value,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }
        return factory;
    }

    /// <summary>Executes <paramref name="gate"/> as a signed-in caller; returns the short-circuit result and whether the action ran.</summary>
    public static async Task<(IActionResult? Result, bool ActionRan)> RunAsync(
        FeatureGatedAttribute gate, IDbContextFactory<BenDataContext> settings)
    {
        var services = new ServiceCollection()
            .AddSingleton(settings)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer")),
        };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());

        var ran = false;
        await gate.OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
        });
        return (context.Result, ran);
    }

    /// <summary>The gate declared on <typeparamref name="TController"/>; fails the test when there is none.</summary>
    public static FeatureGatedAttribute GateOn<TController>()
        => typeof(TController).GetCustomAttribute<FeatureGatedAttribute>(inherit: true)
           ?? throw new Xunit.Sdk.XunitException(
               $"{typeof(TController).Name} carries no [FeatureGated] attribute, so switching its feature off would change nothing.");

    /// <summary>The key a gate was declared with, read back for the "gated on the right flag" check.</summary>
    public static string KeyOf(FeatureGatedAttribute gate) => gate.FeatureKey;
}
