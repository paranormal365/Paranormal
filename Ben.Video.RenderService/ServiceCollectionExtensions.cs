using Microsoft.Extensions.DependencyInjection;

namespace Ben.Video.RenderService;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the render-service engine (region tracking today; the background render worker
    /// and queue join this registration in later phases of item #36). Call after
    /// <c>AddBenVideoEditor()</c> in the host's <c>Program.cs</c>.
    /// </summary>
    public static IServiceCollection AddBenVideoRenderService(this IServiceCollection services)
    {
        services.AddScoped<RenderRegionTracker>();
        return services;
    }
}
