using Ben.Canvas.Core.Options;
using Ben.Canvas.Editor.Extensions;
using Ben.Canvas.Editor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ben.Canvas.Tests.Hosting;

/// <summary>
/// What <c>AddBenCanvasEditor</c> registers, and what it deliberately leaves to the host.
/// </summary>
/// <remarks>
/// The editor is auth-transparent: it names two HttpClients and lets the host decorate one of them.
/// The failure this guards is the video editor's: a library-owned client the host could not reach
/// meant Save to Server answered 401 inside the site for months.
/// </remarks>
public sealed class ServiceRegistrationTests
{
    private static ServiceProvider Build(Action<CanvasEditorOptions>? configure = null, Action<IHttpClientBuilder>? http = null)
    {
        var services = new ServiceCollection();
        services.AddBenCanvasEditor(configure, http);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Both_named_clients_are_registered()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.NotNull(factory.CreateClient(CanvasEditorServiceCollectionExtensions.PersistenceHttpClientName));
        Assert.NotNull(factory.CreateClient(CanvasEditorServiceCollectionExtensions.PublicHttpClientName));
    }

    [Fact]
    public void The_configure_delegate_reaches_the_options()
    {
        using var provider = Build(o => o.ApiBaseUrl = "https://ishaunted.test/webapi");

        Assert.Equal("https://ishaunted.test/webapi", provider.GetRequiredService<IOptions<CanvasEditorOptions>>().Value.ApiBaseUrl);
    }

    [Fact]
    public void The_http_builder_delegate_decorates_only_the_persistence_client()
    {
        var names = new List<string>();

        using var provider = Build(http: b => names.Add(b.Name));

        Assert.Equal([CanvasEditorServiceCollectionExtensions.PersistenceHttpClientName], names);
    }

    /// <summary>A default would hide a host that forgot to supply one.</summary>
    [Fact]
    public void No_sign_in_state_is_registered_by_the_library()
    {
        using var provider = Build();

        Assert.Null(provider.GetService<ICanvasSignInState>());
    }

    [Fact]
    public async Task Each_server_seam_resolves_to_its_http_default()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime, Support.NoJs>();
        services.AddBenCanvasEditor();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<HttpCanvasServerStore>(scope.ServiceProvider.GetRequiredService<ICanvasServerStore>());
        Assert.IsType<TieredLinkPreviewProvider>(scope.ServiceProvider.GetRequiredService<ILinkPreviewProvider>());
        Assert.IsType<HttpCanvasMediaStore>(scope.ServiceProvider.GetRequiredService<ICanvasMediaStore>());
    }

    private sealed class HostServerStore : ICanvasServerStore
    {
        public bool IsAvailable => true;
        public Task<(IReadOnlyList<CanvasServerSummary>? Items, string? Problem)> ListAsync(Guid? caseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(CanvasServerDocument? Document, string? Problem)> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(CanvasServerDocument? Document, string? Problem)> GetPublishedAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CanvasSaveResult> SaveAsync(string documentJson, Guid? existingId, int revision, Guid? caseId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(CanvasServerDocument? Document, string? Problem)> PublishAsync(Guid id, byte[] png, string fileName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(bool Ok, string? Problem)> DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public void A_host_registration_made_first_wins()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICanvasServerStore, HostServerStore>();
        services.AddBenCanvasEditor();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<HostServerStore>(scope.ServiceProvider.GetRequiredService<ICanvasServerStore>());
    }

    [Fact]
    public void A_blank_api_leaves_saving_to_the_server_unavailable()
    {
        using var provider = Build(o => CanvasEditorHostDefaults.ApplyServerIntegration(o, "  "));
        using var scope = provider.CreateScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<ICanvasServerStore>().IsAvailable);
    }

    [Fact]
    public void Toasts_are_available()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<BcToastService>());
    }
}
