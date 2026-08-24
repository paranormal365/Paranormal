using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Refuses every action on the controller with 404 while the named feature switch is off
/// (item 154). 404, not 403: a switched-off section should look exactly like a section that was
/// never built — an "unavailable" answer both leaks configuration and invites "when is it back?".
/// </summary>
/// <remarks>
/// Controller-level on purpose: gating per action is how one endpoint eventually forgets. The
/// flag defaults ON when unset, per the SiteSettingKeys rule — sections that already exist
/// default on, so adding a gate never silently removes a working feature.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FeatureGatedAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _featureKey;

    public FeatureGatedAttribute(string featureKey) => _featureKey = featureKey;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var dbFactory = context.HttpContext.RequestServices
            .GetRequiredService<IDbContextFactory<BenDataContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(context.HttpContext.RequestAborted);

        if (!await SiteSettingsService.GetBoolAsync(
                db, _featureKey, whenUnset: true, context.HttpContext.RequestAborted))
        {
            context.Result = new NotFoundResult();
            return;
        }
        await next();
    }
}
