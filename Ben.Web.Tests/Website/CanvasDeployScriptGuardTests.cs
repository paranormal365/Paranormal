using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The deploy and IIS scripts know about the case canvas at <c>/editors/canvas/</c> (canvas plan M7-01).
/// </summary>
/// <remarks>
/// <para><b>Why a source scan.</b> The scripts run elevated, once in a while, on one machine, and
/// every step that goes missing from them fails in production rather than here: a canvas published
/// with <c>&lt;base href="/"&gt;</c> sits on "Loading" forever; one that keeps
/// <c>appsettings.Development.json</c> talks to localhost; stale <c>.br</c> twins serve the
/// pre-patch bytes; a web.config asking for cross-origin isolation breaks MapKit tiles; a deploy that
/// copied nothing is confirmed by the previous build's files. Each fact below names the step whose
/// absence it catches. They mirror the video editor's guards, which exist because each of those
/// happened once.</para>
///
/// <para>The canvas project lives outside this repository until it joins <c>Ben.slnx</c> (plan M8),
/// so the script must publish a rooted project path — and PowerShell 5.1's <c>Join-Path</c> of two
/// rooted paths produces <c>Z:\repo\Z:\elsewhere</c>, which is why that line is pinned too.</para>
/// </remarks>
public sealed class CanvasDeployScriptGuardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string RepoFile(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private static string DeployScript() => RepoFile("scripts/deploy-ishaunted.ps1");

    private static string SetupScript() => RepoFile("scripts/setup-iis-ishaunted.ps1");

    /// <summary>The canvas publish block: from its heading to the next section rule.</summary>
    private static string CanvasBlock()
    {
        var script = DeployScript();
        var start = script.IndexOf("# ---- Canvas editor (Blazor WebAssembly) ----", StringComparison.Ordinal);
        if (start < 0) throw new Xunit.Sdk.XunitException("the canvas publish block is missing from deploy-ishaunted.ps1");
        var end = script.IndexOf("# ====", start, StringComparison.Ordinal);
        return end < 0 ? script[start..] : script[start..end];
    }

    [Fact]
    public void DeployScript_accepts_canvas_in_the_apps_set()
    {
        var script = DeployScript();
        Assert.True(Regex.IsMatch(script, @"\[ValidateSet\([^)]*'canvas'"),
            "-Apps does not accept 'canvas', so the canvas cannot be deployed at all");
        Assert.True(Regex.IsMatch(script, @"\$Apps\s*=\s*@\('webapi',\s*'editor',\s*'canvas',\s*'files',\s*'website'\)"),
            "the default -Apps list must deploy the canvas, between the video editor and the files");
        Assert.True(Regex.IsMatch(script, @"\$order\s*=\s*@\('webapi',\s*'editor',\s*'canvas',\s*'files',\s*'website'\)"),
            "the canonical order must place the canvas with the static apps, before the website cut-over");
    }

    [Fact]
    public void DeployScript_mounts_canvas_at_editors_canvas()
    {
        var script = DeployScript();
        Assert.True(Regex.IsMatch(script, @"\[string\]\s+\$CanvasPath\s*=\s*'editors/canvas'"),
            "the canvas mount path parameter is missing or not /editors/canvas");
        Assert.Contains("$CanvasBase", script);
        Assert.True(Regex.IsMatch(script, @"\[string\]\s+\$CanvasProjectPath\s*="),
            "there is no -CanvasProjectPath, and the canvas project lives outside this repository until M8");
    }

    [Fact]
    public void DeployScript_publishes_a_project_outside_the_repo()
        => Assert.True(DeployScript().Contains("[IO.Path]::IsPathRooted($project)", StringComparison.Ordinal),
            "Invoke-Publish joins every project to the repo root; on PS 5.1 Join-Path of two rooted paths is 'Z:\\repo\\Z:\\elsewhere'");

    [Fact]
    public void DeployScript_refuses_a_missing_canvas_project()
        => Assert.True(Regex.IsMatch(CanvasBlock(), @"throw\s+""[^""]*-CanvasProjectPath"),
            "a wrong or missing canvas project must stop the deploy with a sentence naming -CanvasProjectPath");

    [Fact]
    public void DeployScript_patches_the_three_canvas_settings()
    {
        var block = CanvasBlock();
        Assert.Contains("Canvas:WebApiBaseUrl", block);
        Assert.Contains("Canvas:SiteBaseUrl", block);
        Assert.Contains("Canvas:MapTokenUrl", block);
        Assert.Contains("/auth/mapkit-token", block);
        // Inside a PowerShell double-quoted string the attribute's quotes are doubled: ""$CanvasBase"".
        Assert.True(Regex.IsMatch(block, @"<base href=""{1,2}\$CanvasBase"),
            "the canvas's <base href> must be patched to its mount path, or the app loads its runtime from the site root");
    }

    [Fact]
    public void DeployScript_removes_the_development_settings_and_stale_twins_for_canvas()
    {
        var block = CanvasBlock();
        Assert.Contains("appsettings.Development.json", block);
        Assert.Contains("survived - it holds the pre-patch bytes", block);
    }

    [Fact]
    public void DeployScript_refuses_a_cross_origin_isolated_canvas()
    {
        var block = CanvasBlock();
        Assert.Contains("Cross-Origin-(Embedder|Opener)-Policy", block);
        Assert.Contains("MapKit tiles would fail", block);
    }

    [Fact]
    public void DeployScript_stamps_and_smoke_checks_the_canvas()
    {
        var script = DeployScript();
        Assert.Contains("$script:CanvasStamp", script);
        Assert.Contains("${CanvasBase}build-info.json", script);
        Assert.Contains("_content/Ben.Canvas.Editor/js/moduleLoader.js", script);
        Assert.True(Regex.IsMatch(script, @"api/canvas-documents""[^\r\n]*expectStatus\s*=\s*401"),
            "the smoke checks must probe /webapi/api/canvas-documents for 401 on the same line");
        Assert.True(Regex.IsMatch(script, @"Invoke-Mirror\s+\(Join-Path\s+\$canvasOut\s+'wwwroot'\)\s+\$CanvasDir"),
            "the canvas is published and patched but never copied to C:\\ishaunted\\editors\\canvas");
    }

    [Fact]
    public void SetupIis_creates_the_canvas_application_on_the_static_pool()
    {
        var script = SetupScript();
        Assert.True(Regex.IsMatch(script, @"Set-App\s+\$CanvasPath\s+\$CanvasDir\s+\$StaticPool"),
            "setup-iis-ishaunted.ps1 does not create /editors/canvas as an IIS Application; without one the website answers its 404 for every canvas file");
        Assert.True(Regex.IsMatch(script, @"\$CanvasPath\s*=\s*'editors/canvas'"),
            "setup-iis-ishaunted.ps1 has no $CanvasPath = 'editors/canvas' parameter");
    }

    /// <summary>
    /// Windows PowerShell 5.1 reads a .ps1 with no byte-order mark as ANSI, so a stray em-dash in a
    /// comment becomes a parse error rather than a typo (deploy-ishaunted.ps1 says so at the top).
    /// </summary>
    /// <remarks>
    /// Checked as the property that actually matters. <c>setup-iis-ishaunted.ps1</c> has no BOM and
    /// must be pure ASCII. <c>deploy-ishaunted.ps1</c> already carries a UTF-8 BOM and five em-dashes
    /// in comments on the base commit, which 5.1 reads correctly because of the BOM — so for it the
    /// rule is "a BOM, or pure ASCII", and everything the canvas added must be pure ASCII regardless.
    /// </remarks>
    [Fact]
    public void Both_scripts_are_pure_ascii()
    {
        var root = RepoRoot();

        var setup = File.ReadAllBytes(Path.Combine(root, "scripts/setup-iis-ishaunted.ps1"));
        var setupOffending = setup.Select((b, i) => (b, i)).Where(x => x.b >= 0x80).Take(3).ToList();
        Assert.True(setupOffending.Count == 0,
            $"setup-iis-ishaunted.ps1 has no BOM and a non-ASCII byte at offset {setupOffending.FirstOrDefault().i}; PS 5.1 will misread it");

        var deploy = File.ReadAllBytes(Path.Combine(root, "scripts/deploy-ishaunted.ps1"));
        var hasBom = deploy.Length >= 3 && deploy[0] == 0xEF && deploy[1] == 0xBB && deploy[2] == 0xBF;
        Assert.True(hasBom || deploy.All(b => b < 0x80),
            "deploy-ishaunted.ps1 has no BOM and non-ASCII bytes; PS 5.1 will misread it");

        var block = CanvasBlock();
        var nonAscii = block.Where(c => c >= 0x80).Take(5).ToArray();
        Assert.True(nonAscii.Length == 0,
            $"the canvas publish block contains non-ASCII characters ({new string(nonAscii)}); keep new script text ASCII");
    }

    [Fact]
    public void StandaloneCanvasAddress_points_at_the_canvas_mount()
    {
        var source = RepoFile("Ben.Web.Services/StandaloneCanvasAddress.cs");
        Assert.Contains("\"/editors/canvas/\"", source);
        Assert.Contains("\"http://localhost:5125/\"", source);
    }

    /// <summary>
    /// The canvas is behind no feature switch, and nothing may quietly put it back behind one.
    /// </summary>
    /// <remarks>
    /// It had one until 2026-09-16, while there were two ways to write up a case and a site had to
    /// choose. Research is boards now, so a site with the switch off would have no research at all —
    /// and a flag whose off position breaks the product is not a choice, it is a trap.
    /// </remarks>
    [Fact]
    public void No_canvas_feature_flag_is_declared_anywhere()
    {
        foreach (var file in new[]
                 {
                     "Ben.Web.Services/SiteFeaturesProvider.cs",
                     "Ben.Data.WebApi/Services/SiteSettingsService.cs",
                 })
        {
            Assert.DoesNotContain("features.canvas-editor", RepoFile(file), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The API lets the canvas host talk to it in development.
    /// </summary>
    /// <remarks>
    /// <b>This was broken and nothing said so.</b> The canvas runs on its own port in development, so
    /// the handover from a case's Research tab exchanges its one-use code cross-origin. With the canvas
    /// origin missing from the development CORS list, that exchange was refused by the browser, the
    /// editor opened SIGNED OUT on whatever board the device happened to be holding, and every test
    /// still passed — they checked that the editor appeared, not that it had arrived signed in
    /// (found while re-shooting the help pictures, 2026-09-16).
    ///
    /// Production needs no entry: the canvas is served from the same origin there, under /editors/canvas/.
    /// </remarks>
    [Fact]
    public void The_development_CORS_list_names_the_canvas_host()
    {
        var settings = RepoFile("Ben.Data.WebApi/appsettings.Development.json");
        var canvas = Ben.Web.Services.StandaloneCanvasAddress.DevelopmentUrl.TrimEnd('/');

        Assert.Contains(canvas, settings, StringComparison.Ordinal);
    }

    [Fact]
    public void DeployDocs_describe_the_canvas()
    {
        var root = RepoRoot();
        var canvasDoc = Path.Combine(root, "docs/deploy-canvas.md");
        Assert.True(File.Exists(canvasDoc), "docs/deploy-canvas.md does not exist");
        var doc = File.ReadAllText(canvasDoc);
        Assert.Contains("-CanvasProjectPath", doc);
        Assert.Contains("dotnet ef database update", doc);
        Assert.Contains("/editors/canvas/", doc);
        Assert.DoesNotContain("IsHauntedDb_player", doc);   // R26: rollout touches production only

        var production = File.ReadAllText(Path.Combine(root, "docs/deploy-production.md"));
        Assert.Contains("| `/editors/canvas` |", production);
        Assert.Contains("Five applications", production);
    }
}
