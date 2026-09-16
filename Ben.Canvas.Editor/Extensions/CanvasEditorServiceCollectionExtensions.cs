using Ben.Canvas.Core.Options;
using Ben.Canvas.Editor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ben.Canvas.Editor.Extensions;

/// <summary>
/// Registers the canvas editor with a host's services.
/// </summary>
public static class CanvasEditorServiceCollectionExtensions
{
    /// <summary>
    /// The HttpClient for everything that needs the signed-in person: boards, publishing, media and
    /// link previews. The host attaches its bearer handler to this name; the editor never does.
    /// </summary>
    public const string PersistenceHttpClientName = "BenCanvas.Persistence";

    /// <summary>
    /// The HttpClient for anonymous calls (our-records link previews, site features). Hosts leave it
    /// undecorated, so a token is never sent where none is needed.
    /// </summary>
    public const string PublicHttpClientName = "BenCanvas.Public";

    /// <summary>
    /// Adds the canvas editor.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configure">Sets <see cref="CanvasEditorOptions"/>; see <c>CanvasEditorHostDefaults</c>.</param>
    /// <param name="configureHttpClient">
    /// Decorates the persistence client, typically with the host's bearer handler. The editor is
    /// auth-transparent: it forwards whatever the host supplies and the API decides.
    /// </param>
    /// <remarks>
    /// <para>No <see cref="ICanvasSignInState"/> is registered here. It is optional, and a default
    /// would hide a host that forgot to supply one.</para>
    ///
    /// <para>Later milestones append to this method: board and gesture services (M2), document and
    /// asset stores and paste (M4), and TryAddScoped defaults for ICanvasServerStore,
    /// ILinkPreviewProvider, IMapConfig and ICanvasMediaStore (M6) - TryAdd, so a host's own
    /// registration made first wins.</para>
    /// </remarks>
    public static IServiceCollection AddBenCanvasEditor(
        this IServiceCollection services,
        Action<CanvasEditorOptions>? configure = null,
        Action<IHttpClientBuilder>? configureHttpClient = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<CanvasEditorOptions>()
                .Configure(o => configure?.Invoke(o));

        var persistence = services.AddHttpClient(PersistenceHttpClientName);
        configureHttpClient?.Invoke(persistence);

        services.AddHttpClient(PublicHttpClientName);

        services.AddScoped<BcToastService>();

        // M2: the board.
        services.TryAddScoped(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CanvasEditorOptions>>().Value;
            return new Ben.Canvas.Core.Commands.CanvasStore { HistoryDepth = options.HistoryDepth, MaxNodes = options.MaxNodes };
        });
        services.TryAddScoped<SelectionState>();
        services.TryAddScoped<CanvasViewportState>();
        services.TryAddScoped<AnnouncerService>();
        services.TryAddScoped<BoardGestureBridge>();
        services.TryAddScoped<KeyboardShortcutService>();

        // M4: the device store, paste and files.
        services.TryAddScoped<CanvasAssetStore>();
        services.TryAddScoped<CanvasDocumentStore>();
        services.TryAddScoped<UnloadGuardService>();
        services.TryAddScoped<PasteService>();
        services.TryAddScoped<CanvasPackageService>();

        // M5: phone and iPad layout.
        services.TryAddScoped<CanvasLayoutState>();

        // M6: the server. Each answers "not available" without an API, so a local-only host needs nothing more.
        services.TryAddScoped<ICanvasServerStore, HttpCanvasServerStore>();
        services.TryAddScoped<ILinkPreviewProvider, TieredLinkPreviewProvider>();
        services.TryAddScoped<ICanvasMediaStore, HttpCanvasMediaStore>();
        services.TryAddScoped<BoardAccess>();
        services.TryAddScoped<CanvasServerSession>();
        services.TryAddScoped<LinkPreviewResolver>();
        services.TryAddScoped<BoardSnapshotService>();

        return services;
    }
}
