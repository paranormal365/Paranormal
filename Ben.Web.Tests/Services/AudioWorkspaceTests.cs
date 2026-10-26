using Ben.Web.Website.Library.Manage.Audio;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Remembering the rest of how the editor was left: zoom, speed, the voice band, the drag mode,
/// silence detection, the open tab, and the spectrogram's Display-tab dials (phase 3, 10/26/2026).
/// </summary>
/// <remarks>
/// It shares EditStateJson with the listening chain, under "workspace". The traps are the column's
/// other tenant and the rows that already exist: a save must not lose the chain, the chain must still
/// read back from a row that now has a workspace in it, and a row from before must open as it did.
/// </remarks>
public sealed class AudioWorkspaceTests
{
    private static readonly AudioWorkspace Customised = new()
    {
        ZoomPxPerSec  = 240,
        Speed         = 0.75,
        VoiceBand     = true,
        DragScrub     = true,
        DetectSilence = true,
        InspectorOpen = false,
        InspectorTab  = "display",
        Analysis      = new SpectrogramAnalysis("bark", 100, 4000, "blackman", 32, 60, 6, true),
        Minimap       = true,
        SpectrogramHeight = 300,
    };

    [Fact]
    public void A_row_saved_before_the_workspace_existed_opens_with_the_defaults()
    {
        var oldRow = AudioListeningChain.Default with { HighPassOn = true, HighPassHz = 120 };

        var workspace = AudioWorkspace.FromEditState(oldRow.ToJson());

        Assert.Equal(AudioWorkspace.Default, workspace);
    }

    [Fact]
    public void Everything_comes_back_as_it_was_saved()
    {
        var json = Customised.Compose(AudioListeningChain.Default.ToJson());

        Assert.Equal(Customised, AudioWorkspace.FromEditState(json));
    }

    [Fact]
    public void The_listening_chain_still_reads_back_with_a_workspace_beside_it()
    {
        var chain = AudioListeningChain.Default with { LowPassOn = true, LowPassHz = 3_000, NoiseGateOn = true };

        var json = Customised.Compose(chain.ToJson());

        var back = AudioListeningChain.FromJson(json);
        Assert.True(back.LowPassOn);
        Assert.Equal(3_000, back.LowPassHz);
        Assert.True(back.NoiseGateOn);
    }

    [Fact]
    public void Saving_the_view_writes_the_workspace_into_the_request()
    {
        var view = AudioViewState.Default with { Workspace = Customised };

        var request = view.ToRequest(existing: null);

        Assert.Equal(Customised, AudioWorkspace.FromEditState(request.EditStateJson));
    }

    [Fact]
    public void A_changed_workspace_is_a_changed_view_so_it_gets_saved()
    {
        var before = AudioViewState.Default;
        var after  = before with { Workspace = before.Workspace with { Speed = 1.5 } };

        Assert.False(after.SameAs(before));
        Assert.True(before.SameAs(AudioViewState.Default));
    }

    [Theory]
    [InlineData("""{"workspace":{"speed":"NaN"}}""")]
    [InlineData("""{"workspace":{"speed":99,"zoomPxPerSec":-5}}""")]
    [InlineData("""{"workspace":{"spectrogramHeight":9000}}""")]
    [InlineData("""{"workspace":{"inspectorTab":"settings","scale":"chromatic","windowFunc":"square"}}""")]
    [InlineData("""{"workspace": 7}""")]
    [InlineData("not json")]
    public void A_value_out_of_range_or_unknown_falls_back_rather_than_breaking_the_editor(string stored)
    {
        var workspace = AudioWorkspace.FromEditState(stored);

        Assert.InRange(workspace.Speed, 0.25, 3);
        Assert.InRange(workspace.ZoomPxPerSec, 0, 2000);
        Assert.InRange(workspace.SpectrogramHeight, 100, 480);
        Assert.Contains(workspace.InspectorTab, AudioWorkspace.Tabs);
        Assert.Contains(workspace.Analysis.Scale, new[] { "linear", "logarithmic", "mel", "bark", "erb" });
        Assert.Equal("hann", workspace.Analysis.WindowFunc);
    }
}
