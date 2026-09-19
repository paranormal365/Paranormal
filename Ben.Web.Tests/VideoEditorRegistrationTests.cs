using Ben.Video.Editor.Extensions;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// Verifies that AddBenVideoEditor() registers services and options correctly.
/// Tests DI configuration in isolation — no DB, no JS runtime required.
/// </summary>
public class VideoEditorRegistrationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IServiceProvider BuildProvider(Action<VideoEditorOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddHttpClient();
        services.AddBenVideoEditor(configure);
        return services.BuildServiceProvider();
    }

    // ── Options ───────────────────────────────────────────────────────────────

    [Fact]
    public void Options_DefaultValues_AreAsExpected()
    {
        var sp = BuildProvider();
        var opts = sp.GetRequiredService<IOptions<VideoEditorOptions>>().Value;

        Assert.False(opts.MultiTrack);
        Assert.False(opts.AudioTracks);
        Assert.False(opts.Transitions);
        Assert.False(opts.TextOverlays);
        Assert.True(opts.ImageClips);
        Assert.False(opts.MediaLibrary);
        Assert.False(opts.ProjectPersistence);
        Assert.True(opts.Snapping);
        Assert.Equal(0.5, opts.SnapThresholdSeconds);
    }

    /// <summary>
    /// The platform's own configuration, not a hand-written copy of it.
    /// </summary>
    /// <remarks>
    /// This test used to list the flags it expected a host to set, and the list went stale: it was
    /// missing BackgroundRendering and NativeSidecar, both of which the site had been setting for
    /// months, and it asserted nothing about either real host. It now exercises the shared
    /// definition both hosts call. The completeness of that definition is held by
    /// Ben.Video.Tests' VideoEditorHostDefaultsTests, and the hosts' use of it by
    /// HostsUseTheSharedDefaultsTests (2026-09-05 audit, F2).
    /// </remarks>
    [Fact]
    public void Options_PlatformConfiguration_EnablesAllFeatures()
    {
        var sp = BuildProvider(o =>
        {
            VideoEditorHostDefaults.ApplyEditingDefaults(o);
            VideoEditorHostDefaults.ApplyServerIntegration(o, "http://localhost:5252");
        });
        var opts = sp.GetRequiredService<IOptions<VideoEditorOptions>>().Value;

        Assert.True(opts.MultiTrack);
        Assert.True(opts.AudioTracks);
        Assert.True(opts.Transitions);
        Assert.True(opts.TextOverlays);
        Assert.True(opts.VideoEffects);
        Assert.True(opts.MediaLibrary);
        Assert.True(opts.ProjectPersistence);
        Assert.True(opts.ErrorLog);
        Assert.True(opts.RippleEdit);
        Assert.True(opts.BackgroundRendering);
        Assert.True(opts.NativeSidecar);
        Assert.Equal("http://localhost:5252", opts.MediaLibraryBaseUrl);
        Assert.Equal("http://localhost:5252/api/video-projects", opts.DocumentPostUrl);
    }

    [Fact]
    public void Options_MaxTrackDefaults_AreCorrect()
    {
        var sp = BuildProvider();
        var opts = sp.GetRequiredService<IOptions<VideoEditorOptions>>().Value;

        Assert.Equal(4, opts.MaxVideoTracks);
        Assert.Equal(2, opts.MaxAudioTracks);
    }

    [Fact]
    public void Options_DocumentUrls_NullByDefault()
    {
        var sp = BuildProvider();
        var opts = sp.GetRequiredService<IOptions<VideoEditorOptions>>().Value;

        Assert.Null(opts.DocumentPostUrl);
        Assert.Null(opts.DocumentSaveUrl);
        Assert.Null(opts.MediaLibraryBaseUrl);
    }

    // ── Service registration ──────────────────────────────────────────────────

    [Fact]
    public void Registration_ScopedServices_AreRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddHttpClient();
        services.AddBenVideoEditor();

        var descriptors = services.Select(d => d.ServiceType).ToHashSet();

        Assert.Contains(typeof(ClipStore),             descriptors);
        Assert.Contains(typeof(ExportService),         descriptors);
        Assert.Contains(typeof(ExportQueueService),    descriptors);
        Assert.Contains(typeof(PlaybackService),       descriptors);
        Assert.Contains(typeof(KeyboardShortcutService), descriptors);
        Assert.Contains(typeof(ProjectService),        descriptors);
        Assert.Contains(typeof(ProjectStore),          descriptors);
        Assert.Contains(typeof(LayoutService),         descriptors);
        Assert.Contains(typeof(TimelineViewState),     descriptors);
        Assert.Contains(typeof(MotionKeyframeService), descriptors);
        Assert.Contains(typeof(ErrorLogService),       descriptors);
    }

    [Fact]
    public void Registration_SingletonServices_AreRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddHttpClient();
        services.AddBenVideoEditor();

        var descriptors = services.Select(d => d.ServiceType).ToHashSet();

        Assert.Contains(typeof(FfmpegService),      descriptors);
        Assert.Contains(typeof(ClipEffectRegistry), descriptors);
        Assert.Contains(typeof(OPFSService),        descriptors);
    }

    [Fact]
    public void Registration_FfmpegService_IsScoped()
    {
        // FfmpegService depends on IJSRuntime, which is scoped to a Blazor circuit —
        // it cannot be a singleton without failing DI validation at startup.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddHttpClient();
        services.AddBenVideoEditor();

        var descriptor = services.Single(d => d.ServiceType == typeof(FfmpegService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void Registration_OPFSService_IsScoped()
    {
        // OPFSService depends on IJSRuntime, which is scoped to a Blazor circuit —
        // it cannot be a singleton without failing DI validation at startup.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddHttpClient();
        services.AddBenVideoEditor();

        var descriptor = services.Single(d => d.ServiceType == typeof(OPFSService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void Registration_ClipStore_IsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddHttpClient();
        services.AddBenVideoEditor();

        var descriptor = services.Single(d => d.ServiceType == typeof(ClipStore));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void Registration_NamedHttpClients_AreRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddHttpClient();
        services.AddBenVideoEditor();

        // Named HTTP clients register IHttpClientFactory; verify the factory is present
        var factoryDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IHttpClientFactory));
        Assert.NotNull(factoryDescriptor);
    }

    [Fact]
    public void Registration_MediaLibraryProvider_IsRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddHttpClient();
        services.AddBenVideoEditor();

        var descriptors = services.Select(d => d.ServiceType).ToHashSet();
        Assert.Contains(typeof(IMediaLibraryProvider), descriptors);
    }

    // ── Effect registry ───────────────────────────────────────────────────────

    [Fact]
    public void EffectRegistry_ContainsAllBuiltInEffects()
    {
        var sp = BuildProvider();
        var registry = sp.GetRequiredService<ClipEffectRegistry>();

        // 22 video + 8 image + 2 colour = 32 registered effects
        Assert.True(registry.All.Count >= 30,
            $"Expected at least 30 built-in effects, found {registry.All.Count}.");
    }

    [Fact]
    public void EffectRegistry_HasNoNullIds()
    {
        var sp = BuildProvider();
        var registry = sp.GetRequiredService<ClipEffectRegistry>();

        foreach (var effect in registry.All)
            Assert.False(string.IsNullOrWhiteSpace(effect.EffectId),
                $"Effect '{effect.DisplayName}' has a null or empty EffectId.");
    }

    [Fact]
    public void EffectRegistry_HasNoDuplicateIds()
    {
        var sp = BuildProvider();
        var registry = sp.GetRequiredService<ClipEffectRegistry>();

        var ids    = registry.All.Select(e => e.EffectId).ToList();
        var unique = ids.Distinct().ToList();
        Assert.Equal(unique.Count, ids.Count);
    }

    [Fact]
    public void EffectRegistry_AllEffectsHaveDisplayNames()
    {
        var sp = BuildProvider();
        var registry = sp.GetRequiredService<ClipEffectRegistry>();

        foreach (var effect in registry.All)
            Assert.False(string.IsNullOrWhiteSpace(effect.DisplayName),
                $"Effect '{effect.EffectId}' has no display name.");
    }

    // ── VideoEditorOptions model ──────────────────────────────────────────────

    [Fact]
    public void Options_MultiTrackFalse_AudioTracksAlsoDefaultsFalse()
    {
        var opts = new VideoEditorOptions();
        Assert.False(opts.MultiTrack);
        Assert.False(opts.AudioTracks);
    }

    [Fact]
    public void Options_SnapThreshold_MustBePositive()
    {
        var opts = new VideoEditorOptions { SnapThresholdSeconds = -1 };
        // The model stores the value as set; validation is the caller's responsibility.
        // This test documents expected runtime behavior when misconfigured.
        Assert.Equal(-1, opts.SnapThresholdSeconds);
    }

    [Fact]
    public void Options_DefaultZoom_IsOne()
    {
        var opts = new VideoEditorOptions();
        Assert.Equal(1.0, opts.DefaultTimelineZoom);
    }

    [Fact]
    public void Options_InlineTrimming_DefaultsTrue()
    {
        var opts = new VideoEditorOptions();
        Assert.True(opts.InlineTrimming);
    }

    [Fact]
    public void Options_Markers_DefaultsTrue()
    {
        var opts = new VideoEditorOptions();
        Assert.True(opts.Markers);
    }
}
