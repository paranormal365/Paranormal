using Ben.Canvas.Core.Options;
using Ben.Canvas.Editor.Extensions;

namespace Ben.Canvas.Tests.Hosting;

/// <summary>
/// Turning server features on only when there is a server.
/// </summary>
/// <remarks>
/// An empty API address is a working configuration - a local-only editor - so it must leave every
/// server feature off rather than half on. The trailing slash matters because every endpoint path is
/// appended to the base.
/// </remarks>
public sealed class CanvasEditorHostDefaultsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_api_url_leaves_a_local_only_editor(string? api)
    {
        var o = new CanvasEditorOptions();

        CanvasEditorHostDefaults.ApplyServerIntegration(o, api, "https://x.test/token", "https://x.test");

        Assert.Null(o.ApiBaseUrl);
        Assert.False(o.ServerSave);
        Assert.False(o.LinkUnfurl);
        Assert.False(o.Publish);
        Assert.Null(o.MapTokenUrl);
    }

    [Fact]
    public void The_api_url_loses_its_trailing_slash_and_turns_server_features_on()
    {
        var o = new CanvasEditorOptions();

        CanvasEditorHostDefaults.ApplyServerIntegration(o, "https://ishaunted.com/webapi/");

        Assert.Equal("https://ishaunted.com/webapi", o.ApiBaseUrl);
        Assert.True(o.ServerSave);
        Assert.True(o.LinkUnfurl);
        Assert.True(o.Publish);
    }

    [Fact]
    public void A_blank_map_token_url_means_no_maps()
    {
        var o = new CanvasEditorOptions();

        CanvasEditorHostDefaults.ApplyServerIntegration(o, "https://ishaunted.com/webapi", "  ");

        Assert.Null(o.MapTokenUrl);
    }

    [Fact]
    public void The_site_url_loses_its_trailing_slash()
    {
        var o = new CanvasEditorOptions();

        CanvasEditorHostDefaults.ApplyServerIntegration(o, "https://ishaunted.com/webapi", null, "https://ishaunted.com/");

        Assert.Equal("https://ishaunted.com", o.SiteBaseUrl);
    }
}
