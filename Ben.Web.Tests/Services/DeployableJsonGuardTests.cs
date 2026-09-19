using System.Text.Json;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Configuration files the DEPLOY SCRIPT parses must be strict JSON — no <c>//</c> comments.
/// </summary>
/// <remarks>
/// <para><b>The bug this exists for.</b> .NET's JSON configuration provider skips comments, so a
/// commented <c>appsettings.json</c> runs perfectly on a developer's machine and in the test
/// suite. Windows PowerShell 5.1's <c>ConvertFrom-Json</c> — which
/// <c>scripts/deploy-ishaunted.ps1</c> uses in <c>Read-JsonFile</c> to merge secrets into the
/// published output — rejects them outright. The failure therefore appears for the first time
/// during a production deployment, on the one machine nobody is developing on. It happened:
/// item 181's <c>MediaTools</c> section was added with three <c>//</c> lines and broke the
/// deploy; the fix was applied to a published artifact copy rather than the source, so the
/// source still carried them on every branch, including master, until this guard was written.
/// </para>
///
/// <para><b>Why the repo writes <c>_comment</c> keys instead.</b> A string-valued key survives
/// every JSON parser, round-trips through <c>ConvertFrom-Json</c>/<c>ConvertTo-Json</c> in the
/// deploy's merge step, and is ignored by the configuration binder. Four such keys already sit
/// in this very file — the convention existed; the new section simply did not follow it.</para>
///
/// <para><b>Why <c>appsettings.Development.json</c> is exempt.</b> The deploy script deletes
/// Development and Production files from the published output rather than parsing them (it
/// merges everything into the base file, which loads in every environment). Its comments are
/// also commented-out ALTERNATIVE settings — a connection string to uncomment, an SMTP host to
/// fill in — which a <c>_comment</c> key could not express. Nothing reads them with a strict
/// parser, so nothing breaks.</para>
/// </remarks>
public sealed class DeployableJsonGuardTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>
    /// Every base <c>appsettings.json</c> shipped by a deployable host. Environment-specific
    /// overlays are deliberately absent — see the class remarks.
    /// </summary>
    private static IEnumerable<string> DeployedConfigFiles()
    {
        var root = RepoRoot().FullName;
        return new[]
        {
            Path.Combine(root, "Ben.Data.WebApi", "appsettings.json"),
            Path.Combine(root, "Ben.Web.Website", "appsettings.json"),
            Path.Combine(root, "Ben.Wasm.Video", "wwwroot", "appsettings.json"),
        }.Where(File.Exists);
    }

    [Fact]
    public void Deployed_appsettings_parse_under_a_strict_json_reader()
    {
        var offenders = new List<string>();

        foreach (var path in DeployedConfigFiles())
        {
            var text = File.ReadAllText(path);
            try
            {
                // Default options: comments and trailing commas are ERRORS, which is exactly
                // how PowerShell 5.1's ConvertFrom-Json behaves on the deployment host.
                using var _ = JsonDocument.Parse(text);
            }
            catch (JsonException ex)
            {
                offenders.Add($"  {Path.GetFileName(Path.GetDirectoryName(path))}/{Path.GetFileName(path)} — {ex.Message}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These configuration files are not strict JSON, so scripts/deploy-ishaunted.ps1 will\n"
            + "fail to parse them on the deployment host (Windows PowerShell's ConvertFrom-Json\n"
            + "rejects what .NET's configuration reader forgives — so this breaks ONLY in\n"
            + "production, and only at deploy time):\n\n"
            + string.Join("\n", offenders)
            + "\n\nUse a \"_comment\" string key instead of a // line, as the rest of the file does.");
    }

    /// <summary>
    /// The guard would be worthless if a strict reader accepted comments after all. This proves
    /// the check discriminates, against the exact text that broke the deploy.
    /// </summary>
    [Fact]
    public void The_guard_rejects_the_shape_that_broke_the_deploy()
    {
        const string withLineComment = """
            {
              // Item 181: an absolute path to ffmpeg enables stripping metadata.
              "MediaTools": { "FfmpegPath": "", "TimeoutSeconds": 120 }
            }
            """;
        // ThrowsAny, not Throws: the reader raises JsonReaderException, a subclass.
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(withLineComment));

        // ...and accepts the convention that replaced it.
        const string withCommentKey = """
            {
              "MediaTools": {
                "_comment": "Item 181: an absolute path to ffmpeg enables stripping metadata.",
                "FfmpegPath": "", "TimeoutSeconds": 120
              }
            }
            """;
        using var parsed = JsonDocument.Parse(withCommentKey);
        Assert.Equal("", parsed.RootElement.GetProperty("MediaTools").GetProperty("FfmpegPath").GetString());
    }

    /// <summary>
    /// A comment key must never be mistaken for a setting. The binder ignores unknown keys, but
    /// one place in this repo it would NOT be ignored is documented in appsettings.json itself:
    /// keys inside Serilog's <c>Override</c> block are logger names, and a comment there crashes
    /// startup. This pins that no such key exists.
    /// </summary>
    [Fact]
    public void No_comment_key_hides_inside_a_serilog_override_block()
    {
        foreach (var path in DeployedConfigFiles())
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("Serilog", out var serilog)
                || serilog.ValueKind != JsonValueKind.Object) continue;
            // Serilog allows the shorthand "MinimumLevel": "Information" — a string, with no
            // Override block to inspect. Checking the kind before descending keeps this guard
            // working across both shapes rather than throwing on the terser one.
            if (!serilog.TryGetProperty("MinimumLevel", out var minimumLevel)
                || minimumLevel.ValueKind != JsonValueKind.Object) continue;
            if (!minimumLevel.TryGetProperty("Override", out var overrides)
                || overrides.ValueKind != JsonValueKind.Object) continue;

            foreach (var property in overrides.EnumerateObject())
            {
                Assert.False(property.Name.StartsWith('_'),
                    $"{path}: \"{property.Name}\" sits inside Serilog:MinimumLevel:Override, where "
                    + "every key is read as a logger name — this crashes startup. Put the note "
                    + "beside the Override block, not inside it.");
            }
        }
    }
}
