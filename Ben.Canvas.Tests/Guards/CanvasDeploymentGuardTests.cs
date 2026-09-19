using System.Text.RegularExpressions;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Guards;

/// <summary>
/// The published canvas app's IIS configuration.
/// </summary>
/// <remarks>
/// Copied in intent from Ben.Video.Tests/Services/EditorDeploymentGuardTests.cs. Every rule here fails
/// quietly in production: a missing MIME map leaves the app on "Loading", a cached index.html points a
/// returning visitor at runtime files the last deploy deleted, and a rewrite section on a server without
/// URL Rewrite fails the whole folder. The one difference from the video editor is deliberate: no
/// cross-origin isolation, because require-corp would blank MapKit tiles and link-card pictures.
/// </remarks>
public sealed class CanvasDeploymentGuardTests
{
    private static string WebConfig()
    {
        var path = Path.Combine(RepoFiles.HostWwwroot(), "web.config");
        Assert.True(File.Exists(path), $"The canvas web.config is missing: {path}");
        return RepoFiles.ReadWithoutComments(path);
    }

    [Theory]
    [InlineData("X-Frame-Options")]
    [InlineData("Content-Security-Policy")]
    [InlineData("X-Content-Type-Options")]
    [InlineData("Referrer-Policy")]
    public void The_editor_sends_the_header(string header) =>
        Assert.Matches($@"<add\s+name=""{Regex.Escape(header)}""", WebConfig());

    [Fact]
    public void The_editor_does_not_ask_for_cross_origin_isolation()
    {
        var config = WebConfig();

        Assert.DoesNotContain("Cross-Origin-Opener-Policy", config);
        Assert.DoesNotContain("Cross-Origin-Embedder-Policy", config);
    }

    [Theory]
    [InlineData(".html")]
    [InlineData(".json")]
    public void The_shell_and_its_settings_are_not_cached(string extension) =>
        Assert.Matches($@"<add\s+extension=""{Regex.Escape(extension)}""\s+policy=""DisableCache""", WebConfig());

    [Theory]
    [InlineData(".wasm")]
    [InlineData(".dat")]
    [InlineData(".blat")]
    [InlineData(".dll")]
    [InlineData(".json")]
    [InlineData(".woff")]
    [InlineData(".woff2")]
    public void Mime_maps_cover_the_runtime(string extension)
    {
        var config = WebConfig();
        var remove = config.IndexOf($"<remove fileExtension=\"{extension}\"", StringComparison.Ordinal);
        var map = config.IndexOf($"<mimeMap fileExtension=\"{extension}\"", StringComparison.Ordinal);

        Assert.True(map >= 0, $"No mimeMap for {extension}: the runtime 404s and the app sits on Loading.");
        Assert.True(remove >= 0 && remove < map, $"{extension} must be removed before it is mapped, or a server-level mapping makes a 500.19.");
    }

    [Fact]
    public void No_rewrite_section() =>
        Assert.DoesNotContain("<rewrite", WebConfig());
}
