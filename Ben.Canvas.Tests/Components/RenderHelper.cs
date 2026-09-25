using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Commands;
using Ben.Canvas.Editor.Extensions;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Ben.Canvas.Tests.Components;

/// <summary>
/// Renders a component to HTML through the framework's own <see cref="HtmlRenderer"/>.
/// </summary>
/// <remarks>
/// The shape of Ben.Web.Tests/Website/BenPlanDesignerTests.cs: what is worth pinning at this level is
/// which markup exists in which state. Interop throws, because a component that calls JavaScript while
/// rendering would fail the same way in a static prerender.
/// </remarks>
public static class RenderHelper
{
    internal sealed class UnusedJs : IJSRuntime
    {
        public const string Message = "The canvas must not call JavaScript while rendering.";

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException(Message);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => throw new InvalidOperationException(Message);
    }

    public static async Task<string> RenderAsync<TComponent>(
        IDictionary<string, object?>? parameters = null,
        Action<IServiceCollection>? configure = null)
        where TComponent : IComponent
    {
        await using var session = await RenderSession.StartAsync(configure);
        return await session.RenderAsync<TComponent>(parameters);
    }

    /// <summary>Renders with a board already loaded into the store.</summary>
    public static async Task<string> RenderWithBoardAsync<TComponent>(
        CanvasDocument document,
        IDictionary<string, object?>? parameters = null,
        Action<IServiceProvider>? arrange = null)
        where TComponent : IComponent
    {
        await using var session = await RenderSession.StartAsync();
        session.Services.GetRequiredService<CanvasStore>().Load(document);
        arrange?.Invoke(session.Services);
        return await session.RenderAsync<TComponent>(parameters);
    }
}

/// <summary>A renderer and its services that live for one test, so a test can render, act and render again.</summary>
public sealed class RenderSession : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly HtmlRenderer _renderer;
    private readonly AsyncServiceScope _scope;

    private RenderSession(ServiceProvider provider)
    {
        _provider = provider;
        _scope = provider.CreateAsyncScope();
        _renderer = new HtmlRenderer(_scope.ServiceProvider, NullLoggerFactory.Instance);
    }

    public IServiceProvider Services => _scope.ServiceProvider;

    public static Task<RenderSession> StartAsync(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime, RenderHelper.UnusedJs>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();
        services.AddBenCanvasEditor();
        services.AddSingleton<NavigationManager>(new FakeNavigation("https://canvas.test/"));
        services.AddSingleton<HarnessRegistry>();
        configure?.Invoke(services);
        return Task.FromResult(new RenderSession(services.BuildServiceProvider()));
    }

    public async Task<string> RenderAsync<TComponent>(IDictionary<string, object?>? parameters = null) where TComponent : IComponent =>
        await _renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await _renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>()));
            _last = output;
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });

    /// <summary>Renders without decoding entities, for tests about what is markup and what is text.</summary>
    public async Task<string> RenderRawAsync<TComponent>(IDictionary<string, object?>? parameters = null) where TComponent : IComponent =>
        await _renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await _renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>()));
            _last = output;
            return output.ToHtmlString();
        });

    private HtmlRootComponent? _last;

    /// <summary>The last rendered component's current markup, after any actions.</summary>
    public Task<string> HtmlAsync() =>
        _renderer.Dispatcher.InvokeAsync(() => System.Net.WebUtility.HtmlDecode(_last!.Value.ToHtmlString()));

    /// <summary>Runs on the renderer's dispatcher, where components may call StateHasChanged.</summary>
    public Task InvokeAsync(Func<Task> work) => _renderer.Dispatcher.InvokeAsync(work);

    public async ValueTask DisposeAsync()
    {
        await _renderer.DisposeAsync();
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }
}

/// <summary>Where a test harness component leaves the component it wraps, so a test can drive it.</summary>
public sealed class HarnessRegistry
{
    public object? Instance { get; set; }
}
