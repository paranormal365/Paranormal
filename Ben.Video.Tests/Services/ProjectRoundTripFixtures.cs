using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;
using Ben.Video.Editor.Effects;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Timeline items with every settable property given a distinctive value.
/// </summary>
/// <remarks>
/// Distinctive on purpose: a property left at its default would round-trip successfully even if
/// nothing carried it, because both sides would land on the same default. Every value here differs
/// from the model's own default, so a dropped field always shows up as a difference.
/// </remarks>
internal static class Fixtures
{
    internal static VideoClip VideoClip() => new()
    {
        Name             = "porch camera",
        TimelinePosition = 3.25,
        Duration         = 41.5,
        Order            = 2,
        LayerIndex       = 0,
        OriginalFileName = "porch.mp4",
        OpfsExt          = ".mp4",
        SourceFileId      = Guid.NewGuid(),
        SourceFileSize    = 48900846,
        SourceContentHash = "a1b2c3",
        SourceBinId      = Guid.NewGuid(),
        LinkedClipId     = Guid.NewGuid(),
        StartTrim        = 1.5,
        EndTrim          = 30.75,
        Speed            = 1.75,
        Width            = 1920,
        Height           = 1080,
        Volume           = 0.6,
        MuteAudio        = true,
        HasAudio         = false,
        VolumeAutomation = [new VolumeKeyframe { Position = 0.25, Volume = 0.4 }],
        Effects          = new ClipEffects { Brightness = 0.2, Contrast = 1.4, Saturation = 0.8 },
        AppliedEffects   = [new AppliedEffect
        {
            EffectId   = "zoom_in",
            Parameters = new Dictionary<string, double> { ["duration"] = 2.5, ["start_zoom"] = 1.8 },
        }],
    };

    internal static AudioClip AudioClip() => new()
    {
        Name             = "basement evp",
        TimelinePosition = 6.5,
        Duration         = 186.0,
        Order            = 1,
        OriginalFileName = "evp.m4a",
        OpfsExt          = ".m4a",
        SourceFileId      = Guid.NewGuid(),
        SourceFileSize    = 7462660,
        SourceContentHash = "d4e5f6",
        SourceBinId      = Guid.NewGuid(),
        LinkedClipId     = Guid.NewGuid(),
        StartTrim        = 12.0,
        EndTrim          = 95.5,
        Volume           = 0.45,
        FadeInSeconds    = 1.25,
        FadeOutSeconds   = 2.5,
        LeftVolume       = 0.8,
        RightVolume      = 0.3,
        VolumeAutomation = [new VolumeKeyframe { Position = 0.5, Volume = 0.9 }],
    };

    internal static ImageClip ImageClip() => new()
    {
        Name             = "site photo",
        TimelinePosition = 11.0,
        Duration         = 7.5,
        Order            = 3,
        OriginalFileName = "site.jpg",
        OpfsExt          = ".jpg",
        SourceFileId      = Guid.NewGuid(),
        SourceFileSize    = 16161,
        SourceContentHash = "091a2b",
        SourceBinId      = Guid.NewGuid(),
        Width            = 1007,
        Height           = 675,
        Effects          = new ClipEffects { Brightness = -0.1, Saturation = 1.6 },
        AppliedEffects   = [new AppliedEffect
        {
            EffectId   = "ken_burns",
            Parameters = new Dictionary<string, double> { ["zoom"] = 1.4, ["direction"] = 2 },
        }],
    };

    internal static CalloutClip Callout() => new()
    {
        Name              = "here",
        TimelinePosition  = 4.0,
        Duration          = 6.5,
        Order             = 4,
        LayerIndex        = 2,
        Shape             = ShapeType.Arrow,
        X                 = 0.35,
        Y                 = 0.42,
        Width             = 0.28,
        Height            = 0.19,
        Rotation          = 22.5,
        FillColor         = ColorHelper.Pack(12, 200, 90, 210),
        StrokeColor       = ColorHelper.Pack(255, 30, 30, 255),
        StrokeWidth       = 5.5,
        Opacity           = 0.72,
        ShadowColor       = ColorHelper.Pack(10, 20, 30, 90),
        ShadowOffsetX     = 6.0,
        ShadowOffsetY     = 7.0,
        ShadowBlur        = 9.0,
        Text              = "the door moved",
        FontFamily        = "Georgia",
        FontSize          = 41,
        FontColor         = ColorHelper.Pack(9, 9, 9, 255),
        FontBold          = true,
        FontUnderline     = true,
        Runs              = [new TextRun { Text = "the door", Bold = true, Color = "#ff0000" }],
        TextAlign         = TextHorizontalAlign.Right,
        TextVerticalAlign = TextVerticalAlign.Top,
        TextWrap          = true,
        TextShadow        = true,
        TextPadding       = 17.5,
        FadeInSeconds     = 0.75,
        FadeOutSeconds    = 1.25,
        OpfsAssetName     = "custom-arrow.svg",
        OpfsExt           = ".svg",
        ControlPointValues = new Dictionary<string, double> { ["bend"] = 0.6, ["headSize"] = 0.3 },
    };

    internal static TextOverlay TextOverlay() => new()
    {
        Name             = "title",
        TimelinePosition = 1.5,
        Duration         = 4.25,
        Order            = 5,
        LayerIndex       = 3,
        Text             = "Basement, 02:14",
        FontFamily       = "Courier New",
        FontSize         = 63,
        FontColor        = "#12ab34",
        FontBold         = true,
        FontUnderline    = true,
        Runs             = [new TextRun { Text = "Basement", Underline = true }],
        BoxColor         = "#101820@0.65",
        HorizontalAlign  = TextHorizontalAlign.Left,
        VerticalAlign    = TextVerticalAlign.Top,
        OffsetX          = 27,
        OffsetY          = 91,
        OverrideX        = 0.31,
        OverrideY        = 0.77,
        FadeInSeconds    = 0.9,
        FadeOutSeconds   = 1.1,
        Opacity          = 0.83,
        MaxWidth          = 0.6,
        ShadowColor      = ColorHelper.Pack(40, 50, 60, 130),
        ShadowOffsetX    = 8.0,
        ShadowOffsetY    = 2.0,
        ShadowBlur       = 11.0,
    };

    internal static ClipArtClip ClipArt() => new()
    {
        Name             = "arrow art",
        TimelinePosition = 8.5,
        Duration         = 3.75,
        Order            = 6,
        LayerIndex       = 4,
        AssetId          = "4d2f6b1e-1111-2222-3333-444455556666",
        AssetSource      = AssetSource.AccountLibrary,
        AssetFormat      = VideoAssetFormat.Svg,
        X                = 0.44,
        Y                = 0.21,
        Width            = 0.33,
        Height           = 0.27,
        Rotation         = 45.5,
        Opacity          = 0.66,
        TintColor        = ColorHelper.Pack(200, 100, 50, 255),
        ControlPointValues = new Dictionary<string, double> { ["thickness"] = 0.8 },
        ControlPointColors = new Dictionary<string, string> { ["body"] = "#00ff00" },
    };

    internal static Transition Transition() => new()
    {
        Name             = "dissolve",
        TimelinePosition = 41.0,
        Duration         = 1.35,
        Order            = 7,
        Style            = TransitionStyle.CircleOpen,
        FromClipId       = Guid.NewGuid(),
        ToClipId         = Guid.NewGuid(),
    };

    internal static MotionKeyframe Keyframe(double time) => new()
    {
        Time          = time,
        X             = 0.28,
        Y             = 0.72,
        Scale         = 1.6,
        ScaleX        = 2.1,
        ScaleY        = 0.7,
        Rotation      = 33.5,
        Alpha         = 0.55,
        Easing        = "EaseInOut",
        HandleOutX    = 0.11,
        HandleOutY    = 0.22,
        HandleInX     = 0.33,
        HandleInY     = 0.44,
        FillColor     = ColorHelper.Pack(1, 2, 3, 4),
        StrokeColor   = ColorHelper.Pack(5, 6, 7, 8),
        ControlPointValues = new Dictionary<string, double> { ["bend"] = 0.9 },
        ShadowColor   = ColorHelper.Pack(9, 8, 7, 6),
        ShadowOffsetX = 12.5,
        ShadowOffsetY = 13.5,
        ShadowBlur    = 14.5,
    };
}
