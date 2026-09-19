using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Video.Tests.Extensions;

/// <summary>
/// Both hosts must configure the editor through <c>VideoEditorHostDefaults</c>, not by hand.
/// </summary>
/// <remarks>
/// <para><c>VideoEditorHostDefaultsTests</c> proves the shared list is complete; this proves the
/// hosts actually use it. Without that, the next person to add a capability can turn it on in one
/// <c>Program.cs</c> and leave the other host dark — exactly how the standalone editor came to be
/// missing nine features (2026-09-05 audit, F2).</para>
///
/// <para>A source scan rather than a runtime check because a <c>Program.cs</c> top-level statement
/// cannot be invoked from a test without starting the host. It reads the two files off disk the way
/// <c>SubPathHostingTests</c> reads the editor's own sources.</para>
/// </remarks>
public sealed class HostsUseTheSharedDefaultsTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    public static TheoryData<string> HostProgramFiles() => new()
    {
        Path.Combine("Ben.Wasm.Video", "Program.cs"),
        Path.Combine("Ben.Web.Website", "Program.cs"),
    };

    [Theory]
    [MemberData(nameof(HostProgramFiles))]
    public void Each_host_applies_the_shared_editing_defaults(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot().FullName, relativePath));

        Assert.Contains("VideoEditorHostDefaults.ApplyEditingDefaults", text, StringComparison.Ordinal);
        Assert.Contains("VideoEditorHostDefaults.ApplyServerIntegration", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// No host sets an editing flag inline. One that did would be invisible to the parity test.
    /// </summary>
    [Theory]
    [MemberData(nameof(HostProgramFiles))]
    public void No_host_sets_an_editing_flag_by_hand(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot().FullName, relativePath));

        string[] editingFlags =
        [
            "MultiTrack", "AudioTracks", "Transitions", "TextOverlays", "VideoEffects",
            "RippleEdit", "ProjectPersistence", "ErrorLog", "BackgroundRendering", "NativeSidecar",
            "MediaLibrary", "MediaLibraryBaseUrl", "AssetCatalogUrl", "DocumentPostUrl",
        ];

        var offenders = editingFlags
            .Where(flag => Regex.IsMatch(text, $@"options\.{flag}\s*="))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{relativePath} configures the editor by hand instead of through VideoEditorHostDefaults, " +
            "so the two hosts can drift again: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The standalone host must not gate the editing set on having a WebApi.
    /// </summary>
    /// <remarks>
    /// Its configure delegate used to <c>return</c> when <c>WebApiBaseUrl</c> was empty, which took
    /// the sidecar and every editing capability with it. <c>ApplyEditingDefaults</c> has to come
    /// before any such early exit; the simplest durable check is that no early return remains.
    /// </remarks>
    [Fact]
    public void The_standalone_host_configures_editing_before_it_looks_for_a_server()
    {
        var path = Path.Combine(RepoRoot().FullName, "Ben.Wasm.Video", "Program.cs");
        var text = File.ReadAllText(path);

        var editing = text.IndexOf("ApplyEditingDefaults", StringComparison.Ordinal);
        var server  = text.IndexOf("ApplyServerIntegration", StringComparison.Ordinal);

        Assert.True(editing >= 0 && server > editing,
            "The standalone host must apply the editing defaults before the server integration.");

        var delegateStart = text.IndexOf("AddBenVideoEditor(options =>", StringComparison.Ordinal);
        var delegateEnd   = text.IndexOf("});", delegateStart, StringComparison.Ordinal);
        var body          = text[delegateStart..delegateEnd];

        Assert.DoesNotContain(" return;", body);
    }
}
