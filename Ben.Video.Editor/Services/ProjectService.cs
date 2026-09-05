using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Video.Editor.Extensions;
using Ben.Video.Editor.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that handles saving and loading <c>.benvideo</c> project files.
///
/// <para><b>Save</b> — serializes the current <see cref="ClipStore"/> state to JSON and
/// triggers a browser file download.  Media files are <em>not</em> embedded; they remain
/// in ffmpeg MEMFS and are simply noted by name so the user can re-link them on the next
/// open.</para>
///
/// <para><b>Load</b> — deserializes a previously saved project file and calls
/// <see cref="RestoreAsync"/> to rebuild the <see cref="ClipStore"/>.  All clips are
/// flagged <see cref="TrackItem.IsMediaMissing"/> = <c>true</c> until the user re-imports
/// the corresponding source files through <c>ClipBrowser</c>.</para>
/// </summary>
public sealed class ProjectService
{
    private readonly ClipStore               _clips;
    private readonly MotionKeyframeService   _motion;
    private readonly IJSRuntime              _js;
    private readonly IHttpClientFactory      _httpClientFactory;
    private readonly VideoEditorOptions      _options;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented          = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters             = { new JsonStringEnumConverter() },
    };

    private IJSObjectReference? _module;

    public ProjectService(ClipStore clips, MotionKeyframeService motion, IJSRuntime js,
        IHttpClientFactory httpClientFactory, IOptions<VideoEditorOptions> options)
    {
        _clips             = clips;
        _motion            = motion;
        _js                = js;
        _httpClientFactory = httpClientFactory;
        _options           = options.Value;
    }

    // ── JS module ─────────────────────────────────────────────────────────────

    private async ValueTask<IJSObjectReference> GetModuleAsync()
    {
        _module ??= await _js.InvokeAsync<IJSObjectReference>(
            "benImportEditorModule", "js/ffmpegInterop.js");
        return _module;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialize the current editor state and download it as a <c>.benvideo</c> file.
    /// </summary>
    /// <param name="projectName">Suggested filename (without extension).</param>
    public async Task SaveAsync(string projectName = "project")
    {
        var file    = BuildProjectFile(projectName);
        var json    = JsonSerializer.Serialize(file, _jsonOptions);
        var bytes   = Encoding.UTF8.GetBytes(json);
        var module  = await GetModuleAsync();
        var safeName = SanitiseName(projectName) + ".benvideo";
        await module.InvokeVoidAsync("downloadBytes", bytes, safeName, "application/json");
    }

    /// <summary>
    /// Serialize the current editor state and HTTP POST it to the configured
    /// <see cref="VideoEditorOptions.DocumentPostUrl"/> (or the supplied override URL).
    /// </summary>
    /// <param name="projectName">Logical project name embedded in the JSON payload.</param>
    /// <param name="urlOverride">
    /// Optional URL override. When null, <see cref="VideoEditorOptions.DocumentPostUrl"/> is used.
    /// </param>
    /// <returns>
    /// The <see cref="HttpResponseMessage"/> returned by the server.
    /// Throws <see cref="InvalidOperationException"/> when no URL is configured.
    /// </returns>
    /// <param name="progress">
    /// Optional upload-progress sink. Receives <see cref="TransferProgress"/> snapshots
    /// as bytes are written to the HTTP stream.
    /// </param>
    public async Task<HttpResponseMessage> SaveToServerAsync(
        string projectName = "project", string? urlOverride = null,
        IProgress<TransferProgress>? progress = null)
    {
        var url = urlOverride ?? _options.DocumentPostUrl
            ?? throw new InvalidOperationException(
                "DocumentPostUrl is not configured. Set VideoEditorOptions.DocumentPostUrl " +
                "via AddBenVideoEditor(options => options.DocumentPostUrl = \"https://...\") " +
                "or supply a urlOverride.");

        var file    = BuildProjectFile(projectName);
        var json    = JsonSerializer.SerializeToUtf8Bytes(file, _jsonOptions);
        var client  = _httpClientFactory.CreateClient(
            ServiceCollectionExtensions.ProjectPersistenceHttpClientName);

        HttpContent content = progress is null
            ? new ByteArrayContent(json)
            : new ProgressContent(new MemoryStream(json), json.Length, progress);
        content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        return await client.PostAsync(url, content);
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Read a <c>.benvideo</c> file from a hidden <c>&lt;input type="file"&gt;</c> element
    /// and return the deserialized <see cref="ProjectFile"/>.
    /// Returns <c>null</c> if the input is empty or the JSON is invalid.
    /// </summary>
    /// <param name="fileInputElement">
    /// An <see cref="ElementReference"/> to the hidden <c>&lt;input type="file"&gt;</c>.
    /// </param>
    public async Task<ProjectFile?> LoadAsync(ElementReference fileInputElement)
    {
        try
        {
            var module = await GetModuleAsync();
            var json   = await module.InvokeAsync<string>("readInputFileAsText", fileInputElement);
            return JsonSerializer.Deserialize<ProjectFile>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// HTTP GET a <c>.benvideo</c> project JSON from a server URL and return the
    /// deserialized <see cref="ProjectFile"/>.
    /// Returns <c>null</c> if the request fails or the response body is not valid JSON.
    /// </summary>
    /// <param name="urlOverride">
    /// URL to GET the project from. When null, <see cref="VideoEditorOptions.DocumentSaveUrl"/>
    /// is used. Throws <see cref="InvalidOperationException"/> when neither is configured.
    /// </param>
    /// <param name="progress">
    /// Optional download-progress sink. Receives <see cref="TransferProgress"/> snapshots
    /// as response bytes arrive. <see cref="TransferProgress.TotalBytes"/> is <c>-1</c>
    /// when the server does not supply a <c>Content-Length</c> header.
    /// </param>
    public async Task<ProjectFile?> LoadFromServerAsync(string? urlOverride = null,
        IProgress<TransferProgress>? progress = null)
    {
        var url = urlOverride ?? _options.DocumentSaveUrl
            ?? throw new InvalidOperationException(
                "DocumentSaveUrl is not configured. Set VideoEditorOptions.DocumentSaveUrl " +
                "via AddBenVideoEditor(options => options.DocumentSaveUrl = \"https://...\") " +
                "or supply a urlOverride.");

        try
        {
            var client = _httpClientFactory.CreateClient(
                ServiceCollectionExtensions.ProjectPersistenceHttpClientName);

            if (progress is null)
                return await client.GetFromJsonAsync<ProjectFile>(url, _jsonOptions);

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var buffer       = new MemoryStream();

            var chunk      = new byte[81_920];
            long received  = 0;
            int  read;

            while ((read = await stream.ReadAsync(chunk)) > 0)
            {
                await buffer.WriteAsync(chunk.AsMemory(0, read));
                received += read;
                progress.Report(new TransferProgress { Bytes = received, TotalBytes = totalBytes });
            }

            buffer.Position = 0;
            return await JsonSerializer.DeserializeAsync<ProjectFile>(buffer, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    // ── Restore ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Restore the <see cref="ClipStore"/> from a loaded <see cref="ProjectFile"/>.
    /// All clips are marked <see cref="TrackItem.IsMediaMissing"/> = <c>true</c>;
    /// the caller is responsible for triggering a UI refresh and prompting the user
    /// to re-link media files.
    /// </summary>
    public void RestoreAsync(ProjectFile file)
    {
        _clips.ReplaceFromProject(file);
        // Restore motion paths — maps ProjectMotionPath → MotionPath
        var paths = file.MotionPaths.Select(p => new MotionPath
        {
            Id        = p.Id,
            LayerId   = p.LayerId,
            LayerType = p.LayerType,
            Keyframes = p.Keyframes.Select(k => new MotionKeyframe
            {
                Time       = k.Time,
                X          = k.X,
                Y          = k.Y,
                Scale      = k.Scale,
                Alpha      = k.Alpha,
                Easing     = k.Easing,
                HandleOutX = k.HandleOutX,
                HandleOutY = k.HandleOutY,
                HandleInX  = k.HandleInX,
                HandleInY  = k.HandleInY,
                FillColor           = k.FillColor,
                StrokeColor         = k.StrokeColor,
                ControlPointValues  = new Dictionary<string, double>(k.ControlPointValues),
                ShadowColor         = k.ShadowColor,
                ShadowOffsetX       = k.ShadowOffsetX,
                ShadowOffsetY       = k.ShadowOffsetY,
                ShadowBlur          = k.ShadowBlur,
            }).OrderBy(k => k.Time).ToList()
        });
        _motion.RestoreAll(paths);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds and returns the serializable project file for the current editor state.
    /// Exposed internally so <see cref="ProjectStore"/> can serialise without a download.
    /// </summary>
    public ProjectFile BuildCurrentProjectFile(string projectName)
        => BuildProjectFile(projectName);

    private ProjectFile BuildProjectFile(string projectName) => new()
    {
        ProjectName = projectName,
        SavedAt     = DateTime.UtcNow,
        Options     = BuildOptionsSnapshot(),
        Tracks      = _clips.Tracks.Select(MapTrack).ToList(),
        Markers     = _clips.Markers.ToList(),
        MotionPaths = _motion.AllPaths.Select(MapMotionPath).ToList(),
        Bin         = MapBin(),
    };

    /// <summary>
    /// The media bin, so what you imported survives a save whether or not you placed it.
    /// </summary>
    private ProjectMediaBin MapBin() => new()
    {
        VideoClips = _clips.BinVideoClips.Select(MapVideoClip).ToList(),
        AudioClips = _clips.BinAudioClips.Select(MapAudioClip).ToList(),
        ImageClips = _clips.BinImageClips.Select(MapImageClip).ToList(),
    };

    private ProjectOptionsSnapshot BuildOptionsSnapshot()
    {
        // Snapshot derived from the live track structure (source of truth at runtime)
        var hasAudioTracks  = _clips.Tracks.Any(t => t.Type == TrackType.Audio);
        var hasTransitions  = _clips.Tracks.Any(t => t.Items.OfType<Transition>().Any());
        var hasTextOverlays = _clips.Tracks.Any(t => t.Items.OfType<TextOverlay>().Any());
        var hasEffects      = _clips.Tracks
            .SelectMany(t => t.Items.OfType<VideoClip>())
            .Any(c => !c.Effects.IsNeutral);

        return new ProjectOptionsSnapshot
        {
            MultiTrack    = _clips.Tracks.Count(t => t.Type == TrackType.Video) > 1,
            AudioTracks   = hasAudioTracks,
            Transitions   = hasTransitions,
            TextOverlays  = hasTextOverlays,
            VideoEffects  = hasEffects,
            Markers       = _clips.Markers.Count > 0,
        };
    }

    private static ProjectTrack MapTrack(TimelineTrack t) => new()
    {
        Id       = t.Id,
        Label    = t.Label,
        Type     = t.Type,
        Order    = t.Order,
        IsMuted  = t.IsMuted,
        IsLocked = t.IsLocked,

        VideoClips   = t.VideoClips.Select(MapVideoClip).ToList(),
        AudioClips   = t.AudioClips.Select(MapAudioClip).ToList(),
        Transitions  = t.Transitions.Select(MapTransition).ToList(),
        TextOverlays = t.TextOverlays.Select(MapTextOverlay).ToList(),
        ImageClips   = t.ImageClips.Select(MapImageClip).ToList(),
        CalloutClips = t.CalloutClips.Select(MapCalloutClip).ToList(),
        ClipArtClips = t.ClipArtClips.Select(MapClipArtClip).ToList(),
    };

    private static ProjectVideoClip MapVideoClip(VideoClip c) => new()
    {
        Id               = c.Id,
        SourceBinId      = c.SourceBinId,
        Name             = c.Name,
        TimelinePosition = c.TimelinePosition,
        Duration         = c.Duration,
        Order            = c.Order,
        StartTrim        = c.StartTrim,
        EndTrim          = c.EndTrim,
        Speed            = c.Speed,
        Width            = c.Width,
        Height           = c.Height,
        Volume           = c.Volume,
        VolumeAutomation = c.VolumeAutomation.ToList(),
        Effects          = c.Effects,
        AppliedEffects   = c.AppliedEffects.Select(e => new ProjectAppliedEffect
        {
            EffectId   = e.EffectId,
            Parameters = new Dictionary<string, double>(e.Parameters),
        }).ToList(),
        IsMediaMissing   = false,           // session clip — media is present
        OriginalFileName = c.MemFsName,     // hint for re-linking on next open
        OpfsExt          = c.OpfsExt,       // OPFS extension for auto-restore
    };

    private static ProjectAudioClip MapAudioClip(AudioClip c) => new()
    {
        Id               = c.Id,
        SourceBinId      = c.SourceBinId,
        Name             = c.Name,
        TimelinePosition = c.TimelinePosition,
        Duration         = c.Duration,
        Order            = c.Order,
        StartTrim        = c.StartTrim,
        EndTrim          = c.EndTrim,
        Volume           = c.Volume,
        FadeInSeconds    = c.FadeInSeconds,
        FadeOutSeconds   = c.FadeOutSeconds,
        VolumeAutomation = c.VolumeAutomation.ToList(),
        LeftVolume       = c.LeftVolume,
        RightVolume      = c.RightVolume,
        IsMediaMissing   = false,
        OriginalFileName = c.MemFsName,
        OpfsExt          = c.OpfsExt,
    };

    private static ProjectTransition MapTransition(Transition t) => new()
    {
        Id               = t.Id,
        Name             = t.Name,
        TimelinePosition = t.TimelinePosition,
        Duration         = t.Duration,
        Order            = t.Order,
        Style            = t.Style,
        FromClipId       = t.FromClipId,
        ToClipId         = t.ToClipId,
    };

    private static ProjectTextOverlay MapTextOverlay(TextOverlay o) => new()
    {
        Id               = o.Id,
        Name             = o.Name,
        TimelinePosition = o.TimelinePosition,
        Duration         = o.Duration,
        Order            = o.Order,
        LayerIndex       = o.LayerIndex,
        Text             = o.Text,
        FontFamily       = o.FontFamily,
        FontSize         = o.FontSize,
        FontColor        = o.FontColor,
        FontBold         = o.FontBold,
        FontUnderline    = o.FontUnderline,
        Runs             = o.Runs?.Select(MapTextRun).ToList(),
        HasBackground    = o.BoxColor != null,
        BackgroundColor  = o.BoxColor?.Split('@')[0] ?? "#000000",
        BackgroundOpacity = ParseBoxOpacity(o.BoxColor),
        HorizontalAlign  = o.HorizontalAlign.ToString().ToLowerInvariant(),
        VerticalAlign    = o.VerticalAlign.ToString().ToLowerInvariant(),
        OffsetX          = o.OffsetX,
        OffsetY          = o.OffsetY,
        OverrideX        = o.OverrideX,
        OverrideY        = o.OverrideY,
        FadeInSeconds    = o.FadeInSeconds,
        FadeOutSeconds   = o.FadeOutSeconds,
        Opacity          = o.Opacity,
        ShadowColor      = o.ShadowColor,
        ShadowOffsetX    = o.ShadowOffsetX,
        ShadowOffsetY    = o.ShadowOffsetY,
        ShadowBlur       = o.ShadowBlur,
    };

    private static ProjectImageClip MapImageClip(ImageClip c) => new()
    {
        Id               = c.Id,
        SourceBinId      = c.SourceBinId,
        Name             = c.Name,
        TimelinePosition = c.TimelinePosition,
        Duration         = c.Duration,
        Order            = c.Order,
        Width            = c.Width,
        Height           = c.Height,
        Effects          = c.Effects,
        AppliedEffects   = c.AppliedEffects.Select(e => new ProjectAppliedEffect
        {
            EffectId   = e.EffectId,
            Parameters = new Dictionary<string, double>(e.Parameters),
        }).ToList(),
        IsMediaMissing   = false,
        OriginalFileName = c.MemFsName,
        OpfsExt          = c.OpfsExt,
    };

    private static ProjectCalloutClip MapCalloutClip(CalloutClip c) => new()
    {
        Id               = c.Id,
        Name             = c.Name,
        TimelinePosition = c.TimelinePosition,
        Duration         = c.Duration,
        Order            = c.Order,
        LayerIndex       = c.LayerIndex,
        Shape            = c.Shape,
        X                = c.X,
        Y                = c.Y,
        Width            = c.Width,
        Height           = c.Height,
        Rotation         = c.Rotation,
        FillColor        = c.FillColor,
        StrokeColor      = c.StrokeColor,
        StrokeWidth      = c.StrokeWidth,
        Opacity          = c.Opacity,
        ShadowColor      = c.ShadowColor,
        ShadowOffsetX    = c.ShadowOffsetX,
        ShadowOffsetY    = c.ShadowOffsetY,
        ShadowBlur       = c.ShadowBlur,
        Text             = c.Text,
        FontFamily       = c.FontFamily,
        FontSize         = c.FontSize,
        FontColor        = c.FontColor,
        FontBold         = c.FontBold,
        FontUnderline    = c.FontUnderline,
        Runs             = c.Runs?.Select(MapTextRun).ToList(),
        FadeInSeconds    = c.FadeInSeconds,
        FadeOutSeconds   = c.FadeOutSeconds,
        OpfsAssetName    = c.OpfsAssetName,
        ControlPointValues = new Dictionary<string, double>(c.ControlPointValues),
    };

    private static ProjectTextRun MapTextRun(TextRun r) => new()
    {
        Text        = r.Text,
        Bold        = r.Bold,
        Underline   = r.Underline,
        Subscript   = r.Subscript,
        Superscript = r.Superscript,
        Color       = r.Color,
    };

    private static ProjectClipArtClip MapClipArtClip(ClipArtClip c) => new()
    {
        Id               = c.Id,
        Name             = c.Name,
        TimelinePosition = c.TimelinePosition,
        Duration         = c.Duration,
        Order            = c.Order,
        LayerIndex       = c.LayerIndex,
        AssetId          = c.AssetId,
        AssetSource      = c.AssetSource,
        AssetFormat      = c.AssetFormat,
        X                = c.X,
        Y                = c.Y,
        Width            = c.Width,
        Height           = c.Height,
        Rotation         = c.Rotation,
        Opacity          = c.Opacity,
        TintColor        = c.TintColor,
        ControlPointValues = new Dictionary<string, double>(c.ControlPointValues),
        ControlPointColors = new Dictionary<string, string>(c.ControlPointColors),
        SettingsAllowRecolor       = c.Settings.AllowRecolor,
        SettingsAllowResize        = c.Settings.AllowResize,
        SettingsAllowOpacity       = c.Settings.AllowOpacity,
        SettingsAllowRotation      = c.Settings.AllowRotation,
        SettingsAllowEffects       = c.Settings.AllowEffects,
        SettingsAllowEasing        = c.Settings.AllowEasing,
        SettingsAllowMotion        = c.Settings.AllowMotion,
        SettingsAllowControlPoints = c.Settings.AllowControlPoints,
    };

    private static double ParseBoxOpacity(string? boxColor)
    {
        if (boxColor == null) return 0.5;
        var parts = boxColor.Split('@');
        return parts.Length == 2 && double.TryParse(parts[1],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var v) ? v : 0.5;
    }

    private static ProjectMotionPath MapMotionPath(MotionPath p) => new()
    {
        Id        = p.Id,
        LayerId   = p.LayerId,
        LayerType = p.LayerType,
        Keyframes = p.Keyframes.Select(k => new ProjectKeyframe
        {
            Time       = k.Time,
            X          = k.X,
            Y          = k.Y,
            Scale      = k.Scale,
            Alpha      = k.Alpha,
            Easing     = k.Easing,
            HandleOutX = k.HandleOutX,
            HandleOutY = k.HandleOutY,
            HandleInX  = k.HandleInX,
            HandleInY  = k.HandleInY,
            FillColor           = k.FillColor,
            StrokeColor         = k.StrokeColor,
            ControlPointValues  = new Dictionary<string, double>(k.ControlPointValues),
            ShadowColor         = k.ShadowColor,
            ShadowOffsetX       = k.ShadowOffsetX,
            ShadowOffsetY       = k.ShadowOffsetY,
            ShadowBlur          = k.ShadowBlur,
        }).ToList(),
    };

    private static string SanitiseName(string name)
        => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));

    // ── ProgressContent ─────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="HttpContent"/> wrapper that forwards bytes written to the
    /// underlying stream to an <see cref="IProgress{T}"/> sink, enabling
    /// upload-progress reporting.
    /// </summary>
    private sealed class ProgressContent : HttpContent
    {
        private readonly Stream                      _payload;
        private readonly long                        _totalBytes;
        private readonly IProgress<TransferProgress> _progress;

        internal ProgressContent(Stream payload, long totalBytes,
            IProgress<TransferProgress> progress)
        {
            _payload    = payload;
            _totalBytes = totalBytes;
            _progress   = progress;
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context)
        {
            var buffer = new byte[81_920];
            long sent  = 0;
            int  read;

            while ((read = await _payload.ReadAsync(buffer)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read));
                sent += read;
                _progress.Report(new TransferProgress { Bytes = sent, TotalBytes = _totalBytes });
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _totalBytes;
            return _totalBytes >= 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _payload.Dispose();
            base.Dispose(disposing);
        }
    }
}
