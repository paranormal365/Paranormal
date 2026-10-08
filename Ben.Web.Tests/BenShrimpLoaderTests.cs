using System.Text.RegularExpressions;
using Ben.Web.Website.Library.Kit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>BenShrimpLoader, rendered for real with Blazor's own HtmlRenderer: show/hide, the Size percentage, the
/// cover mode, the label, and one set of SVG ids per instance.</summary>
public class BenShrimpLoaderTests
{
    private static async Task<string> Render(Dictionary<string, object?> parameters)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<BenShrimpLoader>(ParameterView.FromDictionary(parameters))).ToHtmlString());
    }

    private static RenderFragment Content(string html) => b => b.AddMarkupContent(0, html);

    [Fact]
    public async Task By_default_it_shows_at_the_full_200px_with_its_video()
    {
        var html = await Render(new());
        Assert.Contains("role=\"status\"", html);
        Assert.Contains("--ben-shrimp-loader-size: 200px", html);
        Assert.Contains("/_content/Ben.Web.Website.Library/kit/shrimp-loader.mp4", html);
        Assert.Contains(">Loading</span>", html);                               // the screen-reader label
    }

    [Theory]
    [InlineData(50, "100px")]
    [InlineData(70, "140px")]
    [InlineData(100, "200px")]
    [InlineData(150, "200px")]   // never bigger than 100%
    [InlineData(5, "40px")]      // and never smaller than 20%
    public async Task Size_is_a_percentage_of_200px_from_20_to_100(int size, string px)
    {
        var html = await Render(new() { ["Size"] = size });
        Assert.Contains($"--ben-shrimp-loader-size: {px}", html);
    }

    [Fact]
    public async Task Hidden_on_its_own_it_renders_nothing()
    {
        var html = await Render(new() { ["Visible"] = false });
        Assert.Equal("", html.Trim());
    }

    [Fact]
    public async Task Wrapped_round_content_it_covers_it_while_visible()
    {
        var html = await Render(new() { ["Visible"] = true, ["ChildContent"] = Content("<table id=\"grid\"></table>") });
        Assert.Contains("id=\"grid\"", html);
        Assert.Contains("ben-shrimp-loader-cover", html);
    }

    [Fact]
    public async Task Wrapped_round_content_and_hidden_it_leaves_just_the_content()
    {
        var html = await Render(new() { ["Visible"] = false, ["ChildContent"] = Content("<table id=\"grid\"></table>") });
        Assert.Contains("id=\"grid\"", html);
        Assert.DoesNotContain("ben-shrimp-loader-cover", html);
        Assert.DoesNotContain("<video", html);
    }

    [Fact]
    public async Task Text_shows_under_it_and_is_the_screen_reader_label()
    {
        var html = await Render(new() { ["Text"] = "Loading cases…" });
        Assert.Matches("ben-shrimp-loader__text\"[^>]*>Loading cases&#x2026;</div>", html);   // the ellipsis is HTML-encoded; a scoped-CSS attribute sits in the tag
        Assert.Matches("visually-hidden\"[^>]*>Loading cases&#x2026;</span>", html);
    }

    [Fact]
    public async Task Each_loader_has_its_own_svg_ids_and_its_ring_uses_them()
    {
        var a = await Render(new());
        var b = await Render(new());
        string Trail(string html) => Regex.Match(html, "id=\"(bsl[0-9a-f]{8})-trail\"").Groups[1].Value;
        Assert.NotEqual("", Trail(a));
        Assert.NotEqual(Trail(a), Trail(b));
        Assert.Contains($"stroke=\"url(#{Trail(a)}-trail)\"", a);
        Assert.Contains($"filter=\"url(#{Trail(a)}-glow)\"", a);
    }

    /// <summary>Opt-in (BSL_HARNESS=1): writes the real rendered markup and the component's CSS to one page, to look
    /// at in a browser. Not part of the normal run.</summary>
    [SkippableFact]
    public async Task Writes_a_visual_harness_when_asked()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BSL_HARNESS") == "1");
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Ben.Web.Website.Library", "Kit", "BenShrimpLoader.razor.css")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var css = await File.ReadAllTextAsync(Path.Combine(dir!, "Ben.Web.Website.Library", "Kit", "BenShrimpLoader.razor.css"));
        var kit = Path.Combine(dir!, "Ben.Web.Website.Library", "wwwroot", "kit");
        string Fix(string html) => html.Replace("/_content/Ben.Web.Website.Library/kit/", "file://" + kit + "/");
        var grid = "<table class=\"g\">" + string.Concat(Enumerable.Range(1, 6).Select(i => $"<tr><td>Case {i}</td><td>Thomas House</td><td>Open</td></tr>")) + "</table>";
        var page = "<!doctype html><meta charset=\"utf-8\"><style>"
            + ":root{--bs-body-bg:#0c1017;--bs-secondary-color:#98a3b4}body{background:#0c1017;color:#e9edf4;font:15px system-ui;margin:24px}"
            + ".visually-hidden{position:absolute!important;width:1px!important;height:1px!important;overflow:hidden!important;clip:rect(0,0,0,0)!important}"
            + ".row{display:flex;gap:48px;align-items:flex-end;margin-bottom:28px}.g{border-collapse:collapse;width:520px}.g td{border:1px solid #2a3344;padding:10px}"
            + "h3{font-size:13px;color:#98a3b4;margin:0 0 8px;font-weight:600}" + css + "</style>"
            + "<div class=\"row\">"
            + "<div><h3>Size 40</h3>" + Fix(await Render(new() { ["Size"] = 40 })) + "</div>"
            + "<div><h3>Size 70, Text</h3>" + Fix(await Render(new() { ["Size"] = 70, ["Text"] = "Loading…" })) + "</div>"
            + "<div><h3>Size 100 (and 150 caps here)</h3>" + Fix(await Render(new() { ["Size"] = 150, ["Text"] = "Summoning the grid…" })) + "</div>"
            + "<div><h3>Visible = false</h3><div style=\"width:120px;height:60px;border:1px dashed #445\">"
            + Fix(await Render(new() { ["Visible"] = false })) + "</div></div></div>"
            + "<div class=\"row\"><div><h3>Covering a grid while it loads (Size 50)</h3>"
            + Fix(await Render(new() { ["Size"] = 50, ["Text"] = "Loading cases…", ["ChildContent"] = Content(grid) })) + "</div>"
            + "<div><h3>Same grid, Visible = false</h3>"
            + Fix(await Render(new() { ["Visible"] = false, ["ChildContent"] = Content(grid) })) + "</div></div>";
        await File.WriteAllTextAsync("/tmp/bsl-harness.html", page);
    }
}
