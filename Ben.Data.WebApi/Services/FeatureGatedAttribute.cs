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
/// <para>Controller-level on purpose: gating per action is how one endpoint eventually forgets.</para>
///
/// <para><b>An unset flag reads as its declared default</b>, from
/// <see cref="SiteSettingKeys.DefaultFor"/> — the same list the admin page and the public features
/// endpoint publish. Sections that already exist default on, so adding a gate to one never
/// silently removes a working feature; unbuilt features default off, so gating one never silently
/// switches it on.</para>
///
/// <para>This used to pass <c>whenUnset: true</c> for every key. That was indistinguishable from
/// the rule while every gated flag defaulted on, and wrong the first time an unbuilt one was gated:
/// <c>features.canvas-editor</c> has no row on a site where nobody has touched it, so the canvas
/// API and its link-unfurl fetcher would have answered from the moment the API deployed while the
/// admin page showed the switch as Off (canvas plan review R1, 2026-09-14).</para>
///
/// <para><c>[Authorize]</c> still answers an anonymous caller with 401 first — authorization
/// filters run before action filters — so a deploy's anonymous smoke probe sees 401 either way and
/// only a signed-in probe can tell whether the gate is closed.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FeatureGatedAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _featureKey;

    public FeatureGatedAttribute(string featureKey) => _featureKey = featureKey;

    /// <summary>The <see cref="SiteSettingKeys"/> feature key this gate reads.</summary>
    public string FeatureKey => _featureKey;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var dbFactory = context.HttpContext.RequestServices
            .GetRequiredService<IDbContextFactory<BenDataContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(context.HttpContext.RequestAborted);

        if (!await SiteSettingsService.GetBoolAsync(
                db, _featureKey, whenUnset: SiteSettingKeys.DefaultFor(_featureKey),
                context.HttpContext.RequestAborted))
        {
            context.Result = new NotFoundResult();
            return;
        }
        await next();
    }
}
