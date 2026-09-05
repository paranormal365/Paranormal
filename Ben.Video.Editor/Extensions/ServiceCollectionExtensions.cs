using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Ben.Video.RenderService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace Ben.Video.Editor.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The name used for the <see cref="System.Net.Http.HttpClient"/> that
    /// <see cref="HttpMediaLibraryProvider"/> uses to call the media-library WebAPI.
    /// The host can configure auth on this client by name:
    /// <code>
    /// builder.Services.AddHttpClient(BenVideoEditor.MediaLibraryHttpClientName)
    ///                 .AddHttpMessageHandler&lt;YourAuthHandler&gt;();
    /// </code>
    /// or via the <c>configureHttpClient</c> parameter of <see cref="AddBenVideoEditor"/>.
    /// </summary>
    public const string MediaLibraryHttpClientName = "BenVideo.MediaLibrary";

    /// <summary>
    /// The name used for the <see cref="System.Net.Http.HttpClient"/> that
    /// <see cref="SharedCatalogAssetProvider"/> uses to call the asset catalog and
    /// watermark-config WebAPI endpoints.
    /// </summary>
    public const string AssetCatalogHttpClientName = "BenVideo.AssetCatalog";

    /// <summary>
    /// The name used for the <see cref="System.Net.Http.HttpClient"/> that
    /// <see cref="ProjectService"/> uses when posting project documents to a WebAPI.
    /// The host can attach auth handlers to this client:
    /// <code>
    /// builder.Services.AddHttpClient(ServiceCollectionExtensions.ProjectPersistenceHttpClientName)
    ///                 .AddHttpMessageHandler&lt;YourAuthHandler&gt;();
    /// </code>
    /// </summary>
    public const string ProjectPersistenceHttpClientName = "BenVideo.ProjectPersistence";

    // NOTE: the named HttpClient the sidecar used to talk through ("BenVideo.NativeSidecar") is
    // gone as of item #70 phase 173 — every sidecar request now goes through SidecarTransport,
    // which issues it from the browser via sidecarInterop.js. See that class for why a C#
    // HttpClient was the wrong transport for a service bound to the *user's* loopback interface.

    /// <summary>
    /// Registers all Ben.Video.Editor services with the host application's DI container.
    ///
    /// Usage — default (single-track, no optional features):
    /// <code>builder.Services.AddBenVideoEditor();</code>
    ///
    /// Usage — enable optional features and wire auth to the media-library client:
    /// <code>
    /// builder.Services.AddBenVideoEditor(
    ///     options =>
    ///     {
    ///         options.MultiTrack           = true;
    ///         options.MediaLibrary         = true;
    ///         options.MediaLibraryBaseUrl  = "https://api.example.com";
    ///     },
    ///     configureHttpClient: b =>
    ///     {
    ///         // Forward the Blazor auth token to the media-library WebAPI.
    ///         // The RCL does not handle auth itself — the WebAPI decides which
    ///         // files to return based on the token the host provides here.
    ///         b.AddHttpMessageHandler&lt;YourBearerTokenHandler&gt;();
    ///     });
    /// </code>
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="configure">
    /// Optional delegate to configure <see cref="VideoEditorOptions"/>.
    /// When null, all features default to disabled.
    /// </param>
    /// <param name="configureHttpClient">
    /// Optional delegate to further configure the named <see cref="System.Net.Http.HttpClient"/>
    /// (<c>"BenVideo.MediaLibrary"</c>) used for media-library API calls.
    /// Use this to attach auth delegating handlers — the editor itself has no knowledge
    /// of authentication; the host supplies credentials and the WebAPI enforces permissions.
    /// </param>
    public static IServiceCollection AddBenVideoEditor(
        this IServiceCollection services,
        Action<VideoEditorOptions>?           configure           = null,
        Action<IHttpClientBuilder>?           configureHttpClient = null)
    {
        services.AddOptions<VideoEditorOptions>()
                .Configure(o => configure?.Invoke(o));

        services.AddScoped<FfmpegService>();
        services.AddScoped<SourceMounter>();
        services.AddScoped<ClipStore>();
        services.AddScoped<ExportService>();
        services.AddScoped<ExportQueueService>();
        services.AddScoped<PlaybackService>();
        services.AddScoped<KeyboardShortcutService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<ProjectStore>();
        services.AddScoped<ErrorLogService>();
        services.AddScoped<LayoutService>();
        services.AddScoped<ExportResolutionService>();
        services.AddScoped<PreviewQualityService>();
        services.AddScoped<PreviewGeometryService>();
        services.AddScoped<PreviewFreshnessService>();
        services.AddScoped<TimelineViewState>();
        services.AddScoped<MotionKeyframeService>();
        services.AddScoped<OPFSService>();
        services.AddBenVideoRenderService();
        services.AddScoped<RenderStatusService>();
        services.AddScoped<PreviewSegmentCache>();
        services.AddScoped<MemFsLedger>();
        services.AddScoped<WorkerWatchdog>();
        services.AddScoped<BlobUrlLifecycle>();

        // Item #36 phase C — background render worker. Registered unconditionally (cheap: the
        // worker's ffmpeg core only loads and the queue loop only starts once something calls
        // BackgroundRenderService.Start(), which VideoEditor only does when
        // VideoEditorOptions.BackgroundRendering is true).
        //
        // Item #38 phase 123 (F) — IRenderBackend now resolves to a FallbackRenderBackend wrapping
        // both concrete backends: NativeSidecarBackend (primary) when a paired sidecar is
        // connected, RenderWorkerBackend (fallback) otherwise. NativeSidecarService.IsConnected
        // only ever becomes true after VideoEditorOptions.NativeSidecar is on AND the user has
        // explicitly paired via the toolbar panel — so with the option off, primaryAvailable()
        // never returns true and every job routes to RenderWorkerBackend exactly as before this
        // phase, unconditional registration and all.
        services.AddScoped<RenderWorkerService>();
        services.AddScoped<RenderWorkerBackend>();
        services.AddScoped<NativeSidecarBackend>();
        services.AddScoped<IRenderBackend>(sp => new Ben.Video.RenderService.FallbackRenderBackend(
            primary: sp.GetRequiredService<NativeSidecarBackend>(),
            fallback: sp.GetRequiredService<RenderWorkerBackend>(),
            primaryAvailable: () => sp.GetRequiredService<NativeSidecarService>().IsConnected));
        services.AddScoped(sp => new Ben.Video.RenderService.BackgroundRenderService(
            sp.GetRequiredService<RenderRegionTracker>(),
            sp.GetRequiredService<IRenderBackend>(),
            () => sp.GetRequiredService<PlaybackService>().State.CurrentTime));

        // Effect registry — singleton; built-in plugins registered via the shared
        // DefaultEffectRegistry (Ben.Video.Core, item #38 phase 123) so the sidecar's own
        // registry (Ben.Video.Sidecar's Program.cs) can never silently drift from the browser's.
        // Add third-party IClipEffect implementations the same way before calling AddBenVideoEditor.
        var registry = DefaultEffectRegistry.CreateDefault();
        services.AddSingleton(registry);

        // Media library — named HttpClient so the host can attach auth handlers.
        // The editor is auth-transparent: it forwards whatever credentials the host
        // provides; the WebAPI decides which files each user may access.
        var httpClientBuilder = services.AddHttpClient(MediaLibraryHttpClientName);
        configureHttpClient?.Invoke(httpClientBuilder);
        services.AddScoped<IMediaLibraryProvider, HttpMediaLibraryProvider>();
        // The same object also answers "what can the library be scoped by" (item 91). Registered
        // as a second service resolving to the one instance rather than as its own class, so a
        // host overriding IMediaLibraryProvider — as the Blazor Server site does — replaces both
        // halves together instead of leaving a scope source pointed at a different API.
        services.AddScoped<IMediaLibraryScopeSource>(sp =>
            (IMediaLibraryScopeSource)sp.GetRequiredService<IMediaLibraryProvider>());

        // Project persistence — separate named HttpClient for POST/PUT to a document API.
        services.AddHttpClient(ProjectPersistenceHttpClientName);

        // The default way to save a project to a server: over HTTP, for a host whose browser
        // carries its own credentials. A Blazor Server host replaces this with one that goes
        // through the client it already authenticates — the previous arrangement had no way to
        // reach the circuit's token, so its Save to Server answered 401 (2026-09-05 audit, F13).
        services.TryAddScoped<IProjectServerStore, HttpProjectServerStore>();

        // Native sidecar (item #38 phase E) — scoped so each editor session gets its own
        // connection/pairing state; registered unconditionally (cheap: nothing probes until
        // VideoEditor sees VideoEditorOptions.NativeSidecar is true).
        // Item #70 phase 173 — the one transport every sidecar request takes. Scoped, and it
        // caches its JS module reference, so the poll loops don't re-import per request.
        services.AddScoped<SidecarTransport>();
        services.AddScoped<NativeSidecarService>();
        // Item #70 phase 159 — the source HEAD/PUT step is now shared by segment, probe and
        // thumbnail jobs, so it lives in its own service rather than inside the segment client.
        services.AddScoped<SidecarSourceUploader>();
        // Item #70 phase 160 — scoped, matching the sidecar connection's own lifetime: the map is
        // only meaningful for one browser session talking to one sidecar instance.
        services.AddScoped<RemoteSegmentIndex>();
        services.AddScoped<PreviewUrlRevoker>();
        services.AddScoped<SidecarPreviewAssembler>();
        services.AddScoped<SidecarExportAssembler>();
        services.AddScoped<SidecarSegmentClient>();
        services.AddScoped<SidecarMediaClient>();
        services.AddScoped<SidecarMediaProbe>();

        // Item #38 phase 124 — per-clip native export offload. Injected into ExportService
        // unconditionally, same reasoning as NativeSidecarBackend above: cheap until a clip
        // actually tries to use it, and inert whenever NativeSidecarService.IsConnected is false.
        services.AddScoped<NativeClipEncoder>();

        // ── Asset catalog (Phase 49) ───────────────────────────────────────────
        // Named HttpClient for shared catalog + watermark API calls.
        // Host can attach auth handlers to BenVideo.AssetCatalog by name.
        services.AddHttpClient(AssetCatalogHttpClientName);
        services.AddScoped<IAssetProvider, LocalOpfsAssetProvider>();
        services.AddScoped<IAssetProvider, AccountLibraryAssetProvider>();
        services.AddScoped<SharedCatalogAssetProvider>();
        services.AddScoped<IAssetProvider>(sp => sp.GetRequiredService<SharedCatalogAssetProvider>());
        services.AddScoped<VideoAssetCatalogService>();

        // ── SVG Frame Renderer (Phase 51) ─────────────────────────────────────
        services.AddScoped<SvgFrameRendererService>();
        services.AddScoped<SvgAnimationExporter>();

        // ── Raster ClipArt animation (Phase 102, item #46) ────────────────────
        services.AddScoped<RasterClipArtAnimationExporter>();

        // ── Watermark (Phase 52) ──────────────────────────────────────────────
        services.AddScoped<WatermarkService>();

        // ── Rich text runs (Phase 115, item #16) ──────────────────────────────
        services.AddScoped<RichTextRunParserService>();

        // ── Google Fonts (Phase 116, item #16) ────────────────────────────────
        services.AddScoped<GoogleFontService>();

        return services;
    }
}

